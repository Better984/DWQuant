using ServerTest.Modules.Monitoring.Domain;

namespace ServerTest.Modules.Monitoring.Application
{
    public sealed class ProtocolPerformanceRecorder
    {
        private const int MaxErrorMessageLength = 500;
        private readonly ProtocolPerformanceStorageFeature _storageFeature;
        private readonly ProtocolPerformanceWriteQueue _queue;

        public ProtocolPerformanceRecorder(
            ProtocolPerformanceStorageFeature storageFeature,
            ProtocolPerformanceWriteQueue queue)
        {
            _storageFeature = storageFeature ?? throw new ArgumentNullException(nameof(storageFeature));
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        }

        public bool TryRecordServerMetric(ProtocolPerformanceServerMetric metric)
        {
            if (!_storageFeature.IsEnabled)
            {
                return false;
            }

            if (!IsValid(metric?.ReqId, metric?.ProtocolType))
            {
                return false;
            }

            metric!.Transport = ProtocolPerformanceTransport.Normalize(metric.Transport);
            metric.ProtocolType = metric.ProtocolType.Trim();
            metric.ErrorMessage = TrimError(metric.ErrorMessage);
            metric.ServerElapsedMs = Math.Max(0, metric.ServerElapsedMs);
            return _queue.TryEnqueueServer(metric);
        }

        public int EnqueueClientMetrics(IEnumerable<ProtocolPerformanceClientMetric>? metrics)
        {
            if (!_storageFeature.IsEnabled || metrics == null)
            {
                return 0;
            }

            var acceptedCount = 0;
            foreach (var batch in metrics
                         .Where(metric => IsValid(metric.ReqId, metric.ProtocolType))
                         .Select(metric =>
                         {
                             metric.Transport = ProtocolPerformanceTransport.Normalize(metric.Transport);
                             metric.ProtocolType = metric.ProtocolType.Trim();
                             metric.ErrorMessage = TrimError(metric.ErrorMessage);
                             metric.ClientElapsedMs = Math.Max(0, metric.ClientElapsedMs);
                             return metric;
                         })
                         .Chunk(100))
            {
                if (_queue.TryEnqueueClientBatch(batch))
                {
                    acceptedCount += batch.Length;
                }
            }

            return acceptedCount;
        }

        private static bool IsValid(string? reqId, string? protocolType)
        {
            return !string.IsNullOrWhiteSpace(reqId) && !string.IsNullOrWhiteSpace(protocolType);
        }

        private static string? TrimError(string? errorMessage)
        {
            if (string.IsNullOrWhiteSpace(errorMessage))
            {
                return null;
            }

            var normalized = errorMessage.Trim();
            return normalized.Length <= MaxErrorMessageLength
                ? normalized
                : normalized[..MaxErrorMessageLength];
        }
    }
}
