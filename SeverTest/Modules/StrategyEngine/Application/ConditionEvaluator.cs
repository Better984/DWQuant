using ServerTest.Models.Strategy;
using ServerTest.Modules.StrategyEngine.Domain;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace ServerTest.Modules.StrategyEngine.Application
{
    public sealed class ConditionEvaluator
    {
        private readonly ConditionCacheService _cache;
        private readonly ConcurrentDictionary<MethodCacheKey, ConditionKey> _methodKeyCache =
            new(MethodCacheKeyComparer.Instance);

        public ConditionEvaluator(ConditionCacheService cache)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        /// <summary>
        /// 清理指定策略的条件Key缓存，避免策略反复上下线时缓存持续增长。
        /// </summary>
        public void InvalidateStrategy(string uidCode)
        {
            if (string.IsNullOrWhiteSpace(uidCode))
            {
                return;
            }

            foreach (var pair in _methodKeyCache)
            {
                if (!string.Equals(pair.Key.StrategyUid, uidCode, StringComparison.Ordinal))
                {
                    continue;
                }

                _methodKeyCache.TryRemove(pair.Key, out _);
            }
        }

        public ConditionEvaluationResult Evaluate(StrategyExecutionContext context, StrategyMethod method)
        {
            if (context == null)
            {
                return new ConditionEvaluationResult(string.Empty, false, "Strategy context missing");
            }

            if (method == null)
            {
                return new ConditionEvaluationResult(string.Empty, false, "Strategy method missing");
            }

            var trade = context.StrategyConfig?.Trade;
            if (trade == null)
            {
                return new ConditionEvaluationResult(string.Empty, false, "Trade config missing");
            }

            var strategyUid = context.Strategy.UidCode ?? string.Empty;
            var cacheKey = new MethodCacheKey(strategyUid, method);
            if (!_methodKeyCache.TryGetValue(cacheKey, out var key))
            {
                key = ConditionKeyBuilder.BuildKey(trade, method);
                _methodKeyCache.TryAdd(cacheKey, key);
            }

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

        private readonly struct MethodCacheKey
        {
            public MethodCacheKey(string strategyUid, StrategyMethod method)
            {
                StrategyUid = strategyUid ?? string.Empty;
                Method = method ?? throw new ArgumentNullException(nameof(method));
            }

            public string StrategyUid { get; }
            public StrategyMethod Method { get; }
        }

        private sealed class MethodCacheKeyComparer : IEqualityComparer<MethodCacheKey>
        {
            public static MethodCacheKeyComparer Instance { get; } = new();

            public bool Equals(MethodCacheKey x, MethodCacheKey y)
            {
                return string.Equals(x.StrategyUid, y.StrategyUid, StringComparison.Ordinal)
                       && ReferenceEquals(x.Method, y.Method);
            }

            public int GetHashCode(MethodCacheKey obj)
            {
                return HashCode.Combine(
                    StringComparer.Ordinal.GetHashCode(obj.StrategyUid),
                    RuntimeHelpers.GetHashCode(obj.Method));
            }
        }
    }
}
