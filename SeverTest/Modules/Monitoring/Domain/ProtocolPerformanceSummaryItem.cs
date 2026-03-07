namespace ServerTest.Modules.Monitoring.Domain
{
    public sealed class ProtocolPerformanceSummaryItem
    {
        public string ProtocolType { get; set; } = string.Empty;
        public string Transport { get; set; } = ProtocolPerformanceTransport.Http;
        public int TotalCount { get; set; }
        public int SuccessCount { get; set; }
        public int ErrorCount { get; set; }
        public double? AvgServerElapsedMs { get; set; }
        public int? MaxServerElapsedMs { get; set; }
        public double? AvgClientElapsedMs { get; set; }
        public int? MaxClientElapsedMs { get; set; }
        public double? AvgClientNetworkOverheadMs { get; set; }
        public int SlowCount { get; set; }
        public DateTime LastSeenAt { get; set; }

        public double SuccessRate => TotalCount <= 0 ? 0d : Math.Round(SuccessCount * 100d / TotalCount, 2);
        public double SlowRate => TotalCount <= 0 ? 0d : Math.Round(SlowCount * 100d / TotalCount, 2);
    }
}
