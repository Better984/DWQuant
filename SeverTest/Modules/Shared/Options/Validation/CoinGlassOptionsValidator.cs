using Microsoft.Extensions.Options;

namespace ServerTest.Options
{
    /// <summary>
    /// CoinGlass 配置校验器。
    /// </summary>
    public sealed class CoinGlassOptionsValidator : IValidateOptions<CoinGlassOptions>
    {
        public ValidateOptionsResult Validate(string? name, CoinGlassOptions options)
        {
            if (options == null)
            {
                return ValidateOptionsResult.Fail("CoinGlass 配置不能为空");
            }

            var failures = new List<string>();

            if (string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                failures.Add("CoinGlass.BaseUrl 不能为空");
            }

            if (options.TimeoutSeconds <= 0)
            {
                failures.Add("CoinGlass.TimeoutSeconds 必须大于 0");
            }

            if (string.IsNullOrWhiteSpace(options.FearGreedPath))
            {
                failures.Add("CoinGlass.FearGreedPath 不能为空");
            }

            if (options.FearGreedSeriesLimit <= 0)
            {
                failures.Add("CoinGlass.FearGreedSeriesLimit 必须大于 0");
            }

            if (options.Enabled && string.IsNullOrWhiteSpace(options.ApiKey))
            {
                failures.Add("CoinGlass.Enabled=true 时，CoinGlass.ApiKey 不能为空");
            }

            return failures.Count > 0
                ? ValidateOptionsResult.Fail(failures)
                : ValidateOptionsResult.Success;
        }
    }
}
