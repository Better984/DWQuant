namespace ServerTest.Models.Trading
{
    public sealed class PositionListItem
    {
        public long PositionId { get; set; }
        public long Uid { get; set; }
        public long UsId { get; set; }
        public long? ExchangeApiKeyId { get; set; }
        public string Exchange { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public string Side { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public decimal EntryPrice { get; set; }
        public decimal Qty { get; set; }
        public decimal? StopLossPrice { get; set; }
        public decimal? TakeProfitPrice { get; set; }
        public bool TrailingEnabled { get; set; }
        public bool TrailingTriggered { get; set; }
        public decimal? TrailingStopPrice { get; set; }
        public string? CloseReason { get; set; }
        public DateTime OpenedAt { get; set; }
        public DateTime? ClosedAt { get; set; }
    }

    public sealed class PositionListResponse
    {
        public List<PositionListItem> Items { get; set; } = new();
    }
}
