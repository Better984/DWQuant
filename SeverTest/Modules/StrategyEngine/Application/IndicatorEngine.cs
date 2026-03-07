using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Models;
using ServerTest.Models.Indicator;
using ServerTest.Options;
using System.Collections.Concurrent;
using System.Threading.Channels;
using ServerTest.Services;

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

        public IndicatorEngine(
            IMarketDataProvider marketDataProvider,
            ILogger<IndicatorEngine> logger,
            IOptions<RuntimeQueueOptions> queueOptions,
            TalibWasmNodeInvoker? wasmInvoker = null)
        {
            _marketDataProvider = marketDataProvider ?? throw new ArgumentNullException(nameof(marketDataProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            var options = queueOptions?.Value ?? new RuntimeQueueOptions();
            var indicatorQueueOptions = NormalizeQueueOptions(options.Indicator, _logger);
            // 初始化队列，启用有界通道与背压策略
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
            _calculator = new TalibIndicatorCalculator(_catalog, wasmInvoker);
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

            if (handle.TryGetValue(offset, out value))
            {
                return true;
            }

            if (!handle.Update(normalizedTask, _marketDataProvider, _calculator, _logger))
            {
                return false;
            }

            return handle.TryGetValue(offset, out value);
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
                // 回测链路逐 K 输出量过大，默认关闭“指标任务完成”信息日志，避免刷屏影响排查。
                // _logger.LogInformation(
                //     "指标任务完成: {Exchange} {Symbol} {Timeframe} time={Time} 成功={Success}/{Total}",
                //     normalizedTask.Exchange,
                //     normalizedTask.Symbol,
                //     normalizedTask.Timeframe,
                //     FormatTimestamp(normalizedTask.CandleTimestamp),
                //     successCount,
                //     task.Requests.Count);
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
            return _handles.GetOrAdd(
                request.Key,
                _ => new IndicatorHandle(request.Key, request.Parameters, request.MaxOffset));
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
                return new MarketDataTask(exchange, symbol, timeframe, task.CandleTimestamp, task.IsBarClose);
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
