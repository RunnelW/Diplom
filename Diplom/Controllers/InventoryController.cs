using Diplom.Data;
using Diplom.Models;
using Diplom.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Diplom.Controllers
{
    [Authorize(Roles = "Admin,Storekeeper")]
    public class InventoryController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<AppUser> _userManager;
        private readonly AuditService _auditService;

        public InventoryController(ApplicationDbContext context, UserManager<AppUser> userManager, AuditService auditService)
        {
            _context = context;
            _userManager = userManager;
            _auditService = auditService;
        }

        // GET: Список инвентаризаций
        public async Task<IActionResult> Index()
        {
            var inventories = await _context.Inventories
                .Include(i => i.CreatedByUser)
                .OrderByDescending(i => i.StartDate)
                .ToListAsync();
            return View(inventories);
        }

        // GET: Создание новой инвентаризации
        [HttpGet]
        public IActionResult Create()
        {
            ViewBag.InventoryTypes = new List<string> { "WoodStack", "FinishedGoods" };
            return View();
        }

        // POST: Создание инвентаризации
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string inventoryType, string? comment)
        {
            if (string.IsNullOrWhiteSpace(inventoryType))
            {
                TempData["Error"] = "Выберите тип инвентаризации.";
                return RedirectToAction("Create");
            }

            // Генерируем номер инвентаризации
            var today = DateTime.Now;
            var prefix = $"ИНВ-{today:yyyyMMdd}-";
            var lastInv = await _context.Inventories
                .Where(i => i.InventoryNumber.StartsWith(prefix))
                .OrderByDescending(i => i.InventoryNumber)
                .FirstOrDefaultAsync();

            int nextNumber = 1;
            if (lastInv != null)
            {
                var lastNumStr = lastInv.InventoryNumber.Substring(prefix.Length);
                if (int.TryParse(lastNumStr, out int lastNum))
                    nextNumber = lastNum + 1;
            }

            var inventory = new Inventory
            {
                InventoryNumber = prefix + nextNumber.ToString("D3"),
                InventoryType = inventoryType,
                StartDate = DateTime.Now,
                Status = "В процессе",
                Comment = comment,
                CreatedByUserId = _userManager.GetUserId(User)
            };

            _context.Inventories.Add(inventory);
            await _context.SaveChangesAsync();

            // Перенаправляем на страницу редактирования (ввода фактических остатков)
            return RedirectToAction("Edit", new { id = inventory.Id });
        }

        // GET: Редактирование инвентаризации (ввод фактических остатков)
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var inventory = await _context.Inventories
                .FirstOrDefaultAsync(i => i.Id == id);

            if (inventory == null) return NotFound();

            // Получаем текущие остатки
            List<InventoryItem> items;

            if (inventory.InventoryType == "WoodStack")
            {
                var stacks = await _context.WoodStacks.ToListAsync();
                items = stacks.Select(s => new InventoryItem
                {
                    InventoryId = id,
                    ItemType = "WoodStack",
                    ItemId = s.Id,
                    ItemName = s.WoodType,
                    AccountingVolume = s.CurrentVolume,
                    ActualVolume = s.CurrentVolume,
                    Difference = 0,
                    DifferenceType = "Нет"
                }).ToList();
            }
            else // FinishedGoods
            {
                var products = await _context.FinishedGoodsStocks.ToListAsync();
                items = products.Select(p => new InventoryItem
                {
                    InventoryId = id,
                    ItemType = "FinishedGoods",
                    ItemId = p.Id,
                    ItemName = $"{p.WoodType} ({p.SheetLength:F2}×{p.SheetWidth:F2} м)",
                    AccountingVolume = p.Volume,
                    ActualVolume = p.Volume,
                    Difference = 0,
                    DifferenceType = "Нет"
                }).ToList();
            }

            ViewBag.Inventory = inventory;
            return View(items);
        }

        // POST: Сохранение результатов инвентаризации
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveResults(int id, List<int> itemIds, List<decimal> actualVolumes)
        {
            var inventory = await _context.Inventories.FindAsync(id);
            if (inventory == null) return NotFound();

            if (itemIds == null || actualVolumes == null || itemIds.Count == 0)
            {
                TempData["Error"] = "Нет данных для сохранения.";
                return RedirectToAction("Edit", new { id });
            }

            // Удаляем старые записи для этой инвентаризации
            var existingItems = await _context.InventoryItems
                .Where(i => i.InventoryId == id)
                .ToListAsync();

            if (existingItems.Any())
            {
                _context.InventoryItems.RemoveRange(existingItems);
            }

            // Создаём новые записи
            for (int i = 0; i < itemIds.Count; i++)
            {
                if (i >= actualVolumes.Count) break;

                var itemId = itemIds[i];
                var actualVolume = actualVolumes[i];

                decimal accountingVolume = 0;
                string itemName = "";

                if (inventory.InventoryType == "WoodStack")
                {
                    var stack = await _context.WoodStacks.FindAsync(itemId);
                    if (stack != null)
                    {
                        accountingVolume = stack.CurrentVolume;
                        itemName = stack.WoodType;
                    }
                }
                else
                {
                    var product = await _context.FinishedGoodsStocks.FindAsync(itemId);
                    if (product != null)
                    {
                        accountingVolume = product.Volume;
                        itemName = $"{product.WoodType} ({product.SheetLength:F2}×{product.SheetWidth:F2} м)";
                    }
                }

                var difference = actualVolume - accountingVolume;
                var diffType = difference > 0 ? "Излишек" : (difference < 0 ? "Недостача" : "Нет");

                var newItem = new InventoryItem
                {
                    InventoryId = id,
                    ItemType = inventory.InventoryType,
                    ItemId = itemId,
                    ItemName = itemName,
                    AccountingVolume = accountingVolume,
                    ActualVolume = actualVolume,
                    Difference = difference,
                    DifferenceType = diffType
                };
                _context.InventoryItems.Add(newItem);
            }

            await _context.SaveChangesAsync();

            return RedirectToAction("Results", new { id });
        }

        // GET: Результаты инвентаризации
        [HttpGet]
        public async Task<IActionResult> Results(int id)
        {
            var inventory = await _context.Inventories
                .Include(i => i.CreatedByUser)
                .FirstOrDefaultAsync(i => i.Id == id);

            if (inventory == null) return NotFound();

            var items = await _context.InventoryItems
                .Where(i => i.InventoryId == id)
                .ToListAsync();

            ViewBag.Inventory = inventory;
            return View(items);
        }

        // POST: Завершение инвентаризации (применение расхождений)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Complete(int id)
        {
            var inventory = await _context.Inventories.FindAsync(id);
            if (inventory == null) return NotFound();

            var items = await _context.InventoryItems
                .Where(i => i.InventoryId == id)
                .ToListAsync();

            // Рассчитываем общее расхождение
            decimal totalDifference = 0;

            // Применяем расхождения к остаткам
            foreach (var item in items)
            {
                if (item.Difference != 0)
                {
                    totalDifference += item.Difference;

                    if (item.ItemType == "WoodStack")
                    {
                        var stack = await _context.WoodStacks.FindAsync(item.ItemId);
                        if (stack != null)
                        {
                            stack.CurrentVolume = item.ActualVolume;
                            stack.UpdatedAt = DateTime.Now;
                        }
                    }
                    else if (item.ItemType == "FinishedGoods")
                    {
                        var product = await _context.FinishedGoodsStocks.FindAsync(item.ItemId);
                        if (product != null)
                        {
                            product.Volume = item.ActualVolume;
                            product.UpdatedAt = DateTime.Now;
                        }
                    }
                }
            }

            inventory.Status = "Завершена";
            inventory.EndDate = DateTime.Now;

            await _context.SaveChangesAsync();

            // ЛОГИРОВАНИЕ
            await _auditService.LogAsync("Complete", "Inventory", inventory.Id,
                $"Завершена инвентаризация №{inventory.InventoryNumber}, общее расхождение: {totalDifference:F2} м³");

            TempData["Success"] = $"Инвентаризация №{inventory.InventoryNumber} завершена. Расхождения применены.";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var inventory = await _context.Inventories
                .Include(i => i.Items)  // ← Теперь должно работать
                .FirstOrDefaultAsync(i => i.Id == id);

            if (inventory == null) return NotFound();

            // Если инвентаризация завершена, возвращаем старые остатки
            if (inventory.Status == "Завершена")
            {
                foreach (var item in inventory.Items)
                {
                    if (item.Difference != 0)
                    {
                        if (item.ItemType == "WoodStack")
                        {
                            var stack = await _context.WoodStacks.FindAsync(item.ItemId);
                            if (stack != null)
                            {
                                stack.CurrentVolume = item.AccountingVolume;
                                stack.UpdatedAt = DateTime.Now;
                            }
                        }
                        else if (item.ItemType == "FinishedGoods")
                        {
                            var product = await _context.FinishedGoodsStocks.FindAsync(item.ItemId);
                            if (product != null)
                            {
                                product.Volume = item.AccountingVolume;
                                product.UpdatedAt = DateTime.Now;
                            }
                        }
                    }
                }
                await _context.SaveChangesAsync();
            }

            // Удаляем все позиции инвентаризации
            _context.InventoryItems.RemoveRange(inventory.Items);
            // Удаляем саму инвентаризацию
            _context.Inventories.Remove(inventory);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"Инвентаризация №{inventory.InventoryNumber} удалена, остатки восстановлены.";
            return RedirectToAction("Index");
        }


    }
}
