namespace ServerTest.Options
{
    /// <summary>
    /// 历史K线离线包配置。
    /// </summary>
    public sealed class HistoricalDataPackageOptions
    {
        /// <summary>
        /// 是否启用离线包打包与上传。
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// 定时更新间隔（分钟）。
        /// </summary>
        public int UpdateIntervalMinutes { get; set; } = 1440;

        /// <summary>
        /// OSS 对象前缀目录。
        /// </summary>
        public string PackagePrefix { get; set; } = "marketdata/kline-offline";

        /// <summary>
        /// 允许导出的交易所列表，默认仅币安。
        /// </summary>
        public string[] Exchanges { get; set; } = new[] { "binance" };

        /// <summary>
        /// 允许导出的交易对列表，空表示自动使用系统内置交易对。
        /// </summary>
        public string[] Symbols { get; set; } = Array.Empty<string>();

        /// <summary>
        /// 周期对应的保留天数配置（key 示例：1m、5m、1h、1d）。
        /// </summary>
        public Dictionary<string, int> RetentionDaysByTimeframe { get; set; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["1m"] = 30,
            ["3m"] = 730,
            ["5m"] = 60,
            ["15m"] = 90,
            ["30m"] = 180,
            ["1h"] = 365,
            ["2h"] = 730,
            ["4h"] = 730,
            ["6h"] = 730,
            ["8h"] = 730,
            ["12h"] = 730,
            ["1d"] = 730,
            ["3d"] = 730,
            ["1w"] = 730,
            ["1mo"] = 730
        };

        /// <summary>
        /// 每个数据分片额外追加的查询缓冲根数，用于覆盖边界误差。
        /// </summary>
        public int ExtraBarsBuffer { get; set; } = 2048;

        /// <summary>
        /// 是否同步上传 latest-manifest 别名文件。
        /// </summary>
        public bool UploadLatestManifestAlias { get; set; } = true;

        /// <summary>
        /// 是否启用云端完整性巡检（latest-manifest、版本清单、分片对象存在性）。
        /// </summary>
        public bool EnableCloudIntegrityCheck { get; set; } = true;

        /// <summary>
        /// 是否在检测到云端缺失时自动补给（本轮任务继续生成并上传新版本）。
        /// </summary>
        public bool AutoRepairOnCloudMissing { get; set; } = true;

        /// <summary>
        /// 是否启用旧版本自动清理。
        /// </summary>
        public bool EnableVersionCleanup { get; set; } = true;

        /// <summary>
        /// 至少保留最近 N 个版本（>=1）。
        /// </summary>
        public int KeepLatestVersionCount { get; set; } = 7;

        /// <summary>
        /// 至少保留最近 N 天内的版本（>=1）。
        /// </summary>
        public int KeepLatestVersionDays { get; set; } = 30;

        /// <summary>
        /// 每轮最多扫描的对象数量，防止大目录巡检过重。
        /// </summary>
        public int CleanupMaxScanObjects { get; set; } = 50000;

        /// <summary>
        /// 每轮最多删除的对象数量，避免瞬时删除压力过高。
        /// </summary>
        public int CleanupMaxDeleteObjectsPerRun { get; set; } = 1000;

        /// <summary>
        /// 巡检时最多记录的缺失对象数量，避免日志过长。
        /// </summary>
        public int IntegrityCheckMaxReportMissing { get; set; } = 50;
    }
}
