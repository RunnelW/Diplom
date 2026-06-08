using Diplom.Data;
using Diplom.Models;
using Diplom.Services;
using DocumentFormat.OpenXml.InkML;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using SkiaSharp;
using System.Linq;
using Xceed.Words.NET;

namespace Diplom.Controllers
{
    [Authorize]
    public class WoodMovementsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<AppUser> _userManager;
        private readonly AuditService _auditService;

        public WoodMovementsController(ApplicationDbContext context, UserManager<AppUser> userManager, AuditService auditService)
        {
            _context = context;
            _userManager = userManager;
            _auditService = auditService;
        }

        // Список движений
        public async Task<IActionResult> Index()
        {
            var movements = await _context.WoodMovements
                .Include(m => m.WoodStack)
                .Include(m => m.CreatedByUser)
                .OrderByDescending(m => m.MovementDate)
                .ToListAsync();
            return View(movements);
        }

        // GET: форма прихода
        [HttpGet]
        public IActionResult Income()
        {
            return View();
        }

        // POST: приход с расчётом
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Income(string woodType, string length, string width, string height, string coefficient, string? comment, string? supplier)
        {
            // Функция для преобразования строки в decimal с учётом запятой или точки
            decimal ParseDecimal(string input)
            {
                if (string.IsNullOrWhiteSpace(input)) return 0;
                input = input.Trim().Replace(',', '.');
                if (decimal.TryParse(input, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var result))
                    return result;
                return 0;
            }

            var l = ParseDecimal(length);
            var w = ParseDecimal(width);
            var h = ParseDecimal(height);
            var c = ParseDecimal(coefficient);

            if (string.IsNullOrWhiteSpace(woodType) || l <= 0 || w <= 0 || h <= 0 || c <= 0)
            {
                TempData["Error"] = "Все поля должны быть заполнены положительными числами. Используйте точку или запятую.";
                return View();
            }

            var volume = l * w * h * c;

            try
            {
                var stack = await _context.WoodStacks.FirstOrDefaultAsync(s => s.WoodType == woodType);
                if (stack == null)
                {
                    stack = new WoodStack { WoodType = woodType, CurrentVolume = 0 };
                    _context.WoodStacks.Add(stack);
                    await _context.SaveChangesAsync();
                }

                var movement = new WoodMovement
                {
                    WoodStackId = stack.Id,
                    Volume = volume,
                    MovementType = "Income",
                    Comment = comment,
                    Supplier = supplier,                                    // ДОБАВЛЕНО
                    MovementDate = DateTime.Now,
                    CreatedByUserId = _userManager.GetUserId(User),
                    Length = l,                                            // ДОБАВЛЕНО
                    Width = w,                                             // ДОБАВЛЕНО
                    Height = h,                                            // ДОБАВЛЕНО
                    Coefficient = c                                        // ДОБАВЛЕНО
                };
                _context.WoodMovements.Add(movement);
                stack.CurrentVolume += volume;
                stack.UpdatedAt = DateTime.Now;

                await _context.SaveChangesAsync();
                await _auditService.LogAsync("Create", "WoodMovement", movement.Id, $"Приход: {woodType}, объём: {volume:F2} м³, поставщик: {supplier}");
                TempData["Success"] = $"Принято {volume:F2} м³ древесины {woodType}. Поставщик: {supplier ?? "не указан"}";

                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Ошибка базы данных: " + ex.Message;
                return View();
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> QuickIncome(string woodType, decimal volume, string? comment, string? supplier)
        {
            if (string.IsNullOrWhiteSpace(woodType) || volume <= 0)
            {
                TempData["Error"] = "Выберите тип древесины и укажите объём";
                return RedirectToAction("Index", "Home");
            }

            var stack = await _context.WoodStacks.FirstOrDefaultAsync(s => s.WoodType == woodType);
            if (stack == null)
            {
                stack = new WoodStack { WoodType = woodType, CurrentVolume = 0 };
                _context.WoodStacks.Add(stack);
                await _context.SaveChangesAsync();
            }

            var movement = new WoodMovement
            {
                WoodStackId = stack.Id,
                Volume = volume,
                MovementType = "Income",
                Comment = comment ?? "Быстрый приход",
                Supplier = supplier,
                MovementDate = DateTime.Now,
                CreatedByUserId = _userManager.GetUserId(User)
            };
            _context.WoodMovements.Add(movement);
            stack.CurrentVolume += volume;
            stack.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();
            await _auditService.LogAsync("Create", "WoodMovement", movement.Id, $"Быстрый приход: {woodType}, объём: {volume:F2} м³, поставщик: {supplier ?? "не указан"}");
            TempData["Success"] = $"Принято {volume:F2} м³ {woodType}. Поставщик: {supplier ?? "не указан"}";
            return RedirectToAction("Index", "Home");
        }

        private async Task RemoveEmptyStacksAsync()
        {
            var emptyStacks = await _context.WoodStacks
                .Where(s => s.CurrentVolume <= 0)
                .ToListAsync();

            if (emptyStacks.Any())
            {
                _context.WoodStacks.RemoveRange(emptyStacks);
                await _context.SaveChangesAsync();
            }
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var movement = await _context.WoodMovements
                .Include(m => m.WoodStack)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (movement == null) return NotFound();

            ViewBag.Stacks = await _context.WoodStacks.ToListAsync();
            return View(movement);


        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, IFormCollection form)
        {
            var movement = await _context.WoodMovements
                .Include(m => m.WoodStack)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (movement == null) return NotFound();

            // Функция для парсинга decimal с запятой
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

            // Получаем значения из формы
            var woodStackId = int.Parse(form["woodStackId"]);
            var length = ParseDecimal("length");
            var width = ParseDecimal("width");
            var height = ParseDecimal("height");
            var coefficient = ParseDecimal("coefficient");
            var comment = form["comment"].ToString();
            var supplier = form["supplier"].ToString();

            // Находим штабель
            var newStack = await _context.WoodStacks.FindAsync(woodStackId);
            if (newStack == null)
            {
                TempData["Error"] = "Выбранный штабель не существует.";
                return RedirectToAction("Edit", new { id });
            }

            // Пересчитываем объём
            var newVolume = length * width * height * coefficient;

            // Сохраняем старые значения для корректировки остатков
            var oldVolume = movement.Volume;
            var oldStackId = movement.WoodStackId;

            // Обновляем остатки в штабелях
            if (oldStackId != woodStackId)
            {
                var oldStack = await _context.WoodStacks.FindAsync(oldStackId);
                if (oldStack != null) oldStack.CurrentVolume -= oldVolume;
                newStack.CurrentVolume += newVolume;
            }
            else
            {
                newStack.CurrentVolume += newVolume - oldVolume;
            }

            // Обновляем движение (СОХРАНЯЕМ ВСЕ ПОЛЯ)
            movement.WoodStackId = woodStackId;
            movement.Volume = newVolume;
            movement.Length = length;
            movement.Width = width;
            movement.Height = height;
            movement.Coefficient = coefficient;
            movement.Supplier = supplier;
            movement.Comment = comment;


            await _context.SaveChangesAsync();
            await _auditService.LogAsync("Edit", "WoodMovement", movement.Id, $"Изменён приход: объём {newVolume:F2} м³");
            TempData["Success"] = $"Движение обновлено. Новый объём: {newVolume:F2} м³.";

            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var movement = await _context.WoodMovements
                .Include(m => m.WoodStack)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (movement == null) return NotFound();

            var stack = movement.WoodStack;
            if (stack != null)
            {
                // Возвращаем объём обратно в штабель
                stack.CurrentVolume -= movement.Volume;
                stack.UpdatedAt = DateTime.Now;
            }

            _context.WoodMovements.Remove(movement);
            await _context.SaveChangesAsync();
            await _auditService.LogAsync("Delete", "WoodMovement", id, $"Удалён приход ID: {id}");
            TempData["Success"] = "Движение удалено.";

            return RedirectToAction("Index");
        }

        // GET: Сформировать акт отгрузки
        [HttpGet]
        public async Task<IActionResult> GenerateAct(int id)
        {
            var movement = await _context.WoodMovements
                .Include(m => m.WoodStack)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (movement == null) return NotFound();

            await _auditService.LogAsync("Download", "Act", id, $"Скачан акт прихода: {movement.WoodStack?.WoodType}, объём: {movement.Volume:F2} м³");

            using (var document = new PdfDocument())
            {
                var page = document.AddPage();
                page.Width = 595;
                page.Height = 842;

                using (var gfx = XGraphics.FromPdfPage(page))
                {
                    var font = new XFont("Arial", 14);

                    double leftMargin = 56.7;
                    double rightMargin = 28.35;
                    double topMargin = 56.7;
                    double yPos = topMargin;
                    double pageWidth = page.Width - leftMargin - rightMargin;

                    // ЗАГОЛОВОК
                    gfx.DrawString($"АКТ ПРИХОДА № {movement.Id}", font, XBrushes.Black,
                        new XRect(leftMargin, yPos, pageWidth, 30), XStringFormats.TopCenter);
                    yPos += 35;

                    // ДАТА
                    gfx.DrawString($"от {movement.MovementDate:dd.MM.yyyy}", font, XBrushes.Black, leftMargin, yPos);
                    yPos += 20;

                    // ПОСТАВЩИК
                    string supplier = movement.Supplier ?? "Не указан";
                    gfx.DrawString($"Поставщик: {supplier}", font, XBrushes.Black, leftMargin, yPos);
                    yPos += 30;

                    // ТЕКСТ ПЕРЕД ТАБЛИЦЕЙ
                    gfx.DrawString("Принята следующая продукция:", font, XBrushes.Black, leftMargin, yPos);
                    yPos += 20;

                    // ТАБЛИЦА (3 колонки: №, Тип древесины, Объём)
                    double[] colWidths = { pageWidth * 0.15, pageWidth * 0.55, pageWidth * 0.3 };
                    double xPos = leftMargin;
                    double rowHeight = 22;

                    // Заголовки таблицы
                    string[] headers = { "№", "Тип древесины", "Объём (м³)" };
                    for (int i = 0; i < headers.Length; i++)
                    {
                        gfx.DrawRectangle(XPens.Black, xPos, yPos, colWidths[i], rowHeight);
                        gfx.DrawString(headers[i], font, XBrushes.Black,
                            new XRect(xPos + 5, yPos + 2, colWidths[i] - 10, rowHeight - 4), XStringFormats.TopLeft);
                        xPos += colWidths[i];
                    }
                    yPos += rowHeight;

                    // Данные таблицы (без Grade)
                    xPos = leftMargin;
                    string[] cells = { "1", movement.WoodStack?.WoodType ?? "-", movement.Volume.ToString("F2") };
                    for (int i = 0; i < cells.Length; i++)
                    {
                        gfx.DrawRectangle(XPens.Black, xPos, yPos, colWidths[i], rowHeight);
                        XStringFormat format = (i == 2) ? XStringFormats.TopRight : XStringFormats.TopLeft;
                        gfx.DrawString(cells[i], font, XBrushes.Black,
                            new XRect(xPos + 5, yPos + 2, colWidths[i] - 10, rowHeight - 4), format);
                        xPos += colWidths[i];
                    }
                    yPos += rowHeight;

                    // Итого
                    xPos = leftMargin;
                    gfx.DrawRectangle(XPens.Black, xPos, yPos, colWidths[0] + colWidths[1], rowHeight);
                    gfx.DrawString("Итого:", font, XBrushes.Black,
                        new XRect(xPos + colWidths[0] + colWidths[1] - 40, yPos + 2, 40, rowHeight - 4),
                        XStringFormats.TopRight);
                    xPos += colWidths[0] + colWidths[1];
                    gfx.DrawRectangle(XPens.Black, xPos, yPos, colWidths[2], rowHeight);
                    gfx.DrawString(movement.Volume.ToString("F2"), font, XBrushes.Black,
                        new XRect(xPos + 5, yPos + 2, colWidths[2] - 10, rowHeight - 4), XStringFormats.TopRight);
                    yPos += rowHeight + 20;

                    

                    // ПОДПИСИ
                    double halfWidth = pageWidth / 2;
                    gfx.DrawString("Сдал: _________________________", font, XBrushes.Black, leftMargin, yPos);
                    gfx.DrawString("Принял: _________________________", font, XBrushes.Black, leftMargin + halfWidth, yPos);
                    yPos += 25;

                    // ПЕЧАТИ
                    gfx.DrawString("М.П.", font, XBrushes.Black, leftMargin, yPos);
                    gfx.DrawString("М.П.", font, XBrushes.Black, leftMargin + halfWidth, yPos);
                }

                using (var stream = new MemoryStream())
                {
                    document.Save(stream, false);
                    byte[] fileContent = stream.ToArray();
                    return File(fileContent, "application/pdf", $"Акт_прихода_{movement.Id}.pdf");
                }
            }
        }


    }
}