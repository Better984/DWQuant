using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Models;
using ServerTest.Models.Indicator;
using ServerTest.Options;
using System.Collections.Concurrent;
using System.Threading.Channels;
using ServerTest.Services;
using ServerTest.Modules.MarketStreaming.Application;

namespace ServerTest.Modules.StrategyEngine.Application
{
    public sealed class IndicatorEngine
    {
        private readonly IMarketDataProvider _marketDataProvider;
        private readonly ILogger<IndicatorEngine> _logger;
        private readonly TalibIndicatorCatalog? _catalog;
        private readonly TalibIndicatorCalculator _calculator;
        private readonly Channel<IndicatorTask> _taskChannel;
        private readonly Channel<IndicatorTask> _criticalWriteChannel;
        private readonly QueuePressureMonitor _queueMonitor;
        private int _pendingCriticalWrites;
        private Task? _criticalWriteWorker;
        private readonly object _criticalWriteWorkerLock = new();
        private const int MaxPendingCriticalWrites = 128;

        private readonly ConcurrentDictionary<IndicatorKey, IndicatorHandle> _handles = new();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<IndicatorKey, byte>> _handlesByMarketKey =
            new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, long> _taskPrecomputeWatermark =
            new(StringComparer.Ordinal);

        public IndicatorEngine(
            IMarketDataProvider marketDataProvider,
            ILogger<IndicatorEngine> logger,
            IOptions<RuntimeQueueOptions> queueOptions,
            TalibWasmNodePool? wasmPool = null)
        {
            _marketDataProvider = marketDataProvider ?? throw new ArgumentNullException(nameof(marketDataProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            var options = queueOptions?.Value ?? new RuntimeQueueOptions();
            var indicatorQueueOptions = NormalizeQueueOptions(options.Indicator, _logger);
            _taskChannel = ChannelFactory.Create<IndicatorTask>(
                indicatorQueueOptions,
                "IndicatorEngine",
                logger,
                singleReader: false,
                singleWriter: false);
            _criticalWriteChannel = Channel.CreateBounded<IndicatorTask>(new BoundedChannelOptions(MaxPendingCriticalWrites)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropWrite
            });
            _queueMonitor = new QueuePressureMonitor("IndicatorEngine", indicatorQueueOptions, logger);
            _catalog = TryLoadCatalog();
            _calculator = new TalibIndicatorCalculator(_catalog, wasmPool);
        }

        public void EnqueueTask(IndicatorTask task)
        {
            if (task == null || task.Requests == null || task.Requests.Count == 0)
            {
                return;
            }

            RegisterRequests(task.Requests);
            if (!_taskChannel.Writer.TryWrite(task))
            {
                _logger.LogWarning(
                    "指标任务入队失败: {Exchange} {Symbol} {Timeframe} time={Time}",
                    task.MarketTask.Exchange,
                    task.MarketTask.Symbol,
                    task.MarketTask.Timeframe,
                    FormatTimestamp(task.MarketTask.CandleTimestamp));
                _queueMonitor.OnEnqueueFailed();
                if (task.MarketTask.IsBarClose)
                {
                    TryScheduleCriticalWrite(task);
                }
                return;
            }

            _queueMonitor.OnEnqueueSuccess();

            _logger.LogInformation(
                "指标引擎接收任务: {Exchange} {Symbol} {Timeframe} time={Time} 请求数={Count}",
                task.MarketTask.Exchange,
                task.MarketTask.Symbol,
                task.MarketTask.Timeframe,
                FormatTimestamp(task.MarketTask.CandleTimestamp),
                task.Requests.Count);
        }

        /// <summary>
        /// 策略加载时预注册指标请求，确保自动预计算可以命中对应句柄。
        /// </summary>
        public void RegisterRequestsForStrategy(IReadOnlyList<IndicatorRequest> requests)
        {
            if (requests == null || requests.Count == 0)
            {
                return;
            }

            RegisterRequests(requests);
        }

        /// <summary>
        /// 判断当前行情任务是否已被自动预计算链路覆盖。
        /// </summary>
        public bool IsTaskPrecomputed(MarketDataTask task)
        {
            if (task.CandleTimestamp <= 0)
            {
                return false;
            }

            var normalizedTask = NormalizeTask(task);
            var marketKey = BuildMarketTaskKey(
                normalizedTask.Exchange,
                normalizedTask.Symbol,
                normalizedTask.Timeframe);

            if (!_taskPrecomputeWatermark.TryGetValue(marketKey, out var watermark))
            {
                return false;
            }

            return watermark >= normalizedTask.CandleTimestamp;
        }

        /// <summary>
        /// 标记任务已完成指标刷新（供同步刷新链路补写水位，避免同任务被预计算再次重算）。
        /// </summary>
        public void MarkTaskPrecomputed(MarketDataTask task)
        {
            var normalizedTask = NormalizeTask(task);
            AdvanceTaskWatermark(normalizedTask);
        }

        /// <summary>
        /// 收线场景下的关键补写：队列满时异步等待空位，降低关键指标刷新任务丢失概率。
        /// </summary>
        private void TryScheduleCriticalWrite(IndicatorTask task)
        {
            var pending = Interlocked.Increment(ref _pendingCriticalWrites);
            if (pending > MaxPendingCriticalWrites)
            {
                Interlocked.Decrement(ref _pendingCriticalWrites);
                _logger.LogError(
                    "指标关键补写队列已满，丢弃任务: {Exchange} {Symbol} {Timeframe} time={Time}",
                    task.MarketTask.Exchange,
                    task.MarketTask.Symbol,
                    task.MarketTask.Timeframe,
                    FormatTimestamp(task.MarketTask.CandleTimestamp));
                return;
            }

            if (!_criticalWriteChannel.Writer.TryWrite(task))
            {
                Interlocked.Decrement(ref _pendingCriticalWrites);
                _logger.LogError(
                    "指标关键补写总队列已满，丢弃任务: {Exchange} {Symbol} {Timeframe} time={Time}",
                    task.MarketTask.Exchange,
                    task.MarketTask.Symbol,
                    task.MarketTask.Timeframe,
                    FormatTimestamp(task.MarketTask.CandleTimestamp));
            }
        }

        public async Task RunAsync(int workerCount, CancellationToken cancellationToken)
        {
            if (workerCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(workerCount));
            }

            var workers = new Task[workerCount + 1];
            for (var i = 0; i < workerCount; i++)
            {
                workers[i] = ConsumeAsync(cancellationToken);
            }

            // 关键补写消费 worker 固定单线程，避免高压下每次失败都创建 Task.Run。
            workers[workerCount] = EnsureCriticalWriteWorker(cancellationToken);
            await Task.WhenAll(workers).ConfigureAwait(false);
        }

        /// <summary>
        /// 订阅行情引擎，行情到达时自动更新所有已注册指标（预计算）。
        /// 策略执行时指标已是最新的，HandleTask 中的同步刷新变为缓存命中。
        /// </summary>
        public async Task SubscribeAndAutoUpdateAsync(
            MarketDataEngine marketDataEngine,
            CancellationToken cancellationToken)
        {
            var subscription = marketDataEngine.SubscribeMarketTasks(
                "IndicatorAutoUpdate", onlyBarClose: false);

            _logger.LogInformation("指标自动更新服务已启动，订阅行情引擎");

            while (!cancellationToken.IsCancellationRequested)
            {
                MarketDataTask task;
                try
                {
                    task = await subscription.ReadAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                try
                {
                    PrecomputeHandlesForTask(task);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "指标自动更新异常: {Exchange} {Symbol} {Timeframe}",
                        task.Exchange, task.Symbol, task.Timeframe);
                }
            }

            _logger.LogInformation("指标自动更新服务已停止");
        }

        private void PrecomputeHandlesForTask(MarketDataTask task)
        {
            var normalizedTask = NormalizeTask(task);
            var updated = 0;
            var requiredHandleCount = 0;
            var succeededHandleCount = 0;
            var marketKey = BuildMarketTaskKey(
                normalizedTask.Exchange,
                normalizedTask.Symbol,
                normalizedTask.Timeframe);

            // 同一时间戳已被同步刷新链路完成时，预计算直接跳过，避免重复重算。
            if (normalizedTask.CandleTimestamp > 0
                && _taskPrecomputeWatermark.TryGetValue(marketKey, out var watermark)
                && watermark >= normalizedTask.CandleTimestamp)
            {
                return;
            }

            if (_handlesByMarketKey.TryGetValue(marketKey, out var indexedKeys))
            {
                foreach (var indicatorKey in indexedKeys.Keys)
                {
                    if (!_handles.TryGetValue(indicatorKey, out var handle))
                    {
                        continue;
                    }

                    if (!handle.IsUpdateRequired(normalizedTask))
                    {
                        continue;
                    }

                    requiredHandleCount++;
                    if (handle.Update(normalizedTask, _marketDataProvider, _calculator, _logger))
                    {
                        updated++;
                        succeededHandleCount++;
                    }
                }
            }

            // 仅当本任务所需的句柄全部更新成功时，才推进预计算水位，避免误判“已预计算”。
            if (normalizedTask.CandleTimestamp > 0 && succeededHandleCount >= requiredHandleCount)
            {
                AdvanceTaskWatermark(normalizedTask);
            }
            else if (requiredHandleCount > 0 && succeededHandleCount < requiredHandleCount)
            {
                _logger.LogWarning(
                    "指标预计算未完全成功，保持旧水位: {Exchange} {Symbol} {Timeframe} 时间={Time} 成功={Success}/{Required}",
                    normalizedTask.Exchange,
                    normalizedTask.Symbol,
                    normalizedTask.Timeframe,
                    FormatTimestamp(normalizedTask.CandleTimestamp),
                    succeededHandleCount,
                    requiredHandleCount);
            }

            if (updated > 0 && _logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "指标预计算完成: {Exchange} {Symbol} {Timeframe} 时间={Time} 更新={Count}",
                    normalizedTask.Exchange,
                    normalizedTask.Symbol,
                    normalizedTask.Timeframe,
                    FormatTimestamp(normalizedTask.CandleTimestamp),
                    updated);
            }
        }

        private void AdvanceTaskWatermark(MarketDataTask normalizedTask)
        {
            if (normalizedTask.CandleTimestamp <= 0)
            {
                return;
            }

            var marketKey = BuildMarketTaskKey(
                normalizedTask.Exchange,
                normalizedTask.Symbol,
                normalizedTask.Timeframe);

            _taskPrecomputeWatermark.AddOrUpdate(
                marketKey,
                normalizedTask.CandleTimestamp,
                (_, current) => Math.Max(current, normalizedTask.CandleTimestamp));
        }

        public bool TryGetValue(
            IndicatorRequest request,
            MarketDataTask task,
            int offset,
            out double value)
        {
            value = double.NaN;
            if (request == null)
            {
                return false;
            }

            var normalizedTask = NormalizeTask(task);
            var effectiveRequest = request.WithMaxOffset(Math.Max(request.MaxOffset, offset));
            var handle = GetOrCreateHandle(effectiveRequest);
            handle.UpdateMaxOffset(effectiveRequest.MaxOffset);

            if (handle.TryGetValueFresh(offset, normalizedTask.CandleTimestamp, out value))
            {
                return true;
            }

            if (!handle.Update(normalizedTask, _marketDataProvider, _calculator, _logger))
            {
                return false;
            }

            return handle.TryGetValueFresh(offset, normalizedTask.CandleTimestamp, out value);
        }

        public bool TryGetCachedValue(IndicatorKey key, int offset, out double value)
        {
            value = double.NaN;
            if (!_handles.TryGetValue(key, out var handle))
            {
                return false;
            }

            return handle.TryGetValue(offset, out value);
        }

        private async Task ConsumeAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                IndicatorTask task;
                try
                {
                    task = await _taskChannel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                    _queueMonitor.OnDequeue();
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                ProcessTask(task);
            }
        }

        public (int SuccessCount, int TotalCount) ProcessTaskNow(IndicatorTask task)
        {
            return ProcessTaskInternal(task, log: true);
        }

        private void ProcessTask(IndicatorTask task)
        {
            ProcessTaskInternal(task, log: true);
        }

        private (int SuccessCount, int TotalCount) ProcessTaskInternal(IndicatorTask task, bool log)
        {
            if (task == null || task.Requests == null || task.Requests.Count == 0)
            {
                return (0, 0);
            }

            var normalizedTask = NormalizeTask(task.MarketTask);
            var successCount = 0;
            foreach (var request in task.Requests)
            {
                var handle = GetOrCreateHandle(request);
                handle.UpdateMaxOffset(request.MaxOffset);
                if (handle.Update(normalizedTask, _marketDataProvider, _calculator, _logger))
                {
                    successCount++;
                }
            }

            if (log)
            {
                //_logger.LogInformation(
                //    "指标任务完成: {Exchange} {Symbol} {Timeframe} time={Time} 成功={Success}/{Total}",
                //    normalizedTask.Exchange,
                //    normalizedTask.Symbol,
                //    normalizedTask.Timeframe,
                //    FormatTimestamp(normalizedTask.CandleTimestamp),
                //    successCount,
                //    task.Requests.Count);
            }

            return (successCount, task.Requests.Count);
        }

        private void RegisterRequests(IReadOnlyList<IndicatorRequest> requests)
        {
            foreach (var request in requests)
            {
                var handle = GetOrCreateHandle(request);
                handle.UpdateMaxOffset(request.MaxOffset);
            }
        }

        private IndicatorHandle GetOrCreateHandle(IndicatorRequest request)
        {
            var handle = _handles.GetOrAdd(
                request.Key,
                _ => new IndicatorHandle(request.Key, request.Parameters, request.MaxOffset));
            IndexHandleByMarketKey(handle.Key);
            return handle;
        }

        private void IndexHandleByMarketKey(IndicatorKey key)
        {
            var marketKey = BuildMarketTaskKey(key.Exchange, key.Symbol, key.Timeframe);
            var bucket = _handlesByMarketKey.GetOrAdd(
                marketKey,
                _ => new ConcurrentDictionary<IndicatorKey, byte>());
            bucket.TryAdd(key, 0);
        }

        private static string BuildMarketTaskKey(string exchange, string symbol, string timeframe)
        {
            return $"{exchange}|{symbol}|{timeframe}";
        }

        private static MarketDataTask NormalizeTask(MarketDataTask task)
        {
            var exchange = MarketDataKeyNormalizer.NormalizeExchange(task.Exchange);
            var symbol = MarketDataKeyNormalizer.NormalizeSymbol(task.Symbol);
            var timeframe = MarketDataKeyNormalizer.NormalizeTimeframe(task.Timeframe);
            if (string.IsNullOrWhiteSpace(timeframe))
            {
                return task;
            }

            try
            {
                return new MarketDataTask(
                    exchange,
                    symbol,
                    timeframe,
                    task.CandleTimestamp,
                    task.TimeframeSec,
                    task.IsBarClose,
                    MarketDataTask.NormalizeTraceId(task.TraceId));
            }
            catch
            {
                return task;
            }
        }

        private static string FormatTimestamp(long timestamp)
        {
            if (timestamp <= 0)
            {
                return "N/A";
            }

            return DateTimeOffset.FromUnixTimeMilliseconds(timestamp)
                .ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm:ss");
        }

        private static QueueOptions NormalizeQueueOptions(QueueOptions? source, ILogger logger)
        {
            source ??= new QueueOptions();
            var fullMode = source.FullMode?.Trim();
            if (string.Equals(fullMode, "dropoldest", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fullMode, "dropnewest", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "指标队列 FullMode={FullMode} 可能导致关键任务静默覆盖，已自动调整为 DropWrite",
                    source.FullMode);
                fullMode = "DropWrite";
            }

            return new QueueOptions
            {
                Capacity = Math.Max(1, source.Capacity),
                FullMode = string.IsNullOrWhiteSpace(fullMode) ? "DropWrite" : fullMode,
                WarningThresholdPercent = source.WarningThresholdPercent,
                WarningIntervalSeconds = source.WarningIntervalSeconds
            };
        }

        private Task EnsureCriticalWriteWorker(CancellationToken cancellationToken)
        {
            lock (_criticalWriteWorkerLock)
            {
                if (_criticalWriteWorker == null || _criticalWriteWorker.IsCompleted)
                {
                    _criticalWriteWorker = Task.Run(() => ConsumeCriticalWritesAsync(cancellationToken), cancellationToken);
                }

                return _criticalWriteWorker;
            }
        }

        private async Task ConsumeCriticalWritesAsync(CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var task in _criticalWriteChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    try
                    {
                        await _taskChannel.Writer.WriteAsync(task, cancellationToken).ConfigureAwait(false);
                        _queueMonitor.OnEnqueueSuccess();
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (InvalidOperationException)
                    {
                        _queueMonitor.OnEnqueueFailed();
                    }
                    catch (Exception ex)
                    {
                        _queueMonitor.OnEnqueueFailed();
                        _logger.LogWarning(ex,
                            "指标关键补写异常: {Exchange} {Symbol} {Timeframe} time={Time}",
                            task.MarketTask.Exchange,
                            task.MarketTask.Symbol,
                            task.MarketTask.Timeframe,
                            FormatTimestamp(task.MarketTask.CandleTimestamp));
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _pendingCriticalWrites);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 停机阶段取消消费，忽略即可。
            }
        }

        private TalibIndicatorCatalog? TryLoadCatalog()
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "Config", "talib_indicators_config.json");
            if (!File.Exists(configPath))
            {
                _logger.LogWarning("指标配置未找到: {Path}", configPath);
                return null;
            }

            try
            {
                return TalibIndicatorCatalog.LoadFromFile(configPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "加载指标配置失败");
                return null;
            }
        }
    }
}
