using ServerTest.Models.Strategy;
using ServerTest.Strategy;

namespace ServerTest.Services
{
    public sealed class ConditionEvaluator
    {
        private readonly ConditionCacheService _cache;

        public ConditionEvaluator(ConditionCacheService cache)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        public ConditionEvaluationResult Evaluate(StrategyExecutionContext context, StrategyMethod method)
        {
            var trade = context?.StrategyConfig?.Trade;
            if (trade == null)
            {
                return new ConditionEvaluationResult(string.Empty, false, "Trade config missing");
            }

            var key = ConditionKeyBuilder.BuildKey(trade, method);
            var timestamp = context.Task.CandleTimestamp;
            if (_cache.TryGet(key.Id, timestamp, out var cached))
            {
                return cached;
            }

            var result = ConditionMethodRegistry.Run(
                context,
                method,
                Array.Empty<ConditionEvaluationResult>());

            var message = result.Message?.ToString() ?? string.Empty;
            _cache.Set(key, timestamp, result.Success, message);
            return new ConditionEvaluationResult(key.Id, result.Success, message);
        }
    }
}
