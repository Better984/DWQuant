using ServerTest.Models.Strategy;
using System.Collections.Generic;

namespace ServerTest.Modules.StrategyEngine.Application
{
    /// <summary>
    /// 条件优先级拆分工具。
    /// 重要说明：此优先级映射属于高维护规则，只允许调整映射/列表，不允许改变 Required/Optional 语义。
    /// </summary>
    internal static class ConditionPriorityHelper
    {
        // P0：Cross/穿透类条件（高优先级，先判断以尽早剪枝）
        private static readonly HashSet<string> CrossMethods = new(StringComparer.OrdinalIgnoreCase)
        {
            "CrossUp",
            "CrossDown",
            "CrossOver",
            "CrossUnder",
            "CrossAny"
        };

        public static bool IsCrossMethod(StrategyMethod condition)
        {
            return condition != null
                   && !string.IsNullOrWhiteSpace(condition.Method)
                   && CrossMethods.Contains(condition.Method.Trim());
        }

        public static void SplitByPriority(
            IEnumerable<StrategyMethod> conditions,
            List<StrategyMethod> crossRequired,
            List<StrategyMethod> crossOptional,
            List<StrategyMethod> required,
            List<StrategyMethod> optional)
        {
            foreach (var condition in conditions)
            {
                if (condition == null || !condition.Enabled)
                {
                    continue;
                }

                var isCross = IsCrossMethod(condition);
                if (isCross)
                {
                    if (condition.Required)
                    {
                        crossRequired.Add(condition);
                    }
                    else
                    {
                        crossOptional.Add(condition);
                    }
                    continue;
                }

                if (condition.Required)
                {
                    required.Add(condition);
                }
                else
                {
                    optional.Add(condition);
                }
            }
        }
    }
}
