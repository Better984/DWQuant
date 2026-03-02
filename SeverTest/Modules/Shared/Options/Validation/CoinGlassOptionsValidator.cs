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
            var mode = (options.SourceMode ?? string.Empty).Trim();

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

            if (string.IsNullOrWhiteSpace(options.EtfFlowPath))
            {
                failures.Add("CoinGlass.EtfFlowPath 不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.LiquidationHeatmapModel1Path))
            {
                failures.Add("CoinGlass.LiquidationHeatmapModel1Path 不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.ApiKeyHeaderName))
            {
                failures.Add("CoinGlass.ApiKeyHeaderName 不能为空");
            }

            if (options.FearGreedSeriesLimit <= 0)
            {
                failures.Add("CoinGlass.FearGreedSeriesLimit 必须大于 0");
            }

            if (options.EtfFlowSeriesLimit <= 0)
            {
                failures.Add("CoinGlass.EtfFlowSeriesLimit 必须大于 0");
            }

            if (string.IsNullOrWhiteSpace(options.LiquidationHeatmapDefaultExchange))
            {
                failures.Add("CoinGlass.LiquidationHeatmapDefaultExchange 不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.LiquidationHeatmapDefaultSymbol))
            {
                failures.Add("CoinGlass.LiquidationHeatmapDefaultSymbol 不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.LiquidationHeatmapDefaultRange))
            {
                failures.Add("CoinGlass.LiquidationHeatmapDefaultRange 不能为空");
            }

            if (!string.IsNullOrWhiteSpace(mode) &&
                !string.Equals(mode, "official", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(mode, "pirated_proxy", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add("CoinGlass.SourceMode 仅支持 official / pirated_proxy");
            }

            if (options.Enabled && string.IsNullOrWhiteSpace(options.ApiKey))
            {
                failures.Add("CoinGlass.Enabled=true 时，CoinGlass.ApiKey 不能为空");
            }

            if (options.EnableStreamWsBridge)
            {
                if (string.IsNullOrWhiteSpace(options.StreamWsUrl))
                {
                    failures.Add("CoinGlass.EnableStreamWsBridge=true 时，CoinGlass.StreamWsUrl 不能为空");
                }

                if (options.StreamChannels == null || options.StreamChannels.Count == 0)
                {
                    failures.Add("CoinGlass.EnableStreamWsBridge=true 时，CoinGlass.StreamChannels 至少配置一个频道");
                }

                if (options.WsReconnectDelaySeconds <= 0)
                {
                    failures.Add("CoinGlass.WsReconnectDelaySeconds 必须大于 0");
                }

                if (options.WsChannelCacheSeconds <= 0)
                {
                    failures.Add("CoinGlass.WsChannelCacheSeconds 必须大于 0");
                }
            }

            return failures.Count > 0
                ? ValidateOptionsResult.Fail(failures)
                : ValidateOptionsResult.Success;
        }
    }
}
