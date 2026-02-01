using Microsoft.Extensions.Logging;
using ServerTest.Models;
using ServerTest.Models.Indicator;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace ServerTest.Services
{
    public sealed class IndicatorEngine
    {
        private readonly MarketDataEngine _marketDataEngine;
        private readonly ILogger<IndicatorEngine> _logger;
        private readonly TalibIndicatorCatalog? _catalog;
        private readonly TalibIndicatorCalculator _calculator;
        private readonly Channel<IndicatorTask> _taskChannel = Channel.CreateUnbounded<IndicatorTask>(
            new UnboundedChannelOptions
            {
                SingleReader = false,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

        private readonly ConcurrentDictionary<IndicatorKey, IndicatorHandle> _handles = new();

        public IndicatorEngine(MarketDataEngine marketDataEngine, ILogger<IndicatorEngine> logger)
        {
            _marketDataEngine = marketDataEngine ?? throw new ArgumentNullException(nameof(marketDataEngine));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _catalog = TryLoadCatalog();
            _calculator = new TalibIndicatorCalculator(_catalog);
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
                return;
            }

            _logger.LogInformation(
                "指标引擎接收任务: {Exchange} {Symbol} {Timeframe} time={Time} 请求数={Count}",
                task.MarketTask.Exchange,
                task.MarketTask.Symbol,
                task.MarketTask.Timeframe,
                FormatTimestamp(task.MarketTask.CandleTimestamp),
                task.Requests.Count);
        }

        public Task RunAsync(int workerCount, CancellationToken cancellationToken)
        {
            if (workerCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(workerCount));
            }

            var workers = new Task[workerCount];
            for (var i = 0; i < workerCount; i++)
            {
                workers[i] = ConsumeAsync(cancellationToken);
            }

            return Task.WhenAll(workers);
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

            if (!handle.Update(normalizedTask, _marketDataEngine, _calculator, _logger))
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
                if (handle.Update(normalizedTask, _marketDataEngine, _calculator, _logger))
                {
                    successCount++;
                }
            }

            if (log)
            {
                _logger.LogInformation(
                    "指标任务完成: {Exchange} {Symbol} {Timeframe} time={Time} 成功={Success}/{Total}",
                    normalizedTask.Exchange,
                    normalizedTask.Symbol,
                    normalizedTask.Timeframe,
                    FormatTimestamp(normalizedTask.CandleTimestamp),
                    successCount,
                    task.Requests.Count);
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
