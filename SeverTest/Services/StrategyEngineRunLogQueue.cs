using ServerTest.Models.Strategy;
using System.Threading.Channels;

namespace ServerTest.Services
{
    public sealed class StrategyEngineRunLogQueue
    {
        private readonly Channel<StrategyEngineRunLog> _channel = Channel.CreateUnbounded<StrategyEngineRunLog>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

        public bool TryEnqueue(StrategyEngineRunLog log)
        {
            if (log == null)
            {
                return false;
            }

            return _channel.Writer.TryWrite(log);
        }

        public IAsyncEnumerable<StrategyEngineRunLog> ReadAllAsync(CancellationToken cancellationToken)
        {
            return _channel.Reader.ReadAllAsync(cancellationToken);
        }
    }
}
