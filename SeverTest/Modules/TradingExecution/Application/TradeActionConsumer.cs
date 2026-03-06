using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
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
using ServerTest.Modules.MarketData.Application;
using ServerTest.Modules.MarketData.Domain;
using ServerTest.Modules.MarketStreaming.Application;
using ServerTest.Modules.StrategyEngine.Application;
using ServerTest.Modules.StrategyEngine.Infrastructure;
using ServerTest.Modules.StrategyRuntime.Infrastructure;
using ServerTest.Modules.TradingExecution.Domain;
using ServerTest.Modules.TradingExecution.Infrastructure;

namespace ServerTest.Modules.TradingExecution.Application
{
    public sealed class TradeActionConsumer : BackgroundService
    {
        private readonly StrategyActionTaskQueue _queue;
        private readonly RealTimeStrategyEngine _strategyEngine;
        private readonly StrategyPositionRepository _positionRepository;
        private readonly OrderOpenAttemptRepository _orderOpenAttemptRepository;
        private readonly StrategyRuntimeRepository _runtimeRepository;
        private readonly StrategySystemLogRepository _systemLogRepository;
        private readonly MarketDataEngine _marketDataEngine;
        private readonly ContractDetailsCacheService _contractDetailsCacheService;
        private readonly IOrderExecutor _orderExecutor;
        private readonly StrategyTargetRiskService _targetRiskService;
        private readonly PositionRiskConfigStore _riskConfigStore;
        private readonly PositionRiskIndexManager _riskIndexManager;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly INotificationPublisher _notificationPublisher;
        private readonly ILogger<TradeActionConsumer> _logger;
        private readonly string _recoveryWorkerToken;

        private const int RecoveryMaxAttempts = 6;

        private enum RecoveryTaskType
        {
            CloseWrite,
            OpenCompensation
        }

        private sealed class RecoveryTask
        {
            public long TaskId { get; init; }
            public string ProcessingToken { get; init; } = string.Empty;
            public RecoveryTaskType Type { get; init; }
            public int Attempt { get; init; } = 1;
            public int MaxAttempts { get; init; } = RecoveryMaxAttempts;
            public DateTime NotBeforeUtc { get; init; } = DateTime.UtcNow;
            public long? Uid { get; init; }
            public long? UsId { get; init; }
            public long? OrderRequestId { get; init; }
            public long PositionId { get; init; }
            public long? ExchangeApiKeyId { get; init; }
            public string Exchange { get; init; } = string.Empty;
            public string Symbol { get; init; } = string.Empty;
            public string Side { get; init; } = string.Empty;
            public decimal Qty { get; init; }
            public decimal? ClosePrice { get; init; }
            public DateTime ClosedAtUtc { get; init; }
            public string LastError { get; init; } = string.Empty;
        }

        public TradeActionConsumer(
            StrategyActionTaskQueue queue,
            RealTimeStrategyEngine strategyEngine,
            StrategyPositionRepository positionRepository,
            OrderOpenAttemptRepository orderOpenAttemptRepository,
            StrategyRuntimeRepository runtimeRepository,
            StrategySystemLogRepository systemLogRepository,
            MarketDataEngine marketDataEngine,
            ContractDetailsCacheService contractDetailsCacheService,
            IOrderExecutor orderExecutor,
            StrategyTargetRiskService targetRiskService,
            PositionRiskConfigStore riskConfigStore,
            PositionRiskIndexManager riskIndexManager,
            IServiceScopeFactory serviceScopeFactory,
            INotificationPublisher notificationPublisher,
            ILogger<TradeActionConsumer> logger)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _strategyEngine = strategyEngine ?? throw new ArgumentNullException(nameof(strategyEngine));
            _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
            _orderOpenAttemptRepository = orderOpenAttemptRepository ?? throw new ArgumentNullException(nameof(orderOpenAttemptRepository));
            _runtimeRepository = runtimeRepository ?? throw new ArgumentNullException(nameof(runtimeRepository));
            _systemLogRepository = systemLogRepository ?? throw new ArgumentNullException(nameof(systemLogRepository));
            _marketDataEngine = marketDataEngine ?? throw new ArgumentNullException(nameof(marketDataEngine));
            _contractDetailsCacheService = contractDetailsCacheService ?? throw new ArgumentNullException(nameof(contractDetailsCacheService));
            _orderExecutor = orderExecutor ?? throw new ArgumentNullException(nameof(orderExecutor));
            _targetRiskService = targetRiskService ?? throw new ArgumentNullException(nameof(targetRiskService));
            _riskConfigStore = riskConfigStore ?? throw new ArgumentNullException(nameof(riskConfigStore));
            _riskIndexManager = riskIndexManager ?? throw new ArgumentNullException(nameof(riskIndexManager));
            _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
            _notificationPublisher = notificationPublisher ?? throw new ArgumentNullException(nameof(notificationPublisher));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _recoveryWorkerToken = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await _marketDataEngine.WaitForInitializationAsync();
            await ExecuteWithRecoveryRepositoryAsync(
                    (repository, ct) => repository.EnsureSchemaAsync(ct),
                    stoppingToken)
                .ConfigureAwait(false);
            await ExecuteWithOrderLifecycleRepositoryAsync(
                    (repository, ct) => repository.EnsureSchemaAsync(ct),
                    stoppingToken)
                .ConfigureAwait(false);
            await _orderOpenAttemptRepository.EnsureSchemaAsync(stoppingToken).ConfigureAwait(false);
            await _systemLogRepository.EnsureSchemaAsync(stoppingToken).ConfigureAwait(false);
            var recoveredCount = await ExecuteWithRecoveryRepositoryAsync(
                    (repository, ct) => repository.RecoverStaleProcessingAsync(90, ct),
                    stoppingToken)
                .ConfigureAwait(false);
            if (recoveredCount > 0)
            {
                _logger.LogWarning("交易恢复任务已回收遗留processing任务: count={Count}", recoveredCount);
            }

            var actionLoop = ConsumeActionTasksAsync(stoppingToken);
            var recoveryLoop = ConsumeRecoveryTasksAsync(stoppingToken);
            await Task.WhenAll(actionLoop, recoveryLoop).ConfigureAwait(false);
        }

        private async Task ConsumeActionTasksAsync(CancellationToken stoppingToken)
        {
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

        private async Task ConsumeRecoveryTasksAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                List<TradeRecoveryTaskEntity> tasks;
                try
                {
                    tasks = await ExecuteWithRecoveryRepositoryAsync(
                            (repository, ct) => repository.AcquireDueTasksAsync(8, _recoveryWorkerToken, ct),
                            stoppingToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "拉取交易恢复任务失败");
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
                    continue;
                }

                if (tasks.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(800), stoppingToken).ConfigureAwait(false);
                    continue;
                }

                foreach (var persistedTask in tasks)
                {
                    if (!TryBuildRecoveryTask(persistedTask, out var task))
                    {
                        _logger.LogError("交易恢复任务类型无效: taskId={TaskId} type={TaskType}", persistedTask.TaskId, persistedTask.TaskType);
                        await MarkRecoveryTaskFailedAsync(
                                new RecoveryTask
                                {
                                    TaskId = persistedTask.TaskId,
                                    ProcessingToken = _recoveryWorkerToken,
                                    Attempt = persistedTask.Attempt,
                                    MaxAttempts = persistedTask.MaxAttempts,
                                    Uid = persistedTask.Uid,
                                    UsId = persistedTask.UsId,
                                    OrderRequestId = persistedTask.OrderRequestId,
                                    PositionId = persistedTask.PositionId,
                                    Exchange = persistedTask.Exchange,
                                    Symbol = persistedTask.Symbol,
                                    Side = persistedTask.Side,
                                    Qty = persistedTask.Qty,
                                    LastError = persistedTask.LastError ?? string.Empty
                                },
                                "恢复任务类型无效",
                                stoppingToken)
                            .ConfigureAwait(false);
                        continue;
                    }

                    try
                    {
                        await ProcessRecoveryTaskAsync(task, stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "交易恢复任务处理异常: taskId={TaskId} type={Type} uid={Uid} usId={UsId} attempt={Attempt}",
                            task.TaskId, task.Type, task.Uid, task.UsId, task.Attempt);
                        await RequeueRecoveryTaskAsync(
                                CopyRecoveryTask(task, lastError: ex.Message),
                                "恢复任务异常",
                                stoppingToken)
                            .ConfigureAwait(false);
                    }
                }
            }
        }

        private async Task HandleTaskAsync(StrategyActionTask task, CancellationToken ct)
        {
            if (task == null)
            {
                return;
            }

            if (!ShouldProcessTask(task))
            {
                return;
            }

            if (!StrategyTradeTargetHelper.TryResolveFromTask(task, out var target, out var error))
            {
                _logger.LogWarning("无法解析交易目标: uid={Uid} usId={UsId} method={Method} error={Error}", task.Uid, task.UsId, task.Method, error);
                return;
            }

            var exchange = MarketDataKeyNormalizer.NormalizeExchange(target.Exchange);
            var symbol = MarketDataKeyNormalizer.NormalizeSymbol(target.Symbol);

            if (string.IsNullOrWhiteSpace(exchange) || string.IsNullOrWhiteSpace(symbol))
            {
                _logger.LogWarning("动作缺少交易所/交易对: uid={Uid} usId={UsId}", task.Uid, task.UsId);
                return;
            }

            var isTestingTask = IsTestingTask(task);
            var orderRequestId = await CreateOrderRequestAsync(target, isTestingTask, ct).ConfigureAwait(false);

            if (target.IsClose)
            {
                await HandleCloseAsync(task, target, exchange, symbol, isTestingTask, orderRequestId, ct).ConfigureAwait(false);
                return;
            }

            await HandleOpenAsync(task, target, exchange, symbol, isTestingTask, orderRequestId, ct).ConfigureAwait(false);
        }

        private bool ShouldProcessTask(StrategyActionTask task)
        {
            if (!task.UsId.HasValue || task.UsId.Value <= 0)
            {
                return true;
            }

            if (_queue.IsBlocked(task.UsId))
            {
                _logger.LogInformation("丢弃策略残留动作（策略已封禁）: uid={Uid} usId={UsId} method={Method}",
                    task.Uid, task.UsId, task.Method);
                return false;
            }

            var uidCode = task.UsId.Value.ToString();
            if (!_strategyEngine.HasStrategy(uidCode))
            {
                _logger.LogInformation("丢弃策略残留动作（策略未注册）: uid={Uid} usId={UsId} method={Method}",
                    task.Uid, task.UsId, task.Method);
                return false;
            }

            return true;
        }

        /// <summary>
        /// 处理开仓任务。
        /// 执行流程：1. 参数校验 2. 获取入场价格 3. 下单前持仓上限校验 4. 提交市价单 5. 创建仓位记录 6. 配置风险参数
        /// </summary>
        /// <param name="task">策略动作任务，包含策略ID、交易参数、风险参数等信息</param>
        /// <param name="exchange">交易所名称（已标准化）</param>
        /// <param name="symbol">交易对符号（已标准化）。</param>
        /// <param name="ct">取消令牌</param>
        private async Task HandleOpenAsync(
            StrategyActionTask task,
            StrategyTradeTarget target,
            string exchange,
            string symbol,
            bool isTestingTask,
            long? orderRequestId,
            CancellationToken ct)
        {
            // 1. 验证用户ID和策略ID是否存在
            if (!task.Uid.HasValue || !task.UsId.HasValue)
            {
                _logger.LogWarning("开仓动作缺少uid/usId");
                await MarkOrderRequestAsync(
                        orderRequestId,
                        TradingOrderStatuses.Rejected,
                        TradingOrderEventTypes.Rejected,
                        "开仓动作缺少uid/usId",
                        target,
                        normalizedQty: null,
                        exchangeOrderId: null,
                        averagePrice: null,
                        recoveryTaskId: null,
                        markCompleted: true,
                        ct: ct)
                    .ConfigureAwait(false);
                return;
            }

            // 2. 验证订单数量是否有效
            if (target.RequestedQty <= 0)
            {
                _logger.LogWarning("开仓动作订单数量<=0: uid={Uid} usId={UsId}", task.Uid, task.UsId);
                await MarkOrderRequestAsync(
                        orderRequestId,
                        TradingOrderStatuses.Rejected,
                        TradingOrderEventTypes.Rejected,
                        "开仓动作订单数量<=0",
                        target,
                        normalizedQty: null,
                        exchangeOrderId: null,
                        averagePrice: null,
                        recoveryTaskId: null,
                        markCompleted: true,
                        ct: ct)
                    .ConfigureAwait(false);
                return;
            }

            var positionSide = target.PositionSide;
            var orderSide = target.OrderSide;
            var effectiveMaxPositionQty = StrategyTradeTargetHelper.ResolveEffectiveMaxPositionQty(target);
            var signalTimeUtc = StrategyTradeTargetHelper.ResolveSignalTimeUtc(target);

            // 3. 从市场数据引擎获取当前价格作为预估入场价格
            var entryPrice = ResolveEntryPrice(exchange, symbol);
            if (entryPrice <= 0)
            {
                _logger.LogWarning("入场价格缺失，跳过开仓: {Exchange} {Symbol}", exchange, symbol);
                await MarkOrderRequestAsync(
                        orderRequestId,
                        TradingOrderStatuses.Rejected,
                        TradingOrderEventTypes.Rejected,
                        "入场价格缺失，跳过开仓",
                        target,
                        normalizedQty: null,
                        exchangeOrderId: null,
                        averagePrice: null,
                        recoveryTaskId: null,
                        markCompleted: true,
                        ct: ct)
                    .ConfigureAwait(false);
                return;
            }

            // 4. 下单前做同向持仓上限校验：允许信号照常触发，但在执行阶段阻断超限开仓。
            var openExposure = await _positionRepository.GetOpenExposureAsync(
                    task.Uid.Value,
                    task.UsId.Value,
                    exchange,
                    symbol,
                    positionSide,
                    ct)
                .ConfigureAwait(false);
            var currentOpenQty = Math.Max(0m, openExposure.OpenQty);
            var contract = ResolveContract(exchange, symbol);
            var riskResult = await _targetRiskService.EvaluateAsync(new StrategyTargetRiskContext
            {
                Target = target,
                Contract = contract,
                CurrentOpenQty = currentOpenQty
            }, ct).ConfigureAwait(false);
            target.NormalizedQty = riskResult.NormalizedQty;
            target.RiskChecks = riskResult.Snapshots;
            var requestedOrderQty = riskResult.NormalizedQty;
            if (!riskResult.Success)
            {
                var blockedMessage = riskResult.Message;
                _logger.LogInformation(
                    "开仓被上限阻断: uid={Uid} usId={UsId} {Exchange} {Symbol} {Side} signalTime={SignalTimeUtc:O} signalPrice={SignalPrice} openCount={OpenCount} currentOpenQty={CurrentOpenQty} requestQty={RequestQty} maxQty={MaxQty}",
                    task.Uid,
                    task.UsId,
                    exchange,
                    symbol,
                    positionSide,
                    signalTimeUtc,
                    entryPrice,
                    openExposure.OpenCount,
                    currentOpenQty,
                    requestedOrderQty,
                    effectiveMaxPositionQty);

                if (!isTestingTask)
                {
                    var attemptType = riskResult.Snapshots.Any(snapshot =>
                            string.Equals(snapshot.Rule, "最大持仓", StringComparison.OrdinalIgnoreCase) && !snapshot.Success)
                        ? OrderOpenAttemptTypes.BlockedByMaxPosition
                        : OrderOpenAttemptTypes.BlockedByPlatformRule;
                    await _orderOpenAttemptRepository.InsertAsync(
                            task.Uid.Value,
                            task.UsId.Value,
                            exchange,
                            symbol,
                            positionSide,
                            success: false,
                            errorMessage: blockedMessage,
                            attemptType: attemptType,
                            signalTimeUtc: signalTimeUtc,
                            signalPrice: entryPrice,
                            maxPositionQty: effectiveMaxPositionQty,
                            currentOpenQty: currentOpenQty,
                            requestOrderQty: requestedOrderQty,
                            ct: ct)
                        .ConfigureAwait(false);
                }

                await MarkOrderRequestAsync(
                        orderRequestId,
                        TradingOrderStatuses.Rejected,
                        TradingOrderEventTypes.Rejected,
                        blockedMessage,
                        target,
                        normalizedQty: requestedOrderQty,
                        exchangeOrderId: null,
                        averagePrice: null,
                        recoveryTaskId: null,
                        markCompleted: true,
                        ct: ct)
                    .ConfigureAwait(false);
                return;
            }

            await MarkOrderRequestAsync(
                    orderRequestId,
                    TradingOrderStatuses.Validated,
                    TradingOrderEventTypes.Validated,
                    riskResult.Message,
                    target,
                    normalizedQty: requestedOrderQty,
                    exchangeOrderId: null,
                    averagePrice: null,
                    recoveryTaskId: null,
                    markCompleted: false,
                    ct: ct)
                .ConfigureAwait(false);

            OrderExecutionResult? openOrderResult = null;
            if (!isTestingTask)
            {
                await MarkOrderRequestAsync(
                        orderRequestId,
                        TradingOrderStatuses.Submitting,
                        TradingOrderEventTypes.Submitting,
                        "已提交到交易所执行市价单",
                        target,
                        normalizedQty: requestedOrderQty,
                        exchangeOrderId: null,
                        averagePrice: null,
                        recoveryTaskId: null,
                        markCompleted: false,
                        ct: ct)
                    .ConfigureAwait(false);

                // 5. 非 testing 模式提交市价单到交易所（开仓订单，非平仓）
                openOrderResult = await _orderExecutor.PlaceMarketOrderAsync(new OrderExecutionRequest
                {
                    OrderRequestId = orderRequestId,
                    TargetId = target.TargetId,
                    Uid = task.Uid.Value,
                    ExchangeApiKeyId = target.ExchangeApiKeyId,
                    Exchange = exchange,
                    Symbol = symbol,
                    Side = orderSide,
                    PositionSide = positionSide,
                    TargetType = target.TargetType,
                    Qty = requestedOrderQty,
                    ReduceOnly = false  // 开仓订单，非平仓
                }, ct).ConfigureAwait(false);

                // 6. 检查订单是否成功提交
                if (!openOrderResult.Success)
                {
                    _logger.LogWarning("开仓订单失败: uid={Uid} usId={UsId} 错误={Error}", task.Uid, task.UsId, openOrderResult.ErrorMessage);
                    await MarkOrderRequestAsync(
                            orderRequestId,
                            TradingOrderStatuses.Failed,
                            TradingOrderEventTypes.Failed,
                            openOrderResult.ErrorMessage ?? "开仓订单失败",
                            target,
                            normalizedQty: requestedOrderQty,
                            exchangeOrderId: null,
                            averagePrice: null,
                            recoveryTaskId: null,
                            markCompleted: true,
                            ct: ct)
                        .ConfigureAwait(false);
                    await _orderOpenAttemptRepository.InsertAsync(
                            task.Uid.Value,
                            task.UsId.Value,
                            exchange,
                            symbol,
                            positionSide,
                            success: false,
                            errorMessage: openOrderResult.ErrorMessage,
                            attemptType: OrderOpenAttemptTypes.OrderResult,
                            signalTimeUtc: signalTimeUtc,
                            signalPrice: entryPrice,
                            maxPositionQty: effectiveMaxPositionQty,
                            currentOpenQty: currentOpenQty,
                            requestOrderQty: requestedOrderQty,
                            ct: ct)
                        .ConfigureAwait(false);

                    // 6.1 连续失败3次则自动暂停策略
                    var consecutiveFailures = await _orderOpenAttemptRepository.GetConsecutiveFailuresAsync(task.UsId.Value, 50, ct).ConfigureAwait(false);
                    if (consecutiveFailures >= 3)
                    {
                        const string pauseState = "paused_open_fail";
                        var updated = await _runtimeRepository.UpdateStateAsync(task.UsId.Value, task.Uid.Value, pauseState, ct).ConfigureAwait(false);
                        if (updated > 0)
                        {
                            _strategyEngine.RemoveStrategy(task.UsId.Value.ToString());
                            await _systemLogRepository.InsertAsync(
                                task.UsId.Value, task.Uid.Value,
                                "paused_open_fail",
                                $"连续开仓失败{consecutiveFailures}次，已自动暂停策略",
                                ct).ConfigureAwait(false);
                            _logger.LogWarning("策略已因连续开仓失败{Count}次自动暂停: uid={Uid} usId={UsId}", consecutiveFailures, task.Uid, task.UsId);
                        }
                    }
                    return;
                }

                // 7. 如果订单返回了实际成交均价，使用实际成交价更新入场价格
                if (openOrderResult.AveragePrice.HasValue && openOrderResult.AveragePrice.Value > 0)
                {
                    entryPrice = openOrderResult.AveragePrice.Value;
                }

                await MarkOrderRequestAsync(
                        orderRequestId,
                        TradingOrderStatuses.Submitted,
                        TradingOrderEventTypes.Submitted,
                        "交易所市价单已提交成功",
                        target,
                        normalizedQty: requestedOrderQty,
                        exchangeOrderId: openOrderResult.ExchangeOrderId,
                        averagePrice: openOrderResult.AveragePrice,
                        recoveryTaskId: null,
                        markCompleted: false,
                        ct: ct)
                    .ConfigureAwait(false);
            }

            // 8. 根据入场价格和风险参数计算止损价和止盈价
            var stopLossPrice = BuildStopLossPrice(entryPrice, target.StopLossPct, target.Leverage, positionSide);
            var takeProfitPrice = BuildTakeProfitPrice(entryPrice, target.TakeProfitPct, target.Leverage, positionSide);

            // 9. 创建仓位实体对象，包含所有仓位信息
            var entity = new StrategyPosition
            {
                Uid = task.Uid.Value,
                UsId = task.UsId.Value,
                StrategyVersionId = task.StrategyVersionId,
                ExchangeApiKeyId = isTestingTask ? null : task.ExchangeApiKeyId,
                Exchange = exchange,
                Symbol = symbol,
                Side = positionSide,
                EntryPrice = entryPrice,
                Qty = requestedOrderQty,
                Status = "Open",
                StopLossPrice = stopLossPrice,
                TakeProfitPrice = takeProfitPrice,
                TrailingEnabled = target.TrailingEnabled,
                TrailingStopPrice = null,  // 初始时追踪止损价格为空
                TrailingTriggered = false,  // 追踪止损尚未触发
                TrailingActivationPct = target.TrailingEnabled ? target.TrailingActivationPct : null,
                TrailingDrawdownPct = target.TrailingEnabled ? target.TrailingDrawdownPct : null,
                OpenedAt = DateTime.UtcNow,
                ClosedAt = null
            };

            // 10. 将仓位记录保存到数据库
            long positionId;
            try
            {
                positionId = await ExecuteWithRetryAsync(
                        "开仓写库",
                        () => _positionRepository.InsertAsync(entity, ct),
                        ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "开仓写库失败，触发补偿平仓: uid={Uid} usId={UsId} {Exchange} {Symbol} {Side}",
                    task.Uid,
                    task.UsId,
                    exchange,
                    symbol,
                    positionSide);

                if (!isTestingTask && openOrderResult?.Success == true)
                {
                    await TryCompensateOpenOrderAsync(
                            target,
                            exchange,
                            symbol,
                            orderSide,
                            orderRequestId,
                            requestedOrderQty,
                            ct)
                        .ConfigureAwait(false);
                }
                else
                {
                    await MarkOrderRequestAsync(
                            orderRequestId,
                            TradingOrderStatuses.Failed,
                            TradingOrderEventTypes.Failed,
                            "开仓写库失败",
                            target,
                            normalizedQty: requestedOrderQty,
                            exchangeOrderId: openOrderResult?.ExchangeOrderId,
                            averagePrice: openOrderResult?.AveragePrice,
                            recoveryTaskId: null,
                            markCompleted: true,
                            ct: ct)
                        .ConfigureAwait(false);
                }

                return;
            }
            entity.PositionId = positionId;

            // 10.1 非 testing 模式下记录开仓成功（用于连续失败统计）
            if (!isTestingTask)
            {
                await _orderOpenAttemptRepository.InsertAsync(
                        task.Uid.Value,
                        task.UsId.Value,
                        exchange,
                        symbol,
                        positionSide,
                        success: true,
                        errorMessage: null,
                        attemptType: OrderOpenAttemptTypes.OrderResult,
                        signalTimeUtc: signalTimeUtc,
                        signalPrice: entryPrice,
                        maxPositionQty: effectiveMaxPositionQty,
                        currentOpenQty: currentOpenQty,
                        requestOrderQty: requestedOrderQty,
                        ct: ct)
                    .ConfigureAwait(false);
            }

            // 11. 如果启用了追踪止损，保存追踪止损配置到风险配置存储
            PositionRiskConfig? riskConfig = null;
            if (target.TrailingEnabled)
            {
                riskConfig = new PositionRiskConfig
                {
                    Side = positionSide,
                    ActivationPct = target.TrailingActivationPct,  // 追踪止损激活百分比
                    DrawdownPct = target.TrailingDrawdownPct     // 追踪止损回撤百分比
                };
                _riskConfigStore.Upsert(positionId, riskConfig);
            }

            _riskIndexManager.UpsertPosition(entity, riskConfig);

            // 12. 记录开仓成功日志
            _logger.LogInformation("仓位已开: id={PositionId} uid={Uid} usId={UsId} versionId={VersionId} {Exchange} {Symbol} {Side} mode={Mode}",
                positionId, task.Uid, task.UsId, task.StrategyVersionId, exchange, symbol, positionSide, isTestingTask ? "testing" : "live");

            await MarkOrderRequestAsync(
                    orderRequestId,
                    TradingOrderStatuses.Completed,
                    TradingOrderEventTypes.Completed,
                    "开仓完成",
                    target,
                    normalizedQty: requestedOrderQty,
                    exchangeOrderId: openOrderResult?.ExchangeOrderId,
                    averagePrice: openOrderResult?.AveragePrice ?? entryPrice,
                    recoveryTaskId: null,
                    markCompleted: true,
                    ct: ct)
                .ConfigureAwait(false);

            var payload = JsonSerializer.Serialize(new
            {
                positionId,
                exchange,
                symbol,
                side = positionSide,
                qty = requestedOrderQty,
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
            StrategyTradeTarget target,
            string exchange,
            string symbol,
            bool isTestingTask,
            long? orderRequestId,
            CancellationToken ct)
        {
            if (!task.Uid.HasValue || !task.UsId.HasValue)
            {
                _logger.LogWarning("平仓动作缺少uid/usId");
                await MarkOrderRequestAsync(
                        orderRequestId,
                        TradingOrderStatuses.Rejected,
                        TradingOrderEventTypes.Rejected,
                        "平仓动作缺少uid/usId",
                        target,
                        normalizedQty: null,
                        exchangeOrderId: null,
                        averagePrice: null,
                        recoveryTaskId: null,
                        markCompleted: true,
                        ct: ct)
                    .ConfigureAwait(false);
                return;
            }

            var positionSide = target.PositionSide;
            var orderSide = target.OrderSide;
            var closedCount = 0;
            while (!ct.IsCancellationRequested)
            {
                var existing = await _positionRepository.FindOpenAsync(task.Uid.Value, task.UsId.Value, exchange, symbol, positionSide, ct)
                    .ConfigureAwait(false);
                if (existing == null)
                {
                    if (closedCount == 0)
                    {
                        _logger.LogInformation("没有可平仓位: uid={Uid} usId={UsId} {Exchange} {Symbol} {Side}",
                            task.Uid, task.UsId, exchange, symbol, positionSide);
                        await MarkOrderRequestAsync(
                                orderRequestId,
                                TradingOrderStatuses.Completed,
                                TradingOrderEventTypes.Skipped,
                                "没有可平仓位，已跳过",
                                target,
                                normalizedQty: null,
                                exchangeOrderId: null,
                                averagePrice: null,
                                recoveryTaskId: null,
                                markCompleted: true,
                                ct: ct)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        _logger.LogInformation("平仓动作完成: uid={Uid} usId={UsId} {Exchange} {Symbol} {Side} 共处理{ClosedCount}个仓位",
                            task.Uid, task.UsId, exchange, symbol, positionSide, closedCount);
                        await MarkOrderRequestAsync(
                                orderRequestId,
                                TradingOrderStatuses.Completed,
                                TradingOrderEventTypes.Completed,
                                $"平仓完成，共处理{closedCount}个仓位",
                                target,
                                normalizedQty: null,
                                exchangeOrderId: null,
                                averagePrice: null,
                                recoveryTaskId: null,
                                markCompleted: true,
                                ct: ct)
                            .ConfigureAwait(false);
                    }
                    return;
                }

                var closed = await TryCloseSinglePositionAsync(existing, exchange, symbol, orderSide, isTestingTask, orderRequestId, target, ct)
                    .ConfigureAwait(false);
                if (!closed)
                {
                    _logger.LogWarning("平仓动作中断: uid={Uid} usId={UsId} {Exchange} {Symbol} {Side} 已处理{ClosedCount}个仓位",
                        task.Uid, task.UsId, exchange, symbol, positionSide, closedCount);
                    await MarkOrderRequestAsync(
                            orderRequestId,
                            TradingOrderStatuses.Failed,
                            TradingOrderEventTypes.Failed,
                            $"平仓动作中断，已处理{closedCount}个仓位",
                            target,
                            normalizedQty: null,
                            exchangeOrderId: null,
                            averagePrice: null,
                            recoveryTaskId: null,
                            markCompleted: true,
                            ct: ct)
                        .ConfigureAwait(false);
                    return;
                }

                closedCount++;
            }
        }

        private async Task<bool> TryCloseSinglePositionAsync(
            StrategyPosition existing,
            string exchange,
            string symbol,
            string orderSide,
            bool isTestingTask,
            long? orderRequestId,
            StrategyTradeTarget target,
            CancellationToken ct)
        {
            decimal? closePrice;
            var useLocalSimulation = isTestingTask || !existing.ExchangeApiKeyId.HasValue;
            if (useLocalSimulation)
            {
                closePrice = ResolveLocalClosePrice(exchange, symbol, existing.EntryPrice);
            }
            else
            {
                await MarkOrderRequestAsync(
                        orderRequestId,
                        TradingOrderStatuses.Submitting,
                        TradingOrderEventTypes.Submitting,
                        $"开始平仓 positionId={existing.PositionId}",
                        target,
                        normalizedQty: existing.Qty,
                        exchangeOrderId: null,
                        averagePrice: null,
                        recoveryTaskId: null,
                        markCompleted: false,
                        ct: ct)
                    .ConfigureAwait(false);

                var orderResult = await _orderExecutor.PlaceMarketOrderAsync(new OrderExecutionRequest
                {
                    OrderRequestId = orderRequestId,
                    TargetId = target.TargetId,
                    Uid = existing.Uid,
                    ExchangeApiKeyId = existing.ExchangeApiKeyId,
                    Exchange = exchange,
                    Symbol = symbol,
                    Side = orderSide,
                    PositionSide = existing.Side ?? string.Empty,
                    TargetType = target.TargetType,
                    Qty = existing.Qty,
                    ReduceOnly = true
                }, ct).ConfigureAwait(false);

                if (!orderResult.Success)
                {
                    _logger.LogWarning("平仓订单失败: positionId={PositionId} 错误={Error}", existing.PositionId, orderResult.ErrorMessage);
                    await MarkOrderRequestAsync(
                            orderRequestId,
                            TradingOrderStatuses.Failed,
                            TradingOrderEventTypes.Failed,
                            orderResult.ErrorMessage ?? $"平仓订单失败 positionId={existing.PositionId}",
                            target,
                            normalizedQty: existing.Qty,
                            exchangeOrderId: null,
                            averagePrice: null,
                            recoveryTaskId: null,
                            markCompleted: false,
                            ct: ct)
                        .ConfigureAwait(false);
                    return false;
                }

                await MarkOrderRequestAsync(
                        orderRequestId,
                        TradingOrderStatuses.Submitted,
                        TradingOrderEventTypes.Submitted,
                        $"平仓市价单提交成功 positionId={existing.PositionId}",
                        target,
                        normalizedQty: existing.Qty,
                        exchangeOrderId: orderResult.ExchangeOrderId,
                        averagePrice: orderResult.AveragePrice,
                        recoveryTaskId: null,
                        markCompleted: false,
                        ct: ct)
                    .ConfigureAwait(false);
                closePrice = ResolveClosePrice(orderResult, exchange, symbol);
            }

            var closedAt = DateTime.UtcNow;
            int closeAffected;
            try
            {
                closeAffected = await ExecuteWithRetryAsync(
                        "平仓写库",
                        () => _positionRepository.CloseAsync(
                            existing.PositionId,
                            trailingTriggered: false,
                            closedAt: closedAt,
                            closeReason: null,
                            closePrice: closePrice,
                            ct),
                        ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "平仓订单已提交但本地写库异常，已转入恢复队列: positionId={PositionId} uid={Uid} usId={UsId}",
                    existing.PositionId,
                    existing.Uid,
                    existing.UsId);
                await EnqueueCloseWriteRecoveryAsync(existing, closePrice, closedAt, ex.Message, orderRequestId, ct).ConfigureAwait(false);
                return false;
            }

            if (closeAffected <= 0)
            {
                var latest = await _positionRepository.GetByIdAsync(existing.PositionId, existing.Uid, ct).ConfigureAwait(false);
                if (latest == null || !string.Equals(latest.Status, "Closed", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogError(
                        "平仓订单已提交但本地写库未生效: positionId={PositionId} uid={Uid} usId={UsId}",
                        existing.PositionId,
                        existing.Uid,
                        existing.UsId);
                    await EnqueueCloseWriteRecoveryAsync(existing, closePrice, closedAt, "平仓写库返回0且仓位仍为Open", orderRequestId, ct)
                        .ConfigureAwait(false);
                    return false;
                }

                _logger.LogWarning(
                    "平仓写库返回0行，但仓位已是Closed，按成功处理: positionId={PositionId} uid={Uid} usId={UsId}",
                    existing.PositionId,
                    existing.Uid,
                    existing.UsId);
            }

            CleanupClosedPositionLocalState(existing.PositionId);

            _logger.LogInformation("仓位已平: id={PositionId} uid={Uid} usId={UsId} mode={Mode}",
                existing.PositionId,
                existing.Uid,
                existing.UsId,
                useLocalSimulation ? "testing" : "live");
            return true;
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

        private decimal? ResolveClosePrice(OrderExecutionResult orderResult, string exchange, string symbol)
        {
            if (orderResult.AveragePrice.HasValue && orderResult.AveragePrice.Value > 0)
            {
                return orderResult.AveragePrice.Value;
            }

            var fallbackPrice = ResolveEntryPrice(exchange, symbol);
            return fallbackPrice > 0 ? fallbackPrice : null;
        }

        private decimal? ResolveLocalClosePrice(string exchange, string symbol, decimal fallbackEntryPrice)
        {
            var marketPrice = ResolveEntryPrice(exchange, symbol);
            if (marketPrice > 0)
            {
                return marketPrice;
            }

            return fallbackEntryPrice > 0 ? fallbackEntryPrice : null;
        }

        private ContractDetails? ResolveContract(string exchange, string symbol)
        {
            if (!TryParseExchangeEnum(exchange, out var exchangeEnum))
            {
                return null;
            }

            var contracts = _contractDetailsCacheService.GetContractsByExchange(exchangeEnum);
            if (contracts.Count == 0)
            {
                return null;
            }

            var normalizedSymbol = MarketDataKeyNormalizer.NormalizeSymbol(symbol);
            if (contracts.TryGetValue(normalizedSymbol, out var direct))
            {
                return direct;
            }

            foreach (var item in contracts)
            {
                if (string.Equals(item.Key, normalizedSymbol, StringComparison.OrdinalIgnoreCase) ||
                    item.Key.StartsWith(normalizedSymbol + ":", StringComparison.OrdinalIgnoreCase))
                {
                    return item.Value;
                }
            }

            var parts = normalizedSymbol.Split('/');
            var baseCoin = parts.Length > 0 ? parts[0] : normalizedSymbol;
            var quoteCoin = parts.Length > 1 ? parts[1] : string.Empty;
            return contracts.Values.FirstOrDefault(contract =>
                string.Equals(contract.Base, baseCoin, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(contract.Quote, quoteCoin, StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryParseExchangeEnum(string exchange, out MarketDataConfig.ExchangeEnum exchangeEnum)
        {
            foreach (var candidate in Enum.GetValues<MarketDataConfig.ExchangeEnum>())
            {
                if (string.Equals(MarketDataConfig.ExchangeToString(candidate), exchange, StringComparison.OrdinalIgnoreCase))
                {
                    exchangeEnum = candidate;
                    return true;
                }
            }

            exchangeEnum = default;
            return false;
        }

        private async Task<long?> CreateOrderRequestAsync(StrategyTradeTarget target, bool isTestingTask, CancellationToken ct)
        {
            try
            {
                var entity = new TradingOrderRequestEntity
                {
                    TargetId = target.TargetId,
                    StrategyUid = target.StrategyUid,
                    Uid = target.Uid,
                    UsId = target.UsId,
                    StrategyVersionId = target.StrategyVersionId,
                    StrategyVersionNo = target.StrategyVersionNo,
                    ExchangeApiKeyId = target.ExchangeApiKeyId,
                    Exchange = target.Exchange,
                    Symbol = target.Symbol,
                    TargetType = target.TargetType,
                    PositionSide = target.PositionSide,
                    OrderSide = target.OrderSide,
                    ReduceOnly = target.ReduceOnly,
                    IsTesting = isTestingTask,
                    Stage = target.Stage,
                    Method = target.Method,
                    RequestedQty = target.RequestedQty,
                    NormalizedQty = target.NormalizedQty > 0 ? target.NormalizedQty : null,
                    MaxPositionQty = target.MaxPositionQty,
                    Leverage = target.Leverage,
                    SignalTimeUtc = StrategyTradeTargetHelper.ResolveSignalTimeUtc(target),
                    TriggerResultsJson = SerializeToJson(target.TriggerResults),
                    RiskChecksJson = SerializeToJson(target.RiskChecks),
                    LatestStatus = TradingOrderStatuses.Pending,
                    StatusMessage = "已接收交易目标"
                };

                var requestId = await ExecuteWithOrderLifecycleRepositoryAsync(
                        (repository, token) => repository.InsertAsync(entity, token),
                        ct)
                    .ConfigureAwait(false);

                await AppendOrderRequestEventAsync(
                        requestId,
                        TradingOrderEventTypes.Created,
                        TradingOrderStatuses.Pending,
                        "交易目标已进入订单流水",
                        new
                        {
                            target.TargetId,
                            target.TargetType,
                            target.RequestedQty,
                            target.Exchange,
                            target.Symbol
                        },
                        ct)
                    .ConfigureAwait(false);

                return requestId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "创建订单流水失败: targetId={TargetId} uid={Uid} usId={UsId}",
                    target.TargetId,
                    target.Uid,
                    target.UsId);
                return null;
            }
        }

        private async Task MarkOrderRequestAsync(
            long? orderRequestId,
            string status,
            string eventType,
            string message,
            StrategyTradeTarget target,
            decimal? normalizedQty,
            string? exchangeOrderId,
            decimal? averagePrice,
            long? recoveryTaskId,
            bool markCompleted,
            CancellationToken ct)
        {
            if (!orderRequestId.HasValue || orderRequestId.Value <= 0)
            {
                return;
            }

            try
            {
                await ExecuteWithOrderLifecycleRepositoryAsync(
                        (repository, token) => repository.UpdateStatusAsync(
                            orderRequestId.Value,
                            status,
                            message,
                            normalizedQty,
                            exchangeOrderId,
                            averagePrice,
                            recoveryTaskId,
                            SerializeToJson(target.RiskChecks),
                            markCompleted,
                            token),
                        ct)
                    .ConfigureAwait(false);

                await AppendOrderRequestEventAsync(
                        orderRequestId.Value,
                        eventType,
                        status,
                        message,
                        new
                        {
                            target.TargetId,
                            target.TargetType,
                            normalizedQty,
                            exchangeOrderId,
                            averagePrice,
                            recoveryTaskId,
                            riskChecks = target.RiskChecks
                        },
                        ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "更新订单流水失败: requestId={RequestId} status={Status} message={Message}",
                    orderRequestId,
                    status,
                    message);
            }
        }

        private async Task AppendOrderRequestEventAsync(
            long requestId,
            string eventType,
            string status,
            string message,
            object detail,
            CancellationToken ct)
        {
            if (requestId <= 0)
            {
                return;
            }

            await ExecuteWithOrderLifecycleRepositoryAsync(
                    (repository, token) => repository.AppendEventAsync(new TradingOrderEventEntity
                    {
                        RequestId = requestId,
                        EventType = eventType,
                        Status = status,
                        Message = message,
                        DetailJson = SerializeToJson(detail)
                    }, token),
                    ct)
                .ConfigureAwait(false);
        }

        private async Task MarkRecoveryPendingOrderRequestAsync(long? orderRequestId, long recoveryTaskId, string scene, CancellationToken ct)
        {
            if (!orderRequestId.HasValue || orderRequestId.Value <= 0)
            {
                return;
            }

            try
            {
                await ExecuteWithOrderLifecycleRepositoryAsync(
                        (repository, token) => repository.UpdateStatusAsync(
                            orderRequestId.Value,
                            TradingOrderStatuses.RecoveryPending,
                            $"{scene}已进入恢复队列",
                            null,
                            null,
                            null,
                            recoveryTaskId,
                            null,
                            false,
                            token),
                        ct)
                    .ConfigureAwait(false);

                await AppendOrderRequestEventAsync(
                        orderRequestId.Value,
                        TradingOrderEventTypes.RecoveryQueued,
                        TradingOrderStatuses.RecoveryPending,
                        $"{scene}已进入恢复队列",
                        new { recoveryTaskId, scene },
                        ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "更新恢复挂起状态失败: requestId={RequestId}", orderRequestId);
            }
        }

        private async Task MarkRecoveredOrderRequestAsync(long? orderRequestId, string message, CancellationToken ct)
        {
            if (!orderRequestId.HasValue || orderRequestId.Value <= 0)
            {
                return;
            }

            await ExecuteWithOrderLifecycleRepositoryAsync(
                    (repository, token) => repository.UpdateStatusAsync(
                        orderRequestId.Value,
                        TradingOrderStatuses.Recovered,
                        message,
                        null,
                        null,
                        null,
                        null,
                        null,
                        true,
                        token),
                    ct)
                .ConfigureAwait(false);
            await AppendOrderRequestEventAsync(
                    orderRequestId.Value,
                    TradingOrderEventTypes.Recovered,
                    TradingOrderStatuses.Recovered,
                    message,
                    new { orderRequestId },
                    ct)
                .ConfigureAwait(false);
        }

        private async Task MarkCompletedOrderRequestAsync(long? orderRequestId, string message, CancellationToken ct)
        {
            if (!orderRequestId.HasValue || orderRequestId.Value <= 0)
            {
                return;
            }

            await ExecuteWithOrderLifecycleRepositoryAsync(
                    (repository, token) => repository.UpdateStatusAsync(
                        orderRequestId.Value,
                        TradingOrderStatuses.Completed,
                        message,
                        null,
                        null,
                        null,
                        null,
                        null,
                        true,
                        token),
                    ct)
                .ConfigureAwait(false);
            await AppendOrderRequestEventAsync(
                    orderRequestId.Value,
                    TradingOrderEventTypes.Completed,
                    TradingOrderStatuses.Completed,
                    message,
                    new { orderRequestId },
                    ct)
                .ConfigureAwait(false);
        }

        private async Task MarkFailedOrderRequestAsync(long? orderRequestId, string message, CancellationToken ct)
        {
            if (!orderRequestId.HasValue || orderRequestId.Value <= 0)
            {
                return;
            }

            await ExecuteWithOrderLifecycleRepositoryAsync(
                    (repository, token) => repository.UpdateStatusAsync(
                        orderRequestId.Value,
                        TradingOrderStatuses.Failed,
                        message,
                        null,
                        null,
                        null,
                        null,
                        null,
                        true,
                        token),
                    ct)
                .ConfigureAwait(false);
            await AppendOrderRequestEventAsync(
                    orderRequestId.Value,
                    TradingOrderEventTypes.Failed,
                    TradingOrderStatuses.Failed,
                    message,
                    new { orderRequestId },
                    ct)
                .ConfigureAwait(false);
        }

        private static string SerializeToJson<T>(T value)
        {
            return JsonSerializer.Serialize(value);
        }

        private static StrategyTradeTarget BuildRecoveryTarget(RecoveryTask task)
        {
            var normalizedSide = task.Side?.Trim() ?? string.Empty;
            var positionSide = normalizedSide.Equals("buy", StringComparison.OrdinalIgnoreCase)
                ? "Short"
                : normalizedSide.Equals("sell", StringComparison.OrdinalIgnoreCase)
                    ? "Long"
                    : normalizedSide;
            var orderSide = normalizedSide.Equals("buy", StringComparison.OrdinalIgnoreCase) ||
                            normalizedSide.Equals("sell", StringComparison.OrdinalIgnoreCase)
                ? normalizedSide.ToLowerInvariant()
                : string.Equals(positionSide, "Long", StringComparison.OrdinalIgnoreCase)
                    ? "sell"
                    : "buy";
            return new StrategyTradeTarget
            {
                TargetId = task.OrderRequestId.HasValue && task.OrderRequestId.Value > 0
                    ? $"recovery:{task.OrderRequestId.Value}"
                    : $"recovery-task:{task.TaskId}",
                StrategyUid = task.UsId?.ToString() ?? string.Empty,
                Uid = task.Uid,
                UsId = task.UsId,
                ExchangeApiKeyId = task.ExchangeApiKeyId,
                Exchange = task.Exchange,
                Symbol = task.Symbol,
                PositionSide = positionSide,
                OrderSide = orderSide,
                TargetType = task.Type == RecoveryTaskType.OpenCompensation
                    ? "open_compensation_recovery"
                    : "close_write_recovery",
                ReduceOnly = true,
                RequestedQty = task.Qty,
                NormalizedQty = task.Qty
            };
        }

        private async Task TryCompensateOpenOrderAsync(
            StrategyTradeTarget target,
            string exchange,
            string symbol,
            string openOrderSide,
            long? orderRequestId,
            decimal qty,
            CancellationToken ct)
        {
            if (!target.Uid.HasValue || qty <= 0)
            {
                return;
            }

            var compensateSide = string.Equals(openOrderSide, "buy", StringComparison.OrdinalIgnoreCase)
                ? "sell"
                : "buy";

            var (success, error) = await TryExecuteCompensationOrderAsync(
                    target.Uid.Value,
                    target.ExchangeApiKeyId,
                    exchange,
                    symbol,
                    compensateSide,
                    qty,
                    orderRequestId,
                    target,
                    ct)
                .ConfigureAwait(false);

            if (success)
            {
                _logger.LogWarning(
                    "开仓写库失败后补偿平仓成功: uid={Uid} usId={UsId} {Exchange} {Symbol}",
                    target.Uid,
                    target.UsId,
                    exchange,
                    symbol);
                await MarkOrderRequestAsync(
                        orderRequestId,
                        TradingOrderStatuses.Recovered,
                        TradingOrderEventTypes.Recovered,
                        "开仓写库失败后补偿平仓成功",
                        target,
                        normalizedQty: qty,
                        exchangeOrderId: null,
                        averagePrice: null,
                        recoveryTaskId: null,
                        markCompleted: true,
                        ct: ct)
                    .ConfigureAwait(false);
                return;
            }

            _logger.LogError(
                "开仓写库失败后补偿平仓失败，已转入恢复队列: uid={Uid} usId={UsId} {Exchange} {Symbol} 错误={Error}",
                target.Uid,
                target.UsId,
                exchange,
                symbol,
                error);

            await EnqueueOpenCompensationRecoveryAsync(target, exchange, symbol, compensateSide, error, orderRequestId, qty, ct).ConfigureAwait(false);
        }

        private async Task<(bool Success, string Error)> TryExecuteCompensationOrderAsync(
            long uid,
            long? exchangeApiKeyId,
            string exchange,
            string symbol,
            string side,
            decimal qty,
            long? orderRequestId,
            StrategyTradeTarget target,
            CancellationToken ct)
        {
            try
            {
                var compensateResult = await _orderExecutor.PlaceMarketOrderAsync(new OrderExecutionRequest
                {
                    OrderRequestId = orderRequestId,
                    TargetId = target.TargetId,
                    Uid = uid,
                    ExchangeApiKeyId = exchangeApiKeyId,
                    Exchange = exchange,
                    Symbol = symbol,
                    Side = side,
                    PositionSide = target.PositionSide,
                    TargetType = target.TargetType,
                    Qty = qty,
                    ReduceOnly = true
                }, ct).ConfigureAwait(false);

                return compensateResult.Success
                    ? (true, string.Empty)
                    : (false, compensateResult.ErrorMessage ?? "补偿平仓失败");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        private async Task EnqueueOpenCompensationRecoveryAsync(
            StrategyTradeTarget target,
            string exchange,
            string symbol,
            string compensateSide,
            string error,
            long? orderRequestId,
            decimal qty,
            CancellationToken ct)
        {
            var recoveryTask = new RecoveryTask
            {
                Type = RecoveryTaskType.OpenCompensation,
                Attempt = 1,
                MaxAttempts = RecoveryMaxAttempts,
                NotBeforeUtc = DateTime.UtcNow.AddSeconds(2),
                Uid = target.Uid,
                UsId = target.UsId,
                OrderRequestId = orderRequestId,
                ExchangeApiKeyId = target.ExchangeApiKeyId,
                Exchange = exchange,
                Symbol = symbol,
                Side = compensateSide,
                Qty = qty,
                LastError = error ?? string.Empty
            };

            await TryEnqueueRecoveryTaskAsync(recoveryTask, "开仓补偿恢复", ct).ConfigureAwait(false);
        }

        private async Task EnqueueCloseWriteRecoveryAsync(
            StrategyPosition existing,
            decimal? closePrice,
            DateTime closedAtUtc,
            string error,
            long? orderRequestId,
            CancellationToken ct)
        {
            var recoveryTask = new RecoveryTask
            {
                Type = RecoveryTaskType.CloseWrite,
                Attempt = 1,
                MaxAttempts = RecoveryMaxAttempts,
                NotBeforeUtc = DateTime.UtcNow.AddSeconds(2),
                Uid = existing.Uid,
                UsId = existing.UsId,
                OrderRequestId = orderRequestId,
                PositionId = existing.PositionId,
                ExchangeApiKeyId = existing.ExchangeApiKeyId,
                Exchange = existing.Exchange ?? string.Empty,
                Symbol = existing.Symbol ?? string.Empty,
                Side = existing.Side ?? string.Empty,
                Qty = existing.Qty,
                ClosePrice = closePrice,
                ClosedAtUtc = closedAtUtc,
                LastError = error ?? string.Empty
            };

            await TryEnqueueRecoveryTaskAsync(recoveryTask, "平仓写库恢复", ct).ConfigureAwait(false);
        }

        private async Task TryEnqueueRecoveryTaskAsync(RecoveryTask task, string scene, CancellationToken ct)
        {
            var entity = new TradeRecoveryTaskEntity
            {
                TaskType = MapRecoveryTaskType(task.Type),
                Uid = task.Uid,
                UsId = task.UsId,
                OrderRequestId = task.OrderRequestId,
                PositionId = task.PositionId,
                ExchangeApiKeyId = task.ExchangeApiKeyId,
                Exchange = task.Exchange,
                Symbol = task.Symbol,
                Side = task.Side,
                Qty = task.Qty,
                ClosePrice = task.ClosePrice,
                ClosedAtUtc = task.ClosedAtUtc == default ? null : task.ClosedAtUtc,
                Attempt = Math.Max(1, task.Attempt),
                MaxAttempts = Math.Max(1, task.MaxAttempts),
                NextRetryAtUtc = task.NotBeforeUtc.ToUniversalTime(),
                LastError = task.LastError
            };

            try
            {
                var taskId = await ExecuteWithRecoveryRepositoryAsync(
                        (repository, token) => repository.InsertPendingAsync(entity, token),
                        ct)
                    .ConfigureAwait(false);
                _logger.LogWarning("已持久化交易恢复任务: taskId={TaskId} scene={Scene} type={Type} uid={Uid} usId={UsId} attempt={Attempt}",
                    taskId, scene, task.Type, task.Uid, task.UsId, task.Attempt);
                await MarkRecoveryPendingOrderRequestAsync(task.OrderRequestId, taskId, scene, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "交易恢复任务入库失败: scene={Scene} type={Type} uid={Uid} usId={UsId}",
                    scene, task.Type, task.Uid, task.UsId);
                await MarkFailedOrderRequestAsync(task.OrderRequestId, $"{scene}入恢复队列失败: {ex.Message}", ct).ConfigureAwait(false);
                await TryPublishRecoveryFailedAsync(
                        CopyRecoveryTask(task, lastError: ex.Message),
                        "恢复任务入库失败",
                        ct)
                    .ConfigureAwait(false);
            }
        }

        private async Task ProcessRecoveryTaskAsync(RecoveryTask task, CancellationToken ct)
        {
            if (task.NotBeforeUtc > DateTime.UtcNow)
            {
                var delay = task.NotBeforeUtc - DateTime.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                }
            }

            switch (task.Type)
            {
                case RecoveryTaskType.CloseWrite:
                    await ProcessCloseWriteRecoveryAsync(task, ct).ConfigureAwait(false);
                    return;
                case RecoveryTaskType.OpenCompensation:
                    await ProcessOpenCompensationRecoveryAsync(task, ct).ConfigureAwait(false);
                    return;
                default:
                    await MarkRecoveryTaskFailedAsync(task, "未知恢复任务类型", ct).ConfigureAwait(false);
                    return;
            }
        }

        private async Task ProcessCloseWriteRecoveryAsync(RecoveryTask task, CancellationToken ct)
        {
            if (!task.Uid.HasValue || task.PositionId <= 0)
            {
                await MarkRecoveryTaskFailedAsync(task, "平仓写库恢复参数无效", ct).ConfigureAwait(false);
                return;
            }

            var latest = await _positionRepository.GetByIdAsync(task.PositionId, task.Uid.Value, ct).ConfigureAwait(false);
            if (latest != null && string.Equals(latest.Status, "Closed", StringComparison.OrdinalIgnoreCase))
            {
                CleanupClosedPositionLocalState(task.PositionId);
                _logger.LogInformation("恢复任务命中已闭仓状态: taskId={TaskId} positionId={PositionId} uid={Uid} usId={UsId}",
                    task.TaskId, task.PositionId, task.Uid, task.UsId);
                await MarkRecoveryTaskSucceededAsync(task, ct).ConfigureAwait(false);
                return;
            }

            try
            {
                var affected = await _positionRepository.CloseAsync(
                        task.PositionId,
                        trailingTriggered: false,
                        closedAt: task.ClosedAtUtc == default ? DateTime.UtcNow : task.ClosedAtUtc,
                        closeReason: "recovery",
                        closePrice: task.ClosePrice,
                        ct)
                    .ConfigureAwait(false);

                if (affected > 0)
                {
                    CleanupClosedPositionLocalState(task.PositionId);
                    _logger.LogWarning("恢复任务平仓写库成功: taskId={TaskId} positionId={PositionId} uid={Uid} usId={UsId} attempt={Attempt}",
                        task.TaskId, task.PositionId, task.Uid, task.UsId, task.Attempt);
                    await MarkRecoveryTaskSucceededAsync(task, ct).ConfigureAwait(false);
                    return;
                }

                latest = await _positionRepository.GetByIdAsync(task.PositionId, task.Uid.Value, ct).ConfigureAwait(false);
                if (latest != null && string.Equals(latest.Status, "Closed", StringComparison.OrdinalIgnoreCase))
                {
                    CleanupClosedPositionLocalState(task.PositionId);
                    _logger.LogInformation("恢复任务确认仓位已闭: taskId={TaskId} positionId={PositionId} uid={Uid} usId={UsId}",
                        task.TaskId, task.PositionId, task.Uid, task.UsId);
                    await MarkRecoveryTaskSucceededAsync(task, ct).ConfigureAwait(false);
                    return;
                }

                await RequeueRecoveryTaskAsync(task, "平仓写库恢复仍未成功", ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await RequeueRecoveryTaskAsync(
                        CopyRecoveryTask(task, lastError: ex.Message),
                        "平仓写库恢复异常",
                        ct)
                    .ConfigureAwait(false);
            }
        }

        private async Task ProcessOpenCompensationRecoveryAsync(RecoveryTask task, CancellationToken ct)
        {
            if (!task.Uid.HasValue || task.Qty <= 0)
            {
                await MarkRecoveryTaskFailedAsync(task, "开仓补偿恢复参数无效", ct).ConfigureAwait(false);
                return;
            }

            var (success, error) = await TryExecuteCompensationOrderAsync(
                    task.Uid.Value,
                    task.ExchangeApiKeyId,
                    task.Exchange,
                    task.Symbol,
                    task.Side,
                    task.Qty,
                    task.OrderRequestId,
                    BuildRecoveryTarget(task),
                    ct)
                .ConfigureAwait(false);

            if (success)
            {
                _logger.LogWarning("恢复任务补偿平仓成功: taskId={TaskId} uid={Uid} usId={UsId} {Exchange} {Symbol} attempt={Attempt}",
                    task.TaskId, task.Uid, task.UsId, task.Exchange, task.Symbol, task.Attempt);
                await MarkRecoveryTaskSucceededAsync(task, ct).ConfigureAwait(false);
                return;
            }

            await RequeueRecoveryTaskAsync(
                    CopyRecoveryTask(task, lastError: error),
                    "开仓补偿恢复失败",
                    ct)
                .ConfigureAwait(false);
        }

        private async Task RequeueRecoveryTaskAsync(RecoveryTask task, string reason, CancellationToken ct)
        {
            if (task.Attempt >= task.MaxAttempts)
            {
                _logger.LogError("恢复任务重试耗尽: taskId={TaskId} type={Type} uid={Uid} usId={UsId} positionId={PositionId} reason={Reason} error={Error}",
                    task.TaskId, task.Type, task.Uid, task.UsId, task.PositionId, reason, task.LastError);
                await MarkRecoveryTaskFailedAsync(task, reason, ct).ConfigureAwait(false);
                return;
            }

            var nextAttempt = task.Attempt + 1;
            var nextRetryAtUtc = DateTime.UtcNow.Add(BuildRecoveryDelay(nextAttempt));
            var affected = await ExecuteWithRecoveryRepositoryAsync(
                    (repository, token) => repository.RequeueAsync(
                        task.TaskId,
                        task.ProcessingToken,
                        nextAttempt,
                        nextRetryAtUtc,
                        task.LastError,
                        token),
                    ct)
                .ConfigureAwait(false);
            if (affected <= 0)
            {
                _logger.LogWarning("恢复任务重入库失败（任务可能已被其他节点处理）: taskId={TaskId} type={Type}", task.TaskId, task.Type);
                return;
            }

            _logger.LogWarning(
                "恢复任务重入库成功: taskId={TaskId} type={Type} uid={Uid} usId={UsId} nextAttempt={Attempt} reason={Reason}",
                task.TaskId,
                task.Type,
                task.Uid,
                task.UsId,
                nextAttempt,
                reason);
        }

        private async Task MarkRecoveryTaskSucceededAsync(RecoveryTask task, CancellationToken ct)
        {
            if (task.TaskId <= 0 || string.IsNullOrWhiteSpace(task.ProcessingToken))
            {
                return;
            }

            var affected = await ExecuteWithRecoveryRepositoryAsync(
                    (repository, token) => repository.MarkSucceededAsync(task.TaskId, task.ProcessingToken, token),
                    ct)
                .ConfigureAwait(false);
            if (affected <= 0)
            {
                _logger.LogWarning("标记恢复任务成功失败（任务可能已被处理）: taskId={TaskId} type={Type}", task.TaskId, task.Type);
            }

            if (task.Type == RecoveryTaskType.OpenCompensation)
            {
                await MarkRecoveredOrderRequestAsync(task.OrderRequestId, "开仓补偿恢复成功", ct).ConfigureAwait(false);
                return;
            }

            if (task.Type == RecoveryTaskType.CloseWrite)
            {
                await MarkCompletedOrderRequestAsync(task.OrderRequestId, "平仓写库恢复成功", ct).ConfigureAwait(false);
            }
        }

        private async Task MarkRecoveryTaskFailedAsync(RecoveryTask task, string reason, CancellationToken ct)
        {
            if (task.TaskId > 0 && !string.IsNullOrWhiteSpace(task.ProcessingToken))
            {
                var affected = await ExecuteWithRecoveryRepositoryAsync(
                        (repository, token) => repository.MarkFailedAsync(
                            task.TaskId,
                            task.ProcessingToken,
                            task.Attempt,
                            $"{reason}; {task.LastError}".Trim(';', ' '),
                            token),
                        ct)
                    .ConfigureAwait(false);
                if (affected <= 0)
                {
                    _logger.LogWarning("标记恢复任务失败未生效（任务可能已被处理）: taskId={TaskId} type={Type}", task.TaskId, task.Type);
                }
            }

            await MarkFailedOrderRequestAsync(task.OrderRequestId, $"{reason}; {task.LastError}".Trim(';', ' '), ct).ConfigureAwait(false);

            await TryPublishRecoveryFailedAsync(task, reason, ct).ConfigureAwait(false);
        }

        private bool TryBuildRecoveryTask(TradeRecoveryTaskEntity persistedTask, out RecoveryTask task)
        {
            task = default!;
            if (!TryParseRecoveryTaskType(persistedTask.TaskType, out var taskType))
            {
                return false;
            }

            task = new RecoveryTask
            {
                TaskId = persistedTask.TaskId,
                ProcessingToken = _recoveryWorkerToken,
                Type = taskType,
                Attempt = Math.Max(1, persistedTask.Attempt),
                MaxAttempts = Math.Max(1, persistedTask.MaxAttempts),
                NotBeforeUtc = persistedTask.NextRetryAtUtc,
                Uid = persistedTask.Uid,
                UsId = persistedTask.UsId,
                OrderRequestId = persistedTask.OrderRequestId,
                PositionId = persistedTask.PositionId,
                ExchangeApiKeyId = persistedTask.ExchangeApiKeyId,
                Exchange = persistedTask.Exchange ?? string.Empty,
                Symbol = persistedTask.Symbol ?? string.Empty,
                Side = persistedTask.Side ?? string.Empty,
                Qty = persistedTask.Qty,
                ClosePrice = persistedTask.ClosePrice,
                ClosedAtUtc = persistedTask.ClosedAtUtc ?? default,
                LastError = persistedTask.LastError ?? string.Empty
            };
            return true;
        }

        private static string MapRecoveryTaskType(RecoveryTaskType type)
        {
            return type switch
            {
                RecoveryTaskType.CloseWrite => TradeRecoveryTaskTypes.CloseWrite,
                RecoveryTaskType.OpenCompensation => TradeRecoveryTaskTypes.OpenCompensation,
                _ => type.ToString().ToLowerInvariant()
            };
        }

        private static bool TryParseRecoveryTaskType(string taskType, out RecoveryTaskType type)
        {
            type = default;
            if (string.IsNullOrWhiteSpace(taskType))
            {
                return false;
            }

            switch (taskType.Trim().ToLowerInvariant())
            {
                case TradeRecoveryTaskTypes.CloseWrite:
                    type = RecoveryTaskType.CloseWrite;
                    return true;
                case TradeRecoveryTaskTypes.OpenCompensation:
                    type = RecoveryTaskType.OpenCompensation;
                    return true;
                default:
                    return false;
            }
        }

        private static RecoveryTask CopyRecoveryTask(
            RecoveryTask source,
            int? attempt = null,
            DateTime? notBeforeUtc = null,
            string? lastError = null)
        {
            return new RecoveryTask
            {
                TaskId = source.TaskId,
                ProcessingToken = source.ProcessingToken,
                Type = source.Type,
                Attempt = attempt ?? source.Attempt,
                MaxAttempts = source.MaxAttempts,
                NotBeforeUtc = notBeforeUtc ?? source.NotBeforeUtc,
                Uid = source.Uid,
                UsId = source.UsId,
                OrderRequestId = source.OrderRequestId,
                PositionId = source.PositionId,
                ExchangeApiKeyId = source.ExchangeApiKeyId,
                Exchange = source.Exchange,
                Symbol = source.Symbol,
                Side = source.Side,
                Qty = source.Qty,
                ClosePrice = source.ClosePrice,
                ClosedAtUtc = source.ClosedAtUtc,
                LastError = lastError ?? source.LastError
            };
        }

        private static TimeSpan BuildRecoveryDelay(int attempt)
        {
            var seconds = Math.Min(60, Math.Max(2, attempt * attempt));
            return TimeSpan.FromSeconds(seconds);
        }

        private async Task TryPublishRecoveryFailedAsync(RecoveryTask task, string reason, CancellationToken ct)
        {
            if (!task.Uid.HasValue || task.Uid.Value <= 0)
            {
                return;
            }

            try
            {
                var payload = JsonSerializer.Serialize(new
                {
                    type = task.Type.ToString(),
                    uid = task.Uid,
                    usId = task.UsId,
                    positionId = task.PositionId,
                    exchange = task.Exchange,
                    symbol = task.Symbol,
                    side = task.Side,
                    qty = task.Qty,
                    attempt = task.Attempt,
                    reason,
                    error = task.LastError,
                    occurredAt = DateTime.UtcNow
                });

                await _notificationPublisher.PublishToUserAsync(new NotificationPublishRequest
                {
                    UserId = task.Uid.Value,
                    Category = NotificationCategory.Risk,
                    Severity = NotificationSeverity.Critical,
                    Template = "trade.recovery.failed",
                    PayloadJson = payload,
                    DedupeKey = $"trade_recovery_failed:{task.Type}:{task.UsId}:{task.PositionId}:{task.Exchange}:{task.Symbol}"
                }, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "发布交易恢复失败告警异常: type={Type} uid={Uid} usId={UsId}",
                    task.Type, task.Uid, task.UsId);
            }
        }

        private void CleanupClosedPositionLocalState(long positionId)
        {
            _riskConfigStore.Remove(positionId);
            _riskIndexManager.RemovePosition(positionId);
        }

        private async Task<T> ExecuteWithRetryAsync<T>(
            string operationName,
            Func<Task<T>> operation,
            CancellationToken ct,
            int maxAttempts = 3)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            if (maxAttempts <= 0)
            {
                maxAttempts = 1;
            }

            Exception? lastException = null;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    return await operation().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    if (attempt >= maxAttempts)
                    {
                        break;
                    }

                    var delayMs = 150 * attempt;
                    _logger.LogWarning(
                        ex,
                        "{Operation}失败，准备重试: 第{Attempt}/{MaxAttempts}次，延迟{DelayMs}ms",
                        operationName,
                        attempt,
                        maxAttempts,
                        delayMs);
                    await Task.Delay(delayMs, ct).ConfigureAwait(false);
                }
            }

            throw new InvalidOperationException($"{operationName}连续重试失败", lastException);
        }

        /// <summary>
        /// 在后台服务中按次创建作用域，安全获取 Scoped 仓储。
        /// </summary>
        private async Task ExecuteWithRecoveryRepositoryAsync(
            Func<TradeRecoveryTaskRepository, CancellationToken, Task> action,
            CancellationToken ct)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            using var scope = _serviceScopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<TradeRecoveryTaskRepository>();
            await action(repository, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 在后台服务中按次创建作用域，安全获取 Scoped 仓储并返回结果。
        /// </summary>
        private async Task<T> ExecuteWithRecoveryRepositoryAsync<T>(
            Func<TradeRecoveryTaskRepository, CancellationToken, Task<T>> action,
            CancellationToken ct)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            using var scope = _serviceScopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<TradeRecoveryTaskRepository>();
            return await action(repository, ct).ConfigureAwait(false);
        }

        private async Task ExecuteWithOrderLifecycleRepositoryAsync(
            Func<OrderLifecycleRepository, CancellationToken, Task> action,
            CancellationToken ct)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            using var scope = _serviceScopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<OrderLifecycleRepository>();
            await action(repository, ct).ConfigureAwait(false);
        }

        private async Task<T> ExecuteWithOrderLifecycleRepositoryAsync<T>(
            Func<OrderLifecycleRepository, CancellationToken, Task<T>> action,
            CancellationToken ct)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            using var scope = _serviceScopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<OrderLifecycleRepository>();
            return await action(repository, ct).ConfigureAwait(false);
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

        private static bool IsTestingTask(StrategyActionTask task)
        {
            if (task == null)
            {
                return false;
            }

            return string.Equals(task.StrategyState, "testing", StringComparison.OrdinalIgnoreCase);
        }
    }
}
