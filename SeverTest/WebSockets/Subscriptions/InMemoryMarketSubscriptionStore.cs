using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace ServerTest.WebSockets.Subscriptions
{
    public sealed class InMemoryMarketSubscriptionStore : IMarketSubscriptionStore
    {
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _subscriptions =
            new(StringComparer.Ordinal);

        public void Subscribe(string userId, IEnumerable<string> symbols)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return;
            }

            var set = _subscriptions.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase));
            foreach (var symbol in symbols)
            {
                if (string.IsNullOrWhiteSpace(symbol))
                {
                    continue;
                }

                set.TryAdd(symbol.Trim(), 0);
            }
        }

        public void Unsubscribe(string userId, IEnumerable<string> symbols)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return;
            }

            if (!_subscriptions.TryGetValue(userId, out var set))
            {
                return;
            }

            foreach (var symbol in symbols)
            {
                if (string.IsNullOrWhiteSpace(symbol))
                {
                    continue;
                }

                set.TryRemove(symbol.Trim(), out _);
            }

            if (set.IsEmpty)
            {
                _subscriptions.TryRemove(userId, out _);
            }
        }

        public IReadOnlyCollection<string> GetSubscriptions(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Array.Empty<string>();
            }

            return _subscriptions.TryGetValue(userId, out var set) ? set.Keys.ToList() : Array.Empty<string>();
        }

        public IReadOnlyDictionary<string, IReadOnlyCollection<string>> GetAllSubscriptions()
        {
            var snapshot = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal);
            foreach (var entry in _subscriptions)
            {
                snapshot[entry.Key] = entry.Value.Keys.ToList();
            }
            return snapshot;
        }
    }
}
