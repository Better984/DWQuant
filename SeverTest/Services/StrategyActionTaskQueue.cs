using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using ServerTest.Models.Strategy;

namespace ServerTest.Services
{
    public sealed class StrategyActionTaskQueue
    {
        private readonly Channel<StrategyActionTask> _channel = Channel.CreateUnbounded<StrategyActionTask>(
            new UnboundedChannelOptions
            {
                SingleReader = false,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

        public bool TryEnqueue(StrategyActionTask task)
        {
            return _channel.Writer.TryWrite(task);
        }

        public bool TryDequeue(out StrategyActionTask task)
        {
            return _channel.Reader.TryRead(out task);
        }

        public ValueTask<StrategyActionTask> ReadAsync(CancellationToken cancellationToken)
        {
            return _channel.Reader.ReadAsync(cancellationToken);
        }

        public IAsyncEnumerable<StrategyActionTask> ReadAllAsync(CancellationToken cancellationToken)
        {
            return _channel.Reader.ReadAllAsync(cancellationToken);
        }
    }
}
