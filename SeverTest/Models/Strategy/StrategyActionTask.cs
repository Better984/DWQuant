namespace ServerTest.Models.Strategy
{
    public readonly record struct ConditionEvaluationSnapshot(
        string Key,
        bool Success,
        string Message);

    public sealed class StrategyActionTask
    {
        public string StrategyUid { get; init; } = string.Empty;
        public long? Uid { get; init; }
        public long? UsId { get; init; }
        public long? ExchangeApiKeyId { get; init; }
        public string Exchange { get; init; } = string.Empty;
        public string Symbol { get; init; } = string.Empty;
        public int TimeframeSec { get; init; }
        public decimal OrderQty { get; init; }
        public int Leverage { get; init; }
        public decimal? TakeProfitPct { get; init; }
        public decimal? StopLossPct { get; init; }
        public bool TrailingEnabled { get; init; }
        public decimal? TrailingActivationPct { get; init; }
        public decimal? TrailingDrawdownPct { get; init; }
        public string Stage { get; init; } = string.Empty;
        public MarketDataTask MarketTask { get; init; }
        public string Method { get; init; } = string.Empty;
        public string[] Param { get; init; } = Array.Empty<string>();
        public IReadOnlyList<ConditionEvaluationSnapshot> TriggerResults { get; init; } =
            Array.Empty<ConditionEvaluationSnapshot>();
        public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    }
}
