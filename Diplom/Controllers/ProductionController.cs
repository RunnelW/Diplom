using Diplom.Data;
using Diplom.Models;
using Diplom.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Diplom.Controllers
{
    [Authorize(Roles = "Admin,Director,Master")]
    public class ProductionController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<AppUser> _userManager;
        private readonly AuditService _auditService;

        public ProductionController(ApplicationDbContext context, UserManager<AppUser> userManager, AuditService auditService)
        {
            _context = context;
            _userManager = userManager;
            _auditService = auditService;
        }

        // GET: Список производственных смен
        public async Task<IActionResult> Index()
        {
            var batches = await _context.ProductionBatches
                .Include(b => b.WoodStack)
                .Include(b => b.CreatedByUser)
                .OrderByDescending(b => b.ProductionDate)
                .ToListAsync();
            return View(batches);
        }

        // GET: Форма создания новой смены
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            ViewBag.Stacks = await _context.WoodStacks
                .Where(s => s.CurrentVolume > 0)
                .OrderBy(s => s.WoodType)
                .ToListAsync();
            return View();
        }

        // POST: Сохранение новой смены
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ProductionBatch model)
        {
            var form = Request.Form;

            decimal ParseDecimal(string key)
            {
                if (!form.ContainsKey(key)) return 0;
                var val = form[key].ToString();
                if (string.IsNullOrWhiteSpace(val)) return 0;
                val = val.Trim().Replace(',', '.');
                if (decimal.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var result))
                    return result;
                return 0;
            }

            int ParseInt(string key)
            {
                if (!form.ContainsKey(key)) return 0;
                var val = form[key].ToString();
                if (string.IsNullOrWhiteSpace(val)) return 0;
                if (int.TryParse(val, out var result)) return result;
                return 0;
            }

            model.WoodStackId = ParseInt("woodStackId");
            model.LogLength = ParseDecimal("LogLength");
            model.LogWidth = ParseDecimal("LogWidth");
            model.LogHeight = ParseDecimal("LogHeight");
            model.WoodDensityCoeff = ParseDecimal("WoodDensityCoeff");
            model.SheetLength = ParseDecimal("SheetLength");
            model.SheetWidth = ParseDecimal("SheetWidth");
            model.SheetStackHeight = ParseDecimal("SheetStackHeight");
            model.DensityCoefficient = ParseDecimal("DensityCoefficient");

            if (form.ContainsKey("productionDate") && DateTime.TryParse(form["productionDate"], out var date))
                model.ProductionDate = date;
            else
                model.ProductionDate = DateTime.Now;

            model.Comment = form["comment"].ToString();
            if (string.IsNullOrWhiteSpace(model.Comment))
                model.Comment = "Стандартные отходы на лущении";

            var stack = await _context.WoodStacks.FindAsync(model.WoodStackId);
            if (stack == null)
            {
                TempData["Error"] = "Штабель не найден.";
                return RedirectToAction("Create");
            }

            var rawVolume = model.LogLength * model.LogWidth * model.LogHeight * model.WoodDensityCoeff;
            var veneerVolume = model.SheetLength * model.SheetWidth * model.SheetStackHeight * model.DensityCoefficient;

            if (veneerVolume > rawVolume + 0.01m)
            {
                TempData["Error"] = $"Некорректные данные: выход шпона ({veneerVolume:F2} м³) не может превышать объём сырья ({rawVolume:F2} м³).";
                return RedirectToAction("Create");
            }

            if (rawVolume > stack.CurrentVolume + 0.01m)
            {
                TempData["Error"] = $"Недостаточно сырья. Доступно: {stack.CurrentVolume:F2} м³. Требуется: {rawVolume:F2} м³.";
                return RedirectToAction("Create");
            }

            model.RawVolume = rawVolume;
            model.VeneerVolume = veneerVolume;
            model.WasteVolume = rawVolume - veneerVolume;
            model.Efficiency = rawVolume > 0 ? veneerVolume / rawVolume : 0;
            model.CreatedByUserId = _userManager.GetUserId(User);

            if (model.Efficiency >= 0.40m) model.EfficiencyGrade = "Хорошо";
            else if (model.Efficiency >= 0.25m) model.EfficiencyGrade = "Средне";
            else model.EfficiencyGrade = "Плохо";

            _context.ProductionBatches.Add(model);
            stack.CurrentVolume -= rawVolume;

            await _context.SaveChangesAsync();

            // ============================================================
            // ДОБАВЛЕНИЕ ШПОНА НА СКЛАД ГОТОВОЙ ПРОДУКЦИИ
            // ============================================================

            // Рассчитываем количество листов (при стандартной толщине 1 мм)
            var existingStock = await _context.FinishedGoodsStocks
    .FirstOrDefaultAsync(s => s.WoodType == stack.WoodType &&
                              s.SheetLength == model.SheetLength &&
                              s.SheetWidth == model.SheetWidth);

            if (existingStock != null)
            {
                existingStock.Volume += veneerVolume;
                existingStock.UpdatedAt = DateTime.Now;
            }
            else
            {
                var newStock = new FinishedGoodsStock
                {
                    WoodType = stack.WoodType,
                    SheetLength = model.SheetLength,
                    SheetWidth = model.SheetWidth,
                    Volume = veneerVolume,
                    UpdatedAt = DateTime.Now
                };
                _context.FinishedGoodsStocks.Add(newStock);
            }

            await _context.SaveChangesAsync();
            // ============================================================

            TempData["Success"] = $"Смена сохранена. Сырьё: {rawVolume:F2} м³, Шпон: {veneerVolume:F2} м³. Эффективность: {model.Efficiency:P0}.";
            await _auditService.LogAsync("Create", "Production", model.Id, $"Производственная смена: {stack.WoodType}, сырьё: {rawVolume:F2} м³, шпон: {veneerVolume:F2} м³, эффективность: {(veneerVolume / rawVolume):P0}");
            return RedirectToAction("Index");
        }

        // GET: Edit (отображает форму редактирования)
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var batch = await _context.ProductionBatches
                .Include(b => b.WoodStack)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (batch == null) return NotFound();

            ViewBag.Stacks = await _context.WoodStacks
                .Where(s => s.CurrentVolume > 0 || s.Id == batch.WoodStackId)
                .OrderBy(s => s.WoodType)
                .ToListAsync();

            return View(batch);
        }

        // POST: Edit (сохраняет изменения) - ТОЛЬКО ОДИН POST-МЕТОД!
        // POST: Edit (сохраняет изменения)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ProductionBatch model)
        {
            var batch = await _context.ProductionBatches
                .Include(b => b.WoodStack)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (batch == null) return NotFound();

            var form = Request.Form;

            decimal ParseDecimal(string key)
            {
                if (!form.ContainsKey(key)) return 0;
                var val = form[key].ToString();
                if (string.IsNullOrWhiteSpace(val)) return 0;
                val = val.Trim().Replace(',', '.');
                if (decimal.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var result))
                    return result;
                return 0;
            }

            int ParseInt(string key)
            {
                if (!form.ContainsKey(key)) return 0;
                var val = form[key].ToString();
                if (string.IsNullOrWhiteSpace(val)) return 0;
                if (int.TryParse(val, out var result)) return result;
                return 0;
            }

            var woodStackId = ParseInt("WoodStackId");
            var logLength = ParseDecimal("LogLength");
            var logWidth = ParseDecimal("LogWidth");
            var logHeight = ParseDecimal("LogHeight");
            var woodDensityCoeff = ParseDecimal("WoodDensityCoeff");
            var sheetLength = ParseDecimal("SheetLength");
            var sheetWidth = ParseDecimal("SheetWidth");
            var sheetStackHeight = ParseDecimal("SheetStackHeight");
            var densityCoefficient = ParseDecimal("DensityCoefficient");
            var comment = form["Comment"].ToString();

            var stack = await _context.WoodStacks.FindAsync(woodStackId);
            if (stack == null)
            {
                TempData["Error"] = "Штабель не найден.";
                return RedirectToAction("Edit", new { id });
            }

            var rawVolume = logLength * logWidth * logHeight * woodDensityCoeff;
            var veneerVolume = sheetLength * sheetWidth * sheetStackHeight * densityCoefficient;

            if (veneerVolume > rawVolume + 0.01m)
            {
                TempData["Error"] = $"Выход шпона ({veneerVolume:F2} м³) не может превышать объём сырья ({rawVolume:F2} м³).";
                return RedirectToAction("Edit", new { id });
            }

            // НОВАЯ ПРОВЕРКА: достаточно ли сырья с учётом изменения
            // Возвращаем старое сырьё обратно в штабель
            var oldRawVolume = batch.RawVolume;
            var availableAfterReturn = stack.CurrentVolume + oldRawVolume;

            // Проверяем, хватит ли сырья после возврата старого
            if (rawVolume > availableAfterReturn + 0.01m)
            {
                TempData["Error"] = $"Недостаточно сырья. После возврата старого сырья доступно: {availableAfterReturn:F2} м³. Требуется: {rawVolume:F2} м³.";
                return RedirectToAction("Edit", new { id });
            }

            // Корректируем остатки штабеля
            stack.CurrentVolume = availableAfterReturn - rawVolume;

            // Обновляем запись
            batch.WoodStackId = woodStackId;
            batch.LogLength = logLength;
            batch.LogWidth = logWidth;
            batch.LogHeight = logHeight;
            batch.WoodDensityCoeff = woodDensityCoeff;
            batch.RawVolume = rawVolume;
            batch.SheetLength = sheetLength;
            batch.SheetWidth = sheetWidth;
            batch.SheetStackHeight = sheetStackHeight;
            batch.DensityCoefficient = densityCoefficient;
            batch.VeneerVolume = veneerVolume;
            batch.WasteVolume = rawVolume - veneerVolume;
            batch.Efficiency = rawVolume > 0 ? veneerVolume / rawVolume : 0;
            batch.Comment = comment;

            if (batch.Efficiency >= 0.40m) batch.EfficiencyGrade = "Хорошо";
            else if (batch.Efficiency >= 0.25m) batch.EfficiencyGrade = "Средне";
            else batch.EfficiencyGrade = "Плохо";

            await _context.SaveChangesAsync();
            TempData["Success"] = $"Смена обновлена. Сырьё: {rawVolume:F2} м³, Шпон: {veneerVolume:F2} м³, Эффективность: {batch.Efficiency:P0}.";
            await _auditService.LogAsync("Edit", "Production", batch.Id, $"Изменена смена ID: {batch.Id}");
            return RedirectToAction("Index");
        }

        // POST: Delete
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var batch = await _context.ProductionBatches
                .Include(b => b.WoodStack)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (batch == null) return NotFound();

            var stack = batch.WoodStack;
            if (stack != null)
            {
                stack.CurrentVolume += batch.RawVolume;
            }

            _context.ProductionBatches.Remove(batch);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Смена удалена.";
            await _auditService.LogAsync("Delete", "Production", id, $"Удалена смена ID: {id}");
            return RedirectToAction("Index");
        }
    }
}