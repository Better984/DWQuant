namespace ServerTest.Modules.MarketData.Domain
{
    /// <summary>
    /// 历史K线离线包清单。
    /// </summary>
    public sealed class HistoricalDataPackageManifest
    {
        public string Version { get; set; } = string.Empty;
        public DateTime GeneratedAtUtc { get; set; }
        public int UpdateIntervalMinutes { get; set; }
        public Dictionary<string, int> RetentionDaysByTimeframe { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<HistoricalDataPackageDataset> Datasets { get; set; } = new();
        public string? ManifestUrl { get; set; }
        public bool IsFirstUpload { get; set; }
        public string? CloudIntegrityState { get; set; }
        public string? CloudRepairAction { get; set; }
    }

    /// <summary>
    /// 离线包分片元数据。
    /// </summary>
    public sealed class HistoricalDataPackageDataset
    {
        public string Id { get; set; } = string.Empty;
        public string Exchange { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public string Timeframe { get; set; } = string.Empty;
        public int RetentionDays { get; set; }
        public long StartTime { get; set; }
        public long EndTime { get; set; }
        public int Count { get; set; }
        public long RawBytes { get; set; }
        public long CompressedBytes { get; set; }
        public string Sha256 { get; set; } = string.Empty;
        public string ObjectKey { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }
}
