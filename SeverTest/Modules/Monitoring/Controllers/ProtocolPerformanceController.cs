using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ServerTest.Controllers;
using ServerTest.Modules.Monitoring.Application;
using ServerTest.Modules.Monitoring.Domain;
using ServerTest.Protocol;

namespace ServerTest.Modules.Monitoring.Controllers
{
    [ApiController]
    [Route("api/monitoring/protocol-performance")]
    public sealed class ProtocolPerformanceController : BaseController
    {
        private readonly ProtocolPerformanceStorageFeature _storageFeature;
        private readonly ProtocolPerformanceRecorder _recorder;
        private readonly ProtocolPerformanceService _service;

        public ProtocolPerformanceController(
            ILogger<ProtocolPerformanceController> logger,
            ProtocolPerformanceStorageFeature storageFeature,
            ProtocolPerformanceRecorder recorder,
            ProtocolPerformanceService service)
            : base(logger)
        {
            _storageFeature = storageFeature ?? throw new ArgumentNullException(nameof(storageFeature));
            _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        [HttpPost("report")]
        [ProtocolType("monitoring.protocol.performance.report")]
        public IActionResult Report([FromBody] ProtocolRequest<ProtocolPerformanceReportRequest> request)
        {
            var items = request.Data?.Items ?? Array.Empty<ProtocolPerformanceReportItem>();

            if (!_storageFeature.IsEnabled)
            {
                return Ok(new
                {
                    storageEnabled = false,
                    message = ProtocolPerformanceStorageFeature.DisabledMessage,
                    receivedCount = items.Count,
                    acceptedCount = 0,
                    droppedCount = items.Count
                });
            }

            var metrics = items.Select(MapClientMetric).ToArray();
            var acceptedCount = _recorder.EnqueueClientMetrics(metrics);

            return Ok(new
            {
                storageEnabled = true,
                receivedCount = items.Count,
                acceptedCount,
                droppedCount = Math.Max(0, items.Count - acceptedCount)
            });
        }

        [HttpPost("summary")]
        [ProtocolType("monitoring.protocol.performance.summary")]
        public async Task<IActionResult> SummaryAsync(
            [FromBody] ProtocolRequest<ProtocolPerformanceQueryRequest> request,
            CancellationToken cancellationToken)
        {
            var query = request.Data ?? new ProtocolPerformanceQueryRequest();
            var items = await _service.GetSummaryAsync(query.Hours, query.Transport, query.Top, cancellationToken)
                .ConfigureAwait(false);

            return Ok(new
            {
                storageEnabled = _service.IsStorageEnabled,
                message = _service.IsStorageEnabled ? null : ProtocolPerformanceStorageFeature.DisabledMessage,
                windowHours = Math.Clamp(query.Hours, 1, 24 * 30),
                transport = string.IsNullOrWhiteSpace(query.Transport) ? null : ProtocolPerformanceTransport.Normalize(query.Transport),
                items
            });
        }

        [HttpPost("analyze")]
        [ProtocolType("monitoring.protocol.performance.analyze")]
        public async Task<IActionResult> AnalyzeAsync(
            [FromBody] ProtocolRequest<ProtocolPerformanceQueryRequest> request,
            CancellationToken cancellationToken)
        {
            var query = request.Data ?? new ProtocolPerformanceQueryRequest();
            var result = await _service.AnalyzeAsync(query.Hours, query.Transport, query.Top, cancellationToken)
                .ConfigureAwait(false);
            return Ok(result);
        }

        private static ProtocolPerformanceClientMetric MapClientMetric(ProtocolPerformanceReportItem item)
        {
            var completedAt = item.ClientCompletedAtMs > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(item.ClientCompletedAtMs).UtcDateTime
                : DateTime.UtcNow;
            var startedAt = item.ClientStartedAtMs > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(item.ClientStartedAtMs).UtcDateTime
                : completedAt.AddMilliseconds(-Math.Max(0, item.ClientElapsedMs));

            return new ProtocolPerformanceClientMetric
            {
                ReqId = item.ReqId ?? string.Empty,
                Transport = ProtocolPerformanceTransport.Normalize(item.Transport),
                ProtocolType = item.ProtocolType ?? string.Empty,
                RequestPath = item.RequestPath,
                HttpMethod = item.HttpMethod,
                SystemName = item.SystemName,
                ClientStartedAt = startedAt,
                ClientCompletedAt = completedAt,
                ClientElapsedMs = Math.Max(0, item.ClientElapsedMs),
                ProtocolCode = item.ProtocolCode,
                HttpStatus = item.HttpStatus,
                IsSuccess = item.IsSuccess,
                IsTimeout = item.IsTimeout,
                ErrorMessage = item.ErrorMessage
            };
        }
    }

    public sealed class ProtocolPerformanceReportRequest
    {
        public IReadOnlyList<ProtocolPerformanceReportItem> Items { get; set; } = Array.Empty<ProtocolPerformanceReportItem>();
    }

    public sealed class ProtocolPerformanceReportItem
    {
        public string? ReqId { get; set; }
        public string? Transport { get; set; }
        public string? ProtocolType { get; set; }
        public string? RequestPath { get; set; }
        public string? HttpMethod { get; set; }
        public string? SystemName { get; set; }
        public long ClientStartedAtMs { get; set; }
        public long ClientCompletedAtMs { get; set; }
        public int ClientElapsedMs { get; set; }
        public int? ProtocolCode { get; set; }
        public int? HttpStatus { get; set; }
        public bool IsSuccess { get; set; }
        public bool IsTimeout { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public sealed class ProtocolPerformanceQueryRequest
    {
        public int Hours { get; set; } = 24;
        public int Top { get; set; } = 20;
        public string? Transport { get; set; }
    }
}
