using Microsoft.Extensions.Options;

namespace ServerTest.Options
{
    public sealed class MonitoringOptionsValidator : IValidateOptions<MonitoringOptions>
    {
        public ValidateOptionsResult Validate(string? name, MonitoringOptions options)
        {
            if (options == null)
            {
                return ValidateOptionsResult.Fail("Monitoring 配置不能为空");
            }

            var failures = new List<string>();

            if (options.MaxLogItems <= 0)
            {
                failures.Add("Monitoring.MaxLogItems 必须大于 0");
            }

            if (options.MaxTradingLogItems <= 0)
            {
                failures.Add("Monitoring.MaxTradingLogItems 必须大于 0");
            }

            return failures.Count > 0
                ? ValidateOptionsResult.Fail(failures)
                : ValidateOptionsResult.Success;
        }
    }
}
