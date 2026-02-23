using Microsoft.Extensions.Options;

namespace ServerTest.Options
{
    /// <summary>
    /// 指标框架配置校验器。
    /// </summary>
    public sealed class IndicatorFrameworkOptionsValidator : IValidateOptions<IndicatorFrameworkOptions>
    {
        public ValidateOptionsResult Validate(string? name, IndicatorFrameworkOptions options)
        {
            if (options == null)
            {
                return ValidateOptionsResult.Fail("Indicators 配置不能为空");
            }

            var failures = new List<string>();

            if (options.RefreshScanIntervalSeconds <= 0)
            {
                failures.Add("Indicators.RefreshScanIntervalSeconds 必须大于 0");
            }

            if (options.DefinitionReloadSeconds <= 0)
            {
                failures.Add("Indicators.DefinitionReloadSeconds 必须大于 0");
            }

            if (options.MaxHistoryQueryPoints <= 0)
            {
                failures.Add("Indicators.MaxHistoryQueryPoints 必须大于 0");
            }

            if (options.RedisCacheSeconds <= 0)
            {
                failures.Add("Indicators.RedisCacheSeconds 必须大于 0");
            }

            if (options.StaleToleranceSeconds < 0)
            {
                failures.Add("Indicators.StaleToleranceSeconds 不能小于 0");
            }

            if (options.HistoryCleanupIntervalMinutes <= 0)
            {
                failures.Add("Indicators.HistoryCleanupIntervalMinutes 必须大于 0");
            }

            return failures.Count > 0
                ? ValidateOptionsResult.Fail(failures)
                : ValidateOptionsResult.Success;
        }
    }
}
