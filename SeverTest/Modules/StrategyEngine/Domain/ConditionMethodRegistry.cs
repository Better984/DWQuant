using ServerTest.Models.Strategy;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ServerTest.Modules.StrategyEngine.Domain
{
    public static class ConditionMethodRegistry
    {
        private static readonly Dictionary<string, ExecuteAction> Methods = new(StringComparer.Ordinal)
        {
            { "GreaterThanOrEqual", GreaterThanOrEqual },   // 大于等于：A >= B
            { "GreaterThan",        GreaterThan },          // 大于：A > B
            { "LessThan",           LessThan },             // 小于：A < B
            { "LessThanOrEqual",    LessThanOrEqual },      // 小于等于：A <= B
            { "Equal",              Equal },                // 等于：A == B
            { "NotEqual",           NotEqual },             // 不等于：A != B
            { "CrossUp",            CrossUp },              // 上穿：A 从下向上穿越 B
            { "CrossOver",          CrossUp },              // 上穿别名（兼容旧配置）
            { "CrossDown",          CrossDown },            // 下穿：A 从上向下穿越 B
            { "CrossUnder",         CrossDown },            // 下穿别名
            { "CrossAny",           CrossAny },             // 任意穿越：上穿或下穿都算
            { "Between",            Between },              // 区间内：low <= x <= high
            { "Outside",            Outside },              // 区间外：x < low 或 x > high
            { "Rising",             Rising },               // 连续上涨
            { "Falling",            Falling },              // 连续下跌
            { "AboveFor",           AboveFor },             // 连续高于阈值
            { "BelowFor",           BelowFor },             // 连续低于阈值
            { "ROC",                Roc },                  // 变化率（ROC）
            { "Slope",              Slope },                // 线性回归斜率
            { "TouchUpper",         TouchUpper },           // 触碰上轨（>=）
            { "TouchLower",         TouchLower },           // 触碰下轨（<=）
            { "BreakoutUp",         BreakoutUp },           // 突破上轨（>）
            { "BreakoutDown",       BreakoutDown },         // 突破下轨（<）
            { "ZScore",             ZScore },               // ZScore 超阈值
            { "StdDevGreater",      StdDevGreater },        // 标准差大于阈值
            { "StdDevLess",         StdDevLess },           // 标准差小于阈值
            { "BandwidthExpand",    BandwidthExpand },      // 带宽扩张
            { "BandwidthContract",  BandwidthContract }     // 带宽收敛
        };

        public static ExecuteAction? Get(string methodId)
        {
            return !string.IsNullOrWhiteSpace(methodId) && Methods.TryGetValue(methodId, out var action)
                ? action
                : null;
        }

        public static (bool Success, StringBuilder Message) Run(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            var action = Get(method.Method);
            if (action == null)
            {
                return BuildResult(method.Method, false, "未知的条件检测方法");
            }

            return action(context, method, triggerResults);
        }

        private static (bool Success, StringBuilder Message) GreaterThanOrEqual(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            return CompareValues(context, method, ">=", (left, right) => left >= right);
        }

        private static (bool Success, StringBuilder Message) GreaterThan(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            return CompareValues(context, method, ">", (left, right) => left > right);
        }

        private static (bool Success, StringBuilder Message) LessThan(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            return CompareValues(context, method, "<", (left, right) => left < right);
        }

        private static (bool Success, StringBuilder Message) LessThanOrEqual(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            return CompareValues(context, method, "<=", (left, right) => left <= right);
        }

        private static (bool Success, StringBuilder Message) Equal(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            return CompareValues(context, method, "==", (left, right) => Math.Abs(left - right) < 1e-10);
        }

        private static (bool Success, StringBuilder Message) NotEqual(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            return CompareValues(context, method, "!=", (left, right) => Math.Abs(left - right) >= 1e-10);
        }

        private static (bool Success, StringBuilder Message) CrossUp(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            if (!context.ValueResolver.TryResolvePair(context, method, 0, out var left, out var right))
            {
                return BuildResult(method.Method, false, "未配置取值解析器");
            }

            if (!context.ValueResolver.TryResolvePair(context, method, 1, out var prevLeft, out var prevRight))
            {
                return BuildResult(method.Method, false, "未配置偏移取值解析器");
            }

            bool crossed = prevLeft <= prevRight && left > right;
            var message = new StringBuilder()
                .Append(method.Method)
                .Append(": ")
                .Append(prevLeft.ToString("F4"))
                .Append("->")
                .Append(left.ToString("F4"))
                .Append(" 上穿 ")
                .Append(prevRight.ToString("F4"))
                .Append("->")
                .Append(right.ToString("F4"))
                .Append(" = ")
                .Append(crossed);
            return (crossed, message);
        }

        private static (bool Success, StringBuilder Message) CrossDown(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            if (!context.ValueResolver.TryResolvePair(context, method, 0, out var left, out var right))
            {
                return BuildResult(method.Method, false, "未配置取值解析器");
            }

            if (!context.ValueResolver.TryResolvePair(context, method, 1, out var prevLeft, out var prevRight))
            {
                return BuildResult(method.Method, false, "未配置偏移取值解析器");
            }

            bool crossed = prevLeft >= prevRight && left < right;
            var message = new StringBuilder()
                .Append(method.Method)
                .Append(": ")
                .Append(prevLeft.ToString("F4"))
                .Append("->")
                .Append(left.ToString("F4"))
                .Append(" 下穿 ")
                .Append(prevRight.ToString("F4"))
                .Append("->")
                .Append(right.ToString("F4"))
                .Append(" = ")
                .Append(crossed);
            return (crossed, message);
        }

        private static (bool Success, StringBuilder Message) CrossAny(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            if (!context.ValueResolver.TryResolvePair(context, method, 0, out var left, out var right))
            {
                return BuildResult(method.Method, false, "未配置取值解析器");
            }

            if (!context.ValueResolver.TryResolvePair(context, method, 1, out var prevLeft, out var prevRight))
            {
                return BuildResult(method.Method, false, "未配置偏移取值解析器");
            }

            bool crossed =
                (prevLeft <= prevRight && left > right) ||
                (prevLeft >= prevRight && left < right);
            var message = new StringBuilder()
                .Append(method.Method)
                .Append(": ")
                .Append(prevLeft.ToString("F4"))
                .Append("->")
                .Append(left.ToString("F4"))
                .Append(" 任意穿越 ")
                .Append(prevRight.ToString("F4"))
                .Append("->")
                .Append(right.ToString("F4"))
                .Append(" = ")
                .Append(crossed);
            return (crossed, message);
        }

        private static (bool Success, StringBuilder Message) Between(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            if (!TryResolveValues(context, method, 0, 3, out var values, out var error))
            {
                return BuildResult(method.Method, false, error);
            }

            var x = values[0];
            var low = values[1];
            var high = values[2];
            var min = Math.Min(low, high);
            var max = Math.Max(low, high);
            var success = x >= min && x <= max;
            var message = new StringBuilder()
                .Append(method.Method)
                .Append(": ")
                .Append(x.ToString("F4"))
                .Append(" in [")
                .Append(min.ToString("F4"))
                .Append(", ")
                .Append(max.ToString("F4"))
                .Append("] = ")
                .Append(success);
            return (success, message);
        }

        private static (bool Success, StringBuilder Message) Outside(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            if (!TryResolveValues(context, method, 0, 3, out var values, out var error))
            {
                return BuildResult(method.Method, false, error);
            }

            var x = values[0];
            var low = values[1];
            var high = values[2];
            var min = Math.Min(low, high);
            var max = Math.Max(low, high);
            var success = x < min || x > max;
            var message = new StringBuilder()
                .Append(method.Method)
                .Append(": ")
                .Append(x.ToString("F4"))
                .Append(" outside [")
                .Append(min.ToString("F4"))
                .Append(", ")
                .Append(max.ToString("F4"))
                .Append("] = ")
                .Append(success);
            return (success, message);
        }

        private static (bool Success, StringBuilder Message) Rising(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            return EvaluateMonotonic(context, method, isRising: true);
        }

        private static (bool Success, StringBuilder Message) Falling(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            return EvaluateMonotonic(context, method, isRising: false);
        }

        private static (bool Success, StringBuilder Message) AboveFor(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            return EvaluateHold(context, method, checkAbove: true);
        }

        private static (bool Success, StringBuilder Message) BelowFor(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            return EvaluateHold(context, method, checkAbove: false);
        }

        private static (bool Success, StringBuilder Message) Roc(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            if (!TryGetArgReference(method, 0, out var reference))
            {
                return BuildResult(method.Method, false, "未配置主序列参数");
            }

            if (!TryResolvePeriod(context, method, paramIndex: 0, fallbackArgIndex: 2, out var period, out var error))
            {
                return BuildResult(method.Method, false, error);
            }

            if (!TryResolveThreshold(context, method, argIndex: 1, paramIndex: 1, out var threshold, out error))
            {
                return BuildResult(method.Method, false, error);
            }

            if (!context.ValueResolver.TryResolveValue(context, reference, 0, out var current) ||
                !context.ValueResolver.TryResolveValue(context, reference, period, out var previous))
            {
                return BuildResult(method.Method, false, "无法获取变化率所需的历史数据");
            }

            if (Math.Abs(previous) < 1e-12)
            {
                return BuildResult(method.Method, false, "变化率基准值为0，无法计算");
            }

            var roc = current / previous - 1d;
            var success = roc > threshold;
            var message = new StringBuilder()
                .Append(method.Method)
                .Append(": ROC=")
                .Append(roc.ToString("F6"))
                .Append(" 阈值=")
                .Append(threshold.ToString("F6"))
                .Append(" N=")
                .Append(period)
                .Append(" = ")
                .Append(success);
            return (success, message);
        }

        private static (bool Success, StringBuilder Message) Slope(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            if (!TryGetArgReference(method, 0, out var reference))
            {
                return BuildResult(method.Method, false, "未配置主序列参数");
            }

            if (!TryResolvePeriod(context, method, paramIndex: 0, fallbackArgIndex: 2, out var period, out var error))
            {
                return BuildResult(method.Method, false, error);
            }

            if (!TryResolveThreshold(context, method, argIndex: 1, paramIndex: 1, out var threshold, out error))
            {
                return BuildResult(method.Method, false, error);
            }

            if (!TryResolveSeries(context, reference, period, out var series, out error))
            {
                return BuildResult(method.Method, false, error);
            }

            var slope = ComputeSlope(series);
            var success = slope > threshold;
            var message = new StringBuilder()
                .Append(method.Method)
                .Append(": Slope=")
                .Append(slope.ToString("F6"))
                .Append(" 阈值=")
                .Append(threshold.ToString("F6"))
                .Append(" N=")
                .Append(period)
                .Append(" = ")
                .Append(success);
            return (success, message);
        }

        private static (bool Success, StringBuilder Message) TouchUpper(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            return CompareValues(context, method, ">=", (left, right) => left >= right);
        }

        private static (bool Success, StringBuilder Message) TouchLower(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            return CompareValues(context, method, "<=", (left, right) => left <= right);
        }

        private static (bool Success, StringBuilder Message) BreakoutUp(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            return CompareValues(context, method, ">", (left, right) => left > right);
        }

        private static (bool Success, StringBuilder Message) BreakoutDown(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            return CompareValues(context, method, "<", (left, right) => left < right);
        }

        private static (bool Success, StringBuilder Message) ZScore(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            if (!TryGetArgReference(method, 0, out var reference))
            {
                return BuildResult(method.Method, false, "未配置主序列参数");
            }

            if (!TryResolvePeriod(context, method, paramIndex: 0, fallbackArgIndex: 2, out var period, out var error))
            {
                return BuildResult(method.Method, false, error);
            }

            if (!TryResolveThreshold(context, method, argIndex: 1, paramIndex: 1, out var threshold, out error))
            {
                return BuildResult(method.Method, false, error);
            }

            if (!TryResolveSeries(context, reference, period, out var series, out error))
            {
                return BuildResult(method.Method, false, error);
            }

            var (mean, std) = ComputeMeanStd(series);
            if (std <= 0)
            {
                return BuildResult(method.Method, false, "标准差为0，无法计算ZScore");
            }

            var z = (series[0] - mean) / std;
            var success = z > threshold;
            var message = new StringBuilder()
                .Append(method.Method)
                .Append(": Z=")
                .Append(z.ToString("F6"))
                .Append(" 阈值=")
                .Append(threshold.ToString("F6"))
                .Append(" N=")
                .Append(period)
                .Append(" = ")
                .Append(success);
            return (success, message);
        }

        private static (bool Success, StringBuilder Message) StdDevGreater(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            return EvaluateStdDev(context, method, checkGreater: true);
        }

        private static (bool Success, StringBuilder Message) StdDevLess(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            return EvaluateStdDev(context, method, checkGreater: false);
        }

        private static (bool Success, StringBuilder Message) BandwidthExpand(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            return EvaluateBandwidthTrend(context, method, isExpand: true);
        }

        private static (bool Success, StringBuilder Message) BandwidthContract(
            StrategyExecutionContext context,
            StrategyMethod method,
            IReadOnlyList<ConditionEvaluationResult> triggerResults)
        {
            return EvaluateBandwidthTrend(context, method, isExpand: false);
        }

        private static (bool Success, StringBuilder Message) CompareValues(
            StrategyExecutionContext context,
            StrategyMethod method,
            string op,
            Func<double, double, bool> comparison)
        {
            if (!context.ValueResolver.TryResolvePair(context, method, 0, out var left, out var right))
            {
                return BuildResult(method.Method, false, "未配置取值解析器");
            }

            bool success = comparison(left, right);
            var message = new StringBuilder()
                .Append(method.Method)
                .Append(": ")
                .Append(left.ToString("F4"))
                .Append(" ")
                .Append(op)
                .Append(" ")
                .Append(right.ToString("F4"))
                .Append(" = ")
                .Append(success);
            return (success, message);
        }

        private static (bool Success, StringBuilder Message) EvaluateMonotonic(
            StrategyExecutionContext context,
            StrategyMethod method,
            bool isRising)
        {
            if (!TryGetArgReference(method, 0, out var reference))
            {
                return BuildResult(method.Method, false, "未配置主序列参数");
            }

            if (!TryResolvePeriod(context, method, paramIndex: 0, fallbackArgIndex: 1, out var period, out var error))
            {
                return BuildResult(method.Method, false, error);
            }

            if (period < 1)
            {
                return BuildResult(method.Method, false, "窗口长度必须大于0");
            }

            if (!TryResolveSeries(context, reference, period + 1, out var series, out error))
            {
                return BuildResult(method.Method, false, error);
            }

            var success = true;
            for (var i = 0; i < period; i++)
            {
                if (isRising)
                {
                    if (series[i] <= series[i + 1])
                    {
                        success = false;
                        break;
                    }
                }
                else
                {
                    if (series[i] >= series[i + 1])
                    {
                        success = false;
                        break;
                    }
                }
            }

            var message = new StringBuilder()
                .Append(method.Method)
                .Append(": N=")
                .Append(period)
                .Append(" 当前=")
                .Append(series[0].ToString("F4"))
                .Append(" 最早=")
                .Append(series[^1].ToString("F4"))
                .Append(" = ")
                .Append(success);
            return (success, message);
        }

        private static (bool Success, StringBuilder Message) EvaluateHold(
            StrategyExecutionContext context,
            StrategyMethod method,
            bool checkAbove)
        {
            if (!TryGetArgReference(method, 0, out var valueRef))
            {
                return BuildResult(method.Method, false, "未配置主序列参数");
            }

            if (!TryResolvePeriod(context, method, paramIndex: 0, fallbackArgIndex: 2, out var period, out var error))
            {
                return BuildResult(method.Method, false, error);
            }

            if (period < 1)
            {
                return BuildResult(method.Method, false, "窗口长度必须大于0");
            }

            StrategyValueRef? levelRef = null;
            double? levelConst = null;
            if (TryGetArgReference(method, 1, out var thresholdRef))
            {
                levelRef = thresholdRef;
            }
            else if (TryParseParamDouble(method, 1, out var threshold))
            {
                levelConst = threshold;
            }
            else
            {
                return BuildResult(method.Method, false, "未配置阈值参数");
            }

            for (var i = 0; i < period; i++)
            {
                if (!context.ValueResolver.TryResolveValue(context, valueRef, i, out var value))
                {
                    return BuildResult(method.Method, false, "无法获取连续判断所需数据");
                }

                double levelValue;
                if (levelRef != null)
                {
                    if (!context.ValueResolver.TryResolveValue(context, levelRef, i, out levelValue))
                    {
                        return BuildResult(method.Method, false, "无法获取阈值序列数据");
                    }
                }
                else
                {
                    levelValue = levelConst ?? 0d;
                }

                if (checkAbove)
                {
                    if (value <= levelValue)
                    {
                        return BuildResult(method.Method, false, "连续条件未满足");
                    }
                }
                else
                {
                    if (value >= levelValue)
                    {
                        return BuildResult(method.Method, false, "连续条件未满足");
                    }
                }
            }

            var message = new StringBuilder()
                .Append(method.Method)
                .Append(": N=")
                .Append(period)
                .Append(checkAbove ? " 连续高于阈值" : " 连续低于阈值")
                .Append(" = ")
                .Append(true);
            return (true, message);
        }

        private static (bool Success, StringBuilder Message) EvaluateStdDev(
            StrategyExecutionContext context,
            StrategyMethod method,
            bool checkGreater)
        {
            if (!TryGetArgReference(method, 0, out var reference))
            {
                return BuildResult(method.Method, false, "未配置主序列参数");
            }

            if (!TryResolvePeriod(context, method, paramIndex: 0, fallbackArgIndex: 2, out var period, out var error))
            {
                return BuildResult(method.Method, false, error);
            }

            if (!TryResolveThreshold(context, method, argIndex: 1, paramIndex: 1, out var threshold, out error))
            {
                return BuildResult(method.Method, false, error);
            }

            if (!TryResolveSeries(context, reference, period, out var series, out error))
            {
                return BuildResult(method.Method, false, error);
            }

            var (_, std) = ComputeMeanStd(series);
            var success = checkGreater ? std > threshold : std < threshold;
            var message = new StringBuilder()
                .Append(method.Method)
                .Append(": StdDev=")
                .Append(std.ToString("F6"))
                .Append(" 阈值=")
                .Append(threshold.ToString("F6"))
                .Append(" N=")
                .Append(period)
                .Append(" = ")
                .Append(success);
            return (success, message);
        }

        private static (bool Success, StringBuilder Message) EvaluateBandwidthTrend(
            StrategyExecutionContext context,
            StrategyMethod method,
            bool isExpand)
        {
            if (!TryGetArgReference(method, 0, out var upperRef) ||
                !TryGetArgReference(method, 1, out var lowerRef) ||
                !TryGetArgReference(method, 2, out var middleRef))
            {
                return BuildResult(method.Method, false, "未配置带宽所需的上轨/下轨/中轨参数");
            }

            if (!TryResolvePeriod(context, method, paramIndex: 0, fallbackArgIndex: 3, out var period, out var error))
            {
                return BuildResult(method.Method, false, error);
            }

            if (period < 1)
            {
                return BuildResult(method.Method, false, "窗口长度必须大于0");
            }

            var count = period + 1;
            var series = new double[count];
            for (var i = 0; i < count; i++)
            {
                if (!context.ValueResolver.TryResolveValue(context, upperRef, i, out var upper) ||
                    !context.ValueResolver.TryResolveValue(context, lowerRef, i, out var lower) ||
                    !context.ValueResolver.TryResolveValue(context, middleRef, i, out var middle))
                {
                    return BuildResult(method.Method, false, "无法获取带宽所需数据");
                }

                if (Math.Abs(middle) < 1e-12)
                {
                    return BuildResult(method.Method, false, "中轨为0，无法计算带宽");
                }

                series[i] = (upper - lower) / middle;
            }

            var success = true;
            for (var i = 0; i < period; i++)
            {
                if (isExpand)
                {
                    if (series[i] <= series[i + 1])
                    {
                        success = false;
                        break;
                    }
                }
                else
                {
                    if (series[i] >= series[i + 1])
                    {
                        success = false;
                        break;
                    }
                }
            }

            var message = new StringBuilder()
                .Append(method.Method)
                .Append(": N=")
                .Append(period)
                .Append(" 当前=")
                .Append(series[0].ToString("F6"))
                .Append(" 最早=")
                .Append(series[^1].ToString("F6"))
                .Append(" = ")
                .Append(success);
            return (success, message);
        }

        private static bool TryResolveValues(
            StrategyExecutionContext context,
            StrategyMethod method,
            int offset,
            int requiredCount,
            out double[] values,
            out string error)
        {
            error = string.Empty;
            values = Array.Empty<double>();
            if (!context.ValueResolver.TryResolveValues(context, method, offset, requiredCount, out values))
            {
                error = "取值解析失败或参数不足";
                return false;
            }

            return true;
        }

        private static bool TryGetArgReference(
            StrategyMethod method,
            int index,
            out StrategyValueRef reference)
        {
            reference = null!;
            if (method.Args == null || method.Args.Count <= index || method.Args[index] == null)
            {
                return false;
            }

            reference = method.Args[index];
            return true;
        }

        private static bool TryResolveSeries(
            StrategyExecutionContext context,
            StrategyValueRef reference,
            int count,
            out double[] series,
            out string error)
        {
            error = string.Empty;
            series = Array.Empty<double>();
            if (count <= 0)
            {
                error = "窗口长度无效";
                return false;
            }

            var result = new double[count];
            for (var i = 0; i < count; i++)
            {
                if (!context.ValueResolver.TryResolveValue(context, reference, i, out result[i]))
                {
                    error = "无法获取连续数据";
                    return false;
                }
            }

            series = result;
            return true;
        }

        private static bool TryResolvePeriod(
            StrategyExecutionContext context,
            StrategyMethod method,
            int paramIndex,
            int fallbackArgIndex,
            out int period,
            out string error)
        {
            error = string.Empty;
            period = 0;
            if (TryParseParamDouble(method, paramIndex, out var raw))
            {
                period = Math.Max(0, (int)Math.Round(raw, MidpointRounding.AwayFromZero));
                if (period > 0)
                {
                    return true;
                }
            }

            if (fallbackArgIndex >= 0 && TryGetArgReference(method, fallbackArgIndex, out var reference))
            {
                if (context.ValueResolver.TryResolveValue(context, reference, 0, out var value))
                {
                    period = Math.Max(0, (int)Math.Round(value, MidpointRounding.AwayFromZero));
                    if (period > 0)
                    {
                        return true;
                    }
                }
            }

            error = "未配置有效的窗口长度";
            return false;
        }

        private static bool TryResolveThreshold(
            StrategyExecutionContext context,
            StrategyMethod method,
            int argIndex,
            int paramIndex,
            out double threshold,
            out string error)
        {
            error = string.Empty;
            threshold = 0d;

            if (TryGetArgReference(method, argIndex, out var reference))
            {
                if (context.ValueResolver.TryResolveValue(context, reference, 0, out threshold))
                {
                    return true;
                }
            }

            if (TryParseParamDouble(method, paramIndex, out threshold))
            {
                return true;
            }

            error = "未配置有效的阈值参数";
            return false;
        }

        private static bool TryParseParamDouble(StrategyMethod method, int index, out double value)
        {
            value = 0d;
            if (method.Param == null || method.Param.Length <= index)
            {
                return false;
            }

            var raw = method.Param[index];
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

        private static double ComputeSlope(double[] series)
        {
            if (series == null || series.Length < 2)
            {
                return 0d;
            }

            var n = series.Length;
            double sumX = 0;
            double sumY = 0;
            double sumXY = 0;
            double sumXX = 0;

            for (var i = 0; i < n; i++)
            {
                var x = i;
                var y = series[n - 1 - i];
                sumX += x;
                sumY += y;
                sumXY += x * y;
                sumXX += x * x;
            }

            var denominator = n * sumXX - sumX * sumX;
            if (Math.Abs(denominator) < 1e-12)
            {
                return 0d;
            }

            return (n * sumXY - sumX * sumY) / denominator;
        }

        private static (double Mean, double Std) ComputeMeanStd(double[] series)
        {
            if (series == null || series.Length == 0)
            {
                return (0d, 0d);
            }

            var mean = series.Average();
            var variance = 0d;
            for (var i = 0; i < series.Length; i++)
            {
                var diff = series[i] - mean;
                variance += diff * diff;
            }

            variance /= series.Length;
            return (mean, Math.Sqrt(variance));
        }

        private static (bool Success, StringBuilder Message) BuildResult(string method, bool success, string message)
        {
            var builder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(method))
            {
                builder.Append(method).Append(": ");
            }

            builder.Append(message);
            return (success, builder);
        }
    }
}
