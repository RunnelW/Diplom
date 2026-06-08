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
using System.Text;
using System.Text.RegularExpressions;
using Xceed.Document.NET;
using Xceed.Words.NET;



namespace Diplom.Controllers
{
    [Authorize(Roles = "Admin,Storekeeper")]
    public class ShipmentController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<AppUser> _userManager;
        private readonly AuditService _auditService;


        public ShipmentController(ApplicationDbContext context, UserManager<AppUser> userManager, AuditService auditService)
        {
            _context = context;
            _userManager = userManager;
            _auditService = auditService;


        }

        // GET: Список отгрузок
        public async Task<IActionResult> Index()
        {
            var orders = await _context.ShipmentOrders
                .Include(o => o.Items)
                .ThenInclude(i => i.FinishedGoodsStock)
                .Include(o => o.CreatedByUser)
                .OrderByDescending(o => o.ShipmentDate)
                .ToListAsync();
            return View(orders);
        }

        // GET: Создание новой отгрузки
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            ViewBag.Products = await _context.FinishedGoodsStocks
                .Where(p => p.Volume > 0)  // ← только продукция с остатком
                .OrderBy(p => p.WoodType)
                .ToListAsync();

            // Генерация номера накладной: Н-ГГГГММДД-XXX
            var today = DateTime.Now;
            var prefix = $"Н-{today:yyyyMMdd}-";
            var lastOrder = _context.ShipmentOrders
                .Where(o => o.InvoiceNumber.StartsWith(prefix))
                .OrderByDescending(o => o.InvoiceNumber)
                .FirstOrDefault();

            int nextNumber = 1;
            if (lastOrder != null)
            {
                var lastNumStr = lastOrder.InvoiceNumber.Substring(prefix.Length);
                if (int.TryParse(lastNumStr, out int lastNum))
                    nextNumber = lastNum + 1;
            }

            ViewBag.GeneratedInvoiceNumber = prefix + nextNumber.ToString("D3");
            return View();
        }

        // POST: Сохранение отгрузки
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string invoiceNumber, string customer, DateTime shipmentDate, string? comment)
        {
            // Получаем данные из формы вручную (более надёжный способ)
            var form = Request.Form;

            var productIds = new List<int>();
            var volumes = new List<decimal>();

            // Собираем все productIds и volumes из формы
            foreach (var key in form.Keys)
            {
                if (key.StartsWith("productIds["))
                {
                    if (int.TryParse(form[key], out int productId) && productId > 0)
                    {
                        productIds.Add(productId);
                    }
                }
                else if (key.StartsWith("volumes["))
                {
                    var volumeStr = form[key].ToString().Replace(',', '.');
                    if (decimal.TryParse(volumeStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal volume) && volume > 0)
                    {
                        volumes.Add(volume);
                    }
                }
            }

            // Проверка
            if (productIds.Count == 0 || volumes.Count == 0 || productIds.Count != volumes.Count)
            {
                TempData["Error"] = "Добавьте хотя бы одну позицию с корректными данными.";
                return RedirectToAction("Create");
            }

            var items = new List<ShipmentItem>();
            decimal totalVolume = 0;

            for (int i = 0; i < productIds.Count; i++)
            {
                // Проверяем, что индекс существует в обоих списках
                if (i >= volumes.Count) break;

                if (productIds[i] > 0 && volumes[i] > 0)
                {
                    var product = await _context.FinishedGoodsStocks.FindAsync(productIds[i]);
                    if (product == null) continue;

                    if (volumes[i] > product.Volume + 0.01m)
                    {
                        TempData["Error"] = $"Недостаточно {product.WoodType} ({product.SheetLength:F2}×{product.SheetWidth:F2} м). Доступно: {product.Volume:F2} м³.";
                        return RedirectToAction("Create");
                    }

                    items.Add(new ShipmentItem
                    {
                        FinishedGoodsStockId = productIds[i],
                        Volume = volumes[i]
                    });
                    totalVolume += volumes[i];
                }
            }

            if (!items.Any())
            {
                TempData["Error"] = "Добавьте хотя бы одну позицию для отгрузки.";
                return RedirectToAction("Create");
            }

            // Создаём заказ
            var order = new ShipmentOrder
            {
                InvoiceNumber = invoiceNumber,
                Customer = customer,
                ShipmentDate = shipmentDate,
                Comment = comment,
                CreatedByUserId = _userManager.GetUserId(User),
                Items = items
            };

            _context.ShipmentOrders.Add(order);

            // Списываем остатки
            foreach (var item in items)
            {
                var product = await _context.FinishedGoodsStocks.FindAsync(item.FinishedGoodsStockId);
                if (product != null)
                {
                    product.Volume -= item.Volume;
                    product.UpdatedAt = DateTime.Now;
                }
            }

            await _context.SaveChangesAsync();
            await _auditService.LogAsync("Create", "Shipment", order.Id, $"Отгрузка: {invoiceNumber}, покупатель: {customer}, объём: {totalVolume:F2} м³");

            TempData["Success"] = $"Отгрузка #{invoiceNumber} создана. Всего отгружено: {totalVolume:F2} м³.";
            return RedirectToAction("Index");
        }

        // POST: Удаление отгрузки
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var order = await _context.ShipmentOrders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null) return NotFound();

            // Возвращаем остатки на склад
            foreach (var item in order.Items)
            {
                var product = await _context.FinishedGoodsStocks.FindAsync(item.FinishedGoodsStockId);
                if (product != null)
                {
                    product.Volume += item.Volume;
                    product.UpdatedAt = DateTime.Now;
                }
            }

            _context.ShipmentOrders.Remove(order);
            await _context.SaveChangesAsync();
            await _auditService.LogAsync("Delete", "Shipment", id, $"Удалена отгрузка ID: {id}");


            TempData["Success"] = $"Отгрузка #{order.InvoiceNumber} удалена, остатки возвращены на склад.";
            return RedirectToAction("Index");
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var order = await _context.ShipmentOrders
                .Include(o => o.Items)
                    .ThenInclude(i => i.FinishedGoodsStock)
                .Include(o => o.CreatedByUser)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null) return NotFound();

            return View(order);
        }

        // GET: Редактирование отгрузки
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var order = await _context.ShipmentOrders
                .Include(o => o.Items)
                    .ThenInclude(i => i.FinishedGoodsStock)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null) return NotFound();

            // Получаем ВСЕ продукты со склада
            var allProducts = await _context.FinishedGoodsStocks.ToListAsync();

            // Для каждого продукта считаем доступный объём (остаток + объём из текущей отгрузки)
            var productsWithAvailable = allProducts.Select(p => new
            {
                p.Id,
                p.WoodType,
                p.SheetLength,
                p.SheetWidth,
                p.Volume,
                // Доступный объём = текущий остаток + объём этой позиции в отгрузке (если есть)
                AvailableVolume = p.Volume + (order.Items.FirstOrDefault(i => i.FinishedGoodsStockId == p.Id)?.Volume ?? 0)
            }).ToList();

            ViewBag.Products = productsWithAvailable;

            return View(order);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, IFormCollection form)
        {
            var order = await _context.ShipmentOrders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null) return NotFound();

            // Получаем данные из формы
            string invoiceNumber = form["invoiceNumber"];
            string customer = form["customer"];
            DateTime shipmentDate = DateTime.TryParse(form["shipmentDate"], out var date) ? date : DateTime.Now;
            string comment = form["comment"];

            // Получаем позиции
            var productIds = new List<int>();
            var volumes = new List<decimal>();

            foreach (var key in form.Keys)
            {
                if (key.StartsWith("productIds["))
                {
                    if (int.TryParse(form[key], out int productId) && productId > 0)
                    {
                        productIds.Add(productId);
                    }
                }
                else if (key.StartsWith("volumes["))
                {
                    var volumeStr = form[key].ToString().Replace(',', '.');
                    if (decimal.TryParse(volumeStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal volume) && volume > 0)
                    {
                        volumes.Add(volume);
                    }
                }
            }

            if (productIds.Count == 0 || volumes.Count == 0)
            {
                TempData["Error"] = "Добавьте хотя бы одну позицию";
                return RedirectToAction("Edit", new { id });
            }

            // Возвращаем старые остатки на склад
            foreach (var item in order.Items)
            {
                var product = await _context.FinishedGoodsStocks.FindAsync(item.FinishedGoodsStockId);
                if (product != null)
                {
                    product.Volume += item.Volume;
                }
            }

            // Удаляем старые позиции
            _context.ShipmentItems.RemoveRange(order.Items);

            // Создаём новые позиции и списываем новые остатки
            var items = new List<ShipmentItem>();
            decimal totalVolume = 0;

            for (int i = 0; i < productIds.Count; i++)
            {
                if (i >= volumes.Count) break;
                if (productIds[i] > 0 && volumes[i] > 0)
                {
                    var product = await _context.FinishedGoodsStocks.FindAsync(productIds[i]);
                    if (product == null) continue;

                    if (volumes[i] > product.Volume + 0.01m)
                    {
                        TempData["Error"] = $"Недостаточно продукции на складе. Доступно: {product.Volume:F2} м³.";
                        return RedirectToAction("Edit", new { id });
                    }

                    items.Add(new ShipmentItem
                    {
                        FinishedGoodsStockId = productIds[i],
                        Volume = volumes[i]
                    });
                    totalVolume += volumes[i];
                    product.Volume -= volumes[i];
                }
            }

            // Обновляем заказ
            order.InvoiceNumber = invoiceNumber;
            order.Customer = customer;
            order.ShipmentDate = shipmentDate;
            order.Comment = comment;
            order.Items = items;

            await _context.SaveChangesAsync();
            await _auditService.LogAsync("Edit", "Shipment", order.Id, $"Изменена отгрузка: {invoiceNumber}");

            TempData["Success"] = $"Отгрузка #{invoiceNumber} обновлена. Всего: {totalVolume:F2} м³.";
            return RedirectToAction("Index");
        }

        [HttpGet]
        [HttpGet]
        public async Task<IActionResult> GenerateAct(int id)
        {
            var order = await _context.ShipmentOrders
                .Include(o => o.Items)
                    .ThenInclude(i => i.FinishedGoodsStock)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null) return NotFound();

            var shipmentTotalVolume = order.Items.Sum(i => i.Volume);
            await _auditService.LogAsync("Download", "Act", id, $"Скачан акт отгрузки: накладная {order.InvoiceNumber}, покупатель: {order.Customer}, объём: {shipmentTotalVolume:F2} м³");

            // Создаём PDF-документ
            using (var document = new PdfDocument())
            {
                var page = document.AddPage();
                page.Width = 595;
                page.Height = 842;

                using (var gfx = XGraphics.FromPdfPage(page))
                {
                    // Используем стандартный шрифт (есть везде)
                    var font = new XFont("Arial", 14);

                    double leftMargin = 56.7;
                    double rightMargin = 28.35;
                    double topMargin = 56.7;
                    double yPos = topMargin;
                    double pageWidth = page.Width - leftMargin - rightMargin;

                    // ЗАГОЛОВОК
                    gfx.DrawString($"АКТ ОТГРУЗКИ № {order.InvoiceNumber}", font, XBrushes.Black,
                        new XRect(leftMargin, yPos, pageWidth, 30), XStringFormats.TopCenter);
                    yPos += 35;

                    // ДАТА И ПОКУПАТЕЛЬ
                    gfx.DrawString($"от {order.ShipmentDate:dd.MM.yyyy}", font, XBrushes.Black, leftMargin, yPos);
                    yPos += 20;
                    gfx.DrawString($"Покупатель: {order.Customer}", font, XBrushes.Black, leftMargin, yPos);
                    yPos += 30;

                    // ТЕКСТ ПЕРЕД ТАБЛИЦЕЙ
                    gfx.DrawString("Отгружена следующая продукция:", font, XBrushes.Black, leftMargin, yPos);
                    yPos += 20;

                    // ТАБЛИЦА
                    double[] colWidths = { pageWidth * 0.1, pageWidth * 0.4, pageWidth * 0.35, pageWidth * 0.15 };
                    double xPos = leftMargin;
                    double rowHeight = 22;

                    // Заголовки таблицы
                    string[] headers = { "№", "Тип древесины", "Размер листа (м)", "Объём (м³)" };
                    for (int i = 0; i < headers.Length; i++)
                    {
                        gfx.DrawRectangle(XPens.Black, xPos, yPos, colWidths[i], rowHeight);
                        gfx.DrawString(headers[i], font, XBrushes.Black,
                            new XRect(xPos + 5, yPos + 2, colWidths[i] - 10, rowHeight - 4), XStringFormats.TopLeft);
                        xPos += colWidths[i];
                    }
                    yPos += rowHeight;

                    // Данные таблицы
                    int rowNum = 1;
                    decimal totalVolume = 0;
                    foreach (var item in order.Items)
                    {
                        var woodType = item.FinishedGoodsStock?.WoodType ?? "-";
                        var sheetLength = item.FinishedGoodsStock?.SheetLength ?? 0;
                        var sheetWidth = item.FinishedGoodsStock?.SheetWidth ?? 0;
                        var sheetSize = $"{sheetLength:F2}×{sheetWidth:F2}";
                        var volume = item.Volume;
                        totalVolume += volume;

                        xPos = leftMargin;
                        string[] cells = { rowNum.ToString(), woodType, sheetSize, volume.ToString("F2") };
                        for (int i = 0; i < cells.Length; i++)
                        {
                            gfx.DrawRectangle(XPens.Black, xPos, yPos, colWidths[i], rowHeight);
                            gfx.DrawString(cells[i], font, XBrushes.Black,
                                new XRect(xPos + 5, yPos + 2, colWidths[i] - 10, rowHeight - 4),
                                i == 3 ? XStringFormats.TopRight : XStringFormats.TopLeft);
                            xPos += colWidths[i];
                        }
                        yPos += rowHeight;
                        rowNum++;
                    }

                    // Итого
                    xPos = leftMargin;
                    gfx.DrawRectangle(XPens.Black, xPos, yPos, colWidths[0] + colWidths[1] + colWidths[2], rowHeight);
                    gfx.DrawString("Итого:", font, XBrushes.Black,
                        new XRect(xPos + colWidths[0] + colWidths[1] + colWidths[2] - 40, yPos + 2, 40, rowHeight - 4),
                        XStringFormats.TopRight);
                    xPos += colWidths[0] + colWidths[1] + colWidths[2];
                    gfx.DrawRectangle(XPens.Black, xPos, yPos, colWidths[3], rowHeight);
                    gfx.DrawString(totalVolume.ToString("F2"), font, XBrushes.Black,
                        new XRect(xPos + 5, yPos + 2, colWidths[3] - 10, rowHeight - 4), XStringFormats.TopRight);
                    yPos += rowHeight + 20;
                   

                    // ПОДПИСИ (на одной строке)
                    double halfWidth = pageWidth / 2;
                    gfx.DrawString("Сдал: _________________________", font, XBrushes.Black, leftMargin, yPos);
                    gfx.DrawString("Принял: _________________________", font, XBrushes.Black, leftMargin + halfWidth, yPos);
                    yPos += 25;

                    // ПЕЧАТИ (на одной строке)
                    gfx.DrawString("М.П.", font, XBrushes.Black, leftMargin, yPos);
                    gfx.DrawString("М.П.", font, XBrushes.Black, leftMargin + halfWidth, yPos);
                }

                using (var stream = new MemoryStream())
                {
                    document.Save(stream, false);
                    byte[] fileContent = stream.ToArray();
                    return File(fileContent, "application/pdf", $"Акт_отгрузки_{order.InvoiceNumber}.pdf");
                }
            }
        }

    }
}
