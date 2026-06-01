using Diplom.Models;
using Diplom.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Diplom.Controllers
{
    public class AccountController : Controller
    {

        public IActionResult AccessDenied()
        {
            return View();
        }

        private readonly SignInManager<AppUser> _signInManager;
        private readonly UserManager<AppUser> _userManager;
        private readonly AuditService _auditService;

        public AccountController(SignInManager<AppUser> signInManager, UserManager<AppUser> userManager, AuditService auditService)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _auditService = auditService;
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string email, string password, string? returnUrl = null)
        {
            if (ModelState.IsValid)
            {
                var result = await _signInManager.PasswordSignInAsync(email, password, false, lockoutOnFailure: false);
                if (result.Succeeded)
                {
                    // ЛОГИРОВАНИЕ ВХОДА
                    await _auditService.LogAsync("Login", "Account", null, $"Вход в систему: {email}");
                    return RedirectToLocal(returnUrl);
                }
                ModelState.AddModelError(string.Empty, "Неверный логин или пароль.");
            }
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            var userName = User.Identity?.Name;
            // ЛОГИРОВАНИЕ ВЫХОДА
            await _auditService.LogAsync("Logout", "Account", null, $"Выход из системы: {userName}");

            await _signInManager.SignOutAsync();
            return RedirectToAction("Login", "Account");
        }

        private IActionResult RedirectToLocal(string? returnUrl)
        {
            if (Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            else
                return RedirectToAction("Index", "Home");
        }
    }
}