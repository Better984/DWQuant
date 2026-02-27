namespace ServerTest.Models.Strategy
{
    /// <summary>
    /// 策略链路追踪事件（跨模块统一格式）。
    /// </summary>
    public sealed class StrategyTaskTraceLog
    {
        public long Id { get; set; }
        public string TraceId { get; set; } = string.Empty;
        public string? ParentTraceId { get; set; }
        public string EventStage { get; set; } = string.Empty;
        public string EventStatus { get; set; } = string.Empty;
        public string ActorModule { get; set; } = string.Empty;
        public string ActorInstance { get; set; } = string.Empty;
        public long? Uid { get; set; }
        public long? UsId { get; set; }
        public string? StrategyUid { get; set; }
        public string? Exchange { get; set; }
        public string? Symbol { get; set; }
        public string? Timeframe { get; set; }
        public long? CandleTimestamp { get; set; }
        public bool? IsBarClose { get; set; }
        public string? Method { get; set; }
        public string? Flow { get; set; }
        public int? DurationMs { get; set; }
        public string? MetricsJson { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
