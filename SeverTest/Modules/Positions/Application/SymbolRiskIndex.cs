using System;
using System.Collections.Generic;
using System.Linq;

namespace ServerTest.Modules.Positions.Application
{
    public sealed class SymbolRiskIndex
    {
        private readonly object _sync = new();
        private readonly Dictionary<long, PositionRiskEntry> _entries = new();
        private readonly PriceRangeIndex _stopLossIndex = new();
        private readonly PriceRangeIndex _takeProfitIndex = new();
        private readonly PriceRangeIndex _trailingStopIndex = new();
        private readonly PriceRangeIndex _trailingActivationIndex = new();
        private readonly PriceRangeIndex _trailingUpdateIndex = new();
        private decimal? _lastPrice;

        public SymbolRiskIndex(string exchange, string symbol)
        {
            Exchange = exchange;
            Symbol = symbol;
        }

        public string Exchange { get; }
        public string Symbol { get; }
        public int Count
        {
            get
            {
                lock (_sync)
                {
                    return _entries.Count;
                }
            }
        }

        public decimal? UpdateLastPrice(decimal lastPrice)
        {
            lock (_sync)
            {
                var previous = _lastPrice;
                _lastPrice = lastPrice;
                return previous;
            }
        }

        public void Upsert(PositionRiskEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            lock (_sync)
            {
                if (_entries.TryGetValue(entry.PositionId, out var existing))
                {
                    RemoveInternal(existing);
                }

                _entries[entry.PositionId] = entry;
                IndexEntry(entry);
            }
        }

        public bool Remove(long positionId)
        {
            lock (_sync)
            {
                if (!_entries.TryGetValue(positionId, out var entry))
                {
                    return false;
                }

                RemoveInternal(entry);
                _entries.Remove(positionId);
                return true;
            }
        }

        public bool TryGetEntry(long positionId, out PositionRiskEntry? entry)
        {
            lock (_sync)
            {
                return _entries.TryGetValue(positionId, out entry);
            }
        }

        public IReadOnlyCollection<long> QueryCandidates(decimal rangeLow, decimal rangeHigh)
        {
            var results = new HashSet<long>();

            lock (_sync)
            {
                _stopLossIndex.Query(rangeLow, rangeHigh, results);
                _takeProfitIndex.Query(rangeLow, rangeHigh, results);
                _trailingStopIndex.Query(rangeLow, rangeHigh, results);
                _trailingActivationIndex.Query(rangeLow, rangeHigh, results);
                _trailingUpdateIndex.Query(rangeLow, rangeHigh, results);
            }

            return results;
        }

        public PositionRiskIndexSnapshot BuildSnapshot()
        {
            lock (_sync)
            {
                var snapshot = new PositionRiskIndexSnapshot
                {
                    Exchange = Exchange,
                    Symbol = Symbol,
                    TotalPositions = _entries.Count,
                    GeneratedAt = DateTime.UtcNow
                };

                foreach (var entry in _entries.Values.OrderBy(item => item.PositionId))
                {
                    snapshot.Positions.Add(new RiskPositionSnapshot
                    {
                        PositionId = entry.PositionId,
                        Uid = entry.Uid,
                        UsId = entry.UsId,
                        ExchangeApiKeyId = entry.ExchangeApiKeyId,
                        Exchange = entry.Exchange,
                        Symbol = entry.Symbol,
                        Side = entry.Side,
                        EntryPrice = entry.EntryPrice,
                        Qty = entry.Qty,
                        StopLossPrice = entry.StopLossPrice,
                        TakeProfitPrice = entry.TakeProfitPrice,
                        TrailingEnabled = entry.TrailingEnabled,
                        TrailingStopPrice = entry.TrailingStopPrice,
                        TrailingTriggered = entry.TrailingTriggered,
                        TrailingActivationPct = entry.TrailingActivationPct,
                        TrailingDrawdownPct = entry.TrailingDrawdownPct,
                        TrailingActivationPrice = entry.TrailingActivationPrice,
                        TrailingUpdateThresholdPrice = entry.TrailingUpdateThresholdPrice,
                        Status = entry.Status
                    });
                }

                snapshot.IndexTrees.Add(_stopLossIndex.BuildSnapshot("StopLoss"));
                snapshot.IndexTrees.Add(_takeProfitIndex.BuildSnapshot("TakeProfit"));
                snapshot.IndexTrees.Add(_trailingStopIndex.BuildSnapshot("TrailingStop"));
                snapshot.IndexTrees.Add(_trailingActivationIndex.BuildSnapshot("TrailingActivation"));
                snapshot.IndexTrees.Add(_trailingUpdateIndex.BuildSnapshot("TrailingUpdate"));

                return snapshot;
            }
        }

        public bool TryActivateTrailing(long positionId, decimal newStopPrice)
        {
            lock (_sync)
            {
                if (!_entries.TryGetValue(positionId, out var entry))
                {
                    return false;
                }

                _trailingActivationIndex.Remove(positionId);
                entry.SetTrailingStopPrice(newStopPrice);
                if (entry.TrailingStopPrice.HasValue)
                {
                    _trailingStopIndex.Upsert(positionId, entry.TrailingStopPrice.Value);
                }

                if (entry.TrailingUpdateThresholdPrice.HasValue)
                {
                    _trailingUpdateIndex.Upsert(positionId, entry.TrailingUpdateThresholdPrice.Value);
                }

                return true;
            }
        }

        public bool TryUpdateTrailingStop(long positionId, decimal newStopPrice)
        {
            lock (_sync)
            {
                if (!_entries.TryGetValue(positionId, out var entry))
                {
                    return false;
                }

                _trailingStopIndex.Remove(positionId);
                _trailingUpdateIndex.Remove(positionId);
                entry.SetTrailingStopPrice(newStopPrice);
                if (entry.TrailingStopPrice.HasValue)
                {
                    _trailingStopIndex.Upsert(positionId, entry.TrailingStopPrice.Value);
                }

                if (entry.TrailingUpdateThresholdPrice.HasValue)
                {
                    _trailingUpdateIndex.Upsert(positionId, entry.TrailingUpdateThresholdPrice.Value);
                }

                return true;
            }
        }

        private void RemoveInternal(PositionRiskEntry entry)
        {
            _stopLossIndex.Remove(entry.PositionId);
            _takeProfitIndex.Remove(entry.PositionId);
            _trailingStopIndex.Remove(entry.PositionId);
            _trailingActivationIndex.Remove(entry.PositionId);
            _trailingUpdateIndex.Remove(entry.PositionId);
        }

        private void IndexEntry(PositionRiskEntry entry)
        {
            if (entry.StopLossPrice.HasValue)
            {
                _stopLossIndex.Upsert(entry.PositionId, entry.StopLossPrice.Value);
            }

            if (entry.TakeProfitPrice.HasValue)
            {
                _takeProfitIndex.Upsert(entry.PositionId, entry.TakeProfitPrice.Value);
            }

            if (!entry.TrailingEnabled)
            {
                return;
            }

            if (!entry.HasTrailingConfig)
            {
                if (entry.TrailingStopPrice.HasValue)
                {
                    _trailingStopIndex.Upsert(entry.PositionId, entry.TrailingStopPrice.Value);
                }

                return;
            }

            if (!entry.TrailingStopPrice.HasValue)
            {
                if (entry.TrailingActivationPrice.HasValue && entry.TrailingActivationPrice.Value > 0)
                {
                    _trailingActivationIndex.Upsert(entry.PositionId, entry.TrailingActivationPrice.Value);
                }

                return;
            }

            _trailingStopIndex.Upsert(entry.PositionId, entry.TrailingStopPrice.Value);
            if (entry.TrailingUpdateThresholdPrice.HasValue)
            {
                _trailingUpdateIndex.Upsert(entry.PositionId, entry.TrailingUpdateThresholdPrice.Value);
            }
        }
    }
}
