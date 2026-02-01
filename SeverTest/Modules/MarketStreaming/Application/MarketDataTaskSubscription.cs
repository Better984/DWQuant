using ServerTest.Models;
using ServerTest.Services;
using System.Threading.Channels;

namespace ServerTest.Modules.MarketStreaming.Application
{
    /// <summary>
    /// 行情任务订阅：为每个消费者提供独立通道，避免竞争消费导致丢任务。
    /// </summary>
    public sealed class MarketDataTaskSubscription
    {
        private readonly Channel<MarketDataTask> _channel;
        private readonly QueuePressureMonitor _queueMonitor;

        internal MarketDataTaskSubscription(
            string name,
            Channel<MarketDataTask> channel,
            QueuePressureMonitor queueMonitor,
            bool onlyBarClose)
        {
            Name = name;
            _channel = channel;
            _queueMonitor = queueMonitor;
            OnlyBarClose = onlyBarClose;
        }

        public string Name { get; }
        public bool OnlyBarClose { get; }

        internal bool TryWrite(MarketDataTask task)
        {
            if (!_channel.Writer.TryWrite(task))
            {
                _queueMonitor.OnEnqueueFailed();
                return false;
            }

            _queueMonitor.OnEnqueueSuccess();
            return true;
        }

        internal void Complete()
        {
            _channel.Writer.TryComplete();
        }

        public bool TryRead(out MarketDataTask task)
        {
            if (_channel.Reader.TryRead(out task))
            {
                _queueMonitor.OnDequeue();
                return true;
            }

            return false;
        }

        public ValueTask<MarketDataTask> ReadAsync(CancellationToken cancellationToken)
        {
            return ReadAsyncInternal(cancellationToken);
        }

        public IAsyncEnumerable<MarketDataTask> ReadAllAsync(CancellationToken cancellationToken)
        {
            return ReadAllAsyncInternal(cancellationToken);
        }

        private async ValueTask<MarketDataTask> ReadAsyncInternal(CancellationToken cancellationToken)
        {
            var task = await _channel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            _queueMonitor.OnDequeue();
            return task;
        }

        private async IAsyncEnumerable<MarketDataTask> ReadAllAsyncInternal(
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
