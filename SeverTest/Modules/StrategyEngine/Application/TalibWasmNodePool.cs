using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Options;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace ServerTest.Modules.StrategyEngine.Application
{
    /// <summary>
    /// TALIB WASM Node 多进程池。
    /// 将计算请求 round-robin 分发到 N 个独立 Node.js 子进程，解除单进程全局锁瓶颈。
    /// </summary>
    public sealed class TalibWasmNodePool : IDisposable
    {
        private readonly TalibWasmNodeInvoker[] _invokers;
        private readonly ILogger<TalibWasmNodePool> _logger;
        private int _roundRobin;
        private bool _disposed;

        public TalibWasmNodePool(
            IOptions<TalibCoreOptions> options,
            ILoggerFactory loggerFactory)
        {
            var opts = options?.Value ?? new TalibCoreOptions();
            var poolSize = Math.Max(1, opts.PoolSize);

            _invokers = new TalibWasmNodeInvoker[poolSize];
            for (var i = 0; i < poolSize; i++)
            {
                _invokers[i] = new TalibWasmNodeInvoker(
                    options!, loggerFactory.CreateLogger<TalibWasmNodeInvoker>());
            }

            _logger = loggerFactory.CreateLogger<TalibWasmNodePool>();
            _logger.LogInformation("TalibWasmNodePool 初始化: poolSize={PoolSize}", poolSize);
            PreWarmInvokers();
        }

        public int PoolSize => _invokers.Length;

        public bool IsEnabled => _invokers.Length > 0 && _invokers[0].IsEnabled;

        public bool StrictWasmCore => _invokers.Length > 0 && _invokers[0].StrictWasmCore;

        public TalibWasmNodeInvoker Acquire()
        {
            var index = (Interlocked.Increment(ref _roundRobin) & 0x7FFF_FFFF) % _invokers.Length;
            return _invokers[index];
        }

        public bool TryCompute(
            string indicator,
            double[][] inputs,
            double[] options,
            int expectedLength,
            out List<double?[]> outputs,
            out string? error)
        {
            return Acquire().TryCompute(indicator, inputs, options, expectedLength, out outputs, out error);
        }

        /// <summary>
        /// 启动阶段预热所有 Node 子进程，减少首轮指标任务冷启动抖动。
        /// </summary>
        private void PreWarmInvokers()
        {
            if (!IsEnabled || _invokers.Length == 0)
            {
                return;
            }

            var startTicks = Stopwatch.GetTimestamp();
            var okCount = 0;
            var errors = new ConcurrentBag<string>();

            Parallel.ForEach(_invokers, invoker =>
            {
                if (invoker.WarmUp(out var error))
                {
                    Interlocked.Increment(ref okCount);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(error))
                {
                    errors.Add(error);
                }
            });

            var elapsedMs = (int)((Stopwatch.GetTimestamp() - startTicks) * 1000 / Stopwatch.Frequency);
            if (okCount == _invokers.Length)
            {
                _logger.LogInformation(
                    "TalibWasmNodePool 预热完成: success={Success}/{Total}, elapsed={ElapsedMs}ms",
                    okCount,
                    _invokers.Length,
                    elapsedMs);
                return;
            }

            var sampleErrors = string.Join(" | ", errors.Take(3));
            _logger.LogWarning(
                "TalibWasmNodePool 预热部分失败: success={Success}/{Total}, elapsed={ElapsedMs}ms, errors={Errors}",
                okCount,
                _invokers.Length,
                elapsedMs,
                sampleErrors);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var invoker in _invokers)
            {
                try { invoker.Dispose(); } catch { }
            }
        }
    }
}
