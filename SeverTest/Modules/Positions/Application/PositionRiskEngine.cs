using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServerTest.Domain.Entities;
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
        private readonly ILogger<PositionRiskEngine> _logger;

        private static readonly TimeSpan LoopDelay = TimeSpan.FromSeconds(1);

        public PositionRiskEngine(
            StrategyPositionRepository positionRepository,
            MarketDataEngine marketDataEngine,
            IOrderExecutor orderExecutor,
            PositionRiskConfigStore riskConfigStore,
            ILogger<PositionRiskEngine> logger)
        {
            _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
            _marketDataEngine = marketDataEngine ?? throw new ArgumentNullException(nameof(marketDataEngine));
            _orderExecutor = orderExecutor ?? throw new ArgumentNullException(nameof(orderExecutor));
            _riskConfigStore = riskConfigStore ?? throw new ArgumentNullException(nameof(riskConfigStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await _marketDataEngine.WaitForInitializationAsync();

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var openPositions = await _positionRepository.ListOpenAsync(stoppingToken).ConfigureAwait(false);
                    foreach (var position in openPositions)
                    {
                        await EvaluatePositionAsync(position, stoppingToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "仓位风险循环失败");
                }

                await Task.Delay(LoopDelay, stoppingToken).ConfigureAwait(false);
            }
        }

        private async Task EvaluatePositionAsync(StrategyPosition position, CancellationToken ct)
        {
            var kline = _marketDataEngine.GetLatestKline(position.Exchange, "1m", position.Symbol);
            if (kline == null)
            {
                return;
            }

            var close = kline.Value.close ?? kline.Value.open;
            var high = kline.Value.high ?? close;
            var low = kline.Value.low ?? close;

            if (!high.HasValue || !low.HasValue || !close.HasValue)
            {
                return;
            }

            var lastPrice = Convert.ToDecimal(close.Value);
            var highPrice = Convert.ToDecimal(high.Value);
            var lowPrice = Convert.ToDecimal(low.Value);

            var side = position.Side;
            var stopLossHit = CheckStopLoss(position, highPrice, lowPrice, side);
            var takeProfitHit = CheckTakeProfit(position, highPrice, lowPrice, side);

            var trailingHit = false;
            if (position.TrailingEnabled)
            {
                trailingHit = await UpdateTrailingAsync(position, lastPrice, side, ct).ConfigureAwait(false);
            }

            var shouldClose = stopLossHit || takeProfitHit || trailingHit;
            if (!shouldClose)
            {
                return;
            }

            var orderSide = side.Equals("Long", StringComparison.OrdinalIgnoreCase) ? "sell" : "buy";
            var orderResult = await _orderExecutor.PlaceMarketOrderAsync(new OrderExecutionRequest
            {
                Uid = position.Uid,
                ExchangeApiKeyId = position.ExchangeApiKeyId,
                Exchange = position.Exchange,
                Symbol = position.Symbol,
                Side = orderSide,
                Qty = position.Qty,
                ReduceOnly = true
            }, ct).ConfigureAwait(false);

            if (!orderResult.Success)
            {
                _logger.LogWarning("风险平仓订单失败: positionId={PositionId} 错误={Error}", position.PositionId, orderResult.ErrorMessage);
                return;
            }

            await _positionRepository.CloseAsync(position.PositionId, trailingTriggered: trailingHit, closedAt: DateTime.UtcNow, ct)
                .ConfigureAwait(false);
            _riskConfigStore.Remove(position.PositionId);

            _logger.LogInformation("仓位因风险平仓: id={PositionId} uid={Uid} usId={UsId}", position.PositionId, position.Uid, position.UsId);
        }

        private static bool CheckStopLoss(StrategyPosition position, decimal high, decimal low, string side)
        {
            if (!position.StopLossPrice.HasValue)
            {
                return false;
            }

            return side.Equals("Long", StringComparison.OrdinalIgnoreCase)
                ? low <= position.StopLossPrice.Value
                : high >= position.StopLossPrice.Value;
        }

        private static bool CheckTakeProfit(StrategyPosition position, decimal high, decimal low, string side)
        {
            if (!position.TakeProfitPrice.HasValue)
            {
                return false;
            }

            return side.Equals("Long", StringComparison.OrdinalIgnoreCase)
                ? high >= position.TakeProfitPrice.Value
                : low <= position.TakeProfitPrice.Value;
        }

        private async Task<bool> UpdateTrailingAsync(StrategyPosition position, decimal lastPrice, string side, CancellationToken ct)
        {
            if (!_riskConfigStore.TryGet(position.PositionId, out var config) || config == null)
            {
                if (!position.TrailingStopPrice.HasValue)
                {
                    return false;
                }

                return side.Equals("Long", StringComparison.OrdinalIgnoreCase)
                    ? lastPrice <= position.TrailingStopPrice.Value
                    : lastPrice >= position.TrailingStopPrice.Value;
            }

            var activationPct = config.ActivationPct ?? 0m;
            var drawdownPct = config.DrawdownPct ?? 0m;
            if (activationPct <= 0 || drawdownPct <= 0)
            {
                return false;
            }

            var activationPrice = side.Equals("Long", StringComparison.OrdinalIgnoreCase)
                ? position.EntryPrice * (1 + activationPct)
                : position.EntryPrice * (1 - activationPct);

            if (!position.TrailingStopPrice.HasValue)
            {
                var activated = side.Equals("Long", StringComparison.OrdinalIgnoreCase)
                    ? lastPrice >= activationPrice
                    : lastPrice <= activationPrice;

                if (!activated)
                {
                    return false;
                }

                var initialStop = side.Equals("Long", StringComparison.OrdinalIgnoreCase)
                    ? lastPrice * (1 - drawdownPct)
                    : lastPrice * (1 + drawdownPct);

                await _positionRepository.UpdateTrailingAsync(position.PositionId, initialStop, ct).ConfigureAwait(false);
                return false;
            }

            var currentStop = position.TrailingStopPrice.Value;
            var updatedStop = side.Equals("Long", StringComparison.OrdinalIgnoreCase)
                ? Math.Max(currentStop, lastPrice * (1 - drawdownPct))
                : Math.Min(currentStop, lastPrice * (1 + drawdownPct));

            if (updatedStop != currentStop)
            {
                await _positionRepository.UpdateTrailingAsync(position.PositionId, updatedStop, ct).ConfigureAwait(false);
                position.TrailingStopPrice = updatedStop;
            }

            return side.Equals("Long", StringComparison.OrdinalIgnoreCase)
                ? lastPrice <= position.TrailingStopPrice.Value
                : lastPrice >= position.TrailingStopPrice.Value;
        }
    }
}
