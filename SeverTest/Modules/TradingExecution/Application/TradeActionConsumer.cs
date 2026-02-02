using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServerTest.Domain.Entities;
using ServerTest.Modules.Positions.Infrastructure;
using ServerTest.Modules.Positions.Application;
using ServerTest.Modules.Positions.Domain;
using ServerTest.Models;
using ServerTest.Models.Strategy;
using ServerTest.Modules.Notifications.Application;
using ServerTest.Modules.Notifications.Domain;
using System.Text.Json;
using ServerTest.Modules.MarketStreaming.Application;
using ServerTest.Modules.StrategyEngine.Application;
using ServerTest.Modules.TradingExecution.Domain;

namespace ServerTest.Modules.TradingExecution.Application
{
    public sealed class TradeActionConsumer : BackgroundService
    {
        private readonly StrategyActionTaskQueue _queue;
        private readonly StrategyPositionRepository _positionRepository;
        private readonly MarketDataEngine _marketDataEngine;
        private readonly IOrderExecutor _orderExecutor;
        private readonly PositionRiskConfigStore _riskConfigStore;
        private readonly PositionRiskIndexManager _riskIndexManager;
        private readonly INotificationPublisher _notificationPublisher;
        private readonly ILogger<TradeActionConsumer> _logger;

        public TradeActionConsumer(
            StrategyActionTaskQueue queue,
            StrategyPositionRepository positionRepository,
            MarketDataEngine marketDataEngine,
            IOrderExecutor orderExecutor,
            PositionRiskConfigStore riskConfigStore,
            PositionRiskIndexManager riskIndexManager,
            INotificationPublisher notificationPublisher,
            ILogger<TradeActionConsumer> logger)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
            _marketDataEngine = marketDataEngine ?? throw new ArgumentNullException(nameof(marketDataEngine));
            _orderExecutor = orderExecutor ?? throw new ArgumentNullException(nameof(orderExecutor));
            _riskConfigStore = riskConfigStore ?? throw new ArgumentNullException(nameof(riskConfigStore));
            _riskIndexManager = riskIndexManager ?? throw new ArgumentNullException(nameof(riskIndexManager));
            _notificationPublisher = notificationPublisher ?? throw new ArgumentNullException(nameof(notificationPublisher));
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
                    _logger.LogError(ex, "交易动作处理失败: uid={Uid} usId={UsId} 方法={Method}", task.Uid, task.UsId, task.Method);
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
                _logger.LogWarning("不支持的动作: {Action} uid={Uid} usId={UsId}", action, task.Uid, task.UsId);
                return;
            }

            var exchange = MarketDataKeyNormalizer.NormalizeExchange(task.Exchange);
            var symbol = MarketDataKeyNormalizer.NormalizeSymbol(task.Symbol);

            if (string.IsNullOrWhiteSpace(exchange) || string.IsNullOrWhiteSpace(symbol))
            {
                _logger.LogWarning("动作缺少交易所/交易对: uid={Uid} usId={UsId}", task.Uid, task.UsId);
                return;
            }

            if (isClose)
            {
                await HandleCloseAsync(task, exchange, symbol, positionSide, orderSide, ct).ConfigureAwait(false);
                return;
            }

            await HandleOpenAsync(task, exchange, symbol, positionSide, orderSide, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 处理开仓任务。
        /// 执行流程：1. 参数校验 2. 检查是否已有仓位 3. 获取入场价格 4. 提交市价单 5. 创建仓位记录 6. 配置风险参数
        /// </summary>
        /// <param name="task">策略动作任务，包含策略ID、交易参数、风险参数等信息</param>
        /// <param name="exchange">交易所名称（已标准化）</param>
        /// <param name="symbol">交易对符号（已标准化）。</param>
        /// <param name="positionSide">仓位方向（Long/Short）。</param>
        /// <param name="orderSide">订单方向（buy/sell）。</param>
        /// <param name="ct">取消令牌</param>
        private async Task HandleOpenAsync(
            StrategyActionTask task,
            string exchange,
            string symbol,
            string positionSide,
            string orderSide,
            CancellationToken ct)
        {
            // 1. 验证用户ID和策略ID是否存在
            if (!task.Uid.HasValue || !task.UsId.HasValue)
            {
                _logger.LogWarning("开仓动作缺少uid/usId");
                return;
            }

            // 2. 验证订单数量是否有效
            if (task.OrderQty <= 0)
            {
                _logger.LogWarning("开仓动作订单数量<=0: uid={Uid} usId={UsId}", task.Uid, task.UsId);
                return;
            }

            // 3. 检查是否已存在相同方向的未平仓位（防止重复开仓）
            //var existing = await _positionRepository.FindOpenAsync(task.Uid.Value, task.UsId.Value, exchange, symbol, positionSide, ct)
            //    .ConfigureAwait(false);
            //if (existing != null)
            //{
            //    _logger.LogInformation("开仓已忽略（已有仓位）: uid={Uid} usId={UsId} {Exchange} {Symbol} {Side}",
            //        task.Uid, task.UsId, exchange, symbol, positionSide);
            //    return;
            //}

            // 4. 从市场数据引擎获取当前价格作为预估入场价格
            var entryPrice = ResolveEntryPrice(exchange, symbol);
            if (entryPrice <= 0)
            {
                _logger.LogWarning("入场价格缺失，跳过开仓: {Exchange} {Symbol}", exchange, symbol);
                return;
            }

            // 5. 提交市价单到交易所（开仓订单，非平仓）
            var orderResult = await _orderExecutor.PlaceMarketOrderAsync(new OrderExecutionRequest
            {
                Uid = task.Uid.Value,
                ExchangeApiKeyId = task.ExchangeApiKeyId,
                Exchange = exchange,
                Symbol = symbol,
                Side = orderSide,
                Qty = task.OrderQty,
                ReduceOnly = false  // 开仓订单，非平仓
            }, ct).ConfigureAwait(false);

            // 6. 检查订单是否成功提交
            if (!orderResult.Success)
            {
                _logger.LogWarning("开仓订单失败: uid={Uid} usId={UsId} 错误={Error}", task.Uid, task.UsId, orderResult.ErrorMessage);
                return;
            }

            // 7. 如果订单返回了实际成交均价，使用实际成交价更新入场价格
            if (orderResult.AveragePrice.HasValue && orderResult.AveragePrice.Value > 0)
            {
                entryPrice = orderResult.AveragePrice.Value;
            }

            // 8. 根据入场价格和风险参数计算止损价和止盈价
            var stopLossPrice = BuildStopLossPrice(entryPrice, task.StopLossPct, task.Leverage, positionSide);
            var takeProfitPrice = BuildTakeProfitPrice(entryPrice, task.TakeProfitPct, task.Leverage, positionSide);

            // 9. 创建仓位实体对象，包含所有仓位信息
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
                TrailingStopPrice = null,  // 初始时追踪止损价格为空
                TrailingTriggered = false,  // 追踪止损尚未触发
                OpenedAt = DateTime.UtcNow,
                ClosedAt = null
            };

            // 10. 将仓位记录保存到数据库
            var positionId = await _positionRepository.InsertAsync(entity, ct).ConfigureAwait(false);
            entity.PositionId = positionId;

            // 11. 如果启用了追踪止损，保存追踪止损配置到风险配置存储
            PositionRiskConfig? riskConfig = null;
            if (task.TrailingEnabled)
            {
                riskConfig = new PositionRiskConfig
                {
                    Side = positionSide,
                    ActivationPct = task.TrailingActivationPct,  // 追踪止损激活百分比
                    DrawdownPct = task.TrailingDrawdownPct     // 追踪止损回撤百分比
                };
                _riskConfigStore.Upsert(positionId, riskConfig);
            }

            _riskIndexManager.UpsertPosition(entity, riskConfig);

            // 12. 记录开仓成功日志
            _logger.LogInformation("仓位已开: id={PositionId} uid={Uid} usId={UsId} {Exchange} {Symbol} {Side}",
                positionId, task.Uid, task.UsId, exchange, symbol, positionSide);

            var payload = JsonSerializer.Serialize(new
            {
                positionId,
                exchange,
                symbol,
                side = positionSide,
                qty = task.OrderQty,
                entryPrice,
                openedAt = DateTime.UtcNow
            });

            await _notificationPublisher.PublishToUserAsync(new NotificationPublishRequest
            {
                UserId = task.Uid.Value,
                Category = NotificationCategory.Trade,
                Severity = NotificationSeverity.Info,
                Template = "trade.opened",
                PayloadJson = payload,
                DedupeKey = $"trade_open:{positionId}"
            }, ct).ConfigureAwait(false);
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
                _logger.LogWarning("平仓动作缺少uid/usId");
                return;
            }

            var existing = await _positionRepository.FindOpenAsync(task.Uid.Value, task.UsId.Value, exchange, symbol, positionSide, ct)
                .ConfigureAwait(false);
            if (existing == null)
            {
                _logger.LogInformation("没有可平仓位: uid={Uid} usId={UsId} {Exchange} {Symbol} {Side}",
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
                _logger.LogWarning("平仓订单失败: positionId={PositionId} 错误={Error}", existing.PositionId, orderResult.ErrorMessage);
                return;
            }

            await _positionRepository.CloseAsync(existing.PositionId, trailingTriggered: false, closedAt: DateTime.UtcNow, ct).ConfigureAwait(false);
            _riskConfigStore.Remove(existing.PositionId);
            _riskIndexManager.RemovePosition(existing.PositionId);

            _logger.LogInformation("仓位已平: id={PositionId} uid={Uid} usId={UsId}", existing.PositionId, existing.Uid, existing.UsId);
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

        private static decimal? BuildStopLossPrice(decimal entryPrice, decimal? stopLossPct, int leverage, string side)
        {
            if (!stopLossPct.HasValue || stopLossPct.Value <= 0 || entryPrice <= 0)
            {
                return null;
            }

            // 止损百分比按收益率(ROI)配置，需要除以杠杆折算成价格变动比例
            var effectiveLeverage = Math.Max(1, leverage);
            var priceMovePct = stopLossPct.Value / effectiveLeverage;

            return side.Equals("Long", StringComparison.OrdinalIgnoreCase)
                ? entryPrice * (1 - priceMovePct)
                : entryPrice * (1 + priceMovePct);
        }

        private static decimal? BuildTakeProfitPrice(decimal entryPrice, decimal? takeProfitPct, int leverage, string side)
        {
            if (!takeProfitPct.HasValue || takeProfitPct.Value <= 0 || entryPrice <= 0)
            {
                return null;
            }

            // 止盈百分比按收益率(ROI)配置，需要除以杠杆折算成价格变动比例
            var effectiveLeverage = Math.Max(1, leverage);
            var priceMovePct = takeProfitPct.Value / effectiveLeverage;

            return side.Equals("Long", StringComparison.OrdinalIgnoreCase)
                ? entryPrice * (1 + priceMovePct)
                : entryPrice * (1 - priceMovePct);
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
            reduceOnly = false;  // 开仓订单，非平仓
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
                    reduceOnly = false;  // 开仓订单，非平仓
                    isClose = false;
                    return true;
                case "SHORT":
                    positionSide = "Short";
                    orderSide = "sell";
                    reduceOnly = false;  // 开仓订单，非平仓
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
