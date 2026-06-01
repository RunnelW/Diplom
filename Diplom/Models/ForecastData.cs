namespace Diplom.Models
{
    public class ForecastData
    {
        public DateTime Date { get; set; }
        public float TotalVolume { get; set; }
    }

    public class ForecastOutput
    {
        public float[] Forecast { get; set; } = Array.Empty<float>();
        public float[] LowerBound { get; set; } = Array.Empty<float>();
        public float[] UpperBound { get; set; } = Array.Empty<float>();
    }
}
