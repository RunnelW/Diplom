using Diplom.Data;
using Diplom.Models;
using Diplom.Services;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Diplom.Controllers
{
    [Authorize(Roles = "Admin")]
    public class UsersController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<AppUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly AuditService _auditService;

        public UsersController(ApplicationDbContext context, UserManager<AppUser> userManager, RoleManager<IdentityRole> roleManager, AuditService auditService)
        {
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
            _auditService = auditService;
        }

        public async Task<IActionResult> Index()
        {
            var users = await _userManager.Users.ToListAsync();
            return View(users);
        }

        [HttpGet]
        public IActionResult Create()
        {
            ViewBag.Roles = _roleManager.Roles.Select(r => r.Name).ToList();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string email, string fullName, string position, string role)
        {
            // Проверка обязательных полей
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(position) || string.IsNullOrWhiteSpace(role))
            {
                TempData["Error"] = "Все поля обязательны для заполнения.";
                return RedirectToAction("Create");
            }

            // Проверка уникальности email
            var existing = await _userManager.FindByEmailAsync(email);
            if (existing != null)
            {
                TempData["Error"] = "Пользователь с таким email уже существует.";
                return RedirectToAction("Create");
            }

            // Генерируем случайный пароль (12 символов)
            var generatedPassword = GenerateRandomPassword(12);

            // Создаём пользователя
            var user = new AppUser
            {
                UserName = email,
                Email = email,
                FullName = fullName,
                Position = position
            };

            var result = await _userManager.CreateAsync(user, generatedPassword);          

            if (result.Succeeded)
            {
                // Назначаем роль
                if (!string.IsNullOrEmpty(role) && await _roleManager.RoleExistsAsync(role))
                {
                    await _userManager.AddToRoleAsync(user, role);
                }

                // Показываем сгенерированный пароль
                await _auditService.LogAsync("Create", "User", null, $"Создан пользователь: {email}, роль: {role}");
                TempData["Success"] = $"✅ Пользователь {email} создан!";
                TempData["GeneratedPassword"] = generatedPassword;


                return RedirectToAction("Index");
            }
            else
            {
                var errors = string.Join("; ", result.Errors.Select(e => e.Description));
                TempData["Error"] = $"Ошибка: {errors}";
                return RedirectToAction("Create");
            }

            
        }

        [HttpGet]
        public async Task<IActionResult> DeleteUser(string id)
        {
            if (string.IsNullOrEmpty(id))
                return RedirectToAction("Index");

            var user = await _userManager.FindByIdAsync(id);
            if (user != null && user.Email != "admin@plywood.local")
            {
                await _userManager.DeleteAsync(user);
                TempData["Success"] = "Пользователь удалён.";
            }
            else
            {
                TempData["Error"] = "Нельзя удалить администратора.";
            }
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                TempData["Error"] = "Пользователь не найден.";
                return RedirectToAction("Index");
            }

            var userEmail = user.Email;

            if (userEmail == "admin@plywood.local")
            {
                TempData["Error"] = "Нельзя удалить главного администратора.";
                return RedirectToAction("Index");
            }

            var result = await _userManager.DeleteAsync(user);

            if (result.Succeeded)
            {
                await _auditService.LogAsync("Delete", "User", null, $"Удалён пользователь: {userEmail}");
                TempData["Success"] = $"Пользователь {userEmail} удалён.";
            }
            else
            {
                var errors = string.Join("; ", result.Errors.Select(e => e.Description));
                TempData["Error"] = $"Ошибка при удалении: {errors}";
            }

            return RedirectToAction("Index");
        }

        private string GenerateRandomPassword(int length = 12)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, length)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }
    }
}