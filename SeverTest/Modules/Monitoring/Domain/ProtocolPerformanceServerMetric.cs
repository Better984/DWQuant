namespace ServerTest.Modules.Monitoring.Domain
{
    public sealed class ProtocolPerformanceServerMetric
    {
        public string ReqId { get; set; } = string.Empty;
        public string Transport { get; set; } = ProtocolPerformanceTransport.Http;
        public string ProtocolType { get; set; } = string.Empty;
        public string? RequestPath { get; set; }
        public string? HttpMethod { get; set; }
        public string? UserId { get; set; }
        public string? SystemName { get; set; }
        public string? TraceId { get; set; }
        public string? RemoteIp { get; set; }
        public DateTime ServerStartedAt { get; set; }
        public DateTime ServerCompletedAt { get; set; }
        public int ServerElapsedMs { get; set; }
        public int? ProtocolCode { get; set; }
        public int? HttpStatus { get; set; }
        public bool IsSuccess { get; set; }
        public bool IsTimeout { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
