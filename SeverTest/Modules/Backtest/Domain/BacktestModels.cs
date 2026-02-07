using ServerTest.Models.Strategy;
using System.Collections.Generic;

namespace ServerTest.Modules.Backtest.Domain
{
    /// <summary>
    /// 鍥炴祴璇锋眰浣?
    /// </summary>
    public sealed class BacktestRunRequest
    {
        /// <summary>
        /// 绛栫暐瀹炰緥ID锛堝彲閫夛級
        /// </summary>
        public long? UsId { get; set; }

        /// <summary>
        /// 鐩存帴浼犲叆绛栫暐閰嶇疆锛圝SON瀛楃涓诧紝鍙€夛級
        /// </summary>
        public string? ConfigJson { get; set; }

        /// <summary>
        /// 浜ゆ槗鎵€锛堝锛歜inance锛?
        /// </summary>
        public string Exchange { get; set; } = string.Empty;

        /// <summary>
        /// 鍥炴祴鏍囩殑鍒楄〃锛堜负绌哄垯浣跨敤绛栫暐閰嶇疆涓殑 symbol锛?
        /// </summary>
        public List<string> Symbols { get; set; } = new();

        /// <summary>
        /// 绛栫暐鍛ㄦ湡锛堝锛?m/5m锛屽彲閫夛紝鏈～鍒欏彇绛栫暐閰嶇疆锛?
        /// </summary>
        public string? Timeframe { get; set; }

        /// <summary>
        /// 鍥炴祴寮€濮嬫椂闂达紙yyyy-MM-dd HH:mm:ss锛屽彲閫夛級
        /// </summary>
        public string? StartTime { get; set; }

        /// <summary>
        /// 鍥炴祴缁撴潫鏃堕棿锛坹yyy-MM-dd HH:mm:ss锛屽彲閫夛級
        /// </summary>
        public string? EndTime { get; set; }

        /// <summary>
        /// 鍥炴祴鏍规暟锛堝彲閫夛級
        /// </summary>
        public int? BarCount { get; set; }

        /// <summary>
        /// 鍒濆璧勯噾锛堢敤浜庢敹鐩婄巼鍙ｅ緞锛?
        /// </summary>
        public decimal InitialCapital { get; set; } = 0m;

        /// <summary>
        /// 瑕嗙洊鍗曟涓嬪崟鏁伴噺锛堜负绌哄垯浣跨敤绛栫暐閰嶇疆 trade.sizing.orderQty锛?
        /// </summary>
        public decimal? OrderQtyOverride { get; set; }

        /// <summary>
        /// 瑕嗙洊鏉犳潌锛堜负绌哄垯浣跨敤绛栫暐閰嶇疆锛?
        /// </summary>
        public int? LeverageOverride { get; set; }

        /// <summary>
        /// 瑕嗙洊姝㈢泩鐧惧垎姣旓紙涓虹┖鍒欎娇鐢ㄧ瓥鐣ラ厤缃級
        /// </summary>
        public decimal? TakeProfitPctOverride { get; set; }

        /// <summary>
        /// 瑕嗙洊姝㈡崯鐧惧垎姣旓紙涓虹┖鍒欎娇鐢ㄧ瓥鐣ラ厤缃級
        /// </summary>
        public decimal? StopLossPctOverride { get; set; }

        /// <summary>
        /// 鎵嬬画璐圭巼锛堥粯璁?0.04% = 0.0004锛?
        /// </summary>
        public decimal FeeRate { get; set; } = 0.0004m;

        /// <summary>
        /// 璧勯噾璐圭巼锛堥粯璁?0锛?
        /// </summary>
        public decimal FundingRate { get; set; } = 0m;

        /// <summary>
        /// 鍥哄畾婊戠偣锛坆ps锛?
        /// </summary>
        public int SlippageBps { get; set; } = 0;

        /// <summary>
        /// 鏄惁鑷姩鍙嶅悜
        /// </summary>
        public bool AutoReverse { get; set; }

        /// <summary>
        /// 杩愯鏃堕棿绐楀彛锛堝彲閫夛紝鏈～鍒欎娇鐢ㄧ瓥鐣ラ厤缃級
        /// </summary>
        public StrategyRuntimeConfig? Runtime { get; set; }

        /// <summary>
        /// 鏄惁鍚敤绛栫暐杩愯鏃堕棿闂ㄧ锛堥粯璁ゅ惎鐢級
        /// </summary>
        public bool UseStrategyRuntime { get; set; } = true;

        /// <summary>
        /// 杈撳嚭閫夐」锛堝彲瑁佸壀锛?
        /// </summary>
        public BacktestOutputOptions? Output { get; set; }
    }

    /// <summary>
    /// 鍥炴祴杈撳嚭閫夐」
    /// </summary>
    public sealed class BacktestOutputOptions
    {
        public bool IncludeTrades { get; set; } = true;
        public bool IncludeEquityCurve { get; set; } = true;
        public bool IncludeEvents { get; set; } = true;
        public string EquityCurveGranularity { get; set; } = "1m";
    }

    /// <summary>
    /// 鍥炴祴缁撴灉
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
        /// 鍥炴祴鑰楁椂锛堟绉掞級
        /// </summary>
        public long DurationMs { get; set; }
        public BacktestStats TotalStats { get; set; } = new();
        public List<BacktestSymbolResult> Symbols { get; set; } = new();
    }

    /// <summary>
    /// 鍗曟爣鐨勫洖娴嬬粨鏋?
    /// </summary>
    public sealed class BacktestSymbolResult
    {
        public string Symbol { get; set; } = string.Empty;
        public int Bars { get; set; }
        public decimal InitialCapital { get; set; }
        public BacktestStats Stats { get; set; } = new();
        public List<BacktestTrade> Trades { get; set; } = new();
        public List<BacktestEquityPoint> EquityCurve { get; set; } = new();
        public List<BacktestEvent> Events { get; set; } = new();
    }

    /// <summary>
    /// 浜ゆ槗鏄庣粏
    /// </summary>
    public sealed class BacktestTrade
    {
        public string Symbol { get; set; } = string.Empty;
        public string Side { get; set; } = string.Empty; // Long/Short
        public long EntryTime { get; set; }
        public long ExitTime { get; set; }
        public decimal EntryPrice { get; set; }
        public decimal ExitPrice { get; set; }
        public decimal Qty { get; set; }
        public decimal ContractSize { get; set; }
        public decimal Fee { get; set; }
        public decimal PnL { get; set; }
        public string ExitReason { get; set; } = string.Empty;
        public int SlippageBps { get; set; }
    }

    /// <summary>
    /// 璧勯噾/鏉冪泭鏇茬嚎鑺傜偣
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
    /// 鍥炴祴浜嬩欢
    /// </summary>
    public sealed class BacktestEvent
    {
        public long Timestamp { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 缁熻鎸囨爣
    /// </summary>
    public sealed class BacktestStats
    {
        public decimal TotalProfit { get; set; }
        public decimal TotalReturn { get; set; }
        public decimal MaxDrawdown { get; set; }
        public decimal WinRate { get; set; }
        public int TradeCount { get; set; }
        public decimal AvgProfit { get; set; }
        public decimal ProfitFactor { get; set; }
        public decimal AvgWin { get; set; }
        public decimal AvgLoss { get; set; }
    }

    /// <summary>
    /// 回测实时推送上下文（用于关联 reqId 与用户）
    /// </summary>
    public sealed class BacktestProgressContext
    {
        public long? UserId { get; set; }
        public string? ReqId { get; set; }
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
    }
}


