using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Diplom.Data;
using Diplom.Models;

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
            var stocks = await _context.FinishedGoodsStocks
                .Where(s => s.Volume > 0)  
                .OrderBy(s => s.WoodType)
                .ThenBy(s => s.SheetLength)
                .ThenBy(s => s.SheetWidth)
                .ToListAsync();
            return View(stocks);
        }

    }
}