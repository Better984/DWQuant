namespace ServerTest.Modules.MarketData.Domain
{
    public sealed class MarketDataGap
    {
        public DateTime GapStartTime { get; set; }
        public DateTime GapEndTime { get; set; }
        public decimal GapMinutes { get; set; }
    }
}
