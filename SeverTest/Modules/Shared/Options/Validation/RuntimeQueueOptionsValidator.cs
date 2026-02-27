using Microsoft.Extensions.Options;

namespace ServerTest.Options
{
    /// <summary>
    /// 运行时队列配置校验，避免误配置导致无界队列。
    /// </summary>
    public sealed class RuntimeQueueOptionsValidator : IValidateOptions<RuntimeQueueOptions>
    {
        private static readonly HashSet<string> AllowedFullModes = new(StringComparer.OrdinalIgnoreCase)
        {
            "wait",
            "dropoldest",
            "dropnewest",
            "dropwrite"
        };

        public ValidateOptionsResult Validate(string? name, RuntimeQueueOptions options)
        {
            if (options == null)
            {
                return ValidateOptionsResult.Fail("RuntimeQueue 配置不能为空");
            }

            var failures = new List<string>();
            ValidateQueue("MarketData", options.MarketData, failures);
            ValidateQueue("Indicator", options.Indicator, failures);
            ValidateQueue("StrategyAction", options.StrategyAction, failures);
            ValidateQueue("StrategyRunLog", options.StrategyRunLog, failures);

            return failures.Count > 0
                ? ValidateOptionsResult.Fail(failures)
                : ValidateOptionsResult.Success;
        }

        private static void ValidateQueue(string name, QueueOptions? options, List<string> failures)
        {
            if (options == null)
            {
                failures.Add($"{name} 队列配置不能为空");
                return;
            }

            if (options.Capacity <= 0)
            {
                failures.Add($"{name} 队列容量必须大于 0");
            }

            if (!AllowedFullModes.Contains(options.FullMode ?? string.Empty))
            {
                failures.Add($"{name} 队列满载策略不合法: {options.FullMode}");
            }

            if (options.WarningThresholdPercent < 1 || options.WarningThresholdPercent > 100)
            {
                failures.Add($"{name} 队列告警阈值必须在 1-100 之间");
            }

            if (options.WarningIntervalSeconds <= 0)
            {
                failures.Add($"{name} 队列告警间隔必须大于 0");
            }
        }
    }
}
