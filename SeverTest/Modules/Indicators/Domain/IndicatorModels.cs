namespace ServerTest.Modules.Indicators.Domain
{
    /// <summary>
    /// 指标定义（元数据 + 刷新策略）。
    /// </summary>
    public sealed class IndicatorDefinition
    {
        public string Code { get; set; } = string.Empty;
        public string Provider { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Shape { get; set; } = string.Empty;
        public string? Unit { get; set; }
        public string? Description { get; set; }
        public int RefreshIntervalSec { get; set; }
        public int TtlSec { get; set; }
        public int HistoryRetentionDays { get; set; }
        public string SourceEndpoint { get; set; } = string.Empty;
        public string DefaultScopeKey { get; set; } = "global";
        public string? ConfigJson { get; set; }
        public bool Enabled { get; set; }
        public int SortOrder { get; set; }
    }

    /// <summary>
    /// 指标最新快照。
    /// </summary>
    public sealed class IndicatorSnapshot
    {
        public string Code { get; set; } = string.Empty;
        public string ScopeKey { get; set; } = string.Empty;
        public string Provider { get; set; } = string.Empty;
        public string Shape { get; set; } = string.Empty;
        public string? Unit { get; set; }
        public string? DisplayName { get; set; }
        public string? Description { get; set; }
        public string PayloadJson { get; set; } = "{}";
        public long SourceTs { get; set; }
        public long FetchedAt { get; set; }
        public long ExpireAt { get; set; }
    }

    /// <summary>
    /// 指标历史点位。
    /// </summary>
    public sealed class IndicatorHistoryPoint
    {
        public long SourceTs { get; set; }
        public string PayloadJson { get; set; } = "{}";
    }

    /// <summary>
    /// 采集器拉取结果。
    /// </summary>
    public sealed class IndicatorCollectResult
    {
        public long SourceTs { get; set; }
        public string PayloadJson { get; set; } = "{}";
        public IReadOnlyList<IndicatorHistoryPoint> History { get; set; } = Array.Empty<IndicatorHistoryPoint>();
    }

    /// <summary>
    /// 对外查询返回结果（含是否过期标记）。
    /// </summary>
    public sealed class IndicatorQueryResult
    {
        public IndicatorDefinition Definition { get; set; } = new();
        public IndicatorSnapshot Snapshot { get; set; } = new();
        public bool Stale { get; set; }
        public string Origin { get; set; } = string.Empty;
    }

    /// <summary>
    /// 历史查询结果。
    /// </summary>
    public sealed class IndicatorHistoryQueryResult
    {
        public IndicatorDefinition Definition { get; set; } = new();
        public string ScopeKey { get; set; } = string.Empty;
        public IReadOnlyList<IndicatorHistoryPoint> Points { get; set; } = Array.Empty<IndicatorHistoryPoint>();
    }
}
