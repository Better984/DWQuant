using System.Collections.Concurrent;

namespace ServerTest.Services
{
    public sealed class PositionRiskConfig
    {
        public string Side { get; init; } = "Long";
        public decimal? ActivationPct { get; init; }
        public decimal? DrawdownPct { get; init; }
    }

    public sealed class PositionRiskConfigStore
    {
        private readonly ConcurrentDictionary<long, PositionRiskConfig> _store = new();

        public void Upsert(long positionId, PositionRiskConfig config)
        {
            if (positionId <= 0 || config == null)
            {
                return;
            }

            _store[positionId] = config;
        }

        public bool TryGet(long positionId, out PositionRiskConfig? config)
        {
            return _store.TryGetValue(positionId, out config);
        }

        public void Remove(long positionId)
        {
            _store.TryRemove(positionId, out _);
        }
    }
}
