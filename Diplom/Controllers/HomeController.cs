using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Diplom.Data;
using Diplom.Models;
using Microsoft.AspNetCore.Authorization;

namespace Diplom.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;

        public HomeController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            // Пересчитываем остатки из движений (эталонный способ)
            var stacks = await _context.WoodStacks
                .Select(s => new WoodStack
                {
                    Id = s.Id,
                    WoodType = s.WoodType,
                    CurrentVolume = _context.WoodMovements
                        .Where(wm => wm.WoodStackId == s.Id)
                        .Sum(wm => wm.MovementType == "Income" ? wm.Volume : -wm.Volume),
                    UpdatedAt = DateTime.Now
                })
                .Where(s => s.CurrentVolume > 0)
                .ToListAsync();

            // Обновляем поле CurrentVolume в таблице WoodStacks (для синхронизации)
            foreach (var stack in stacks)
            {
                var dbStack = await _context.WoodStacks.FindAsync(stack.Id);
                if (dbStack != null && dbStack.CurrentVolume != stack.CurrentVolume)
                {
                    dbStack.CurrentVolume = stack.CurrentVolume;
                    dbStack.UpdatedAt = DateTime.Now;
                }
            }
            await _context.SaveChangesAsync();

            // 🔥 ПОСЛЕДНИЕ ДВИЖЕНИЯ (добавляем обратно)
            var recentMovements = await _context.WoodMovements
                .Include(m => m.WoodStack)
                .Include(m => m.CreatedByUser)
                .OrderByDescending(m => m.MovementDate)
                .Take(10)
                .ToListAsync();

            // 🔥 ПРИХОД ЗА СЕГОДНЯ (добавляем обратно)
            var today = DateTime.Today;
            var todayIncome = await _context.WoodMovements
                .Where(m => m.MovementType == "Income" && m.MovementDate.Date == today)
                .SumAsync(m => (decimal?)m.Volume) ?? 0;

            ViewBag.RecentMovements = recentMovements;
            ViewBag.TodayIncome = todayIncome;

            return View(stacks);
        }
    }
}