using Microsoft.Extensions.Logging;
using ServerTest.Models;
using ServerTest.Models.Indicator;
using ServerTest.Models.Strategy;
using ServerTest.Modules.StrategyEngine.Domain;
using System.Globalization;
using ServerTest.Modules.MarketStreaming.Application;

namespace ServerTest.Modules.StrategyEngine.Application
{
    public sealed class IndicatorValueResolver : IStrategyValueResolver
    {
        private readonly IMarketDataProvider _marketDataProvider;
        private readonly IndicatorEngine _indicatorEngine;
        private readonly ILogger<IndicatorValueResolver> _logger;

        public IndicatorValueResolver(
            IMarketDataProvider marketDataProvider,
            IndicatorEngine indicatorEngine,
            ILogger<IndicatorValueResolver> logger)
        {
            _marketDataProvider = marketDataProvider ?? throw new ArgumentNullException(nameof(marketDataProvider));
            _indicatorEngine = indicatorEngine ?? throw new ArgumentNullException(nameof(indicatorEngine));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool TryResolvePair(
            StrategyExecutionContext context,
            StrategyMethod method,
            int offset,
            out double left,
            out double right)
        {
            left = double.NaN;
            right = double.NaN;
            if (!TryResolveValues(context, method, offset, 2, out var values))
            {
                return false;
            }

            left = values[0];
            right = values[1];
            return true;
        }

        public bool TryResolveValues(
            StrategyExecutionContext context,
            StrategyMethod method,
            int offset,
            int requiredCount,
            out double[] values)
        {
            values = Array.Empty<double>();
            if (requiredCount <= 0)
            {
                values = Array.Empty<double>();
                return true;
            }

            var result = new double[requiredCount];
            var argCount = method.Args?.Count ?? 0;
            var paramCount = method.Param?.Length ?? 0;

            for (var i = 0; i < requiredCount; i++)
            {
                if (method.Args != null && i < method.Args.Count)
                {
                    if (!TryResolveValue(context, method.Args[i], offset, out result[i]))
                    {
                        return false;
                    }
                    continue;
                }

                var paramIndex = i - argCount;
                if (paramIndex < 0)
                {
                    paramIndex = 0;
                }

                if (!TryResolveParamValue(context, method, paramIndex, out result[i]))
                {
                    _logger.LogInformation(
                        "策略参数不足，无法解析条件: {Uid} Method={Method} ArgsCount={ArgsCount} ParamCount={ParamCount} Need={Need}",
                        context.Strategy.UidCode,
                        method.Method,
                        argCount,
                        paramCount,
                        requiredCount);
                    return false;
                }
            }

            values = result;
            return true;
        }

        private bool TryResolveParamPair(
            StrategyExecutionContext context,
            StrategyMethod method,
            out double left,
            out double right)
        {
            left = double.NaN;
            right = double.NaN;

            if (!TryResolveParamValue(context, method, 0, out left))
            {
                return false;
            }

            return TryResolveParamValue(context, method, 1, out right);
        }

        private bool TryResolveParamValue(
            StrategyExecutionContext context,
            StrategyMethod method,
            int paramIndex,
            out double value)
        {
            value = double.NaN;
            if (method.Param == null || method.Param.Length <= paramIndex)
            {
                return false;
            }

            var raw = method.Param[paramIndex];
            var reference = new StrategyValueRef
            {
                RefType = "Const",
                Input = raw ?? string.Empty
            };

            return TryResolveConstant(context, reference, out value);
        }

        public bool TryResolveValue(
            StrategyExecutionContext context,
            StrategyValueRef reference,
            int offsetAdd,
            out double value)
        {
            value = double.NaN;
            if (reference == null)
            {
                return false;
            }

            var baseOffset = GetBaseOffset(reference);
            var effectiveOffset = baseOffset + Math.Max(0, offsetAdd);

            var refType = reference.RefType?.Trim() ?? string.Empty;
            if (refType.Equals("Indicator", StringComparison.OrdinalIgnoreCase))
            {
                return TryResolveIndicator(context, reference, effectiveOffset, out value);
            }

            if (refType.Equals("Const", StringComparison.OrdinalIgnoreCase) ||
                refType.Equals("Number", StringComparison.OrdinalIgnoreCase) ||
                refType.Equals("Constant", StringComparison.OrdinalIgnoreCase))
            {
                return TryResolveConstant(context, reference, out value);
            }

            return TryResolveField(context, reference, effectiveOffset, out value);
        }

        private bool TryResolveConstant(
            StrategyExecutionContext context,
            StrategyValueRef reference,
            out double value)
        {
            value = double.NaN;
            var raw = reference?.Input?.Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                value = 0d;
                return true;
            }

            if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            {
                value = parsed;
                LogResolvedConstant(context, reference, value);
                return true;
            }

            if (double.TryParse(raw, NumberStyles.Any, CultureInfo.CurrentCulture, out parsed))
            {
                value = parsed;
                LogResolvedConstant(context, reference, value);
                return true;
            }

            _logger.LogInformation(
                "常量解析失败: {Uid} input={Input}",
                context.Strategy.UidCode,
                reference?.Input);
            return false;
        }

        private bool TryResolveField(
            StrategyExecutionContext context,
            StrategyValueRef reference,
            int offset,
            out double value)
        {
            value = double.NaN;
            var trade = context.StrategyConfig.Trade;
            if (trade == null)
            {
                _logger.LogInformation(
                    "字段解析失败: 策略缺少Trade配置 {Uid}",
                    context.Strategy.UidCode);
                return false;
            }

            if (string.Equals(reference.CalcMode, "OnBarClose", StringComparison.OrdinalIgnoreCase) &&
                !context.Task.IsBarClose)
            {
                _logger.LogInformation(
                    "字段解析跳过: OnBarClose但当前为更新任务 {Uid} timeframe={Timeframe}",
                    context.Strategy.UidCode,
                    reference.Timeframe);
                return false;
            }

            var exchange = MarketDataKeyNormalizer.NormalizeExchange(trade.Exchange);
            var symbol = MarketDataKeyNormalizer.NormalizeSymbol(trade.Symbol);
            var timeframe = MarketDataKeyNormalizer.NormalizeTimeframe(reference.Timeframe);
            if (string.IsNullOrWhiteSpace(timeframe))
            {
                timeframe = MarketDataKeyNormalizer.TimeframeFromSeconds(trade.TimeframeSec);
            }

            if (string.IsNullOrWhiteSpace(exchange) ||
                string.IsNullOrWhiteSpace(symbol) ||
                string.IsNullOrWhiteSpace(timeframe))
            {
                _logger.LogInformation(
                    "字段解析失败: 交易所/币对/周期无效 {Uid} exchange={Exchange} symbol={Symbol} timeframe={Timeframe}",
                    context.Strategy.UidCode,
                    exchange,
                    symbol,
                    timeframe);
                return false;
            }

            long? endTimestamp = string.Equals(reference.CalcMode, "OnBarClose", StringComparison.OrdinalIgnoreCase)
                ? context.Task.CandleTimestamp
                : null;

            var candles = _marketDataProvider.GetHistoryKlines(
                exchange,
                timeframe,
                symbol,
                endTimestamp,
                offset + 1);

            if (candles.Count <= offset)
            {
                _logger.LogInformation(
                    "字段解析失败: K线数量不足 {Uid} needOffset={Offset} count={Count}",
                    context.Strategy.UidCode,
                    offset,
                    candles.Count);
                return false;
            }

            var index = candles.Count - 1 - offset;
            if (index < 0)
            {
                _logger.LogInformation(
                    "字段解析失败: 索引无效 {Uid} index={Index}",
                    context.Strategy.UidCode,
                    index);
                return false;
            }

            var candle = candles[index];
            value = TalibIndicatorCalculator.ResolveValue(candle, reference.Input ?? string.Empty);
            if (double.IsNaN(value))
            {
                _logger.LogInformation(
                    "字段解析失败: 值为NaN {Uid} input={Input}",
                    context.Strategy.UidCode,
                    reference.Input);
                return false;
            }

            LogResolvedField(context, reference, candle, value, offset);

            return true;
        }

        private bool TryResolveIndicator(
            StrategyExecutionContext context,
            StrategyValueRef reference,
            int offset,
            out double value)
        {
            value = double.NaN;
            var trade = context.StrategyConfig.Trade;
            if (trade == null)
            {
                _logger.LogInformation(
                    "指标解析失败: 策略缺少Trade配置 {Uid}",
                    context.Strategy.UidCode);
                return false;
            }

            if (string.Equals(reference.CalcMode, "OnBarClose", StringComparison.OrdinalIgnoreCase) &&
                !context.Task.IsBarClose)
            {
                _logger.LogInformation(
                    "指标解析跳过: OnBarClose但当前为更新任务 {Uid} indicator={Indicator} timeframe={Timeframe}",
                    context.Strategy.UidCode,
                    reference.Indicator,
                    reference.Timeframe);
                return false;
            }

            var request = IndicatorKeyFactory.BuildRequest(trade, reference);
            if (request == null)
            {
                _logger.LogDebug("策略 {Uid} 的指标引用无效", context.Strategy.UidCode);
                return false;
            }

            if (!_indicatorEngine.TryGetValue(request, context.Task, offset, out value))
            {
                _logger.LogInformation(
                    "指标解析失败: 无法取得指标值 {Uid} indicator={Indicator} offset={Offset}",
                    context.Strategy.UidCode,
                    reference.Indicator,
                    offset);
                return false;
            }

            LogResolvedIndicator(context, reference, request, value, offset);

            return true;
        }

        private void LogResolvedConstant(
            StrategyExecutionContext context,
            StrategyValueRef reference,
            double value)
        {
            _logger.LogInformation(
                "条件检测取值 固定值: {Uid} input={Input} value={Value:F6}",
                context.Strategy.UidCode,
                reference?.Input,
                value);
        }

        private void LogResolvedField(
            StrategyExecutionContext context,
            StrategyValueRef reference,
            ccxt.OHLCV? candle,
            double value,
            int offset)
        {
            var candleTimestamp = candle?.timestamp ?? 0;
            var timeText = FormatKlineTime(candleTimestamp);
            var open = candle?.open ?? 0;
            var high = candle?.high ?? 0;
            var low = candle?.low ?? 0;
            var close = candle?.close ?? 0;

            //_logger.LogInformation(
            //    "条件检测取值 K线: {Uid} field={Field} time={Time} open={Open:F4} high={High:F4} low={Low:F4} close={Close:F4} value={Value:F6} offset={Offset}",
            //    context.Strategy.UidCode,
            //    reference?.Input,
            //    timeText,
            //    open,
            //    high,
            //    low,
            //    close,
            //    value,
            //    offset)
        }

        private void LogResolvedIndicator(
            StrategyExecutionContext context,
            StrategyValueRef reference,
            IndicatorRequest request,
            double value,
            int offset)
        {
            _logger.LogInformation(
                "条件检测取值 指标: {Uid} indicator={Indicator} output={Output} timeframe={Timeframe} value={Value:F6} offset={Offset}",
                context.Strategy.UidCode,
                reference?.Indicator,
                reference?.Output,
                request?.Key.Timeframe,
                value,
                offset);
        }

        private static string FormatKlineTime(long timestamp)
        {
            if (timestamp <= 0)
            {
                return "N/A";
            }

            return DateTimeOffset.FromUnixTimeMilliseconds(timestamp)
                .ToLocalTime()
                .ToString("MM-dd HH:mm");
        }

        private static int GetBaseOffset(StrategyValueRef reference)
        {
            if (reference.OffsetRange == null || reference.OffsetRange.Length == 0)
            {
                return 0;
            }

            var min = reference.OffsetRange[0];
            var max = reference.OffsetRange.Length > 1 ? reference.OffsetRange[1] : min;
            return Math.Max(0, Math.Min(min, max));
        }
    }
}
