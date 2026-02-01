using Microsoft.Extensions.Options;

namespace ServerTest.Options
{
    public sealed class ConditionCacheOptionsValidator : IValidateOptions<ConditionCacheOptions>
    {
        public ValidateOptionsResult Validate(string? name, ConditionCacheOptions options)
        {
            if (options == null)
            {
                return ValidateOptionsResult.Fail("ConditionCache 配置不能为空");
            }

            if (options.CleanupIntervalSeconds <= 0)
            {
                return ValidateOptionsResult.Fail("ConditionCache:CleanupIntervalSeconds 必须大于 0");
            }

            return ValidateOptionsResult.Success;
        }
    }
}
