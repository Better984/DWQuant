namespace ServerTest.Modules.Monitoring.Domain
{
    public sealed class ProtocolPerformanceAnalysisResult
    {
        public bool StorageEnabled { get; set; } = true;
        public string? Message { get; set; }
        public DateTime GeneratedAt { get; set; }
        public DateTime WindowStart { get; set; }
        public DateTime WindowEnd { get; set; }
        public double? GlobalAvgServerElapsedMs { get; set; }
        public double? GlobalAvgClientElapsedMs { get; set; }
        public IReadOnlyList<ProtocolPerformanceAnalysisItem> Items { get; set; } = Array.Empty<ProtocolPerformanceAnalysisItem>();
    }

    public sealed class ProtocolPerformanceAnalysisItem
    {
        public string ProtocolType { get; set; } = string.Empty;
        public string Transport { get; set; } = ProtocolPerformanceTransport.Http;
        public string Severity { get; set; } = string.Empty;
        public int TotalCount { get; set; }
        public double? AvgServerElapsedMs { get; set; }
        public int? MaxServerElapsedMs { get; set; }
        public double? AvgClientElapsedMs { get; set; }
        public int? MaxClientElapsedMs { get; set; }
        public double? AvgClientNetworkOverheadMs { get; set; }
        public int SlowCount { get; set; }
        public double SlowRate { get; set; }
        public IReadOnlyList<string> Reasons { get; set; } = Array.Empty<string>();
    }

    internal sealed class ProtocolPerformanceGlobalStats
    {
        public double? AvgServerElapsedMs { get; set; }
        public double? AvgClientElapsedMs { get; set; }
    }
}
