namespace ServerTest.Models.Strategy
{
    /// <summary>
    /// 测试用：策略检查日志实体
    /// 注意：这是测试功能，用于记录每个策略维度的每一次检查过程，后续会删除
    /// </summary>
    public sealed class TestStrategyCheckLog
    {
        public long Id { get; set; }
        public long Uid { get; set; }
        public long UsId { get; set; }
        public string Exchange { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public string Timeframe { get; set; } = string.Empty;
        public long CandleTimestamp { get; set; }
        public string Stage { get; set; } = string.Empty;
        public int GroupIndex { get; set; }
        public string ConditionKey { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
        public bool IsRequired { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? CheckProcess { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
