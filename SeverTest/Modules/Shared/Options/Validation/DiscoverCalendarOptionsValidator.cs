using Microsoft.Extensions.Options;

namespace ServerTest.Options
{
    /// <summary>
    /// 发现页日历配置校验器。
    /// </summary>
    public sealed class DiscoverCalendarOptionsValidator : IValidateOptions<DiscoverCalendarOptions>
    {
        public ValidateOptionsResult Validate(string? name, DiscoverCalendarOptions options)
        {
            if (options == null)
            {
                return ValidateOptionsResult.Fail("DiscoverCalendar 配置不能为空");
            }

            var failures = new List<string>();

            if (options.PollIntervalSeconds <= 0)
            {
                failures.Add("DiscoverCalendar.PollIntervalSeconds 必须大于 0");
            }

            if (options.InitialLatestCount <= 0)
            {
                failures.Add("DiscoverCalendar.InitialLatestCount 必须大于 0");
            }

            if (options.MaxPullLimit <= 0)
            {
                failures.Add("DiscoverCalendar.MaxPullLimit 必须大于 0");
            }

            if (options.MemoryCacheMaxItems <= 0)
            {
                failures.Add("DiscoverCalendar.MemoryCacheMaxItems 必须大于 0");
            }

            if (options.ProviderPerPage <= 0 || options.ProviderPerPage > 1000)
            {
                failures.Add("DiscoverCalendar.ProviderPerPage 必须在 1-1000 之间");
            }

            if (options.InitBackfillMaxPages <= 0)
            {
                failures.Add("DiscoverCalendar.InitBackfillMaxPages 必须大于 0");
            }

            if (options.FutureWindowDays < 0 || options.FutureWindowDays > 30)
            {
                failures.Add("DiscoverCalendar.FutureWindowDays 必须在 0-30 之间");
            }

            if (string.IsNullOrWhiteSpace(options.Language))
            {
                failures.Add("DiscoverCalendar.Language 不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.CentralBankActivitiesPath))
            {
                failures.Add("DiscoverCalendar.CentralBankActivitiesPath 不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.FinancialEventsPath))
            {
                failures.Add("DiscoverCalendar.FinancialEventsPath 不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.EconomicDataPath))
            {
                failures.Add("DiscoverCalendar.EconomicDataPath 不能为空");
            }

            return failures.Count > 0
                ? ValidateOptionsResult.Fail(failures)
                : ValidateOptionsResult.Success;
        }
    }
}
