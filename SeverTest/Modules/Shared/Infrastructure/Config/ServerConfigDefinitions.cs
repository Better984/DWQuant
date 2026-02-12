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
            new ServerConfigDefinition("MarketStreaming:UseRedisSubscriptionStore", "行情推送", "bool", "是否使用 Redis 作为订阅存储（需重启）", false),

            new ServerConfigDefinition("BacktestWorker:DispatchPollingIntervalMs", "回测分布式", "int", "核心节点任务分发轮询间隔（毫秒，需重启）", false),
            new ServerConfigDefinition("BacktestWorker:HeartbeatSeconds", "回测分布式", "int", "算力节点心跳上报间隔（秒，需重启）", false),
            new ServerConfigDefinition("BacktestWorker:ReconnectDelaySeconds", "回测分布式", "int", "算力节点断线重连间隔（秒，需重启）", false),
            new ServerConfigDefinition("BacktestWorker:MaxParallelTasksPerWorker", "回测分布式", "int", "算力节点可并行执行任务数（需重启）", false),

            new ServerConfigDefinition("Backtest:MaxConcurrentTasks", "回测", "int", "全局最大并发回测任务数（实时生效）", true),
            new ServerConfigDefinition("Backtest:InnerParallelism", "回测", "int", "单任务主循环每个时间点的 symbol 并行度（<=0 表示按 CPU 核心数，实时生效）", true),
            new ServerConfigDefinition("Backtest:MaxTasksPerUser", "回测", "int", "单用户最大并发回测任务数（实时生效）", true),
            new ServerConfigDefinition("Backtest:MaxQueueSize", "回测", "int", "回测队列最大排队数量（实时生效）", true),
            new ServerConfigDefinition("Backtest:MaxBarCount", "回测", "int", "单次回测最大 K 线数量（实时生效）", true),
            new ServerConfigDefinition("Backtest:TaskTimeoutMinutes", "回测", "int", "单次回测最大执行时间（分钟，实时生效）", true),
            new ServerConfigDefinition("Backtest:ResultRetentionDays", "回测", "int", "回测结果保留天数（实时生效）", true),
            new ServerConfigDefinition("Backtest:ExecutionModeDefault", "回测", "string", "默认执行模式（batch_open_close/timeline，实时生效）", true),
            new ServerConfigDefinition("Backtest:BatchCloseParallelism", "回测", "int", "批量模式统一平仓并行度（<=0 按 CPU 核心数，实时生效）", true),
            new ServerConfigDefinition("Backtest:BatchCloseCandidateSliceSize", "回测", "int", "批量模式单个并行任务处理的平仓候选数量（实时生效）", true),
            new ServerConfigDefinition("Backtest:BatchAllowOverlappingPositions", "回测", "bool", "批量模式是否允许重叠仓位（实时生效）", true),
            new ServerConfigDefinition("Backtest:ObjectPool:BarDictionaryPrewarm", "回测", "int", "回测对象池：K线字典预热数量（需重启）", false),
            new ServerConfigDefinition("Backtest:ObjectPool:ConditionResultListPrewarm", "回测", "int", "回测对象池：条件结果列表预热数量（需重启）", false),
            new ServerConfigDefinition("Backtest:ObjectPool:StrategyMethodListPrewarm", "回测", "int", "回测对象池：条件方法列表预热数量（需重启）", false),
            new ServerConfigDefinition("Backtest:ObjectPool:TimestampSetPrewarm", "回测", "int", "回测对象池：时间戳集合预热数量（需重启）", false),
            new ServerConfigDefinition("Backtest:ObjectPool:IndicatorTaskPrewarm", "回测", "int", "回测对象池：指标任务预热数量（需重启）", false),
            new ServerConfigDefinition("Backtest:ObjectPool:StrategyContextPrewarm", "回测", "int", "回测对象池：策略上下文预热数量（需重启）", false),
            new ServerConfigDefinition("Backtest:WorkerPollingIntervalMs", "回测", "int", "任务轮询间隔（毫秒，需重启）", false),
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
