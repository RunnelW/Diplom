using Diplom.Data;
using Diplom.Models;
using Diplom.Services;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using static System.Net.Mime.MediaTypeNames;


namespace Diplom.Controllers
{
    [Authorize(Roles = "Admin,Storekeeper")]
    public class RawMaterialsController : Controller
    {

        private readonly AuditService _auditService;

      

        public async Task<IActionResult> Index()
        {
            var items = await _context.RawMaterials
                .Include(r => r.CreatedByUser)
                .ToListAsync();
            return View(items);
        }

        // GET: отобразить форму создания
        public IActionResult Create()
        {
            // Создаём пустую модель, чтобы представление не ругалось на null
            return View(new RawMaterial());
        }

        private readonly ApplicationDbContext _context;
        private readonly UserManager<AppUser> _userManager;

        public RawMaterialsController(ApplicationDbContext context, UserManager<AppUser> userManager, AuditService auditService)
        {
            _context = context;
            _userManager = userManager;
            _auditService = auditService;
        }

        // POST: сохранить новую партию с автогенерацией номера
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(IFormCollection form)
        {
            try
            {
                var model = new RawMaterial();

                // Генерация номера партии
                var today = DateTime.Now;
                var prefix = $"П-{today:yyyyMMdd}-";
                var lastBatch = _context.RawMaterials
                    .Where(r => r.BatchNumber.StartsWith(prefix))
                    .OrderByDescending(r => r.BatchNumber)
                    .FirstOrDefault();

                int nextNumber = 1;
                if (lastBatch != null)
                {
                    var lastNumStr = lastBatch.BatchNumber.Substring(prefix.Length);
                    if (int.TryParse(lastNumStr, out int lastNum))
                        nextNumber = lastNum + 1;
                }
                model.BatchNumber = prefix + nextNumber.ToString("D3");

                // Заполнение остальных полей
                model.ArrivalDate = DateTime.TryParse(form["ArrivalDate"], out var date) ? date : today;
                model.WoodType = form["WoodType"];
                if (string.IsNullOrWhiteSpace(model.WoodType)) model.WoodType = "Берёза";
                model.Volume = decimal.TryParse(form["Volume"], out var vol) ? vol : 0;
                model.Grade = int.TryParse(form["Grade"], out var grade) ? grade : 1;
                model.Supplier = form["Supplier"];
                model.Status = form["Status"];
                if (string.IsNullOrWhiteSpace(model.Status)) model.Status = "На складе";
                model.Comment = form["Comment"];
                model.CreatedAt = today;
                model.UpdatedAt = today;

                // Привязываем текущего пользователя
                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser != null)
                {
                    model.CreatedByUserId = currentUser.Id;
                }
                else
                {
                    // Если пользователь не найден (не должно случиться из-за [Authorize])
                    ModelState.AddModelError("", "Не удалось определить пользователя.");
                    return View(model);
                }

                _context.RawMaterials.Add(model);
                await _context.SaveChangesAsync();

                TempData["Success"] = $"Партия {model.BatchNumber} добавлена!";
                await _auditService.LogAsync("Create", "RawMaterial", model.Id, $"Создана партия: {model.BatchNumber}, объём: {model.Volume} м³");
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Ошибка: " + ex.Message);
                return View(new RawMaterial());
            }

            
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var item = await _context.RawMaterials.FindAsync(id);
            if (item == null) return NotFound();
            return View(item);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, RawMaterial model)
        {
            if (id != model.Id) return NotFound();

            // Загружаем существующую запись из БД (без отслеживания, чтобы получить CreatedByUserId)
            var existing = await _context.RawMaterials.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
            if (existing == null) return NotFound();

            // Восстанавливаем поля, которые не должны меняться
            model.CreatedByUserId = existing.CreatedByUserId;
            model.CreatedAt = existing.CreatedAt;
            model.UpdatedAt = DateTime.Now;

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(model);
                    await _context.SaveChangesAsync();
                    TempData["Success"] = "Партия обновлена!";
                    await _auditService.LogAsync("Edit", "RawMaterial", model.Id, $"Изменена партия: {model.BatchNumber}, объём: {model.Volume} м³");
                    return RedirectToAction(nameof(Index));
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.RawMaterials.Any(e => e.Id == id)) return NotFound();
                    else throw;
                }
            }
            return View(model);
        }

        // GET: RawMaterials/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();
            var item = await _context.RawMaterials.FindAsync(id);
            if (item == null) return NotFound();
            return View(item);
        }

        // GET: RawMaterials/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();
            var item = await _context.RawMaterials.FindAsync(id);
            if (item == null) return NotFound();
            return View(item);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var item = await _context.RawMaterials.FindAsync(id);
            if (item != null) _context.RawMaterials.Remove(item);
            await _context.SaveChangesAsync();
            TempData["Success"] = "Партия удалена!";
            await _auditService.LogAsync("Delete", "RawMaterial", id, $"Удалена партия ID: {id}");
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> GenerateAct(int id)
        {
            var rawMaterial = await _context.RawMaterials.FindAsync(id);
            if (rawMaterial == null) return NotFound();
            await _auditService.LogAsync("Download", "Act", id, $"Скачан акт прихода: партия {rawMaterial.BatchNumber}, объём: {rawMaterial.Volume:F2} м³");

            string templatePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "templates", "Акт_приема_леса_Template.docx");
            string tempFile = Path.Combine(Path.GetTempPath(), $"Акт_приема_{rawMaterial.BatchNumber}_{DateTime.Now.Ticks}.docx");

            if (!System.IO.File.Exists(templatePath))
            {
                return Content($"Шаблон не найден: {templatePath}");
            }

            try
            {
                System.IO.File.Copy(templatePath, tempFile, true);

                using (WordprocessingDocument wordDoc = WordprocessingDocument.Open(tempFile, true))
                {
                    var body = wordDoc.MainDocumentPart.Document.Body;

                    foreach (var text in body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>())
                    {
                        text.Text = text.Text.Replace("{BatchNumber}", rawMaterial.BatchNumber);
                        text.Text = text.Text.Replace("{ArrivalDate:dd.MM.yyyy}", rawMaterial.ArrivalDate.ToString("dd.MM.yyyy"));
                        text.Text = text.Text.Replace("{Supplier}", rawMaterial.Supplier ?? "Не указан");
                        text.Text = text.Text.Replace("{WoodType}", rawMaterial.WoodType);
                        text.Text = text.Text.Replace("{Volume}", rawMaterial.Volume.ToString("F2"));
                    }
                }

                byte[] fileContent = System.IO.File.ReadAllBytes(tempFile);
                System.IO.File.Delete(tempFile);

                return File(fileContent, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", $"Акт_приема_{rawMaterial.BatchNumber}.docx");
            }
            catch (Exception ex)
            {
                return Content($"Ошибка: {ex.Message}");
            }
        }
    }
}