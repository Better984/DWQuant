using Microsoft.Extensions.Options;

namespace ServerTest.Options
{
    public sealed class MarketDataQueryOptionsValidator : IValidateOptions<MarketDataQueryOptions>
    {
        public ValidateOptionsResult Validate(string? name, MarketDataQueryOptions options)
        {
            if (options == null)
            {
                return ValidateOptionsResult.Fail("MarketDataQuery 配置不能为空");
            }

            var failures = new List<string>();

            if (options.DefaultBatchSize <= 0)
            {
                failures.Add("MarketDataQuery.DefaultBatchSize 必须大于 0");
            }

            if (options.MaxLimitPerRequest <= 0)
            {
                failures.Add("MarketDataQuery.MaxLimitPerRequest 必须大于 0");
            }

            if (options.CacheHistoryLength <= 0)
            {
                failures.Add("MarketDataQuery.CacheHistoryLength 必须大于 0");
            }

            return failures.Count > 0
                ? ValidateOptionsResult.Fail(failures)
                : ValidateOptionsResult.Success;
        }
    }
}
