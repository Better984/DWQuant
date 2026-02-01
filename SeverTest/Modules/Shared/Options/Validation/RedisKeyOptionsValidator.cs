using Microsoft.Extensions.Options;

namespace ServerTest.Options
{
    public sealed class RedisKeyOptionsValidator : IValidateOptions<RedisKeyOptions>
    {
        public ValidateOptionsResult Validate(string? name, RedisKeyOptions options)
        {
            if (options == null)
            {
                return ValidateOptionsResult.Fail("RedisKey 配置不能为空");
            }

            var failures = new List<string>();

            if (string.IsNullOrWhiteSpace(options.MarketSubUserSetKey))
            {
                failures.Add("RedisKey.MarketSubUserSetKey 不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.MarketSubUserPrefix))
            {
                failures.Add("RedisKey.MarketSubUserPrefix 不能为空");
            }

            return failures.Count > 0
                ? ValidateOptionsResult.Fail(failures)
                : ValidateOptionsResult.Success;
        }
    }
}
