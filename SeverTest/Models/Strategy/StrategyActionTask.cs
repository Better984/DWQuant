namespace ServerTest.Models.Strategy
{
    public readonly record struct ConditionEvaluationSnapshot(
        string Key,
        bool Success,
        string Message);

    public sealed class StrategyActionTask
    {
        public string StrategyUid { get; init; } = string.Empty;
        public string Stage { get; init; } = string.Empty;
        public MarketDataTask MarketTask { get; init; }
        public string Method { get; init; } = string.Empty;
        public string[] Param { get; init; } = Array.Empty<string>();
        public IReadOnlyList<ConditionEvaluationSnapshot> TriggerResults { get; init; } =
            Array.Empty<ConditionEvaluationSnapshot>();
        public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    }
}
