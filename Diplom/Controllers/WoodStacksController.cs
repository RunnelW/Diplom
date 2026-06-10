using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Diplom.Data;
using Diplom.Models;

namespace Diplom.Controllers
{
    [Authorize]
    public class WoodStacksController : Controller
    {
        private readonly ApplicationDbContext _context;

        public WoodStacksController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var stacks = await _context.WoodStacks
                .Where(s => s.CurrentVolume > 0)  // ← добавляем фильтр
                .OrderBy(s => s.WoodType)
                .ToListAsync();

            return View(stacks);
        }
    }
    
}