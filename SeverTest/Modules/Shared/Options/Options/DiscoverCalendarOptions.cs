namespace ServerTest.Options
{
    /// <summary>
    /// 发现页日历拉取配置（央行活动/财经事件/经济数据）。
    /// </summary>
    public sealed class DiscoverCalendarOptions
    {
        /// <summary>
        /// 是否启用发现页日历系统。
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 是否在启动时自动建表。
        /// </summary>
        public bool AutoCreateSchema { get; set; } = true;

        /// <summary>
        /// 后台轮询刷新间隔（秒）。
        /// 默认 600 秒（10 分钟）。
        /// </summary>
        public int PollIntervalSeconds { get; set; } = 600;

        /// <summary>
        /// 首次无本地数据时，默认返回最新条数。
        /// </summary>
        public int InitialLatestCount { get; set; } = 20;

        /// <summary>
        /// 单次查询最大返回条数。
        /// </summary>
        public int MaxPullLimit { get; set; } = 200;

        /// <summary>
        /// 服务器内存缓存最多保留条数（每个日历子模块各自一份）。
        /// </summary>
        public int MemoryCacheMaxItems { get; set; } = 1000;

        /// <summary>
        /// 日历语言参数（透传给上游 language，默认中文）。
        /// </summary>
        public string Language { get; set; } = "zh";

        /// <summary>
        /// 上游分页每页条数（用于 page/per_page）。
        /// </summary>
        public int ProviderPerPage { get; set; } = 1000;

        /// <summary>
        /// 初始化补齐最多拉取页数。
        /// </summary>
        public int InitBackfillMaxPages { get; set; } = 20;

        /// <summary>
        /// 是否启用未来窗口探测补齐。
        /// 为兼容上游默认仅返回当天数据的情况，开启后会在首页请求中附带 start_time/end_time，
        /// 一次性拉取“当天 + 未来 N 天”的日历。
        /// </summary>
        public bool FutureWindowProbeEnabled { get; set; } = true;

        /// <summary>
        /// 未来窗口探测天数。
        /// 例如为 7 时，表示从当天 00:00 拉取到未来第 7 天 23:59:59.999。
        /// </summary>
        public int FutureWindowDays { get; set; } = 7;

        /// <summary>
        /// 央行活动接口路径（基于 CoinGlass BaseUrl）。
        /// </summary>
        public string CentralBankActivitiesPath { get; set; } = "/v4/api/calendar/central-bank-activities";

        /// <summary>
        /// 财经事件接口路径（基于 CoinGlass BaseUrl）。
        /// </summary>
        public string FinancialEventsPath { get; set; } = "/v4/api/calendar/financial-events";

        /// <summary>
        /// 经济数据接口路径（基于 CoinGlass BaseUrl）。
        /// </summary>
        public string EconomicDataPath { get; set; } = "/v4/api/calendar/economic-data";
    }
}
