namespace ServerTest.Models.Trading
{
    public sealed class PositionListItem
    {
        public long PositionId { get; set; }
        public long Uid { get; set; }
        public long UsId { get; set; }
        public long? StrategyVersionId { get; set; }
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
        public decimal? ClosePrice { get; set; }
        public decimal? RealizedPnl { get; set; }
        public DateTime OpenedAt { get; set; }
        public DateTime? ClosedAt { get; set; }
    }

    public sealed class PositionListResponse
    {
        public List<PositionListItem> Items { get; set; } = new();
    }

    public sealed class PositionOverviewResponse
    {
        public DateTime QueryAt { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public int HistoryTotalCount { get; set; }
        public int RecentOpenCount { get; set; }
        public int CurrentOpenCount { get; set; }
        public int RangeClosedCount { get; set; }
        public int RangeWinCount { get; set; }
        public decimal? RangeWinRate { get; set; }
        public decimal RangeRealizedPnl { get; set; }
        public int FloatingPriceHitCount { get; set; }
        public int FloatingPriceMissCount { get; set; }
        public decimal TotalFloatingPnl { get; set; }
        public decimal? TotalFloatingPnlRatio { get; set; }
        public List<PositionHistoryDetailItem> RecentOpenings { get; set; } = new();
        public List<PositionStrategyOpenStatItem> StrategyOpenStats { get; set; } = new();
        public List<PositionVersionParticipationItem> InvolvedVersions { get; set; } = new();
        public List<PositionFloatingItem> CurrentOpenPositions { get; set; } = new();
    }

    public sealed class PositionHistoryDetailItem
    {
        public long PositionId { get; set; }
        public long UsId { get; set; }
        public long DefId { get; set; }
        public string AliasName { get; set; } = string.Empty;
        public string StrategyName { get; set; } = string.Empty;
        public string StrategyState { get; set; } = string.Empty;
        public string DefType { get; set; } = string.Empty;
        public long? StrategyVersionId { get; set; }
        public long EffectiveVersionId { get; set; }
        public int? EffectiveVersionNo { get; set; }
        public DateTime? EffectiveVersionCreatedAt { get; set; }
        public string? EffectiveVersionChangelog { get; set; }
        public string VersionSource { get; set; } = "pinned";
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
        public decimal? ClosePrice { get; set; }
        public decimal? RealizedPnl { get; set; }
        public DateTime OpenedAt { get; set; }
        public DateTime? ClosedAt { get; set; }
    }

    public sealed class PositionRecentSummaryResponse
    {
        public DateTime QueryAt { get; set; }
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public bool HasData { get; set; }
        public int WindowDays { get; set; }
        public List<int> CandidateWindowDays { get; set; } = new();
        public int OpenCount { get; set; }
        public int ClosedCount { get; set; }
        public int WinCount { get; set; }
        public decimal? WinRate { get; set; }
        public decimal RealizedPnl { get; set; }
        public int CurrentOpenCount { get; set; }
        public decimal CurrentFloatingPnl { get; set; }
        public int FloatingPriceHitCount { get; set; }
        public int FloatingPriceMissCount { get; set; }
    }

    public sealed class PositionRecentActivityResponse
    {
        public DateTime QueryAt { get; set; }
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public int Limit { get; set; }
        public List<PositionRecentActivityItem> Items { get; set; } = new();
    }

    public sealed class PositionRecentActivityItem
    {
        /// <summary>
        /// 事件类型：open/close/warn。
        /// </summary>
        public string EventType { get; set; } = string.Empty;
        public DateTime EventAt { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? Exchange { get; set; }
        public string? Symbol { get; set; }
        public string? Side { get; set; }
        public long? PositionId { get; set; }
        public decimal? RealizedPnl { get; set; }
        public string? Severity { get; set; }
    }

    public sealed class PositionStrategyOpenStatItem
    {
        public long UsId { get; set; }
        public long DefId { get; set; }
        public string AliasName { get; set; } = string.Empty;
        public string StrategyName { get; set; } = string.Empty;
        public int OpenSuccessCount { get; set; }
        public int CurrentOpenCount { get; set; }
        public int ClosedCount { get; set; }
        public DateTime FirstOpenedAt { get; set; }
        public DateTime LastOpenedAt { get; set; }
    }

    public sealed class PositionVersionParticipationItem
    {
        public long VersionId { get; set; }
        public int? VersionNo { get; set; }
        public DateTime? VersionCreatedAt { get; set; }
        public string? Changelog { get; set; }
        public int OpenSuccessCount { get; set; }
        public int StrategyCount { get; set; }
        public List<long> StrategyUsIds { get; set; } = new();
        public string? StrategyAliasNames { get; set; }
        public int SnapshotCount { get; set; }
        public int InferredCount { get; set; }
        public int PinnedCount { get; set; }
    }

    public sealed class PositionFloatingItem
    {
        public long PositionId { get; set; }
        public long UsId { get; set; }
        public string AliasName { get; set; } = string.Empty;
        public string StrategyName { get; set; } = string.Empty;
        public long EffectiveVersionId { get; set; }
        public int? EffectiveVersionNo { get; set; }
        public string VersionSource { get; set; } = "pinned";
        public string Exchange { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public string Side { get; set; } = string.Empty;
        public decimal EntryPrice { get; set; }
        public decimal Qty { get; set; }
        public DateTime OpenedAt { get; set; }
        public decimal? LatestPrice { get; set; }
        public decimal? FloatingPnl { get; set; }
        public decimal? FloatingPnlRatio { get; set; }
    }
}
