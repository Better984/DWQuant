using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServerTest.Domain.Entities;
using ServerTest.Infrastructure.Repositories;
using ServerTest.Models;
using ServerTest.Models.Strategy;

namespace ServerTest.Services
{
    public sealed class TradeActionConsumer : BackgroundService
    {
        private readonly StrategyActionTaskQueue _queue;
        private readonly StrategyPositionRepository _positionRepository;
        private readonly MarketDataEngine _marketDataEngine;
        private readonly IOrderExecutor _orderExecutor;
        private readonly PositionRiskConfigStore _riskConfigStore;
        private readonly ILogger<TradeActionConsumer> _logger;

        public TradeActionConsumer(
            StrategyActionTaskQueue queue,
            StrategyPositionRepository positionRepository,
            MarketDataEngine marketDataEngine,
            IOrderExecutor orderExecutor,
            PositionRiskConfigStore riskConfigStore,
            ILogger<TradeActionConsumer> logger)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
            _marketDataEngine = marketDataEngine ?? throw new ArgumentNullException(nameof(marketDataEngine));
            _orderExecutor = orderExecutor ?? throw new ArgumentNullException(nameof(orderExecutor));
            _riskConfigStore = riskConfigStore ?? throw new ArgumentNullException(nameof(riskConfigStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await _marketDataEngine.WaitForInitializationAsync();

            await foreach (var task in _queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await HandleTaskAsync(task, stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Trade action handling failed: uid={Uid} usId={UsId} method={Method}", task.Uid, task.UsId, task.Method);
                }
            }
        }

        private async Task HandleTaskAsync(StrategyActionTask task, CancellationToken ct)
        {
            if (task == null)
            {
                return;
            }

            var action = task.Param != null && task.Param.Length > 0 ? task.Param[0] : string.Empty;
            if (!TryMapAction(action, out var positionSide, out var orderSide, out var reduceOnly, out var isClose))
            {
                _logger.LogWarning("Unsupported action: {Action} uid={Uid} usId={UsId}", action, task.Uid, task.UsId);
                return;
            }

            var exchange = MarketDataKeyNormalizer.NormalizeExchange(task.Exchange);
            var symbol = MarketDataKeyNormalizer.NormalizeSymbol(task.Symbol);

            if (string.IsNullOrWhiteSpace(exchange) || string.IsNullOrWhiteSpace(symbol))
            {
                _logger.LogWarning("Missing exchange/symbol for action: uid={Uid} usId={UsId}", task.Uid, task.UsId);
                return;
            }

            if (isClose)
            {
                await HandleCloseAsync(task, exchange, symbol, positionSide, orderSide, ct).ConfigureAwait(false);
                return;
            }

            await HandleOpenAsync(task, exchange, symbol, positionSide, orderSide, ct).ConfigureAwait(false);
        }

        private async Task HandleOpenAsync(
            StrategyActionTask task,
            string exchange,
            string symbol,
            string positionSide,
            string orderSide,
            CancellationToken ct)
        {
            if (!task.Uid.HasValue || !task.UsId.HasValue)
            {
                _logger.LogWarning("Missing uid/usId for open action.");
                return;
            }

            if (task.OrderQty <= 0)
            {
                _logger.LogWarning("OrderQty <= 0 for open action: uid={Uid} usId={UsId}", task.Uid, task.UsId);
                return;
            }

            var existing = await _positionRepository.FindOpenAsync(task.Uid.Value, task.UsId.Value, exchange, symbol, positionSide, ct)
                .ConfigureAwait(false);
            if (existing != null)
            {
                _logger.LogInformation("Open ignored (already open): uid={Uid} usId={UsId} {Exchange} {Symbol} {Side}",
                    task.Uid, task.UsId, exchange, symbol, positionSide);
                return;
            }

            var entryPrice = ResolveEntryPrice(exchange, symbol);
            if (entryPrice <= 0)
            {
                _logger.LogWarning("Entry price missing, skip open: {Exchange} {Symbol}", exchange, symbol);
                return;
            }

            var orderResult = await _orderExecutor.PlaceMarketOrderAsync(new OrderExecutionRequest
            {
                Uid = task.Uid.Value,
                ExchangeApiKeyId = task.ExchangeApiKeyId,
                Exchange = exchange,
                Symbol = symbol,
                Side = orderSide,
                Qty = task.OrderQty,
                ReduceOnly = false
            }, ct).ConfigureAwait(false);

            if (!orderResult.Success)
            {
                _logger.LogWarning("Open order failed: uid={Uid} usId={UsId} err={Error}", task.Uid, task.UsId, orderResult.ErrorMessage);
                return;
            }

            if (orderResult.AveragePrice.HasValue && orderResult.AveragePrice.Value > 0)
            {
                entryPrice = orderResult.AveragePrice.Value;
            }

            var stopLossPrice = BuildStopLossPrice(entryPrice, task.StopLossPct, positionSide);
            var takeProfitPrice = BuildTakeProfitPrice(entryPrice, task.TakeProfitPct, positionSide);

            var entity = new StrategyPosition
            {
                Uid = task.Uid.Value,
                UsId = task.UsId.Value,
                ExchangeApiKeyId = task.ExchangeApiKeyId,
                Exchange = exchange,
                Symbol = symbol,
                Side = positionSide,
                EntryPrice = entryPrice,
                Qty = task.OrderQty,
                Status = "Open",
                StopLossPrice = stopLossPrice,
                TakeProfitPrice = takeProfitPrice,
                TrailingEnabled = task.TrailingEnabled,
                TrailingStopPrice = null,
                TrailingTriggered = false,
                OpenedAt = DateTime.UtcNow,
                ClosedAt = null
            };

            var positionId = await _positionRepository.InsertAsync(entity, ct).ConfigureAwait(false);

            if (task.TrailingEnabled)
            {
                _riskConfigStore.Upsert(positionId, new PositionRiskConfig
                {
                    Side = positionSide,
                    ActivationPct = task.TrailingActivationPct,
                    DrawdownPct = task.TrailingDrawdownPct
                });
            }

            _logger.LogInformation("Position opened: id={PositionId} uid={Uid} usId={UsId} {Exchange} {Symbol} {Side}",
                positionId, task.Uid, task.UsId, exchange, symbol, positionSide);
        }

        private async Task HandleCloseAsync(
            StrategyActionTask task,
            string exchange,
            string symbol,
            string positionSide,
            string orderSide,
            CancellationToken ct)
        {
            if (!task.Uid.HasValue || !task.UsId.HasValue)
            {
                _logger.LogWarning("Missing uid/usId for close action.");
                return;
            }

            var existing = await _positionRepository.FindOpenAsync(task.Uid.Value, task.UsId.Value, exchange, symbol, positionSide, ct)
                .ConfigureAwait(false);
            if (existing == null)
            {
                _logger.LogInformation("No open position to close: uid={Uid} usId={UsId} {Exchange} {Symbol} {Side}",
                    task.Uid, task.UsId, exchange, symbol, positionSide);
                return;
            }

            var orderResult = await _orderExecutor.PlaceMarketOrderAsync(new OrderExecutionRequest
            {
                Uid = existing.Uid,
                ExchangeApiKeyId = existing.ExchangeApiKeyId,
                Exchange = exchange,
                Symbol = symbol,
                Side = orderSide,
                Qty = existing.Qty,
                ReduceOnly = true
            }, ct).ConfigureAwait(false);

            if (!orderResult.Success)
            {
                _logger.LogWarning("Close order failed: positionId={PositionId} err={Error}", existing.PositionId, orderResult.ErrorMessage);
                return;
            }

            await _positionRepository.CloseAsync(existing.PositionId, trailingTriggered: false, closedAt: DateTime.UtcNow, ct).ConfigureAwait(false);
            _riskConfigStore.Remove(existing.PositionId);

            _logger.LogInformation("Position closed: id={PositionId} uid={Uid} usId={UsId}", existing.PositionId, existing.Uid, existing.UsId);
        }

        private decimal ResolveEntryPrice(string exchange, string symbol)
        {
            var kline = _marketDataEngine.GetLatestKline(exchange, "1m", symbol);
            if (kline == null)
            {
                return 0m;
            }

            var close = kline.Value.close;
            if (close.HasValue)
            {
                return Convert.ToDecimal(close.Value);
            }

            var open = kline.Value.open;
            return open.HasValue ? Convert.ToDecimal(open.Value) : 0m;
        }

        private static decimal? BuildStopLossPrice(decimal entryPrice, decimal? stopLossPct, string side)
        {
            if (!stopLossPct.HasValue || stopLossPct.Value <= 0 || entryPrice <= 0)
            {
                return null;
            }

            return side.Equals("Long", StringComparison.OrdinalIgnoreCase)
                ? entryPrice * (1 - stopLossPct.Value)
                : entryPrice * (1 + stopLossPct.Value);
        }

        private static decimal? BuildTakeProfitPrice(decimal entryPrice, decimal? takeProfitPct, string side)
        {
            if (!takeProfitPct.HasValue || takeProfitPct.Value <= 0 || entryPrice <= 0)
            {
                return null;
            }

            return side.Equals("Long", StringComparison.OrdinalIgnoreCase)
                ? entryPrice * (1 + takeProfitPct.Value)
                : entryPrice * (1 - takeProfitPct.Value);
        }

        private static bool TryMapAction(
            string action,
            out string positionSide,
            out string orderSide,
            out bool reduceOnly,
            out bool isClose)
        {
            positionSide = string.Empty;
            orderSide = string.Empty;
            reduceOnly = false;
            isClose = false;

            if (string.IsNullOrWhiteSpace(action))
            {
                return false;
            }

            switch (action.Trim().ToUpperInvariant())
            {
                case "LONG":
                    positionSide = "Long";
                    orderSide = "buy";
                    reduceOnly = false;
                    isClose = false;
                    return true;
                case "SHORT":
                    positionSide = "Short";
                    orderSide = "sell";
                    reduceOnly = false;
                    isClose = false;
                    return true;
                case "CLOSELONG":
                    positionSide = "Long";
                    orderSide = "sell";
                    reduceOnly = true;
                    isClose = true;
                    return true;
                case "CLOSESHORT":
                    positionSide = "Short";
                    orderSide = "buy";
                    reduceOnly = true;
                    isClose = true;
                    return true;
                default:
                    return false;
            }
        }
    }
}
