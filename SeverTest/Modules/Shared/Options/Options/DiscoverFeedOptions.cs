namespace ServerTest.Options
{
    /// <summary>
    /// 发现页资讯拉取配置（新闻 + 快讯）。
    /// </summary>
    public sealed class DiscoverFeedOptions
    {
        /// <summary>
        /// 是否启用发现页资讯系统。
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 是否在启动时自动建表。
        /// </summary>
        public bool AutoCreateSchema { get; set; } = true;

        /// <summary>
        /// 后台轮询刷新间隔（秒）。
        /// </summary>
        public int PollIntervalSeconds { get; set; } = 60;

        /// <summary>
        /// 首次无本地数据时，默认返回最新条数。
        /// </summary>
        public int InitialLatestCount { get; set; } = 20;

        /// <summary>
        /// 单次查询最大返回条数。
        /// </summary>
        public int MaxPullLimit { get; set; } = 200;

        /// <summary>
        /// 服务器内存缓存最多保留条数（每个模块各自一份）。
        /// </summary>
        public int MemoryCacheMaxItems { get; set; } = 1000;

        /// <summary>
        /// 是否优先保留中文资讯内容（当前主要作用于新闻模块）。
        /// </summary>
        public bool PreferChineseContent { get; set; } = true;

        /// <summary>
        /// 新闻语言参数（透传给上游 language，默认中文）。
        /// </summary>
        public string ArticleLanguage { get; set; } = "zh";

        /// <summary>
        /// 快讯语言参数（透传给上游 language，默认中文）。
        /// </summary>
        public string NewsflashLanguage { get; set; } = "zh";

        /// <summary>
        /// 上游分页每页条数（用于 page/per_page）。
        /// </summary>
        public int ProviderPerPage { get; set; } = 1000;

        /// <summary>
        /// 初始化补齐最多拉取页数。
        /// </summary>
        public int InitBackfillMaxPages { get; set; } = 20;

        /// <summary>
        /// 新闻列表接口路径（基于 CoinGlass BaseUrl）。
        /// </summary>
        public string ArticleListPath { get; set; } = "/v4/api/article/list";

        /// <summary>
        /// 快讯列表接口路径（基于 CoinGlass BaseUrl）。
        /// </summary>
        public string NewsflashListPath { get; set; } = "/v4/api/newsflash/list";
    }
}
