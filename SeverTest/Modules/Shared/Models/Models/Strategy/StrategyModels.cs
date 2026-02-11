namespace ServerTest.Models.Strategy
{
    public enum StrategyState
    {
        Draft,
        Completed,
        Running,
        Paused,
        PausedOpenPosition,
        Deleted,
        Testing
    }

    public sealed class Strategy
    {
        public long Id { get; set; }
        public string UidCode { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public StrategyState State { get; set; } = StrategyState.Draft;
        public long CreatorUserId { get; set; }
        public long? ExchangeApiKeyId { get; set; }
        public long? VersionId { get; set; }
        public int Version { get; set; } = 1;

        public StrategyVisibility Visibility { get; set; } = new();
        public StrategySource Source { get; set; } = new();

        public List<string> PreposeUidCodes { get; set; } = new();
        public List<string> Tags { get; set; } = new();

        public StrategyConfig StrategyConfig { get; set; } = new();

        public StrategyTimestamps Timestamps { get; set; } = new();
    }

    public sealed class StrategyDocument
    {
        public StrategyUserStrategy UserStrategy { get; set; } = new();
        public StrategyDefinition Definition { get; set; } = new();
        public StrategyVersion Version { get; set; } = new();
    }

    public sealed class StrategyUserStrategy
    {
        public long UsId { get; set; }
        public long Uid { get; set; }
        public long DefId { get; set; }
        public string AliasName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string Visibility { get; set; } = "private";
        public string? ShareCode { get; set; }
        public decimal PriceUsdt { get; set; }
        public StrategySourceRef Source { get; set; } = new();
        public long PinnedVersionId { get; set; }
        public long? ExchangeApiKeyId { get; set; }
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    public sealed class StrategyDefinition
    {
        public long DefId { get; set; }
        public string DefType { get; set; } = "custom";
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public long CreatorUid { get; set; }
    }

    public sealed class StrategyVersion
    {
        public long VersionId { get; set; }
        public int VersionNo { get; set; } = 1;
        public string ContentHash { get; set; } = string.Empty;
        public string? ArtifactUri { get; set; }
        public StrategyConfig ConfigJson { get; set; } = new();
    }

    public sealed class StrategySourceRef
    {
        public string Type { get; set; } = "custom";
        public string? Ref { get; set; }
    }

    public sealed class StrategyVisibility
    {
        public bool IsPublic { get; set; }
        public decimal PriceUsdt { get; set; }
        public string? ShareCode { get; set; }
    }

    public sealed class StrategySource
    {
        public string Type { get; set; } = "custom";
        public long? SourceId { get; set; }
        public long? SourceCreatorUserId { get; set; }
    }

    public sealed class StrategyTimestamps
    {
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? DeletedAt { get; set; }
    }

    public sealed class StrategyConfig
    {
        public TradeConfig Trade { get; set; } = new();
        public StrategyLogic Logic { get; set; } = new();
        /// <summary>
        /// 策略运行时间配置
        /// </summary>
        public StrategyRuntimeConfig Runtime { get; set; } = new();
    }

    /// <summary>
    /// 策略运行时间配置
    /// </summary>
    public sealed class StrategyRuntimeConfig
    {
        /// <summary>
        /// 运行时间类型：Always / Template / Custom
        /// </summary>
        public string ScheduleType { get; set; } = "Always";

        /// <summary>
        /// 非运行时间的处理策略：BlockEntryAllowExit / BlockAll
        /// </summary>
        public string OutOfSessionPolicy { get; set; } = "BlockEntryAllowExit";
        /// <summary>
        /// 模板ID列表（当ScheduleType为Template时使用）
        /// </summary>
        public List<string> TemplateIds { get; set; } = new();

        /// <summary>
        /// 模板配置列表（当ScheduleType为Template时使用）
        /// </summary>
        public List<StrategyRuntimeTemplateConfig> Templates { get; set; } = new();

        /// <summary>
        /// 自定义时间配置
        /// </summary>
        public StrategyRuntimeCustomConfig Custom { get; set; } = new();
    }

    /// <summary>
    /// 自定义运行时间配置
    /// </summary>
    public sealed class StrategyRuntimeCustomConfig
    {
        /// <summary>
        /// 模式：Allow / Deny
        /// </summary>
        public string Mode { get; set; } = "Deny";

        /// <summary>
        /// 时区（IANA 或 Windows 时区 ID）
        /// </summary>
        public string Timezone { get; set; } = "Asia/Shanghai";

        /// <summary>
        /// 启用的星期（如 mon/tue/…）
        /// </summary>
        public List<string> Days { get; set; } = new();

        /// <summary>
        /// 时间段列表
        /// </summary>
        public List<StrategyRuntimeTimeRange> TimeRanges { get; set; } = new();
    }

    /// <summary>
    /// 模板运行时间配置
    /// </summary>
    public sealed class StrategyRuntimeTemplateConfig
    {
        /// <summary>
        /// 模板名称
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 模板时区（IANA 或 Windows 时区 ID）
        /// </summary>
        public string Timezone { get; set; } = "Asia/Shanghai";

        /// <summary>
        /// 启用的星期（如 mon/tue/…）
        /// </summary>
        public List<string> Days { get; set; } = new();

        /// <summary>
        /// 时间段列表
        /// </summary>
        public List<StrategyRuntimeTimeRange> TimeRanges { get; set; } = new();
    }

    /// <summary>
    /// 运行时间段
    /// </summary>
    public sealed class StrategyRuntimeTimeRange
    {
        /// <summary>
        /// 开始时间（HH:mm）
        /// </summary>
        public string Start { get; set; } = "00:00";

        /// <summary>
        /// 结束时间（HH:mm）
        /// </summary>
        public string End { get; set; } = "23:59";
    }

    public sealed class TradeConfig
    {
        public string Exchange { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public int TimeframeSec { get; set; }

        public string PositionMode { get; set; } = "Cross";
        public string OpenConflictPolicy { get; set; } = "GiveUp";

        public TradeSizing Sizing { get; set; } = new();
        public TradeRisk Risk { get; set; } = new();
    }

    public sealed class TradeSizing
    {
        public decimal OrderQty { get; set; }
        public decimal MaxPositionQty { get; set; }
        public int Leverage { get; set; }
    }

    public sealed class TradeRisk
    {
        public decimal TakeProfitPct { get; set; }
        public decimal StopLossPct { get; set; }
        public TradeTrailingStop Trailing { get; set; } = new();
    }

    public sealed class TradeTrailingStop
    {
        public bool Enabled { get; set; }
        public decimal ActivationProfitPct { get; set; }
        public decimal CloseOnDrawdownPct { get; set; }
    }

    public sealed class StrategyLogic
    {
        public StrategyLogicSide Entry { get; set; } = new();
        public StrategyLogicSide Exit { get; set; } = new();
    }

    public sealed class StrategyLogicSide
    {
        public StrategyLogicBranch Long { get; set; } = new();
        public StrategyLogicBranch Short { get; set; } = new();
    }

    public sealed class StrategyLogicBranch
    {
        public bool Enabled { get; set; } = true;
        public int MinPassConditionContainer { get; set; } = 1;
        public List<ConditionContainer> Containers { get; set; } = new();
        /// <summary>
        /// 开仓筛选器（仅用于 Entry 分支）
        /// </summary>
        public ConditionGroupSet? Filters { get; set; }
        public ActionSet OnPass { get; set; } = new();
    }

    public sealed class ConditionContainer
    {
        public ConditionGroupSet Checks { get; set; } = new();
    }

    public sealed class ConditionGroupSet
    {
        public bool Enabled { get; set; } = true;
        public int MinPassGroups { get; set; } = 1;
        public List<ConditionGroup> Groups { get; set; } = new();
    }

    public sealed class ConditionGroup
    {
        public bool Enabled { get; set; } = true;
        public int MinPassConditions { get; set; } = 1;
        public List<StrategyMethod> Conditions { get; set; } = new();
    }

    public sealed class ActionSet
    {
        public bool Enabled { get; set; } = true;
        public int MinPassConditions { get; set; } = 1;
        public List<StrategyMethod> Conditions { get; set; } = new();
    }

    public sealed class StrategyMethod
    {
        public bool Enabled { get; set; } = true;
        public bool Required { get; set; }
        public string Method { get; set; } = string.Empty;
        public string[] Param { get; set; } = System.Array.Empty<string>();
        public List<StrategyValueRef> Args { get; set; } = new();
    }

    public sealed class StrategyValueRef
    {
        public string RefType { get; set; } = "Field";
        public string Indicator { get; set; } = string.Empty;
        public string Timeframe { get; set; } = string.Empty;
        public string Input { get; set; } = string.Empty;
        public List<double> Params { get; set; } = new();
        public string Output { get; set; } = "Value";
        public int[] OffsetRange { get; set; } = new[] { 0, 0 };
        public string CalcMode { get; set; } = "OnBarClose";
    }
}
