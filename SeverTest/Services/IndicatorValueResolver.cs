using Microsoft.Extensions.Logging;
using ServerTest.Models;
using ServerTest.Models.Indicator;
using ServerTest.Models.Strategy;
using ServerTest.Strategy;

namespace ServerTest.Services
{
    public sealed class IndicatorValueResolver : IStrategyValueResolver
    {
        private readonly MarketDataEngine _marketDataEngine;
        private readonly IndicatorEngine _indicatorEngine;
        private readonly ILogger<IndicatorValueResolver> _logger;

        public IndicatorValueResolver(
            MarketDataEngine marketDataEngine,
            IndicatorEngine indicatorEngine,
            ILogger<IndicatorValueResolver> logger)
        {
            _marketDataEngine = marketDataEngine ?? throw new ArgumentNullException(nameof(marketDataEngine));
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

            if (method.Args == null || method.Args.Count < 2)
            {
                _logger.LogInformation(
                    "策略参数不足，无法解析条件: {Uid} Method={Method} ArgsCount={Count}",
                    context.Strategy.UidCode,
                    method.Method,
                    method.Args?.Count ?? 0);
                return false;
            }

            if (!TryResolveValue(context, method.Args[0], offset, out left))
            {
                return false;
            }

            return TryResolveValue(context, method.Args[1], offset, out right);
        }

        private bool TryResolveValue(
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

            return TryResolveField(context, reference, effectiveOffset, out value);
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

            var candles = _marketDataEngine.GetHistoryKlines(
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
                _logger.LogDebug("Invalid indicator reference for strategy {Uid}", context.Strategy.UidCode);
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

            return true;
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
