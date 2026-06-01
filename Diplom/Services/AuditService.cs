using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Diplom.Data;
using Diplom.Models;
using System.Security.Claims;

namespace Diplom.Services
{
    public class AuditService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AuditService(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task LogAsync(string action, string entity, int? entityId = null, string? details = null)
        {
            var user = _httpContextAccessor.HttpContext?.User;
            var userId = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userName = user?.Identity?.Name ?? "System";
            var ipAddress = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

            var log = new AuditLog
            {
                UserId = userId,
                UserName = userName,
                Action = action,
                Entity = entity,
                EntityId = entityId,
                Details = details,
                IpAddress = ipAddress,
                Timestamp = DateTime.Now
            };

            _context.AuditLogs.Add(log);
            await _context.SaveChangesAsync();
        }
    }
}
