using Microsoft.Extensions.Logging;
using ServerTest.Modules.Positions.Domain;
using ServerTest.Modules.Positions.Infrastructure;
using ServerTest.Models.Trading;
using ServerTest.Modules.TradingExecution.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ServerTest.Modules.Positions.Application
{
    public sealed class StrategyPositionCloseService
    {
        private readonly StrategyPositionRepository _positionRepository;
        private readonly IOrderExecutor _orderExecutor;
        private readonly PositionRiskConfigStore _riskConfigStore;
        private readonly ILogger<StrategyPositionCloseService> _logger;

        public StrategyPositionCloseService(
            StrategyPositionRepository positionRepository,
            IOrderExecutor orderExecutor,
            PositionRiskConfigStore riskConfigStore,
            ILogger<StrategyPositionCloseService> logger)
        {
            _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
            _orderExecutor = orderExecutor ?? throw new ArgumentNullException(nameof(orderExecutor));
            _riskConfigStore = riskConfigStore ?? throw new ArgumentNullException(nameof(riskConfigStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<StrategyClosePositionsResult> CloseByStrategyAsync(long uid, long usId, CancellationToken ct)
        {
            var positions = await _positionRepository
                .GetByUsIdAsync(uid, usId, null, null, "Open", ct)
                .ConfigureAwait(false);

            if (positions.Count == 0)
            {
                return new StrategyClosePositionsResult
                {
                    TotalPositions = 0,
                    ClosedPositions = 0,
                    FailedGroups = new List<StrategyCloseGroupResult>()
                };
            }

            // 按交易所/币对/方向/APIKey 分组，确保平仓口径一致
            var grouped = positions
                .GroupBy(item => new
                {
                    item.Exchange,
                    item.Symbol,
                    item.Side,
                    item.ExchangeApiKeyId
                })
                .ToList();

            var closedPositions = 0;
            var failedGroups = new List<StrategyCloseGroupResult>();

            _logger.LogInformation("一键平仓开始: uid={Uid} usId={UsId} 分组数={Groups} 仓位数={Count}",
                uid, usId, grouped.Count, positions.Count);

            foreach (var group in grouped)
            {
                var qty = group.Sum(item => item.Qty);
                if (qty <= 0)
                {
                    failedGroups.Add(new StrategyCloseGroupResult
                    {
                        Exchange = group.Key.Exchange,
                        Symbol = group.Key.Symbol,
                        Side = group.Key.Side,
                        ExchangeApiKeyId = group.Key.ExchangeApiKeyId,
                        Qty = qty,
                        Error = "持仓数量无效"
                    });
                    continue;
                }

                var orderSide = ResolveCloseOrderSide(group.Key.Side);
                if (string.IsNullOrWhiteSpace(orderSide))
                {
                    failedGroups.Add(new StrategyCloseGroupResult
                    {
                        Exchange = group.Key.Exchange,
                        Symbol = group.Key.Symbol,
                        Side = group.Key.Side,
                        ExchangeApiKeyId = group.Key.ExchangeApiKeyId,
                        Qty = qty,
                        Error = "仓位方向无效"
                    });
                    continue;
                }

                var orderResult = await _orderExecutor.PlaceMarketOrderAsync(new OrderExecutionRequest
                {
                    Uid = uid,
                    ExchangeApiKeyId = group.Key.ExchangeApiKeyId,
                    Exchange = group.Key.Exchange,
                    Symbol = group.Key.Symbol,
                    Side = orderSide,
                    Qty = qty,
                    ReduceOnly = true
                }, ct).ConfigureAwait(false);

                if (!orderResult.Success)
                {
                    failedGroups.Add(new StrategyCloseGroupResult
                    {
                        Exchange = group.Key.Exchange,
                        Symbol = group.Key.Symbol,
                        Side = group.Key.Side,
                        ExchangeApiKeyId = group.Key.ExchangeApiKeyId,
                        Qty = qty,
                        Error = orderResult.ErrorMessage ?? "平仓失败"
                    });
                    continue;
                }

                foreach (var position in group)
                {
                    // 手动批量平仓需要记录 close_reason=ManualBatch
                    await _positionRepository.CloseAsync(position.PositionId, trailingTriggered: false, closedAt: DateTime.UtcNow, "ManualBatch", ct)
                        .ConfigureAwait(false);
                    _riskConfigStore.Remove(position.PositionId);
                    closedPositions++;
                }
            }

            _logger.LogInformation("一键平仓完成: uid={Uid} usId={UsId} 已平仓={Closed} 失败分组={Failed}",
                uid, usId, closedPositions, failedGroups.Count);

            return new StrategyClosePositionsResult
            {
                TotalPositions = positions.Count,
                ClosedPositions = closedPositions,
                FailedGroups = failedGroups
            };
        }

        public async Task<PositionCloseResult> CloseByPositionAsync(long uid, long positionId, CancellationToken ct)
        {
            if (uid <= 0 || positionId <= 0)
            {
                return new PositionCloseResult
                {
                    PositionId = positionId,
                    Success = false,
                    Error = "无效的仓位ID"
                };
            }

            var position = await _positionRepository.GetByIdAsync(positionId, uid, ct).ConfigureAwait(false);
            if (position == null)
            {
                return new PositionCloseResult
                {
                    PositionId = positionId,
                    Success = false,
                    Error = "未找到仓位"
                };
            }

            if (!string.Equals(position.Status, "Open", StringComparison.OrdinalIgnoreCase))
            {
                return new PositionCloseResult
                {
                    PositionId = position.PositionId,
                    Success = false,
                    Error = "仓位已平或不可平"
                };
            }

            if (position.Qty <= 0)
            {
                return new PositionCloseResult
                {
                    PositionId = position.PositionId,
                    Success = false,
                    Error = "仓位数量无效"
                };
            }

            var orderSide = ResolveCloseOrderSide(position.Side);
            if (string.IsNullOrWhiteSpace(orderSide))
            {
                return new PositionCloseResult
                {
                    PositionId = position.PositionId,
                    Success = false,
                    Error = "仓位方向无效"
                };
            }

            _logger.LogInformation("手动平仓开始: uid={Uid} positionId={PositionId} {Exchange} {Symbol} {Side} qty={Qty}",
                uid, position.PositionId, position.Exchange, position.Symbol, position.Side, position.Qty);

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
                return new PositionCloseResult
                {
                    PositionId = position.PositionId,
                    Success = false,
                    Error = orderResult.ErrorMessage ?? "平仓失败"
                };
            }

            // 手动单仓平仓需要记录 close_reason=ManualSingle
            await _positionRepository.CloseAsync(position.PositionId, trailingTriggered: false, closedAt: DateTime.UtcNow, "ManualSingle", ct)
                .ConfigureAwait(false);
            _riskConfigStore.Remove(position.PositionId);

            _logger.LogInformation("手动平仓完成: uid={Uid} positionId={PositionId}", uid, position.PositionId);

            return new PositionCloseResult
            {
                PositionId = position.PositionId,
                Success = true
            };
        }

        private static string ResolveCloseOrderSide(string? side)
        {
            if (string.Equals(side, "Long", StringComparison.OrdinalIgnoreCase))
            {
                return "sell";
            }

            if (string.Equals(side, "Short", StringComparison.OrdinalIgnoreCase))
            {
                return "buy";
            }

            return string.Empty;
        }
    }
}
