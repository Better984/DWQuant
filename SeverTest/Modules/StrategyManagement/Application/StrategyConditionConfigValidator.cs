using System.Globalization;
using System.Linq;
using ServerTest.Models;
using ServerTest.Models.Strategy;

namespace ServerTest.Modules.StrategyManagement.Application
{
    public static class StrategyConditionConfigValidator
    {
        private const int MaxGroupsPerSet = 3;
        private const int MaxConditionsPerGroup = 6;
        private const int MaxTotalConditions = 108;
        private const int MaxParamCount = 8;
        private const double MaxAbsNumeric = 1_000_000_000d;

        private static readonly Dictionary<string, int> ConditionMethodArgCounts = new(StringComparer.OrdinalIgnoreCase)
        {
            { "GreaterThanOrEqual", 2 },
            { "GreaterThan", 2 },
            { "LessThan", 2 },
            { "LessThanOrEqual", 2 },
            { "Equal", 2 },
            { "NotEqual", 2 },
            { "CrossUp", 2 },
            { "CrossOver", 2 },
            { "CrossDown", 2 },
            { "CrossUnder", 2 },
            { "CrossAny", 2 },
            { "Between", 3 },
            { "Outside", 3 },
            { "Rising", 1 },
            { "Falling", 1 },
            { "AboveFor", 2 },
            { "BelowFor", 2 },
            { "ROC", 2 },
            { "Slope", 2 },
            { "TouchUpper", 2 },
            { "TouchLower", 2 },
            { "BreakoutUp", 2 },
            { "BreakoutDown", 2 },
            { "ZScore", 2 },
            { "StdDevGreater", 2 },
            { "StdDevLess", 2 },
            { "BandwidthExpand", 3 },
            { "BandwidthContract", 3 },
        };

        private static readonly HashSet<string> AllowedRefTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Field",
            "Indicator",
            "PublicIndicator",
            "Const",
            "Number",
        };

        private static readonly HashSet<string> MainPaneIndicators = new(StringComparer.OrdinalIgnoreCase)
        {
            "MA",
            "MAVP",
            "SMA",
            "EMA",
            "WMA",
            "DEMA",
            "TEMA",
            "TRIMA",
            "KAMA",
            "MAMA",
            "FAMA",
            "T3",
            "BBANDS",
            "MIDPOINT",
            "MIDPRICE",
            "SAR",
            "SAREXT",
            "HT_TRENDLINE",
            "AVGPRICE",
            "MEDPRICE",
            "TYPPRICE",
            "WCLPRICE",
        };

        private enum PaneKind
        {
            None = 0,
            Main = 1,
            Sub = 2
        }

        public static bool TryValidate(StrategyConfig? config, out string error)
        {
            error = string.Empty;
            if (config?.Logic == null)
            {
                return true;
            }

            var defaultTimeframe = ResolveDefaultTimeframe(config.Trade?.TimeframeSec ?? 0);
            var totalConditions = 0;
            if (!TryValidateBranch(config.Logic.Entry?.Long, "Entry.Long", defaultTimeframe, ref totalConditions, out error))
            {
                return false;
            }
            if (!TryValidateBranch(config.Logic.Entry?.Short, "Entry.Short", defaultTimeframe, ref totalConditions, out error))
            {
                return false;
            }
            if (!TryValidateBranch(config.Logic.Exit?.Long, "Exit.Long", defaultTimeframe, ref totalConditions, out error))
            {
                return false;
            }
            if (!TryValidateBranch(config.Logic.Exit?.Short, "Exit.Short", defaultTimeframe, ref totalConditions, out error))
            {
                return false;
            }

            return true;
        }

        private static bool TryValidateBranch(
            StrategyLogicBranch? branch,
            string branchPath,
            string defaultTimeframe,
            ref int totalConditions,
            out string error)
        {
            error = string.Empty;
            if (branch == null)
            {
                return true;
            }

            if (branch.Filters != null &&
                !TryValidateGroupSet(branch.Filters, $"{branchPath}.Filters", defaultTimeframe, ref totalConditions, out error))
            {
                return false;
            }

            var containers = branch.Containers ?? new List<ConditionContainer>();
            for (var i = 0; i < containers.Count; i++)
            {
                var container = containers[i];
                if (container?.Checks == null)
                {
                    continue;
                }

                var path = $"{branchPath}.Containers[{i}]";
                if (!TryValidateGroupSet(container.Checks, path, defaultTimeframe, ref totalConditions, out error))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryValidateGroupSet(
            ConditionGroupSet? groupSet,
            string path,
            string defaultTimeframe,
            ref int totalConditions,
            out string error)
        {
            error = string.Empty;
            if (groupSet == null)
            {
                return true;
            }

            var groups = groupSet.Groups ?? new List<ConditionGroup>();
            if (groups.Count > MaxGroupsPerSet)
            {
                error = $"{path} 条件组数量超过限制（最多{MaxGroupsPerSet}组）";
                return false;
            }

            for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                var group = groups[groupIndex];
                var conditions = group?.Conditions ?? new List<StrategyMethod>();
                if (conditions.Count > MaxConditionsPerGroup)
                {
                    error = $"{path}.Groups[{groupIndex}] 条件数量超过限制（最多{MaxConditionsPerGroup}条）";
                    return false;
                }

                var duplicateGuard = new HashSet<string>(StringComparer.Ordinal);
                for (var conditionIndex = 0; conditionIndex < conditions.Count; conditionIndex++)
                {
                    totalConditions += 1;
                    if (totalConditions > MaxTotalConditions)
                    {
                        error = $"策略条件总数超过限制（最多{MaxTotalConditions}条）";
                        return false;
                    }

                    var condition = conditions[conditionIndex];
                    var conditionPath = $"{path}.Groups[{groupIndex}].Conditions[{conditionIndex}]";
                    if (!TryValidateCondition(condition, conditionPath, defaultTimeframe, out error))
                    {
                        return false;
                    }

                    var fingerprint = BuildConditionFingerprint(condition, defaultTimeframe);
                    if (!duplicateGuard.Add(fingerprint))
                    {
                        error = $"{conditionPath} 与同组其他条件重复，不允许保存";
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool TryValidateCondition(
            StrategyMethod? condition,
            string path,
            string defaultTimeframe,
            out string error)
        {
            error = string.Empty;
            if (condition == null)
            {
                error = $"{path} 条件为空";
                return false;
            }

            var method = NormalizeMethodAlias(condition.Method);
            if (string.IsNullOrWhiteSpace(method))
            {
                error = $"{path} 条件方法为空";
                return false;
            }

            if (!ConditionMethodArgCounts.TryGetValue(method, out var expectedArgsCount))
            {
                error = $"{path} 使用了不支持的条件方法：{condition.Method}";
                return false;
            }

            var args = condition.Args ?? new List<StrategyValueRef>();
            if (args.Count < expectedArgsCount)
            {
                error = $"{path} 参数数量不足，期望 {expectedArgsCount}，实际 {args.Count}";
                return false;
            }

            var refs = args.Take(expectedArgsCount).ToArray();
            for (var i = 0; i < refs.Length; i++)
            {
                if (!TryValidateReference(refs[i], $"{path}.Args[{i}]", defaultTimeframe, out error))
                {
                    return false;
                }
            }

            if (expectedArgsCount >= 2)
            {
                if (!TryValidateReferencePair(refs[0], refs[1], path, defaultTimeframe, out error))
                {
                    return false;
                }
            }

            if (expectedArgsCount >= 3)
            {
                if (!TryValidateReferencePair(refs[0], refs[2], path, defaultTimeframe, out error))
                {
                    return false;
                }

                if (BuildReferenceSemanticKey(refs[1], defaultTimeframe) == BuildReferenceSemanticKey(refs[2], defaultTimeframe))
                {
                    error = $"{path} 第二参数与第三参数不能相同";
                    return false;
                }
            }

            if (!TryValidateParams(condition.Param, path, out error))
            {
                return false;
            }

            return true;
        }

        private static bool TryValidateReference(
            StrategyValueRef? reference,
            string path,
            string defaultTimeframe,
            out string error)
        {
            error = string.Empty;
            if (reference == null)
            {
                error = $"{path} 参数为空";
                return false;
            }

            var refType = (reference.RefType ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(refType) || !AllowedRefTypes.Contains(refType))
            {
                error = $"{path} 引用类型不支持：{reference.RefType}";
                return false;
            }

            if (IsConstRef(reference))
            {
                if (!TryParseNumber(reference.Input, out var value))
                {
                    error = $"{path} 常量值无效：{reference.Input}";
                    return false;
                }

                if (Math.Abs(value) > MaxAbsNumeric)
                {
                    error = $"{path} 常量值过大，超过限制";
                    return false;
                }

                return true;
            }

            if (IsFieldRef(reference))
            {
                if (string.IsNullOrWhiteSpace(reference.Input))
                {
                    error = $"{path} 字段引用缺少 input";
                    return false;
                }
                return true;
            }

            if (IsIndicatorRef(reference))
            {
                var hasIndicator = !string.IsNullOrWhiteSpace(reference.Indicator);
                var hasInput = !string.IsNullOrWhiteSpace(reference.Input);
                if (!hasIndicator && !hasInput)
                {
                    error = $"{path} 指标引用缺少标识";
                    return false;
                }

                var indicatorTimeframe = NormalizeTimeframe(reference.Timeframe);
                if (!string.IsNullOrWhiteSpace(indicatorTimeframe) &&
                    !string.IsNullOrWhiteSpace(defaultTimeframe) &&
                    !string.Equals(indicatorTimeframe, defaultTimeframe, StringComparison.OrdinalIgnoreCase))
                {
                    error = $"{path} 指标周期必须与策略周期一致（{defaultTimeframe}）";
                    return false;
                }
            }

            return true;
        }

        private static bool TryValidateReferencePair(
            StrategyValueRef left,
            StrategyValueRef right,
            string path,
            string defaultTimeframe,
            out string error)
        {
            error = string.Empty;

            if (IsConstRef(left) && IsConstRef(right))
            {
                error = $"{path} 不允许数字与数字直接比较";
                return false;
            }

            if (BuildReferenceSemanticKey(left, defaultTimeframe) == BuildReferenceSemanticKey(right, defaultTimeframe))
            {
                error = $"{path} 不允许同一指标或字段与自身比较";
                return false;
            }

            if (IsFieldRef(left) && IsFieldRef(right))
            {
                error = $"{path} 不允许K线字段互相比较";
                return false;
            }

            if (IsIndicatorRef(left) && IsIndicatorRef(right))
            {
                var leftTimeframe = ResolveReferenceTimeframe(left, defaultTimeframe);
                var rightTimeframe = ResolveReferenceTimeframe(right, defaultTimeframe);
                if (!string.IsNullOrWhiteSpace(leftTimeframe) &&
                    !string.IsNullOrWhiteSpace(rightTimeframe) &&
                    !string.Equals(leftTimeframe, rightTimeframe, StringComparison.OrdinalIgnoreCase))
                {
                    error = $"{path} 不允许跨周期指标直接比较（{leftTimeframe} vs {rightTimeframe}）";
                    return false;
                }
            }

            if (!IsConstRef(left) && !IsConstRef(right))
            {
                var leftPane = ResolvePaneKind(left);
                var rightPane = ResolvePaneKind(right);
                if (leftPane != PaneKind.None && rightPane != PaneKind.None && leftPane != rightPane)
                {
                    error = $"{path} 不允许主图与副图指标直接比较";
                    return false;
                }
            }

            return true;
        }

        private static bool TryValidateParams(string[]? rawParams, string path, out string error)
        {
            error = string.Empty;
            if (rawParams == null || rawParams.Length == 0)
            {
                return true;
            }

            if (rawParams.Length > MaxParamCount)
            {
                error = $"{path} 参数数量超过限制（最多{MaxParamCount}个）";
                return false;
            }

            for (var i = 0; i < rawParams.Length; i++)
            {
                var raw = (rawParams[i] ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                if (!TryParseNumber(raw, out var value))
                {
                    error = $"{path}.Param[{i}] 不是有效数值：{rawParams[i]}";
                    return false;
                }

                if (Math.Abs(value) > MaxAbsNumeric)
                {
                    error = $"{path}.Param[{i}] 数值超过限制";
                    return false;
                }
            }

            return true;
        }

        private static string BuildConditionFingerprint(StrategyMethod condition, string defaultTimeframe)
        {
            var method = NormalizeMethodAlias(condition.Method);
            var args = condition.Args ?? new List<StrategyValueRef>();
            var argKeys = args.Select(arg => BuildReferenceSemanticKey(arg, defaultTimeframe));
            var paramKeys = (condition.Param ?? Array.Empty<string>())
                .Select(NormalizeNumberString);
            return $"{method}|{string.Join("#", argKeys)}|{string.Join(",", paramKeys)}";
        }

        private static string BuildReferenceSemanticKey(StrategyValueRef? reference, string? defaultTimeframe = null)
        {
            if (reference == null)
            {
                return string.Empty;
            }

            if (IsConstRef(reference))
            {
                return $"const|{NormalizeNumberString(reference.Input)}";
            }

            if (IsFieldRef(reference))
            {
                return $"field|{NormalizeUpper(reference.Input)}|{NormalizeCalcMode(reference.CalcMode)}";
            }

            if (IsIndicatorRef(reference))
            {
                var paramText = reference.Params == null
                    ? string.Empty
                    : string.Join(",", reference.Params.Select(value =>
                    {
                        var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                        return number.ToString(CultureInfo.InvariantCulture);
                    }));

                return string.Join("|",
                    "indicator",
                    NormalizeUpper(reference.Indicator),
                    NormalizeUpper(ResolveReferenceTimeframe(reference, defaultTimeframe ?? string.Empty)),
                    NormalizeUpper(reference.Input),
                    NormalizeUpper(reference.Output),
                    paramText,
                    NormalizeCalcMode(reference.CalcMode));
            }

            return string.Join("|",
                NormalizeLower(reference.RefType),
                NormalizeUpper(reference.Indicator),
                NormalizeUpper(ResolveReferenceTimeframe(reference, defaultTimeframe ?? string.Empty)),
                NormalizeUpper(reference.Input),
                NormalizeUpper(reference.Output),
                NormalizeCalcMode(reference.CalcMode));
        }

        private static PaneKind ResolvePaneKind(StrategyValueRef reference)
        {
            if (IsConstRef(reference))
            {
                return PaneKind.None;
            }

            if (IsFieldRef(reference))
            {
                return PaneKind.Main;
            }

            if (IsIndicatorRef(reference))
            {
                var indicator = (reference.Indicator ?? string.Empty).Trim();
                return MainPaneIndicators.Contains(indicator) ? PaneKind.Main : PaneKind.Sub;
            }

            return PaneKind.None;
        }

        private static bool IsConstRef(StrategyValueRef reference)
        {
            var refType = NormalizeLower(reference.RefType);
            return refType == "const" || refType == "number";
        }

        private static bool IsFieldRef(StrategyValueRef reference)
        {
            return NormalizeLower(reference.RefType) == "field";
        }

        private static bool IsIndicatorRef(StrategyValueRef reference)
        {
            var refType = NormalizeLower(reference.RefType);
            return refType == "indicator" || refType == "publicindicator";
        }

        private static bool TryParseNumber(string? raw, out double value)
        {
            value = 0d;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }

            return double.TryParse(raw, NumberStyles.Any, CultureInfo.CurrentCulture, out value);
        }

        private static string NormalizeMethodAlias(string? method)
        {
            var value = (method ?? string.Empty).Trim();
            if (value.Equals("CrossOver", StringComparison.OrdinalIgnoreCase))
            {
                return "CrossUp";
            }

            if (value.Equals("CrossUnder", StringComparison.OrdinalIgnoreCase))
            {
                return "CrossDown";
            }

            return value;
        }

        private static string NormalizeUpper(string? raw)
        {
            return (raw ?? string.Empty).Trim().ToUpperInvariant();
        }

        private static string NormalizeLower(string? raw)
        {
            return (raw ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static string ResolveDefaultTimeframe(int timeframeSec)
        {
            return NormalizeTimeframe(MarketDataKeyNormalizer.TimeframeFromSeconds(timeframeSec));
        }

        private static string ResolveReferenceTimeframe(StrategyValueRef reference, string defaultTimeframe)
        {
            var timeframe = NormalizeTimeframe(reference.Timeframe);
            return string.IsNullOrWhiteSpace(timeframe) ? defaultTimeframe : timeframe;
        }

        private static string NormalizeTimeframe(string? raw)
        {
            return MarketDataKeyNormalizer.NormalizeTimeframe(raw ?? string.Empty);
        }

        private static string NormalizeCalcMode(string? raw)
        {
            var value = NormalizeUpper(raw);
            return string.IsNullOrWhiteSpace(value) ? "ONBARCLOSE" : value;
        }

        private static string NormalizeNumberString(string? raw)
        {
            if (!TryParseNumber(raw, out var value))
            {
                return (raw ?? string.Empty).Trim();
            }
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
