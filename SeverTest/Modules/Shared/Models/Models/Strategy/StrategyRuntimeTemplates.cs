using System.Collections.Generic;

namespace ServerTest.Models.Strategy
{
    /// <summary>
    /// 运行时间模板定义（服务端提供模板列表）
    /// </summary>
    public sealed class StrategyRuntimeTemplateDefinition
    {
        /// <summary>
        /// 模板 ID（策略仅保存该 ID）
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 模板名称
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 模板时区（IANA 或 Windows 时区 ID）
        /// </summary>
        public string Timezone { get; set; } = "UTC";

        /// <summary>
        /// 启用的星期（mon/tue/...）
        /// </summary>
        public List<string> Days { get; set; } = new();

        /// <summary>
        /// 时间段列表（允许分钟级）
        /// </summary>
        public List<StrategyRuntimeTimeRange> TimeRanges { get; set; } = new();

        /// <summary>
        /// 交易日历异常
        /// </summary>
        public List<StrategyRuntimeCalendarException> Calendar { get; set; } = new();
    }

    /// <summary>
    /// 交易日历异常（休市/覆盖/追加）
    /// </summary>
    public sealed class StrategyRuntimeCalendarException
    {
        /// <summary>
        /// 日期（yyyy-MM-dd，模板时区）
        /// </summary>
        public string Date { get; set; } = string.Empty;

        /// <summary>
        /// 类型：Closed / Override / Append
        /// </summary>
        public string Type { get; set; } = "Closed";

        /// <summary>
        /// 异常名称（可选）
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// 异常时间段（Override / Append 使用）
        /// </summary>
        public List<StrategyRuntimeTimeRange> TimeRanges { get; set; } = new();
    }

    /// <summary>
    /// 时区选项（前端展示）
    /// </summary>
    public sealed class StrategyRuntimeTimezoneOption
    {
        public string Value { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }
}
