namespace ServerTest.Options
{
    public class HistoricalMarketDataOptions
    {
        public bool SyncEnabled { get; set; } = true;
        public bool PreloadEnabled { get; set; } = true;
        public string PreloadStartDate { get; set; } = "2025-01-01";
        public int SyncBatchSize { get; set; } = 1000;
        public int SyncMaxParallel { get; set; } = 3;
        public int SyncMinGapMinutes { get; set; } = 2;
        public int SyncIntervalMinutes { get; set; } = 60;
        public string DefaultStartDate { get; set; } = "2025-01-01";
        public int MaxQueryBars { get; set; } = 2000;
        public int MaxCacheBars { get; set; } = int.MaxValue;
    }
}
