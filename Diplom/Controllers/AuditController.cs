using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Diplom.Data;
using Diplom.Models;

namespace Diplom.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AuditController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AuditController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Журнал действий (без фильтров)
        public async Task<IActionResult> Index()
        {
            var logs = await _context.AuditLogs
                .OrderByDescending(l => l.Timestamp)
                .Take(500)
                .ToListAsync();

            System.Diagnostics.Debug.WriteLine($"=== AuditLogs Count: {logs.Count} ===");

            return View(logs);
        }
    }
}