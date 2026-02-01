namespace ServerTest.Models.Market
{
    public sealed class MarketDataNextTickInfo
    {
        public DateTimeOffset? Next1mCloseAt { get; init; }
        public int? Next1mCloseInSeconds { get; init; }
        public IReadOnlyList<string> UpdateTimeframes { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> ClosingTimeframes { get; init; } = Array.Empty<string>();
    }
}
