using Microsoft.Extensions.Options;

namespace ServerTest.Options
{
    public sealed class StrategyDiagnosticsOptionsValidator : IValidateOptions<StrategyDiagnosticsOptions>
    {
        public ValidateOptionsResult Validate(string? name, StrategyDiagnosticsOptions options)
        {
            if (options == null)
            {
                return ValidateOptionsResult.Fail("StrategyDiagnostics 配置不能为空");
            }

            var failures = new List<string>();

            if (options.SlowRunThresholdMs <= 0)
            {
                failures.Add("StrategyDiagnostics.SlowRunThresholdMs 必须大于 0");
            }

            if (options.SlowIndicatorRefreshThresholdMs <= 0)
            {
                failures.Add("StrategyDiagnostics.SlowIndicatorRefreshThresholdMs 必须大于 0");
            }

            if (options.SlowMarketDispatchThresholdMs <= 0)
            {
                failures.Add("StrategyDiagnostics.SlowMarketDispatchThresholdMs 必须大于 0");
            }

            if (options.SlowActionEnqueueThresholdMs <= 0)
            {
                failures.Add("StrategyDiagnostics.SlowActionEnqueueThresholdMs 必须大于 0");
            }

            if (options.SlowTradeActionThresholdMs <= 0)
            {
                failures.Add("StrategyDiagnostics.SlowTradeActionThresholdMs 必须大于 0");
            }

            if (options.SlowTaskTraceThresholdMs <= 0)
            {
                failures.Add("StrategyDiagnostics.SlowTaskTraceThresholdMs 必须大于 0");
            }

            return failures.Count > 0
                ? ValidateOptionsResult.Fail(failures)
                : ValidateOptionsResult.Success;
        }
    }
}
