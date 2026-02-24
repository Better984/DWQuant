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
        private readonly IOrderExecutor _orderExecutor;
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
            IOrderExecutor orderExecutor,
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
            _orderExecutor = orderExecutor ?? throw new ArgumentNullException(nameof(orderExecutor));
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

            var isTestingTask = IsTestingTask(task);

            if (isClose)
            {
                await HandleCloseAsync(task, exchange, symbol, positionSide, orderSide, isTestingTask, ct).ConfigureAwait(false);
                return;
            }

            await HandleOpenAsync(task, exchange, symbol, positionSide, orderSide, isTestingTask, ct).ConfigureAwait(false);
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
        /// <param name="positionSide">仓位方向（Long/Short）。</param>
        /// <param name="orderSide">订单方向（buy/sell）。</param>
        /// <param name="ct">取消令牌</param>
        private async Task HandleOpenAsync(
            StrategyActionTask task,
            string exchange,
            string symbol,
            string positionSide,
            string orderSide,
            bool isTestingTask,
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

            var effectiveMaxPositionQty = ResolveEffectiveMaxPositionQty(task);
            var signalTimeUtc = ResolveSignalTimeUtc(task);

            // 3. 从市场数据引擎获取当前价格作为预估入场价格
            var entryPrice = ResolveEntryPrice(exchange, symbol);
            if (entryPrice <= 0)
            {
                _logger.LogWarning("入场价格缺失，跳过开仓: {Exchange} {Symbol}", exchange, symbol);
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
            var requestedOrderQty = task.OrderQty;
            if (currentOpenQty + requestedOrderQty > effectiveMaxPositionQty)
            {
                var blockedMessage = $"开仓被最大持仓限制阻断：当前同向持仓{currentOpenQty} + 本次开仓{requestedOrderQty} > 上限{effectiveMaxPositionQty}";
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
                    await _orderOpenAttemptRepository.InsertAsync(
                            task.Uid.Value,
                            task.UsId.Value,
                            exchange,
                            symbol,
                            positionSide,
                            success: false,
                            errorMessage: blockedMessage,
                            attemptType: OrderOpenAttemptTypes.BlockedByMaxPosition,
                            signalTimeUtc: signalTimeUtc,
                            signalPrice: entryPrice,
                            maxPositionQty: effectiveMaxPositionQty,
                            currentOpenQty: currentOpenQty,
                            requestOrderQty: requestedOrderQty,
                            ct: ct)
                        .ConfigureAwait(false);
                }
                return;
            }

            OrderExecutionResult? openOrderResult = null;
            if (!isTestingTask)
            {
                // 5. 非 testing 模式提交市价单到交易所（开仓订单，非平仓）
                openOrderResult = await _orderExecutor.PlaceMarketOrderAsync(new OrderExecutionRequest
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
                if (!openOrderResult.Success)
                {
                    _logger.LogWarning("开仓订单失败: uid={Uid} usId={UsId} 错误={Error}", task.Uid, task.UsId, openOrderResult.ErrorMessage);
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
            }

            // 8. 根据入场价格和风险参数计算止损价和止盈价
            var stopLossPrice = BuildStopLossPrice(entryPrice, task.StopLossPct, task.Leverage, positionSide);
            var takeProfitPrice = BuildTakeProfitPrice(entryPrice, task.TakeProfitPct, task.Leverage, positionSide);

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
                Qty = task.OrderQty,
                Status = "Open",
                StopLossPrice = stopLossPrice,
                TakeProfitPrice = takeProfitPrice,
                TrailingEnabled = task.TrailingEnabled,
                TrailingStopPrice = null,  // 初始时追踪止损价格为空
                TrailingTriggered = false,  // 追踪止损尚未触发
                TrailingActivationPct = task.TrailingEnabled ? task.TrailingActivationPct : null,
                TrailingDrawdownPct = task.TrailingEnabled ? task.TrailingDrawdownPct : null,
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
                    await TryCompensateOpenOrderAsync(task, exchange, symbol, orderSide, ct).ConfigureAwait(false);
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
            _logger.LogInformation("仓位已开: id={PositionId} uid={Uid} usId={UsId} versionId={VersionId} {Exchange} {Symbol} {Side} mode={Mode}",
                positionId, task.Uid, task.UsId, task.StrategyVersionId, exchange, symbol, positionSide, isTestingTask ? "testing" : "live");

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
            bool isTestingTask,
            CancellationToken ct)
        {
            if (!task.Uid.HasValue || !task.UsId.HasValue)
            {
                _logger.LogWarning("平仓动作缺少uid/usId");
                return;
            }

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
                    }
                    else
                    {
                        _logger.LogInformation("平仓动作完成: uid={Uid} usId={UsId} {Exchange} {Symbol} {Side} 共处理{ClosedCount}个仓位",
                            task.Uid, task.UsId, exchange, symbol, positionSide, closedCount);
                    }
                    return;
                }

                var closed = await TryCloseSinglePositionAsync(existing, exchange, symbol, orderSide, isTestingTask, ct)
                    .ConfigureAwait(false);
                if (!closed)
                {
                    _logger.LogWarning("平仓动作中断: uid={Uid} usId={UsId} {Exchange} {Symbol} {Side} 已处理{ClosedCount}个仓位",
                        task.Uid, task.UsId, exchange, symbol, positionSide, closedCount);
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
                    return false;
                }

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
                await EnqueueCloseWriteRecoveryAsync(existing, closePrice, closedAt, ex.Message, ct).ConfigureAwait(false);
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
                    await EnqueueCloseWriteRecoveryAsync(existing, closePrice, closedAt, "平仓写库返回0且仓位仍为Open", ct)
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

        private static decimal ResolveEffectiveMaxPositionQty(StrategyActionTask task)
        {
            if (task.MaxPositionQty > 0)
            {
                return task.MaxPositionQty;
            }

            // 兼容历史配置：未配置上限时按“单次开仓量”退化，保持至少单仓可开。
            return task.OrderQty > 0 ? task.OrderQty : 0m;
        }

        private static DateTime ResolveSignalTimeUtc(StrategyActionTask task)
        {
            if (task.MarketTask.CandleTimestamp > 0)
            {
                try
                {
                    return DateTimeOffset.FromUnixTimeMilliseconds(task.MarketTask.CandleTimestamp).UtcDateTime;
                }
                catch (ArgumentOutOfRangeException)
                {
                    // 回退到任务创建时间，避免异常时间戳导致执行链路中断。
                }
            }

            return task.CreatedAt == default
                ? DateTime.UtcNow
                : task.CreatedAt.UtcDateTime;
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

        private async Task TryCompensateOpenOrderAsync(
            StrategyActionTask task,
            string exchange,
            string symbol,
            string openOrderSide,
            CancellationToken ct)
        {
            if (!task.Uid.HasValue || task.OrderQty <= 0)
            {
                return;
            }

            var compensateSide = string.Equals(openOrderSide, "buy", StringComparison.OrdinalIgnoreCase)
                ? "sell"
                : "buy";

            var (success, error) = await TryExecuteCompensationOrderAsync(
                    task.Uid.Value,
                    task.ExchangeApiKeyId,
                    exchange,
                    symbol,
                    compensateSide,
                    task.OrderQty,
                    ct)
                .ConfigureAwait(false);

            if (success)
            {
                _logger.LogWarning(
                    "开仓写库失败后补偿平仓成功: uid={Uid} usId={UsId} {Exchange} {Symbol}",
                    task.Uid,
                    task.UsId,
                    exchange,
                    symbol);
                return;
            }

            _logger.LogError(
                "开仓写库失败后补偿平仓失败，已转入恢复队列: uid={Uid} usId={UsId} {Exchange} {Symbol} 错误={Error}",
                task.Uid,
                task.UsId,
                exchange,
                symbol,
                error);

            await EnqueueOpenCompensationRecoveryAsync(task, exchange, symbol, compensateSide, error, ct).ConfigureAwait(false);
        }

        private async Task<(bool Success, string Error)> TryExecuteCompensationOrderAsync(
            long uid,
            long? exchangeApiKeyId,
            string exchange,
            string symbol,
            string side,
            decimal qty,
            CancellationToken ct)
        {
            try
            {
                var compensateResult = await _orderExecutor.PlaceMarketOrderAsync(new OrderExecutionRequest
                {
                    Uid = uid,
                    ExchangeApiKeyId = exchangeApiKeyId,
                    Exchange = exchange,
                    Symbol = symbol,
                    Side = side,
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
            StrategyActionTask task,
            string exchange,
            string symbol,
            string compensateSide,
            string error,
            CancellationToken ct)
        {
            var recoveryTask = new RecoveryTask
            {
                Type = RecoveryTaskType.OpenCompensation,
                Attempt = 1,
                MaxAttempts = RecoveryMaxAttempts,
                NotBeforeUtc = DateTime.UtcNow.AddSeconds(2),
                Uid = task.Uid,
                UsId = task.UsId,
                ExchangeApiKeyId = task.ExchangeApiKeyId,
                Exchange = exchange,
                Symbol = symbol,
                Side = compensateSide,
                Qty = task.OrderQty,
                LastError = error ?? string.Empty
            };

            await TryEnqueueRecoveryTaskAsync(recoveryTask, "开仓补偿恢复", ct).ConfigureAwait(false);
        }

        private async Task EnqueueCloseWriteRecoveryAsync(
            StrategyPosition existing,
            decimal? closePrice,
            DateTime closedAtUtc,
            string error,
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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "交易恢复任务入库失败: scene={Scene} type={Type} uid={Uid} usId={UsId}",
                    scene, task.Type, task.Uid, task.UsId);
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
