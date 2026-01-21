using System.Collections.Concurrent;
using ServerTest.Models.Strategy;
using ServerTest.Strategy;

namespace ServerTest.Services
{
    public sealed class ConditionCacheService
    {
        private sealed class ConditionCacheEntry
        {
            public ConditionCacheEntry(string keyText)
            {
                KeyText = keyText;
            }

            public readonly object Sync = new();
            public string KeyText { get; }
            public long Timestamp;
            public bool Success;
            public string Message = string.Empty;
        }

        private readonly ConcurrentDictionary<string, ConditionCacheEntry> _cache = new();

        public bool TryGet(string keyId, long timestamp, out ConditionEvaluationResult result)
        {
            result = default;
            if (!_cache.TryGetValue(keyId, out var entry))
            {
                return false;
            }

            lock (entry.Sync)
            {
                if (entry.Timestamp != timestamp)
                {
                    return false;
                }

                result = new ConditionEvaluationResult(keyId, entry.Success, entry.Message);
                return true;
            }
        }

        public void Set(ConditionKey key, long timestamp, bool success, string message)
        {
            var entry = _cache.GetOrAdd(key.Id, _ => new ConditionCacheEntry(key.Text));
            lock (entry.Sync)
            {
                entry.Timestamp = timestamp;
                entry.Success = success;
                entry.Message = message ?? string.Empty;
            }
        }

        public void Remove(string keyId)
        {
            _cache.TryRemove(keyId, out _);
        }
    }
}
