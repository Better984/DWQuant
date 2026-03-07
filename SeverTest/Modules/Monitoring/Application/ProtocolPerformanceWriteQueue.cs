using Microsoft.Extensions.Logging;
using ServerTest.Modules.Monitoring.Domain;
using System.Threading.Channels;

namespace ServerTest.Modules.Monitoring.Application
{
    public sealed class ProtocolPerformanceWriteQueue
    {
        private const int Capacity = 10000;

        private readonly Channel<ProtocolPerformanceWriteItem> _channel;
        private readonly ILogger<ProtocolPerformanceWriteQueue> _logger;
        private long _droppedCount;

        public ProtocolPerformanceWriteQueue(ILogger<ProtocolPerformanceWriteQueue> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _channel = Channel.CreateBounded<ProtocolPerformanceWriteItem>(new BoundedChannelOptions(Capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.DropWrite
            });
        }

        public bool TryEnqueueServer(ProtocolPerformanceServerMetric metric)
        {
            return TryWrite(new ProtocolPerformanceWriteItem
            {
                ServerMetric = metric
            });
        }

        public bool TryEnqueueClientBatch(IReadOnlyCollection<ProtocolPerformanceClientMetric> metrics)
        {
            if (metrics == null || metrics.Count == 0)
            {
                return true;
            }

            return TryWrite(new ProtocolPerformanceWriteItem
            {
                ClientMetrics = metrics
            });
        }

        public IAsyncEnumerable<ProtocolPerformanceWriteItem> ReadAllAsync(CancellationToken cancellationToken)
        {
            return _channel.Reader.ReadAllAsync(cancellationToken);
        }

        private bool TryWrite(ProtocolPerformanceWriteItem item)
        {
            if (_channel.Writer.TryWrite(item))
            {
                return true;
            }

            var droppedCount = Interlocked.Increment(ref _droppedCount);
            if (droppedCount == 1 || droppedCount % 100 == 0)
            {
                _logger.LogWarning("协议性能写入队列已满，累计丢弃 {DroppedCount} 条监控数据", droppedCount);
            }

            return false;
        }
    }

    public sealed class ProtocolPerformanceWriteItem
    {
        public ProtocolPerformanceServerMetric? ServerMetric { get; init; }
        public IReadOnlyCollection<ProtocolPerformanceClientMetric>? ClientMetrics { get; init; }
    }
}
