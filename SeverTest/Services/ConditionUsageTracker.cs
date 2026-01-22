using ServerTest.Models.Strategy;
using System.Collections.Concurrent;
using StrategyModel = ServerTest.Models.Strategy.Strategy;

namespace ServerTest.Services
{
    public sealed class ConditionUsageTracker
    {
        private readonly ConditionCacheService _cache;
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _strategyKeys = new();
        private readonly ConcurrentDictionary<string, int> _keyRefCounts = new();

        public ConditionUsageTracker(ConditionCacheService cache)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        public void UpsertStrategy(StrategyModel strategy)
        {
            if (strategy == null)
            {
                return;
            }

            var nextKeys = BuildConditionKeys(strategy).Distinct().ToList();
            var keySet = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
            foreach (var key in nextKeys)
            {
                keySet.TryAdd(key, 0);
            }

            if (_strategyKeys.TryGetValue(strategy.UidCode, out var existing))
            {
                UpdateRefCounts(existing.Keys, nextKeys);
                _strategyKeys[strategy.UidCode] = keySet;
            }
            else
            {
                foreach (var key in nextKeys)
                {
                    _keyRefCounts.AddOrUpdate(key, 1, (_, count) => count + 1);
                }

                _strategyKeys.TryAdd(strategy.UidCode, keySet);
            }
        }

        public void RemoveStrategy(string uidCode)
        {
            if (string.IsNullOrWhiteSpace(uidCode))
            {
                return;
            }

            if (!_strategyKeys.TryRemove(uidCode, out var keys))
            {
                return;
            }

            foreach (var key in keys.Keys)
            {
                _keyRefCounts.AddOrUpdate(key, 0, (_, count) => Math.Max(0, count - 1));
            }
        }

        public void PurgeUnused()
        {
            foreach (var pair in _keyRefCounts)
            {
                if (pair.Value > 0)
                {
                    continue;
                }

                if (_keyRefCounts.TryRemove(pair.Key, out _))
                {
                    _cache.Remove(pair.Key);
                }
            }
        }

        private void UpdateRefCounts(IEnumerable<string> existingKeys, IReadOnlyList<string> nextKeys)
        {
            var existingSet = new HashSet<string>(existingKeys, StringComparer.Ordinal);
            var nextSet = new HashSet<string>(nextKeys, StringComparer.Ordinal);

            foreach (var removed in existingSet.Except(nextSet))
            {
                _keyRefCounts.AddOrUpdate(removed, 0, (_, count) => Math.Max(0, count - 1));
            }

            foreach (var added in nextSet.Except(existingSet))
            {
                _keyRefCounts.AddOrUpdate(added, 1, (_, count) => count + 1);
            }
        }

        private static IEnumerable<string> BuildConditionKeys(StrategyModel strategy)
        {
            var trade = strategy.StrategyConfig?.Trade;
            var logic = strategy.StrategyConfig?.Logic;
            if (trade == null || logic == null)
            {
                yield break;
            }

            foreach (var key in EnumerateKeys(trade, logic.Entry.Long))
            {
                yield return key;
            }

            foreach (var key in EnumerateKeys(trade, logic.Entry.Short))
            {
                yield return key;
            }

            foreach (var key in EnumerateKeys(trade, logic.Exit.Long))
            {
                yield return key;
            }

            foreach (var key in EnumerateKeys(trade, logic.Exit.Short))
            {
                yield return key;
            }
        }

        private static IEnumerable<string> EnumerateKeys(TradeConfig trade, StrategyLogicBranch branch)
        {
            if (branch == null || branch.Containers == null)
            {
                yield break;
            }

            foreach (var container in branch.Containers)
            {
                if (container?.Checks?.Groups == null)
                {
                    continue;
                }

                foreach (var group in container.Checks.Groups)
                {
                    if (group?.Conditions == null)
                    {
                        continue;
                    }

                    foreach (var condition in group.Conditions)
                    {
                        if (condition == null)
                        {
                            continue;
                        }

                        var key = ConditionKeyBuilder.BuildKey(trade, condition);
                        yield return key.Id;
                    }
                }
            }
        }
    }
}
