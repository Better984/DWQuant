using Microsoft.Extensions.Logging;
using ServerTest.Domain.Entities;
using ServerTest.Models;
using ServerTest.Modules.MarketData.Infrastructure;
using ServerTest.Modules.Positions.Infrastructure;
using System.Text.RegularExpressions;

namespace ServerTest.Modules.Positions.Application
{
    /// <summary>
    /// 重启后按开仓后 K 线回放重算 trailing 参数，恢复尚未激活的追踪止损状态。
    /// </summary>
    public sealed class TrailingRecoveryService
    {
        private static readonly Regex SafeIdentifier = new("^[a-z0-9_]+$", RegexOptions.Compiled);

        private readonly HistoricalMarketDataRepository _klineRepository;
        private readonly StrategyPositionRepository _positionRepository;
        private readonly ILogger<TrailingRecoveryService> _logger;

        public TrailingRecoveryService(
            HistoricalMarketDataRepository klineRepository,
            StrategyPositionRepository positionRepository,
            ILogger<TrailingRecoveryService> logger)
        {
            _klineRepository = klineRepository ?? throw new ArgumentNullException(nameof(klineRepository));
            _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 对尚未激活 trailing 的仓位回放 K 线重算，若得出 trailing_stop_price 则更新 DB 并返回该值（调用方可更新 position 对象）。
        /// </summary>
        public async Task<decimal?> ReplayAndUpdateAsync(StrategyPosition position, CancellationToken ct = default)
        {
            if (position == null || !position.TrailingEnabled)
            {
                return null;
            }

            if (position.TrailingStopPrice.HasValue)
            {
                return null;
            }

            var activationPct = position.TrailingActivationPct ?? 0m;
            var drawdownPct = position.TrailingDrawdownPct ?? 0m;
            if (activationPct <= 0 || drawdownPct <= 0 || drawdownPct >= 1)
            {
                return null;
            }

            var exchange = MarketDataKeyNormalizer.NormalizeExchange(position.Exchange);
            var symbol = MarketDataKeyNormalizer.NormalizeSymbol(position.Symbol);
            if (string.IsNullOrWhiteSpace(exchange) || string.IsNullOrWhiteSpace(symbol))
            {
                return null;
            }

            var tableName = BuildKlineTableName(exchange, symbol, "1m");
            var exists = await _klineRepository.TableExistsAsync(tableName, ct).ConfigureAwait(false);
            if (!exists)
            {
                _logger.LogDebug("历史 K 线表不存在，跳过 trailing 回放: positionId={PositionId} table={Table}",
                    position.PositionId, tableName);
                return null;
            }

            var openedAtMs = new DateTimeOffset(position.OpenedAt).ToUnixTimeMilliseconds();
            var endMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            const int maxBars = 5000;

            var rows = await _klineRepository.QueryRangeAsync(tableName, openedAtMs, endMs, maxBars, ct)
                .ConfigureAwait(false);
            if (rows == null || rows.Count == 0)
            {
                return null;
            }

            var activationPrice = BuildActivationPrice(position.EntryPrice, activationPct, position.Side);
            if (activationPrice <= 0)
            {
                return null;
            }

            decimal? computedStop = null;
            var isLong = position.Side.Equals("Long", StringComparison.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                var high = row.High ?? 0m;
                var low = row.Low ?? 0m;

                if (!computedStop.HasValue)
                {
                    var activated = isLong ? high >= activationPrice : low <= activationPrice;
                    if (!activated)
                    {
                        continue;
                    }

                    var favorablePrice = isLong ? high : low;
                    computedStop = isLong
                        ? favorablePrice * (1 - drawdownPct)
                        : favorablePrice * (1 + drawdownPct);
                }
                else
                {
                    var updateThreshold = isLong
                        ? computedStop.Value / (1 - drawdownPct)
                        : computedStop.Value / (1 + drawdownPct);
                    var shouldUpdate = isLong ? high >= updateThreshold : low <= updateThreshold;
                    if (shouldUpdate)
                    {
                        var favorablePrice = isLong ? high : low;
                        var updatedStop = isLong
                            ? Math.Max(computedStop.Value, favorablePrice * (1 - drawdownPct))
                            : Math.Min(computedStop.Value, favorablePrice * (1 + drawdownPct));
                        computedStop = updatedStop;
                    }
                }
            }

            if (!computedStop.HasValue)
            {
                return null;
            }

            var affected = await _positionRepository.UpdateTrailingAsync(position.PositionId, computedStop.Value, ct)
                .ConfigureAwait(false);
            if (affected > 0)
            {
                _logger.LogInformation(
                    "trailing 回放重算完成: positionId={PositionId} uid={Uid} usId={UsId} 新stop={Stop}",
                    position.PositionId, position.Uid, position.UsId, computedStop.Value);
            }

            return computedStop;
        }

        private static decimal BuildActivationPrice(decimal entryPrice, decimal activationPct, string side)
        {
            if (entryPrice <= 0 || activationPct <= 0)
            {
                return 0m;
            }

            return side.Equals("Long", StringComparison.OrdinalIgnoreCase)
                ? entryPrice * (1 + activationPct)
                : entryPrice * (1 - activationPct);
        }

        private static string BuildKlineTableName(string exchangeId, string symbol, string timeframe)
        {
            var exchangeKey = MarketDataKeyNormalizer.NormalizeExchange(exchangeId);
            var symbolKey = MarketDataKeyNormalizer.NormalizeSymbol(symbol);
            var timeframeKey = MarketDataKeyNormalizer.NormalizeTimeframe(timeframe);

            var symbolPart = symbolKey.Replace("/", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
            if (!SafeIdentifier.IsMatch(exchangeKey) || !SafeIdentifier.IsMatch(symbolPart) || !SafeIdentifier.IsMatch(timeframeKey))
            {
                throw new InvalidOperationException("Invalid market data identifier for trailing replay.");
            }

            return $"{exchangeKey}_futures_{symbolPart}_{timeframeKey}";
        }
    }
}
