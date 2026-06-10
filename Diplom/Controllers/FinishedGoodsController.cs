using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Diplom.Data;
using Diplom.Models;
using static Diplom.Controllers.ReportsController;

namespace Diplom.Controllers
{
    [Authorize]
    public class FinishedGoodsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public FinishedGoodsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Список готовой продукции на складе
        public async Task<IActionResult> Index()
        {
            // Та же логика что и в Reports/FinishedGoodsStock
            var production = await _context.ProductionBatches
                .Include(p => p.WoodStack)
                .ToListAsync();

            var shipments = await _context.ShipmentItems
                .Include(s => s.ShipmentOrder)
                .Include(s => s.FinishedGoodsStock)
                .ToListAsync();

            var summary = production
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

            foreach (var s in summary)
                s.RemainingVolume = s.ProducedVolume - s.ShippedVolume;

            ViewBag.TotalProduced  = summary.Sum(s => s.ProducedVolume);
            ViewBag.TotalShipped   = summary.Sum(s => s.ShippedVolume);
            ViewBag.TotalRemaining = summary.Sum(s => s.RemainingVolume);

            // Детали по листам (размер, местоположение) — только для карточек
            var stocks = await _context.FinishedGoodsStocks
                .Where(s => s.Volume > 0)
                .OrderBy(s => s.WoodType)
                .ThenBy(s => s.SheetLength)
                .ThenBy(s => s.SheetWidth)
                .ToListAsync();

            ViewBag.Summary = summary;
            return View(stocks);
        }

    }
}