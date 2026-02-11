using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServerTest.Models;
using ServerTest.Modules.Positions.Infrastructure;
using ServerTest.Modules.TradingExecution.Domain;
using ServerTest.Modules.MarketStreaming.Application;

namespace ServerTest.Modules.Positions.Application
{
    public sealed class PositionRiskEngine : BackgroundService
    {
        private readonly StrategyPositionRepository _positionRepository;
        private readonly MarketDataEngine _marketDataEngine;
        private readonly IOrderExecutor _orderExecutor;
        private readonly PositionRiskConfigStore _riskConfigStore;
        private readonly PositionRiskIndexManager _riskIndexManager;
        private readonly ILogger<PositionRiskEngine> _logger;

        private static readonly TimeSpan LoopDelay = TimeSpan.FromSeconds(1);

        public PositionRiskEngine(
            StrategyPositionRepository positionRepository,
            MarketDataEngine marketDataEngine,
            IOrderExecutor orderExecutor,
            PositionRiskConfigStore riskConfigStore,
            PositionRiskIndexManager riskIndexManager,
            ILogger<PositionRiskEngine> logger)
        {
            _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
            _marketDataEngine = marketDataEngine ?? throw new ArgumentNullException(nameof(marketDataEngine));
            _orderExecutor = orderExecutor ?? throw new ArgumentNullException(nameof(orderExecutor));
            _riskConfigStore = riskConfigStore ?? throw new ArgumentNullException(nameof(riskConfigStore));
            _riskIndexManager = riskIndexManager ?? throw new ArgumentNullException(nameof(riskIndexManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await _marketDataEngine.WaitForInitializationAsync();
            await InitializeIndexAsync(stoppingToken).ConfigureAwait(false);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "仓位风险循环失败");
                }

                await Task.Delay(LoopDelay, stoppingToken).ConfigureAwait(false);
            }
        }

        private async Task InitializeIndexAsync(CancellationToken ct)
        {
            var openPositions = await _positionRepository.ListOpenAsync(ct).ConfigureAwait(false);
            _riskIndexManager.RebuildFromPositions(
                openPositions,
                positionId => _riskConfigStore.TryGet(positionId, out var config) ? config : null);

            _logger.LogInformation("风控索引初始化完成: 仓位数={Count}", openPositions.Count);
        }

        private async Task RunOnceAsync(CancellationToken ct)
        {
            var indices = _riskIndexManager.GetIndicesSnapshot();
            if (indices.Count == 0)
            {
                return;
            }

            foreach (var index in indices)
            {
                var kline = _marketDataEngine.GetLatestKline(index.Exchange, "1m", index.Symbol);
                if (kline == null)
                {
                    continue;
                }

                var close = kline.Value.close ?? kline.Value.open;
                var high = kline.Value.high ?? close;
                var low = kline.Value.low ?? close;

                if (!high.HasValue || !low.HasValue || !close.HasValue)
                {
                    continue;
                }

                var lastPrice = Convert.ToDecimal(close.Value);
                var highPrice = Convert.ToDecimal(high.Value);
                var lowPrice = Convert.ToDecimal(low.Value);

                // 使用上一次价格 + 当前高低点构建区间，避免秒级波动跨桶遗漏
                var previous = index.UpdateLastPrice(lastPrice);
                var rangeLow = Math.Min(lowPrice, lastPrice);
                var rangeHigh = Math.Max(highPrice, lastPrice);
                if (previous.HasValue)
                {
                    rangeLow = Math.Min(rangeLow, previous.Value);
                    rangeHigh = Math.Max(rangeHigh, previous.Value);
                }

                var candidates = index.QueryCandidates(rangeLow, rangeHigh);
                if (candidates.Count == 0)
                {
                    continue;
                }

                foreach (var positionId in candidates)
                {
                    if (!index.TryGetEntry(positionId, out var entry) || entry == null)
                    {
                        continue;
                    }

                    await EvaluateEntryAsync(entry, rangeLow, rangeHigh, ct)
                        .ConfigureAwait(false);
                }
            }
        }

        private async Task EvaluateEntryAsync(
            PositionRiskEntry entry,
            decimal rangeLow,
            decimal rangeHigh,
            CancellationToken ct)
        {
            if (entry == null)
            {
                return;
            }

            if (!string.Equals(entry.Status, "Open", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var side = entry.Side;
            var stopLossHit = CheckStopLoss(entry, rangeHigh, rangeLow, side);
            var takeProfitHit = CheckTakeProfit(entry, rangeHigh, rangeLow, side);

            var trailingHit = false;
            if (entry.TrailingEnabled)
            {
                trailingHit = await EvaluateTrailingAsync(entry, rangeLow, rangeHigh, ct).ConfigureAwait(false);
            }

            var shouldClose = stopLossHit || takeProfitHit || trailingHit;
            if (!shouldClose)
            {
                return;
            }

            var closeReason = trailingHit
                ? "TrailingStop"
                : stopLossHit
                    ? "StopLoss"
                    : "TakeProfit";

            var orderSide = side.Equals("Long", StringComparison.OrdinalIgnoreCase) ? "sell" : "buy";
            var orderResult = await _orderExecutor.PlaceMarketOrderAsync(new OrderExecutionRequest
            {
                Uid = entry.Uid,
                ExchangeApiKeyId = entry.ExchangeApiKeyId,
                Exchange = entry.Exchange,
                Symbol = entry.Symbol,
                Side = orderSide,
                Qty = entry.Qty,
                ReduceOnly = true
            }, ct).ConfigureAwait(false);

            if (!orderResult.Success)
            {
                _logger.LogWarning("风险平仓订单失败: positionId={PositionId} 错误={Error}", entry.PositionId, orderResult.ErrorMessage);
                return;
            }

            await _positionRepository.CloseAsync(
                    entry.PositionId,
                    trailingTriggered: trailingHit,
                    closedAt: DateTime.UtcNow,
                    closeReason,
                    orderResult.AveragePrice,
                    ct)
                .ConfigureAwait(false);
            _riskConfigStore.Remove(entry.PositionId);
            _riskIndexManager.RemovePosition(entry.PositionId);

            _logger.LogInformation("仓位因风险平仓: id={PositionId} uid={Uid} usId={UsId}", entry.PositionId, entry.Uid, entry.UsId);
        }

        private static bool CheckStopLoss(PositionRiskEntry entry, decimal high, decimal low, string side)
        {
            if (!entry.StopLossPrice.HasValue)
            {
                return false;
            }

            return side.Equals("Long", StringComparison.OrdinalIgnoreCase)
                ? low <= entry.StopLossPrice.Value
                : high >= entry.StopLossPrice.Value;
        }

        private static bool CheckTakeProfit(PositionRiskEntry entry, decimal high, decimal low, string side)
        {
            if (!entry.TakeProfitPrice.HasValue)
            {
                return false;
            }

            return side.Equals("Long", StringComparison.OrdinalIgnoreCase)
                ? high >= entry.TakeProfitPrice.Value
                : low <= entry.TakeProfitPrice.Value;
        }

        private async Task<bool> EvaluateTrailingAsync(
            PositionRiskEntry entry,
            decimal rangeLow,
            decimal rangeHigh,
            CancellationToken ct)
        {
            if (!entry.TrailingEnabled)
            {
                return false;
            }

            if (!entry.HasTrailingConfig)
            {
                if (!entry.TrailingStopPrice.HasValue)
                {
                    return false;
                }

                return entry.Side.Equals("Long", StringComparison.OrdinalIgnoreCase)
                    ? rangeLow <= entry.TrailingStopPrice.Value
                    : rangeHigh >= entry.TrailingStopPrice.Value;
            }

            var activationPct = entry.TrailingActivationPct ?? 0m;
            var drawdownPct = entry.TrailingDrawdownPct ?? 0m;
            if (activationPct <= 0 || drawdownPct <= 0 || drawdownPct >= 1)
            {
                return false;
            }

            if (!entry.TrailingStopPrice.HasValue)
            {
                var activationPrice = entry.TrailingActivationPrice ?? 0m;
                if (activationPrice <= 0)
                {
                    return false;
                }

                var activated = entry.Side.Equals("Long", StringComparison.OrdinalIgnoreCase)
                    ? rangeHigh >= activationPrice
                    : rangeLow <= activationPrice;

                if (!activated)
                {
                    return false;
                }

                var favorablePrice = entry.Side.Equals("Long", StringComparison.OrdinalIgnoreCase)
                    ? rangeHigh
                    : rangeLow;

                var initialStop = entry.Side.Equals("Long", StringComparison.OrdinalIgnoreCase)
                    ? favorablePrice * (1 - drawdownPct)
                    : favorablePrice * (1 + drawdownPct);

                var rows = await _positionRepository.UpdateTrailingAsync(entry.PositionId, initialStop, ct).ConfigureAwait(false);
                if (rows <= 0)
                {
                    return false;
                }

                _riskIndexManager.TryActivateTrailing(entry.PositionId, initialStop);

                return entry.Side.Equals("Long", StringComparison.OrdinalIgnoreCase)
                    ? rangeLow <= initialStop
                    : rangeHigh >= initialStop;
            }

            if (entry.TrailingUpdateThresholdPrice.HasValue)
            {
                var updateThreshold = entry.TrailingUpdateThresholdPrice.Value;
                var shouldUpdate = entry.Side.Equals("Long", StringComparison.OrdinalIgnoreCase)
                    ? rangeHigh >= updateThreshold
                    : rangeLow <= updateThreshold;

                if (shouldUpdate)
                {
                    var updatedStop = entry.Side.Equals("Long", StringComparison.OrdinalIgnoreCase)
                        ? Math.Max(entry.TrailingStopPrice.Value, rangeHigh * (1 - drawdownPct))
                        : Math.Min(entry.TrailingStopPrice.Value, rangeLow * (1 + drawdownPct));

                    if (updatedStop != entry.TrailingStopPrice.Value)
                    {
                        var rows = await _positionRepository.UpdateTrailingAsync(entry.PositionId, updatedStop, ct)
                            .ConfigureAwait(false);
                        if (rows > 0)
                        {
                            _riskIndexManager.TryUpdateTrailingStop(entry.PositionId, updatedStop);
                        }
                    }
                }
            }

            return entry.Side.Equals("Long", StringComparison.OrdinalIgnoreCase)
                ? rangeLow <= entry.TrailingStopPrice.Value
                : rangeHigh >= entry.TrailingStopPrice.Value;
        }
    }
}
