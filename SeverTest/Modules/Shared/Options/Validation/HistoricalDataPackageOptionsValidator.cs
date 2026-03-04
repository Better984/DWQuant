using Microsoft.Extensions.Options;
using ServerTest.Models;

namespace ServerTest.Options
{
    /// <summary>
    /// 历史K线离线包配置校验器。
    /// </summary>
    public sealed class HistoricalDataPackageOptionsValidator : IValidateOptions<HistoricalDataPackageOptions>
    {
        public ValidateOptionsResult Validate(string? name, HistoricalDataPackageOptions options)
        {
            if (options == null)
            {
                return ValidateOptionsResult.Fail("HistoricalDataPackage 配置不能为空");
            }

            var failures = new List<string>();
            if (options.UpdateIntervalMinutes <= 0)
            {
                failures.Add("HistoricalDataPackage.UpdateIntervalMinutes 必须大于 0");
            }

            if (string.IsNullOrWhiteSpace(options.PackagePrefix))
            {
                failures.Add("HistoricalDataPackage.PackagePrefix 不能为空");
            }

            if (options.ExtraBarsBuffer < 0)
            {
                failures.Add("HistoricalDataPackage.ExtraBarsBuffer 不能小于 0");
            }

            if (options.KeepLatestVersionCount <= 0)
            {
                failures.Add("HistoricalDataPackage.KeepLatestVersionCount 必须大于 0");
            }

            if (options.KeepLatestVersionDays <= 0)
            {
                failures.Add("HistoricalDataPackage.KeepLatestVersionDays 必须大于 0");
            }

            if (options.CleanupMaxScanObjects <= 0)
            {
                failures.Add("HistoricalDataPackage.CleanupMaxScanObjects 必须大于 0");
            }

            if (options.CleanupMaxDeleteObjectsPerRun <= 0)
            {
                failures.Add("HistoricalDataPackage.CleanupMaxDeleteObjectsPerRun 必须大于 0");
            }

            if (options.IntegrityCheckMaxReportMissing <= 0)
            {
                failures.Add("HistoricalDataPackage.IntegrityCheckMaxReportMissing 必须大于 0");
            }

            var allowedTimeframes = Enum.GetValues<MarketDataConfig.TimeframeEnum>()
                .Select(MarketDataConfig.TimeframeToString)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (options.RetentionDaysByTimeframe == null || options.RetentionDaysByTimeframe.Count == 0)
            {
                failures.Add("HistoricalDataPackage.RetentionDaysByTimeframe 至少配置一个周期");
            }
            else
            {
                foreach (var pair in options.RetentionDaysByTimeframe)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key))
                    {
                        failures.Add("HistoricalDataPackage.RetentionDaysByTimeframe 存在空周期键");
                        continue;
                    }

                    if (!allowedTimeframes.Contains(pair.Key))
                    {
                        failures.Add($"HistoricalDataPackage.RetentionDaysByTimeframe 包含不支持的周期: {pair.Key}");
                    }

                    if (pair.Value < 0)
                    {
                        failures.Add($"HistoricalDataPackage.RetentionDaysByTimeframe[{pair.Key}] 不能小于 0");
                    }
                }
            }

            return failures.Count > 0
                ? ValidateOptionsResult.Fail(failures)
                : ValidateOptionsResult.Success;
        }
    }
}
