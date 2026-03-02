namespace ServerTest.Modules.Discover.Domain
{
    /// <summary>
    /// 发现页资讯模块类型。
    /// </summary>
    public enum DiscoverFeedKind
    {
        Article = 1,
        Newsflash = 2
    }

    /// <summary>
    /// 新闻/快讯统一数据实体。
    /// </summary>
    public sealed class DiscoverFeedItem
    {
        public long Id { get; set; }
        public string DedupeKey { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string ContentHtml { get; set; } = string.Empty;
        public string SourceName { get; set; } = string.Empty;
        public string? SourceLogo { get; set; }
        public string? PictureUrl { get; set; }
        public long ReleaseTime { get; set; }
        public string RawPayloadJson { get; set; } = "{}";
        public long CreatedAt { get; set; }
    }

    /// <summary>
    /// 拉取请求参数。
    /// </summary>
    public sealed class DiscoverPullQuery
    {
        public long? LatestId { get; set; }
        public long? BeforeId { get; set; }
        public int? Limit { get; set; }
    }

    /// <summary>
    /// 拉取响应。
    /// </summary>
    public sealed class DiscoverPullResult
    {
        public string Mode { get; set; } = "latest";
        public long LatestServerId { get; set; }
        public bool HasMore { get; set; }
        public IReadOnlyList<DiscoverFeedItem> Items { get; set; } = Array.Empty<DiscoverFeedItem>();
    }
}
