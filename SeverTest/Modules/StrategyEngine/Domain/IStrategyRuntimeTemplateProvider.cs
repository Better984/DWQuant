using System.Collections.Generic;
using ServerTest.Models.Strategy;

namespace ServerTest.Modules.StrategyEngine.Domain
{
    /// <summary>
    /// 运行时间模板读取器（供运行时与配置校验使用）
    /// </summary>
    public interface IStrategyRuntimeTemplateProvider
    {
        /// <summary>
        /// 模板列表快照
        /// </summary>
        IReadOnlyList<StrategyRuntimeTemplateDefinition> Templates { get; }

        /// <summary>
        /// 时区选项
        /// </summary>
        IReadOnlyList<StrategyRuntimeTimezoneOption> Timezones { get; }

        /// <summary>
        /// 根据模板 ID 获取模板
        /// </summary>
        bool TryGet(string id, out StrategyRuntimeTemplateDefinition template);
    }
}
