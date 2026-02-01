using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Models.Strategy;
using ServerTest.Options;
using System.Threading.Channels;
using ServerTest.Services;

namespace ServerTest.Modules.StrategyEngine.Application
{
    public sealed class StrategyEngineRunLogQueue
    {
        private readonly Channel<StrategyEngineRunLog> _channel;
        private readonly QueuePressureMonitor _queueMonitor;

        public StrategyEngineRunLogQueue(
            ILogger<StrategyEngineRunLogQueue> logger,
            IOptions<RuntimeQueueOptions> queueOptions)
        {
            var options = queueOptions?.Value ?? new RuntimeQueueOptions();
            // 初始化队列，启用有界通道与背压策略
            _channel = ChannelFactory.Create<StrategyEngineRunLog>(
                options.StrategyRunLog,
                "StrategyRunLogQueue",
                logger,
                singleReader: true,
                singleWriter: false);
            _queueMonitor = new QueuePressureMonitor("StrategyRunLogQueue", options.StrategyRunLog, logger);
        }

        public bool TryEnqueue(StrategyEngineRunLog log)
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

        public IAsyncEnumerable<StrategyEngineRunLog> ReadAllAsync(CancellationToken cancellationToken)
        {
            return ReadAllAsyncInternal(cancellationToken);
        }

        private async IAsyncEnumerable<StrategyEngineRunLog> ReadAllAsyncInternal(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var log in _channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                _queueMonitor.OnDequeue();
                yield return log;
            }
        }
    }
}
