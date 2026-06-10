using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Diplom.Data;
using Diplom.Models;

namespace Diplom.Controllers
{
    [Authorize(Roles = "Admin,Director,Manager")]
    public class ReportsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ReportsController(ApplicationDbContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            return View();
        }

        // =====================================================
        // 1. ОСТАТКИ СЫРЬЯ
        // =====================================================
        public async Task<IActionResult> Stock(DateTime? date)
        {
            var targetDate = date ?? DateTime.Now;

            // Получаем все движения до указанной даты
            var movements = await _context.WoodMovements
                .Include(m => m.WoodStack)
                .Where(m => m.MovementDate <= targetDate)
                .ToListAsync();

            // Группируем по штабелям и считаем остатки (Income - Outcome)
            var stocks = movements
                .GroupBy(m => m.WoodStack)
                .Select(g => new StockReportData
                {
                    WoodType = g.Key?.WoodType ?? "Неизвестно",
                    CurrentVolume = g.Sum(m => m.MovementType == "Income" ? m.Volume : -m.Volume),
                    LastUpdate = targetDate
                })
                .Where(s => s.CurrentVolume != 0)  // 🔥 Убираем нулевые остатки
                .OrderByDescending(s => s.CurrentVolume)
                .ToList();

            ViewBag.TargetDate = targetDate.ToString("dd.MM.yyyy");
            ViewBag.TotalVolume = stocks.Sum(s => s.CurrentVolume);
            ViewBag.IsHistorical = date.HasValue;

            return View(stocks);
        }

        // =====================================================
        // 2. ДИНАМИКА ОТГРУЗОК
        // =====================================================
        public async Task<IActionResult> Shipments(int? year)
        {
            var query = _context.ShipmentOrders
                .Include(o => o.Items)
                .AsQueryable();

            if (year.HasValue && year.Value > 2000)
            {
                query = query.Where(o => o.ShipmentDate.Year == year.Value);
            }

            var shipments = await query
                .Select(o => new { o.ShipmentDate, o.Items })
                .ToListAsync();

            var monthlyData = shipments
                .GroupBy(o => new { o.ShipmentDate.Year, o.ShipmentDate.Month })
                .Select(g => new MonthlyReportData
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    TotalVolume = g.Sum(o => o.Items.Sum(i => i.Volume))
                })
                .OrderBy(d => d.Year).ThenBy(d => d.Month)
                .ToList();

            ViewBag.Years = await _context.ShipmentOrders
                .Select(o => o.ShipmentDate.Year)
                .Distinct()
                .OrderByDescending(y => y)
                .ToListAsync();

            ViewBag.SelectedYear = year ?? DateTime.Now.Year;

            return View(monthlyData);
        }

        // =====================================================
        // 3. ЭФФЕКТИВНОСТЬ ПРОИЗВОДСТВА
        // =====================================================
        public async Task<IActionResult> Efficiency(int? year)
        {
            var query = _context.ProductionBatches
                .Include(p => p.WoodStack)
                .AsQueryable();

            if (year.HasValue && year.Value > 2000)
            {
                query = query.Where(p => p.ProductionDate.Year == year.Value);
            }

            var data = await query
                .GroupBy(p => p.WoodStack.WoodType)
                .Select(g => new EfficiencyReportData
                {
                    WoodType = g.Key,
                    TotalRaw = g.Sum(p => p.RawVolume),
                    TotalVeneer = g.Sum(p => p.VeneerVolume),
                    AvgEfficiency = g.Average(p => p.Efficiency),
                    BatchCount = g.Count()
                })
                .OrderByDescending(d => d.AvgEfficiency)
                .ToListAsync();

            ViewBag.Years = await _context.ProductionBatches
                .Select(p => p.ProductionDate.Year)
                .Distinct()
                .OrderByDescending(y => y)
                .ToListAsync();

            ViewBag.SelectedYear = year ?? DateTime.Now.Year;
            ViewBag.OverallEfficiency = data.Any() ? data.Average(d => d.AvgEfficiency) : 0;

            return View(data);
        }

        // =====================================================
        // 4. ДВИЖЕНИЯ ПО ШТАБЕЛЯМ
        // =====================================================
        public async Task<IActionResult> Movements(DateTime? startDate, DateTime? endDate, string? woodType)
        {
            var query = _context.WoodMovements
                .Include(m => m.WoodStack)
                .Include(m => m.CreatedByUser)
                .AsQueryable();

            if (startDate.HasValue)
                query = query.Where(m => m.MovementDate >= startDate.Value);
            if (endDate.HasValue)
                query = query.Where(m => m.MovementDate <= endDate.Value.AddDays(1));
            if (!string.IsNullOrEmpty(woodType))
                query = query.Where(m => m.WoodStack.WoodType == woodType);

            var movements = await query
                .OrderByDescending(m => m.MovementDate)
                .ToListAsync();

            ViewBag.WoodTypes = await _context.WoodMovements
    .Include(m => m.WoodStack)
    .Where(m => m.WoodStack != null && m.WoodStack.CurrentVolume > 0)
    .Select(m => m.WoodStack.WoodType)
    .Distinct()
    .ToListAsync();

            ViewBag.StartDate = startDate?.ToString("yyyy-MM-dd") ?? DateTime.Now.AddMonths(-1).ToString("yyyy-MM-dd");
            ViewBag.EndDate = endDate?.ToString("yyyy-MM-dd") ?? DateTime.Now.ToString("yyyy-MM-dd");
            ViewBag.SelectedWoodType = woodType;

            return View(movements);
        }

        // =====================================================
        // 5. РЕЙТИНГ ПОКУПАТЕЛЕЙ
        // =====================================================
        public async Task<IActionResult> TopCustomers()
        {
            // Сначала получаем все отгрузки с их позициями
            var shipments = await _context.ShipmentOrders
                .Include(o => o.Items)
                .ToListAsync();

            // Группируем в памяти (LINQ to Objects)
            var customers = shipments
                .GroupBy(o => o.Customer)
                .Select(g => new CustomerReportData
                {
                    CustomerName = g.Key,
                    TotalVolume = g.Sum(o => o.Items.Sum(i => i.Volume)),
                    OrderCount = g.Count(),
                    LastOrderDate = g.Max(o => o.ShipmentDate)
                })
                .OrderByDescending(c => c.TotalVolume)
                .Take(10)
                .ToList();

            ViewBag.TotalVolume = customers.Sum(c => c.TotalVolume);
            return View(customers);
        }

        // =====================================================
        // ОСТАТКИ ГОТОВОЙ ПРОДУКЦИИ НА ДАТУ
        // =====================================================
        public async Task<IActionResult> FinishedGoodsStock(DateTime? date)
        {
            var targetDate = date ?? DateTime.Now;

            // Получаем производство (приход готовой продукции) до указанной даты
            var production = await _context.ProductionBatches
                .Include(p => p.WoodStack)
                .Where(p => p.ProductionDate <= targetDate)
                .ToListAsync();

            // Получаем отгрузки (расход готовой продукции) до указанной даты
            var shipments = await _context.ShipmentItems
                .Include(s => s.ShipmentOrder)
                .Include(s => s.FinishedGoodsStock)
                .Where(s => s.ShipmentOrder.ShipmentDate <= targetDate)
                .ToListAsync();

            // Считаем остатки по типам древесины
            var stocks = production
                .GroupBy(p => p.WoodStack.WoodType)
                .Select(g => new FinishedGoodsStockReportData
                {
                    WoodType = g.Key,
                    ProducedVolume = g.Sum(p => p.VeneerVolume),
                    ShippedVolume = shipments
                        .Where(s => s.FinishedGoodsStock?.WoodType == g.Key)
                        .Sum(s => s.Volume),
                    BatchCount = g.Count()
                })
                .Where(s => s.ProducedVolume > 0 || s.ShippedVolume > 0)
                .OrderBy(s => s.WoodType)
                .ToList();

            foreach (var stock in stocks)
            {
                stock.RemainingVolume = stock.ProducedVolume - stock.ShippedVolume;
            }

            ViewBag.TargetDate = targetDate.ToString("dd.MM.yyyy");
            ViewBag.TotalRemaining = stocks.Sum(s => s.RemainingVolume);
            ViewBag.TotalProduced = stocks.Sum(s => s.ProducedVolume);
            ViewBag.TotalShipped = stocks.Sum(s => s.ShippedVolume);
            ViewBag.IsHistorical = date.HasValue;

            return View(stocks);
        }

    }






    // Вспомогательные классы
    public class MonthlyReportData
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public decimal TotalVolume { get; set; }
        public string MonthName => new DateTime(Year, Month, 1).ToString("MMMM yyyy");
    }

    public class EfficiencyReportData
    {
        public string WoodType { get; set; }
        public decimal TotalRaw { get; set; }
        public decimal TotalVeneer { get; set; }
        public decimal AvgEfficiency { get; set; }
        public int BatchCount { get; set; }
        public decimal WasteVolume => TotalRaw - TotalVeneer;
    }

    public class CustomerReportData
    {
        public string CustomerName { get; set; }
        public decimal TotalVolume { get; set; }
        public int OrderCount { get; set; }
        public DateTime LastOrderDate { get; set; }
    }

    public class StockReportData
    {
        public string WoodType { get; set; } = string.Empty;
        public decimal CurrentVolume { get; set; }
        public DateTime LastUpdate { get; set; }
    }

    public class FinishedGoodsStockReportData
    {
        public string WoodType { get; set; } = string.Empty;
        public decimal ProducedVolume { get; set; }
        public decimal ShippedVolume { get; set; }
        public decimal RemainingVolume { get; set; }
        public int BatchCount { get; set; }
    }
}

