using Microsoft.Extensions.Options;

namespace ServerTest.Options
{
    /// <summary>
    /// 策略租约配置校验
    /// </summary>
    public sealed class StrategyOwnershipOptionsValidator : IValidateOptions<StrategyOwnershipOptions>
    {
        public ValidateOptionsResult Validate(string? name, StrategyOwnershipOptions options)
        {
            if (options == null)
            {
                return ValidateOptionsResult.Fail("StrategyOwnership 配置不能为空");
            }

            if (!options.Enabled)
            {
                return ValidateOptionsResult.Success;
            }

            var failures = new List<string>();
            if (options.LeaseSeconds <= 0)
            {
                failures.Add("策略租约 LeaseSeconds 必须大于 0");
            }

            if (options.RenewIntervalSeconds <= 0)
            {
                failures.Add("策略租约 RenewIntervalSeconds 必须大于 0");
            }

            if (options.SyncIntervalSeconds <= 0)
            {
                failures.Add("策略租约 SyncIntervalSeconds 必须大于 0");
            }

            if (options.RenewIntervalSeconds >= options.LeaseSeconds)
            {
                failures.Add("策略租约续租间隔必须小于租约时长");
            }

            if (string.IsNullOrWhiteSpace(options.KeyPrefix))
            {
                failures.Add("策略租约 KeyPrefix 不能为空");
            }

            return failures.Count > 0
                ? ValidateOptionsResult.Fail(failures)
                : ValidateOptionsResult.Success;
        }
    }
}
