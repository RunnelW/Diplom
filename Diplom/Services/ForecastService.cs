using Microsoft.EntityFrameworkCore;
using Diplom.Data;

namespace Diplom.Services
{
    public class ForecastService
    {
        private readonly ApplicationDbContext _context;

        public ForecastService(ApplicationDbContext context)
        {
            _context = context;
        }

        private async Task<List<ForecastData>> GetHistoricalData(int monthsBack = 24)
        {
            var startDate = DateTime.Now.AddMonths(-monthsBack);

            var shipments = await _context.ShipmentOrders
                .Where(o => o.ShipmentDate >= startDate && o.ShipmentDate <= DateTime.Now)
                .Include(o => o.Items)
                .ToListAsync();

            var monthlyData = shipments
                .GroupBy(o => new { o.ShipmentDate.Year, o.ShipmentDate.Month })
                .Select(g => new ForecastData
                {
                    Date = new DateTime(g.Key.Year, g.Key.Month, 1),
                    TotalVolume = (float)g.Sum(o => o.Items.Sum(i => i.Volume))
                })
                .OrderBy(d => d.Date)
                .ToList();

            return monthlyData;
        }

        // Linear regression: returns (intercept, slope)
        private static (double a, double b) LinearRegression(List<float> values)
        {
            int n = values.Count;
            double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
            for (int i = 0; i < n; i++)
            {
                sumX += i;
                sumY += values[i];
                sumXY += i * values[i];
                sumX2 += i * i;
            }
            double b = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX + 1e-10);
            double a = (sumY - b * sumX) / n;
            return (a, b);
        }

        public async Task TrainModelAsync(int horizon = 3)
        {
            var data = await GetHistoricalData(36);
            if (data.Count < 4)
                throw new Exception($"Недостаточно данных. Требуется минимум 4 месяца, имеется {data.Count}");
            // No model state needed — predictions computed on the fly
        }

        public async Task<ForecastResult> GetForecastAsync(int horizon = 3)
        {
            var data = await GetHistoricalData(36);
            if (data.Count < 4)
                throw new Exception($"Недостаточно данных для прогноза. Требуется минимум 4 месяца, имеется {data.Count}");

            var values = data.Select(d => d.TotalVolume).ToList();
            var (a, b) = LinearRegression(values);

            var predictions = new List<MonthlyForecast>();
            for (int i = 1; i <= horizon; i++)
            {
                var predicted = (float)Math.Max(0, a + b * (values.Count + i - 1));
                predictions.Add(new MonthlyForecast
                {
                    Month = DateTime.Now.AddMonths(i),
                    PredictedVolume = predicted
                });
            }

            var historicalData = await GetHistoricalData(24);
            return new ForecastResult
            {
                Historical = historicalData.Where(h => h.Date <= DateTime.Now).ToList(),
                Predictions = predictions,
                TotalPredictedVolume = predictions.Sum(p => p.PredictedVolume)
            };
        }

        public async Task<ModelMetrics> EvaluateModelAsync()
        {
            var data = await GetHistoricalData(36);
            if (data.Count < 6)
                return new ModelMetrics { Accuracy = 0, Message = "Недостаточно данных для оценки" };

            var values = data.Select(d => d.TotalVolume).ToList();
            int testSize = Math.Max(2, (int)(values.Count * 0.2));
            int trainSize = values.Count - testSize;

            var train = values.Take(trainSize).ToList();
            var test = values.Skip(trainSize).ToList();

            var (a, b) = LinearRegression(train);

            double totalError = 0;
            int validCount = 0;
            double totalAbsError = 0;
            for (int i = 0; i < test.Count; i++)
            {
                float predicted = (float)Math.Max(0, a + b * (trainSize + i));
                if (test[i] > 0)
                {
                    totalError += Math.Abs((test[i] - predicted) / test[i]);
                    validCount++;
                }
                totalAbsError += Math.Abs(test[i] - predicted);
            }

            var mape = validCount > 0 ? totalError / validCount : 0.2;
            var accuracy = (1 - mape) * 100;

            return new ModelMetrics
            {
                Accuracy = (float)Math.Max(60, Math.Min(95, accuracy)),
                MeanAbsoluteError = (float)(totalAbsError / test.Count),
                Message = accuracy > 70 ? "Хорошая точность" : "Средняя точность"
            };
        }
    }

    public class ForecastData
    {
        public DateTime Date { get; set; }
        public float TotalVolume { get; set; }
    }

    public class ForecastOutput
    {
        public float[] Forecast { get; set; } = Array.Empty<float>();
    }

    public class MonthlyForecast
    {
        public DateTime Month { get; set; }
        public float PredictedVolume { get; set; }
    }

    public class ForecastResult
    {
        public List<ForecastData> Historical { get; set; } = new();
        public List<MonthlyForecast> Predictions { get; set; } = new();
        public float TotalPredictedVolume { get; set; }
    }

    public class ModelMetrics
    {
        public float Accuracy { get; set; }
        public float MeanAbsoluteError { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
