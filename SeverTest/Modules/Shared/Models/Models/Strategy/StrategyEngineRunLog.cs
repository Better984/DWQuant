namespace ServerTest.Models.Strategy
{
    public sealed class StrategyEngineRunLog
    {
        public DateTime RunAt { get; set; }
        public string Exchange { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public string Timeframe { get; set; } = string.Empty;
        public long CandleTimestamp { get; set; }
        public bool IsBarClose { get; set; }
        public int DurationMs { get; set; }
        public int MatchedCount { get; set; }
        public int ExecutedCount { get; set; }
        public int SkippedCount { get; set; }
        public int ConditionEvalCount { get; set; }
        public int ActionExecCount { get; set; }
        public int OpenTaskCount { get; set; }
        public string? ExecutedStrategyIds { get; set; }
        public string? OpenTaskStrategyIds { get; set; }
        public string? ExtraJson { get; set; }
        public string? EngineInstance { get; set; }
    }
}
