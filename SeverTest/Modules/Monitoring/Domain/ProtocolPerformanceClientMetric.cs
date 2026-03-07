namespace ServerTest.Modules.Monitoring.Domain
{
    public sealed class ProtocolPerformanceClientMetric
    {
        public string ReqId { get; set; } = string.Empty;
        public string Transport { get; set; } = ProtocolPerformanceTransport.Http;
        public string ProtocolType { get; set; } = string.Empty;
        public string? RequestPath { get; set; }
        public string? HttpMethod { get; set; }
        public string? SystemName { get; set; }
        public DateTime ClientStartedAt { get; set; }
        public DateTime ClientCompletedAt { get; set; }
        public int ClientElapsedMs { get; set; }
        public int? ProtocolCode { get; set; }
        public int? HttpStatus { get; set; }
        public bool IsSuccess { get; set; }
        public bool IsTimeout { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
