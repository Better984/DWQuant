using System.Collections.Generic;

namespace ServerTest.Infrastructure.Config
{
    /// <summary>
    /// 服务器配置项定义（用于初始化与校验）
    /// </summary>
    public static class ServerConfigDefinitions
    {
        public static readonly IReadOnlyList<ServerConfigDefinition> All = new List<ServerConfigDefinition>
        {
            new ServerConfigDefinition("RateLimit:HttpRps", "限流", "int", "HTTP 请求每秒限流阈值（实时生效）", true),
            new ServerConfigDefinition("RateLimit:WsRps", "限流", "int", "WebSocket 请求每秒限流阈值（实时生效）", true),

            new ServerConfigDefinition("RequestLimits:DefaultMaxBodyBytes", "请求限制", "int", "默认请求体大小上限（字节，实时生效）", true),

            new ServerConfigDefinition("Monitoring:MaxLogItems", "监控", "int", "系统日志显示上限（需重启）", false),
            new ServerConfigDefinition("Monitoring:MaxTradingLogItems", "监控", "int", "交易日志显示上限（需重启）", false),

            new ServerConfigDefinition("MarketDataQuery:DefaultBatchSize", "行情查询", "int", "历史数据默认批次大小（需重启）", false),
            new ServerConfigDefinition("MarketDataQuery:MaxLimitPerRequest", "行情查询", "int", "单次最大拉取数量（需重启）", false),
            new ServerConfigDefinition("MarketDataQuery:CacheHistoryLength", "行情查询", "int", "历史缓存长度（需重启）", false),

            new ServerConfigDefinition("HistoricalData:SyncEnabled", "历史数据", "bool", "是否启用历史数据同步（需重启）", false),
            new ServerConfigDefinition("HistoricalData:PreloadEnabled", "历史数据", "bool", "是否启用启动预热（需重启）", false),
            new ServerConfigDefinition("HistoricalData:PreloadStartDate", "历史数据", "string", "预热起始日期（yyyy-MM-dd，需重启）", false),
            new ServerConfigDefinition("HistoricalData:SyncBatchSize", "历史数据", "int", "同步批次大小（需重启）", false),
            new ServerConfigDefinition("HistoricalData:SyncMaxParallel", "历史数据", "int", "同步并发数（需重启）", false),
            new ServerConfigDefinition("HistoricalData:SyncMinGapMinutes", "历史数据", "int", "同步最小间隔（分钟，需重启）", false),
            new ServerConfigDefinition("HistoricalData:SyncIntervalMinutes", "历史数据", "int", "同步周期（分钟，需重启）", false),
            new ServerConfigDefinition("HistoricalData:DefaultStartDate", "历史数据", "string", "默认查询起始日期（yyyy-MM-dd，需重启）", false),
            new ServerConfigDefinition("HistoricalData:MaxQueryBars", "历史数据", "int", "单次查询最大 K 线数量（需重启）", false),
            new ServerConfigDefinition("HistoricalData:MaxCacheBars", "历史数据", "int", "单品种缓存 K 线数量（需重启）", false),

            new ServerConfigDefinition("ConditionCache:CleanupIntervalSeconds", "策略运行", "int", "条件缓存清理间隔（秒，需重启）", false),

            new ServerConfigDefinition("Startup:MarketDataInitTimeoutSeconds", "启动", "int", "行情初始化超时（秒，需重启）", false),
            new ServerConfigDefinition("Startup:StrategyRuntimeWarmupSeconds", "启动", "int", "策略运行预热等待（秒，需重启）", false),

            new ServerConfigDefinition("StrategyOwnership:Enabled", "策略租约", "bool", "是否启用多实例租约（需重启）", false),
            new ServerConfigDefinition("StrategyOwnership:LeaseSeconds", "策略租约", "int", "租约时长（秒，需重启）", false),
            new ServerConfigDefinition("StrategyOwnership:RenewIntervalSeconds", "策略租约", "int", "租约续期间隔（秒，需重启）", false),
            new ServerConfigDefinition("StrategyOwnership:SyncIntervalSeconds", "策略租约", "int", "租约同步间隔（秒，需重启）", false),
            new ServerConfigDefinition("StrategyOwnership:KeyPrefix", "策略租约", "string", "租约键前缀（需重启）", false),

            new ServerConfigDefinition("WebSocket:KickPolicy", "WebSocket", "string", "连接挤出策略（需重启）", false),
            new ServerConfigDefinition("WebSocket:KeepAliveSeconds", "WebSocket", "int", "心跳间隔（秒，需重启）", false),
            new ServerConfigDefinition("WebSocket:MaxConnectionsPerSystem", "WebSocket", "int", "同系统最大连接数（需重启）", false),
            new ServerConfigDefinition("WebSocket:ConnectionKeyTtlSeconds", "WebSocket", "int", "连接键过期时间（秒，需重启）", false),
            new ServerConfigDefinition("WebSocket:ConnectionKeyRefreshSeconds", "WebSocket", "int", "连接键刷新间隔（秒，需重启）", false),

            new ServerConfigDefinition("Trading:EnableSandboxMode", "交易", "bool", "是否启用沙盒模式（需重启）", false),
        };
    }

    public sealed class ServerConfigDefinition
    {
        public ServerConfigDefinition(string key, string category, string valueType, string description, bool isRealtime)
        {
            Key = key;
            Category = category;
            ValueType = valueType;
            Description = description;
            IsRealtime = isRealtime;
        }

        public string Key { get; }
        public string Category { get; }
        public string ValueType { get; }
        public string Description { get; }
        public bool IsRealtime { get; }
    }
}
