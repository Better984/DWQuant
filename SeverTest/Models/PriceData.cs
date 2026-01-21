namespace ServerTest.Models
{
    public class PriceData
    {
        public string Exchange { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public DateTime Timestamp { get; set; }
        public decimal? Volume { get; set; }
        public decimal? High24h { get; set; }
        public decimal? Low24h { get; set; }
    }
}
