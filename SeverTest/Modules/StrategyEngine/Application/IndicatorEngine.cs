using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Models;
using ServerTest.Models.Indicator;
using ServerTest.Modules.MarketStreaming.Application;
using ServerTest.Options;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace ServerTest.Modules.StrategyEngine.Application
{
    public sealed class IndicatorEngine
    {
        // 临时排障日志：测试完成后请删除。
        private const bool EnableTempIndicatorPerfLog = true;
        private readonly IMarketDataProvider _marketDataProvider;
        private readonly ILogger<IndicatorEngine> _logger;
        private readonly TalibIndicatorCalculator _calculator;
        private readonly int _computeParallelism;

        private readonly ConcurrentDictionary<IndicatorKey, IndicatorHandle> _handles = new();

        public IndicatorEngine(
            IMarketDataProvider marketDataProvider,
            ILogger<IndicatorEngine> logger,
            IOptions<RuntimeQueueOptions> queueOptions,
            TalibWasmNodePool? wasmPool = null)
        {
            _ = queueOptions;
            _marketDataProvider = marketDataProvider ?? throw new ArgumentNullException(nameof(marketDataProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _calculator = new TalibIndicatorCalculator(TryLoadCatalog(), wasmPool);
            var cpuParallelism = Math.Max(2, Environment.ProcessorCount);
            if (wasmPool is { IsEnabled: true })
            {
                // WASM 调用最终受 Node 池并发能力约束，避免线程数远大于池大小导致大量锁等待。
                _computeParallelism = Math.Max(1, Math.Min(cpuParallelism, wasmPool.PoolSize));
                _logger.LogInformation(
                    "指标并行度已按 WASM 池收敛: parallelism={Parallelism}, cpuParallelism={CpuParallelism}, wasmPoolSize={WasmPoolSize}",
                    _computeParallelism,
                    cpuParallelism,
                    wasmPool.PoolSize);
            }
            else
            {
                _computeParallelism = cpuParallelism;
            }
        }

        /// <summary>
        /// 极简模式：收到任务后直接异步并行计算，不走队列补写/预计算分支。
        /// </summary>
        public void EnqueueTask(IndicatorTask task)
        {
            if (task == null || task.Requests == null || task.Requests.Count == 0)
            {
                return;
            }

            RegisterRequests(task.Requests);
            _ = ProcessTaskInternalAsync(task, log: false).ContinueWith(t =>
            {
                _logger.LogWarning(
                    t.Exception,
                    "指标异步任务执行异常: {Exchange} {Symbol} {Timeframe} time={Time}",
                    task.MarketTask.Exchange,
                    task.MarketTask.Symbol,
                    task.MarketTask.Timeframe,
                    FormatTimestamp(task.MarketTask.CandleTimestamp));
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        /// <summary>
        /// 策略加载时预注册指标请求，提前创建句柄，减少首次执行抖动。
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
        /// 极简模式下不做“预计算水位”判定，统一按任务触发计算。
        /// </summary>
        public bool IsTaskPrecomputed(MarketDataTask task)
        {
            return false;
        }

        /// <summary>
        /// 极简模式下不维护水位；保留该方法仅为兼容调用方。
        /// </summary>
        public void MarkTaskPrecomputed(MarketDataTask task)
        {
            // 无操作
        }

        /// <summary>
        /// 保留兼容入口：当前版本已关闭指标独立队列消费，主计算由策略执行前直接触发。
        /// </summary>
        public async Task RunAsync(int workerCount, CancellationToken cancellationToken)
        {
            if (workerCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(workerCount));
            }

            _logger.LogInformation("指标独立消费 worker 已关闭，使用策略执行前并行刷新，workerCount={WorkerCount}", workerCount);
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 服务停止时正常退出。
            }
        }

        /// <summary>
        /// 极简模式下不做自动预计算，统一使用“任务到来即算”的主路径。
        /// </summary>
        public async Task SubscribeAndAutoUpdateAsync(
            MarketDataEngine marketDataEngine,
            CancellationToken cancellationToken)
        {
            _ = marketDataEngine;
            _ = cancellationToken;
            _logger.LogInformation("指标自动预计算已关闭，使用主执行链路并行计算");
            await Task.CompletedTask.ConfigureAwait(false);
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

        public (int SuccessCount, int TotalCount) ProcessTaskNow(IndicatorTask task)
        {
            return ProcessTaskInternalAsync(task, log: false).GetAwaiter().GetResult();
        }

        private async Task<(int SuccessCount, int TotalCount)> ProcessTaskInternalAsync(
            IndicatorTask task,
            bool log,
            CancellationToken cancellationToken = default)
        {
            if (task == null || task.Requests == null || task.Requests.Count == 0)
            {
                return (0, 0);
            }

            var mergedRequests = MergeRequests(task.Requests);
            if (mergedRequests.Count == 0)
            {
                return (0, 0);
            }

            var acceptedAt = DateTime.Now;
            var taskStartTicks = Stopwatch.GetTimestamp();
            var normalizedTask = NormalizeTask(task.MarketTask);
            var successCount = 0;
            var detailLogs = EnableTempIndicatorPerfLog
                ? new ConcurrentBag<IndicatorComputeDetail>()
                : null;
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = _computeParallelism,
                CancellationToken = cancellationToken
            };

            await Parallel.ForEachAsync(mergedRequests, parallelOptions, (request, _) =>
            {
                var indicatorStartTicks = Stopwatch.GetTimestamp();
                var threadId = Environment.CurrentManagedThreadId;
                var handle = GetOrCreateHandle(request);
                handle.UpdateMaxOffset(request.MaxOffset);
                var updated = handle.Update(normalizedTask, _marketDataProvider, _calculator, _logger, out var computeCore);
                if (updated)
                {
                    Interlocked.Increment(ref successCount);
                }

                if (detailLogs != null)
                {
                    detailLogs.Add(new IndicatorComputeDetail(
                        request.Key.ToString(),
                        computeCore,
                        ElapsedMs(indicatorStartTicks, Stopwatch.GetTimestamp()),
                        updated,
                        threadId));
                }

                return ValueTask.CompletedTask;
            }).ConfigureAwait(false);

            var totalMs = ElapsedMs(taskStartTicks, Stopwatch.GetTimestamp());
            if (EnableTempIndicatorPerfLog)
            {
                //_logger.LogInformation(
                //    "[临时指标日志][任务] {Exchange} {Symbol} {Timeframe} K线={CandleTime} 接收时间={AcceptedAt} 指标数={Count} 成功={Success} 并行度={Parallelism} 总耗时={TotalMs}ms",
                //    normalizedTask.Exchange,
                //    normalizedTask.Symbol,
                //    normalizedTask.Timeframe,
                //    FormatTimestamp(normalizedTask.CandleTimestamp),
                //    acceptedAt.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                //    mergedRequests.Count,
                //    successCount,
                //    _computeParallelism,
                //    totalMs);

                if (detailLogs != null && !detailLogs.IsEmpty)
                {
                    var details = detailLogs.ToArray();
                    Array.Sort(details, static (a, b) => b.DurationMs.CompareTo(a.DurationMs));

                    var threadIds = new SortedSet<int>();
                    var coreCounter = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    foreach (var detail in details)
                    {
                        threadIds.Add(detail.ThreadId);
                        var coreKey = string.IsNullOrWhiteSpace(detail.Core) ? "未知" : detail.Core;
                        if (coreCounter.TryGetValue(coreKey, out var existed))
                        {
                            coreCounter[coreKey] = existed + 1;
                        }
                        else
                        {
                            coreCounter[coreKey] = 1;
                        }
                    }

                    var coreSummaryParts = new List<string>(coreCounter.Count);
                    foreach (var pair in coreCounter.OrderByDescending(static p => p.Value))
                    {
                        coreSummaryParts.Add($"{pair.Key}:{pair.Value}");
                    }
                    var coreSummary = coreSummaryParts.Count == 0 ? "无" : string.Join(", ", coreSummaryParts);
                    var threadSummary = threadIds.Count == 0 ? "无" : string.Join(",", threadIds);
                    _logger.LogInformation(
                        "[临时指标日志][核心汇总] {Exchange} {Symbol} {Timeframe} 参与线程数={ThreadCount} 线程={Threads} 计算核心分布={CoreSummary}",
                        normalizedTask.Exchange,
                        normalizedTask.Symbol,
                        normalizedTask.Timeframe,
                        threadIds.Count,
                        threadSummary,
                        coreSummary);

                    //foreach (var detail in details)
                    //{
                    //    _logger.LogInformation(
                    //        "[临时指标日志][明细] Key={Key} 成功={Success} 核心={Core} 线程={ThreadId} 耗时={DurationMs}ms",
                    //        detail.Key,
                    //        detail.Success,
                    //        detail.Core,
                    //        detail.ThreadId,
                    //        detail.DurationMs);
                    //}
                }
            }

            if (log)
            {
                _logger.LogDebug(
                    "指标任务完成: {Exchange} {Symbol} {Timeframe} time={Time} 成功={Success}/{Total}",
                    normalizedTask.Exchange,
                    normalizedTask.Symbol,
                    normalizedTask.Timeframe,
                    FormatTimestamp(normalizedTask.CandleTimestamp),
                    successCount,
                    mergedRequests.Count);
            }

            return (successCount, mergedRequests.Count);
        }

        private static int ElapsedMs(long startTicks, long endTicks)
        {
            return (int)((endTicks - startTicks) * 1000 / Stopwatch.Frequency);
        }

        private static List<IndicatorRequest> MergeRequests(IReadOnlyList<IndicatorRequest> requests)
        {
            var map = new Dictionary<IndicatorKey, IndicatorRequest>();
            for (var i = 0; i < requests.Count; i++)
            {
                var request = requests[i];
                if (request == null)
                {
                    continue;
                }

                if (map.TryGetValue(request.Key, out var existing))
                {
                    map[request.Key] = existing.WithMaxOffset(Math.Max(existing.MaxOffset, request.MaxOffset));
                }
                else
                {
                    map[request.Key] = request;
                }
            }

            return map.Values.ToList();
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

        private readonly record struct IndicatorComputeDetail(
            string Key,
            string Core,
            int DurationMs,
            bool Success,
            int ThreadId);
    }
}
