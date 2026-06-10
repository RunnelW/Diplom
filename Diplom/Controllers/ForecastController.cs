using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Diplom.Services;

namespace Diplom.Controllers
{
    [Authorize]
    public class ForecastController : Controller
    {
        private readonly ForecastService _forecastService;

        public ForecastController(ForecastService forecastService)
        {
            _forecastService = forecastService;
        }

        // GET: Страница прогнозирования
        public async Task<IActionResult> Index()
        {
            try
            {
                var metrics = await _forecastService.EvaluateModelAsync();
                var forecast = await _forecastService.GetForecastAsync(3);

                ViewBag.Metrics = metrics;
                return View(forecast);
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
                return View(new ForecastResult());
            }
        }

        // POST: Переобучить модель
        [HttpPost]
        public async Task<IActionResult> Retrain()
        {
            try
            {
                await _forecastService.TrainModelAsync(3);  // ← ИСПРАВЛЕНО: передаём только horizon
                TempData["Success"] = "Модель успешно переобучена";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }
            return RedirectToAction("Index");
        }
    }
}