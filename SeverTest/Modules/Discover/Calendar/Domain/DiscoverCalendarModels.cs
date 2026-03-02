namespace ServerTest.Modules.Discover.Domain
{
    /// <summary>
    /// Discover 日历类型。
    /// </summary>
    public enum DiscoverCalendarKind
    {
        CentralBankActivities = 1,
        FinancialEvents = 2,
        EconomicData = 3
    }

    /// <summary>
    /// Discover 日历事件实体。
    /// </summary>
    public sealed class DiscoverCalendarItem
    {
        public long Id { get; set; }
        public string DedupeKey { get; set; } = string.Empty;
        public string CalendarName { get; set; } = string.Empty;
        public string CountryCode { get; set; } = string.Empty;
        public string CountryName { get; set; } = string.Empty;
        public long PublishTimestamp { get; set; }
        public int ImportanceLevel { get; set; }
        public bool HasExactPublishTime { get; set; }
        public string? DataEffect { get; set; }
        public string? ForecastValue { get; set; }
        public string? PreviousValue { get; set; }
        public string? RevisedPreviousValue { get; set; }
        public string? PublishedValue { get; set; }
        public string RawPayloadJson { get; set; } = "{}";
        public long CreatedAt { get; set; }
        public long UpdatedAt { get; set; }
    }

    /// <summary>
    /// 日历拉取请求。
    /// </summary>
    public sealed class DiscoverCalendarPullQuery
    {
        public long? LatestId { get; set; }
        public long? BeforeId { get; set; }
        public long? StartTime { get; set; }
        public long? EndTime { get; set; }
        public int? Limit { get; set; }
    }

    /// <summary>
    /// 日历拉取响应。
    /// </summary>
    public sealed class DiscoverCalendarPullResult
    {
        public string Mode { get; set; } = "latest";
        public long LatestServerId { get; set; }
        public bool HasMore { get; set; }
        public IReadOnlyList<DiscoverCalendarItem> Items { get; set; } = Array.Empty<DiscoverCalendarItem>();
    }
}
