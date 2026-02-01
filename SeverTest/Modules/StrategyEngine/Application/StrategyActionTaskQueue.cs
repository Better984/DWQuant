using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Models.Strategy;
using ServerTest.Options;
using System.Threading.Channels;
using ServerTest.Services;

namespace ServerTest.Modules.StrategyEngine.Application
{
    public sealed class StrategyActionTaskQueue
    {
        private readonly Channel<StrategyActionTask> _channel;
        private readonly QueuePressureMonitor _queueMonitor;

        public StrategyActionTaskQueue(
            ILogger<StrategyActionTaskQueue> logger,
            IOptions<RuntimeQueueOptions> queueOptions)
        {
            var options = queueOptions?.Value ?? new RuntimeQueueOptions();
            // 初始化队列，启用有界通道与背压策略
            _channel = ChannelFactory.Create<StrategyActionTask>(
                options.StrategyAction,
                "StrategyActionQueue",
                logger,
                singleReader: false,
                singleWriter: false);
            _queueMonitor = new QueuePressureMonitor("StrategyActionQueue", options.StrategyAction, logger);
        }

        public bool TryEnqueue(StrategyActionTask task)
        {
            if (!_channel.Writer.TryWrite(task))
            {
                _queueMonitor.OnEnqueueFailed();
                return false;
            }

            _queueMonitor.OnEnqueueSuccess();
            return true;
        }

        public bool TryDequeue(out StrategyActionTask task)
        {
            if (_channel.Reader.TryRead(out task))
            {
                _queueMonitor.OnDequeue();
                return true;
            }

            return false;
        }

        public ValueTask<StrategyActionTask> ReadAsync(CancellationToken cancellationToken)
        {
            return ReadAsyncInternal(cancellationToken);
        }

        public IAsyncEnumerable<StrategyActionTask> ReadAllAsync(CancellationToken cancellationToken)
        {
            return ReadAllAsyncInternal(cancellationToken);
        }

        private async ValueTask<StrategyActionTask> ReadAsyncInternal(CancellationToken cancellationToken)
        {
            var task = await _channel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            _queueMonitor.OnDequeue();
            return task;
        }

        private async IAsyncEnumerable<StrategyActionTask> ReadAllAsyncInternal(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var task in _channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                _queueMonitor.OnDequeue();
                yield return task;
            }
        }
    }
}
