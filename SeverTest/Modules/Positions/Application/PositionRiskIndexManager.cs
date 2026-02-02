using Microsoft.Extensions.Logging;
using ServerTest.Domain.Entities;
using ServerTest.Models;
using ServerTest.Modules.MarketStreaming.Application;
using ServerTest.Modules.Positions.Domain;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace ServerTest.Modules.Positions.Application
{
    public sealed class PositionRiskIndexManager
    {
        private readonly ConcurrentDictionary<string, SymbolRiskIndex> _indices = new();
        private readonly ConcurrentDictionary<long, string> _symbolKeyByPositionId = new();
        private readonly ILogger<PositionRiskIndexManager> _logger;

        public PositionRiskIndexManager(ILogger<PositionRiskIndexManager> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IReadOnlyList<SymbolRiskIndex> GetIndicesSnapshot()
        {
            return _indices.Values.ToList();
        }

        public IReadOnlyList<PositionRiskIndexSnapshot> BuildSnapshots()
        {
            var list = new List<PositionRiskIndexSnapshot>();
            foreach (var index in _indices.Values.OrderBy(item => item.Exchange).ThenBy(item => item.Symbol))
            {
                list.Add(index.BuildSnapshot());
            }

            return list;
        }

        public void UpsertPosition(StrategyPosition position, PositionRiskConfig? config)
        {
            if (position == null)
            {
                return;
            }

            if (!string.Equals(position.Status, "Open", StringComparison.OrdinalIgnoreCase))
            {
                RemovePosition(position.PositionId);
                return;
            }

            var exchange = MarketDataKeyNormalizer.NormalizeExchange(position.Exchange);
            var symbol = MarketDataKeyNormalizer.NormalizeSymbol(position.Symbol);
            if (string.IsNullOrWhiteSpace(exchange) || string.IsNullOrWhiteSpace(symbol))
            {
                _logger.LogWarning("风控索引更新失败：交易所/交易对无效 positionId={PositionId}", position.PositionId);
                return;
            }

            position.Exchange = exchange;
            position.Symbol = symbol;

            var key = BuildKey(exchange, symbol);
            if (_symbolKeyByPositionId.TryGetValue(position.PositionId, out var existingKey) && existingKey != key)
            {
                if (_indices.TryGetValue(existingKey, out var existingIndex))
                {
                    existingIndex.Remove(position.PositionId);
                }
            }

            var index = _indices.GetOrAdd(key, _ => new SymbolRiskIndex(exchange, symbol));
            _symbolKeyByPositionId[position.PositionId] = key;
            index.Upsert(new PositionRiskEntry(position, config));
        }

        public void RemovePosition(long positionId)
        {
            if (positionId <= 0)
            {
                return;
            }

            if (!_symbolKeyByPositionId.TryRemove(positionId, out var key))
            {
                return;
            }

            if (_indices.TryGetValue(key, out var index))
            {
                index.Remove(positionId);
                if (index.Count == 0)
                {
                    _indices.TryRemove(key, out _);
                }
            }
        }

        public bool TryGetEntry(long positionId, out PositionRiskEntry? entry)
        {
            entry = null;
            if (!_symbolKeyByPositionId.TryGetValue(positionId, out var key))
            {
                return false;
            }

            if (!_indices.TryGetValue(key, out var index))
            {
                return false;
            }

            return index.TryGetEntry(positionId, out entry);
        }

        public bool TryActivateTrailing(long positionId, decimal newStopPrice)
        {
            if (!_symbolKeyByPositionId.TryGetValue(positionId, out var key))
            {
                return false;
            }

            if (!_indices.TryGetValue(key, out var index))
            {
                return false;
            }

            return index.TryActivateTrailing(positionId, newStopPrice);
        }

        public bool TryUpdateTrailingStop(long positionId, decimal newStopPrice)
        {
            if (!_symbolKeyByPositionId.TryGetValue(positionId, out var key))
            {
                return false;
            }

            if (!_indices.TryGetValue(key, out var index))
            {
                return false;
            }

            return index.TryUpdateTrailingStop(positionId, newStopPrice);
        }

        public void RebuildFromPositions(IEnumerable<StrategyPosition> positions, Func<long, PositionRiskConfig?> configResolver)
        {
            if (positions == null)
            {
                return;
            }

            foreach (var position in positions)
            {
                if (position == null)
                {
                    continue;
                }

                PositionRiskConfig? config = null;
                if (configResolver != null)
                {
                    config = configResolver(position.PositionId);
                }

                UpsertPosition(position, config);
            }
        }

        private static string BuildKey(string exchange, string symbol)
        {
            return $"{exchange}|{symbol}";
        }
    }
}
