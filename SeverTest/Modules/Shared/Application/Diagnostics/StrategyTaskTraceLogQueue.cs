using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Models.Strategy;
using ServerTest.Options;
using ServerTest.Services;
using System.Threading.Channels;

namespace ServerTest.Modules.Shared.Application.Diagnostics
{
    /// <summary>
    /// 策略链路追踪日志队列（异步写库，避免阻塞主执行链路）。
    /// </summary>
    public sealed class StrategyTaskTraceLogQueue
    {
        private readonly Channel<StrategyTaskTraceLog> _channel;
        private readonly QueuePressureMonitor _queueMonitor;

        public StrategyTaskTraceLogQueue(
            ILogger<StrategyTaskTraceLogQueue> logger,
            IOptions<RuntimeQueueOptions> queueOptions)
        {
            var options = queueOptions?.Value ?? new RuntimeQueueOptions();
            _channel = ChannelFactory.Create<StrategyTaskTraceLog>(
                options.StrategyTaskTraceLog,
                "StrategyTaskTraceLogQueue",
                logger,
                singleReader: true,
                singleWriter: false);
            _queueMonitor = new QueuePressureMonitor("StrategyTaskTraceLogQueue", options.StrategyTaskTraceLog, logger);
        }

        public bool TryEnqueue(StrategyTaskTraceLog log)
        {
            if (log == null)
            {
                return false;
            }

            if (!_channel.Writer.TryWrite(log))
            {
                _queueMonitor.OnEnqueueFailed();
                return false;
            }

            _queueMonitor.OnEnqueueSuccess();
            return true;
        }

        public bool TryDequeue(out StrategyTaskTraceLog log)
        {
            if (_channel.Reader.TryRead(out var item))
            {
                _queueMonitor.OnDequeue();
                log = item;
                return true;
            }

            log = null!;
            return false;
        }

        public ValueTask<StrategyTaskTraceLog> ReadAsync(CancellationToken cancellationToken)
        {
            return ReadAsyncInternal(cancellationToken);
        }

        private async ValueTask<StrategyTaskTraceLog> ReadAsyncInternal(CancellationToken cancellationToken)
        {
            var item = await _channel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            _queueMonitor.OnDequeue();
            return item;
        }
    }
}
