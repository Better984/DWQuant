namespace ServerTest.Models.Strategy
{
    public sealed class StrategyRunCheckLog
    {
        public long Id { get; set; }
        public long Uid { get; set; }
        public long UsId { get; set; }
        public string State { get; set; } = string.Empty;
        public string Exchange { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string? DetailJson { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
