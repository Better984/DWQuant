using ServerTest.Models.Strategy;
using System.Collections.Generic;

namespace ServerTest.Modules.Backtest.Domain
{
        /// <summary>
    /// 回测运行请求
    /// </summary>
    public sealed class BacktestRunRequest
    {
                /// <summary>
        /// 策略实例ID（可选）
        /// </summary>
        public long? UsId { get; set; }

                /// <summary>
        /// 策略配置JSON（优先于usId）
        /// </summary>
        public string? ConfigJson { get; set; }

                /// <summary>
        /// 交易所（如 Binance）
        /// </summary>
        public string Exchange { get; set; } = string.Empty;

                /// <summary>
        /// 标的列表（默认取策略配置的 symbol）
        /// </summary>
        public List<string> Symbols { get; set; } = new();

                /// <summary>
        /// 周期（如 1m/5m，默认取策略配置）
        /// </summary>
        public string? Timeframe { get; set; }

                /// <summary>
        /// 开始时间（yyyy-MM-dd HH:mm:ss）
        /// </summary>
        public string? StartTime { get; set; }

                /// <summary>
        /// 结束时间（yyyy-MM-dd HH:mm:ss）
        /// </summary>
        public string? EndTime { get; set; }

                /// <summary>
        /// K线数量
        /// </summary>
        public int? BarCount { get; set; }

                /// <summary>
        /// 初始资金
        /// </summary>
        public decimal InitialCapital { get; set; } = 0m;

                /// <summary>
        /// 覆盖下单数量（覆盖 trade.sizing.orderQty）
        /// </summary>
        public decimal? OrderQtyOverride { get; set; }

                /// <summary>
        /// 覆盖杠杆倍数
        /// </summary>
        public int? LeverageOverride { get; set; }

                /// <summary>
        /// 覆盖止盈百分比
        /// </summary>
        public decimal? TakeProfitPctOverride { get; set; }

                /// <summary>
        /// 覆盖止损百分比
        /// </summary>
        public decimal? StopLossPctOverride { get; set; }

                /// <summary>
        /// 手续费率（0.04% = 0.0004）
        /// </summary>
        public decimal FeeRate { get; set; } = 0.0004m;

                /// <summary>
        /// 资金费率（默认 0）
        /// </summary>
        public decimal FundingRate { get; set; } = 0m;

                /// <summary>
        /// 固定滑点（bps）
        /// </summary>
        public int SlippageBps { get; set; } = 0;

                /// <summary>
        /// 是否自动反向
        /// </summary>
        public bool AutoReverse { get; set; }

                /// <summary>
        /// 运行时间配置（可覆盖策略配置）
        /// </summary>
        public StrategyRuntimeConfig? Runtime { get; set; }

                /// <summary>
        /// 是否启用运行时间门禁
        /// </summary>
        public bool UseStrategyRuntime { get; set; } = true;

        /// <summary>
        /// 执行模式（可选）：
        /// - batch_open_close：先批量开仓检测，再统一并行平仓（高性能默认）
        /// - timeline：时间轴串行模式（兼容旧逻辑）
        /// </summary>
        public string? ExecutionMode { get; set; }

        /// <summary>
        /// 输出选项
        /// </summary>
        public BacktestOutputOptions? Output { get; set; }
    }

    /// <summary>
    /// 回测执行模式常量
    /// </summary>
    public static class BacktestExecutionModes
    {
        public const string BatchOpenClose = "batch_open_close";
        public const string Timeline = "timeline";
    }

        /// <summary>
    /// 回测输出选项
    /// </summary>
    public sealed class BacktestOutputOptions
    {
        public bool IncludeTrades { get; set; } = true;
        public bool IncludeEquityCurve { get; set; } = true;
        public bool IncludeEvents { get; set; } = true;
        public string EquityCurveGranularity { get; set; } = "1m";
    }

        /// <summary>
    /// 回测运行结果
    /// </summary>
    public sealed class BacktestRunResult
    {
        public string Exchange { get; set; } = string.Empty;
        public string Timeframe { get; set; } = string.Empty;
        public long StartTimestamp { get; set; }
        public long EndTimestamp { get; set; }
        public int TotalBars { get; set; }
        public string EquityCurveGranularity { get; set; } = "1m";
                /// <summary>
        /// 回测耗时（毫秒）
        /// </summary>
        public long DurationMs { get; set; }
        public BacktestStats TotalStats { get; set; } = new();
        public List<BacktestSymbolResult> Symbols { get; set; } = new();
    }

        /// <summary>
    /// 回测标的结果
    /// </summary>
    public sealed class BacktestSymbolResult
    {
        public string Symbol { get; set; } = string.Empty;
        public int Bars { get; set; }
        public decimal InitialCapital { get; set; }
        public BacktestStats Stats { get; set; } = new();
        /// <summary>
        /// 交易明细汇总（关键指标）
        /// </summary>
        public BacktestTradeSummary TradeSummary { get; set; } = new();
        /// <summary>
        /// 资金曲线汇总（关键指标）
        /// </summary>
        public BacktestEquitySummary EquitySummary { get; set; } = new();
        /// <summary>
        /// 事件日志汇总（关键指标）
        /// </summary>
        public BacktestEventSummary EventSummary { get; set; } = new();
        /// <summary>
        /// 交易明细原始串列表（前端按需解析）
        /// </summary>
        public List<string> TradesRaw { get; set; } = new();
        /// <summary>
        /// 资金曲线原始串列表（前端按需解析）
        /// </summary>
        public List<string> EquityCurveRaw { get; set; } = new();
        /// <summary>
        /// 事件日志原始串列表（前端按需解析）
        /// </summary>
        public List<string> EventsRaw { get; set; } = new();
    }

        /// <summary>
    /// 回测交易
    /// </summary>
    public sealed class BacktestTrade
    {
        public string Symbol { get; set; } = string.Empty;
        public string Side { get; set; } = string.Empty; // Long/Short
        public long EntryTime { get; set; }
        public long ExitTime { get; set; }
        public decimal EntryPrice { get; set; }
        public decimal ExitPrice { get; set; }
        public decimal? StopLossPrice { get; set; }
        public decimal? TakeProfitPrice { get; set; }
        public decimal Qty { get; set; }
        public decimal ContractSize { get; set; }
        public decimal Fee { get; set; }
        public decimal PnL { get; set; }
        public string ExitReason { get; set; } = string.Empty;
        public int SlippageBps { get; set; }
    }

        /// <summary>
    /// 回测资金曲线点
    /// </summary>
    public sealed class BacktestEquityPoint
    {
        public long Timestamp { get; set; }
        public decimal Equity { get; set; }
        public decimal RealizedPnl { get; set; }
        public decimal UnrealizedPnl { get; set; }
        public decimal PeriodRealizedPnl { get; set; }
        public decimal PeriodUnrealizedPnl { get; set; }
    }

        /// <summary>
    /// 回测事件
    /// </summary>
    public sealed class BacktestEvent
    {
        public long Timestamp { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 交易明细汇总
    /// </summary>
    public sealed class BacktestTradeSummary
    {
        public int TotalCount { get; set; }
        public int WinCount { get; set; }
        public int LossCount { get; set; }
        public decimal MaxProfit { get; set; }
        public decimal MaxLoss { get; set; }
        public decimal TotalFee { get; set; }
        public long FirstEntryTime { get; set; }
        public long LastExitTime { get; set; }
    }

    /// <summary>
    /// 资金曲线汇总
    /// </summary>
    public sealed class BacktestEquitySummary
    {
        public int PointCount { get; set; }
        public decimal MaxEquity { get; set; }
        public long MaxEquityAt { get; set; }
        public decimal MinEquity { get; set; }
        public long MinEquityAt { get; set; }
        public decimal MaxPeriodProfit { get; set; }
        public long MaxPeriodProfitAt { get; set; }
        public decimal MaxPeriodLoss { get; set; }
        public long MaxPeriodLossAt { get; set; }
    }

    /// <summary>
    /// 事件日志汇总
    /// </summary>
    public sealed class BacktestEventSummary
    {
        public int TotalCount { get; set; }
        public long FirstTimestamp { get; set; }
        public long LastTimestamp { get; set; }
        public Dictionary<string, int> TypeCounts { get; set; } = new();
    }

        /// <summary>
    /// 回测统计指标
    /// </summary>
    public sealed class BacktestStats
    {
        // ---- 基础指标 ----
        public decimal TotalProfit { get; set; }
        public decimal TotalReturn { get; set; }
        public decimal MaxDrawdown { get; set; }
        public decimal WinRate { get; set; }
        public int TradeCount { get; set; }
        public decimal AvgProfit { get; set; }
        public decimal ProfitFactor { get; set; }
        public decimal AvgWin { get; set; }
        public decimal AvgLoss { get; set; }

        // ---- 高级指标 ----
        /// <summary>夏普比率（年化，无风险利率=0）</summary>
        public decimal SharpeRatio { get; set; }
        /// <summary>Sortino 比率（年化，仅下行波动率）</summary>
        public decimal SortinoRatio { get; set; }
        /// <summary>年化收益率</summary>
        public decimal AnnualizedReturn { get; set; }
        /// <summary>最大连续亏损次数</summary>
        public int MaxConsecutiveLosses { get; set; }
        /// <summary>最大连续盈利次数</summary>
        public int MaxConsecutiveWins { get; set; }
        /// <summary>平均持仓时间（毫秒）</summary>
        public long AvgHoldingMs { get; set; }
        /// <summary>最大回撤持续时间（毫秒）</summary>
        public long MaxDrawdownDurationMs { get; set; }
        /// <summary>Calmar 比率（年化收益率 / 最大回撤）</summary>
        public decimal CalmarRatio { get; set; }
    }

    /// <summary>
    /// 回测实时推送上下文（用于关联 reqId 与用户）
    /// </summary>
    public sealed class BacktestProgressContext
    {
        public long? UserId { get; set; }
        public string? ReqId { get; set; }
        /// <summary>
        /// 异步任务ID（仅队列任务存在，用于进度持久化）
        /// </summary>
        public long? TaskId { get; set; }
    }

    /// <summary>
    /// 回测实时推送消息
    /// </summary>
    public sealed class BacktestProgressMessage
    {
        /// <summary>
        /// 消息事件类型：stage / positions
        /// </summary>
        public string EventKind { get; set; } = "stage";

        /// <summary>
        /// 阶段编码（例如 main_loop / collect_positions）
        /// </summary>
        public string Stage { get; set; } = string.Empty;

        /// <summary>
        /// 阶段名称（前端展示）
        /// </summary>
        public string StageName { get; set; } = string.Empty;

        /// <summary>
        /// 阶段描述
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// 主循环进度（已处理 bar）
        /// </summary>
        public int? ProcessedBars { get; set; }

        /// <summary>
        /// 主循环总 bar
        /// </summary>
        public int? TotalBars { get; set; }

        /// <summary>
        /// 百分比进度 [0,1]
        /// </summary>
        public decimal? Progress { get; set; }

        /// <summary>
        /// 当前阶段累计耗时（毫秒）
        /// </summary>
        public long? ElapsedMs { get; set; }

        /// <summary>
        /// 当前已发现仓位/交易数量
        /// </summary>
        public int? FoundPositions { get; set; }

        /// <summary>
        /// 预计总仓位/交易数量
        /// </summary>
        public int? TotalPositions { get; set; }

        /// <summary>
        /// 本次增量仓位数量
        /// </summary>
        public int? ChunkCount { get; set; }

        /// <summary>
        /// 当前已平仓胜场数
        /// </summary>
        public int? WinCount { get; set; }

        /// <summary>
        /// 当前已平仓负场数
        /// </summary>
        public int? LossCount { get; set; }

        /// <summary>
        /// 当前胜率（0~1）
        /// </summary>
        public decimal? WinRate { get; set; }

        /// <summary>
        /// 是否完成当前阶段
        /// </summary>
        public bool? Completed { get; set; }

        /// <summary>
        /// 当前增量所属标的
        /// </summary>
        public string? Symbol { get; set; }

        /// <summary>
        /// 增量仓位列表（测试阶段可全量流式）
        /// </summary>
        public List<BacktestTrade>? Positions { get; set; }

        /// <summary>
        /// 前端是否用本次仓位列表覆盖当前预览（用于“最近100条”窗口）
        /// </summary>
        public bool? ReplacePositions { get; set; }
    }

    /// <summary>
    /// 回测任务（持久化到 backtest_task 表）
    /// </summary>
    public sealed class BacktestTask
    {
        public long TaskId { get; set; }
        public long UserId { get; set; }
        public string? ReqId { get; set; }
        public string? AssignedWorkerId { get; set; }
        public string Status { get; set; } = BacktestTaskStatus.Queued;
        public decimal Progress { get; set; }
        public string? Stage { get; set; }
        public string? StageName { get; set; }
        public string? Message { get; set; }
        public string RequestJson { get; set; } = string.Empty;
        public string? ResultJson { get; set; }
        public string? ErrorMessage { get; set; }
        public string Exchange { get; set; } = string.Empty;
        public string Timeframe { get; set; } = string.Empty;
        public string Symbols { get; set; } = string.Empty;
        public int BarCount { get; set; }
        public int TradeCount { get; set; }
        public long DurationMs { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// 回测任务状态常量
    /// </summary>
    public static class BacktestTaskStatus
    {
        public const string Queued = "queued";
        public const string Running = "running";
        public const string Completed = "completed";
        public const string Failed = "failed";
        public const string Cancelled = "cancelled";
    }

    /// <summary>
    /// 回测任务简要信息（列表查询返回，不含 result_json）
    /// </summary>
    public sealed class BacktestTaskSummary
    {
        public long TaskId { get; set; }
        public string? AssignedWorkerId { get; set; }
        public string Status { get; set; } = string.Empty;
        public decimal Progress { get; set; }
        public string? Stage { get; set; }
        public string? StageName { get; set; }
        public string? Message { get; set; }
        public string? ErrorMessage { get; set; }
        public string Exchange { get; set; } = string.Empty;
        public string Timeframe { get; set; } = string.Empty;
        public string Symbols { get; set; } = string.Empty;
        public int BarCount { get; set; }
        public int TradeCount { get; set; }
        public long DurationMs { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }
}
