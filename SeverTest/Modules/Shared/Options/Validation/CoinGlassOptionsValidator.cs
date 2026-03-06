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

            if (string.IsNullOrWhiteSpace(options.EtfFlowPath) &&
                string.IsNullOrWhiteSpace(options.EtfFlowPathTemplate))
            {
                failures.Add("CoinGlass.EtfFlowPath 与 CoinGlass.EtfFlowPathTemplate 不能同时为空");
            }

            if (string.IsNullOrWhiteSpace(options.LiquidationHeatmapModel1Path))
            {
                failures.Add("CoinGlass.LiquidationHeatmapModel1Path 不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.ExchangeAssetsPath))
            {
                failures.Add("CoinGlass.ExchangeAssetsPath 不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.ExchangeBalanceListPath))
            {
                failures.Add("CoinGlass.ExchangeBalanceListPath 不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.ExchangeBalanceChartPath))
            {
                failures.Add("CoinGlass.ExchangeBalanceChartPath 不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.HyperliquidWhaleAlertPath))
            {
                failures.Add("CoinGlass.HyperliquidWhaleAlertPath 不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.HyperliquidWhalePositionPath))
            {
                failures.Add("CoinGlass.HyperliquidWhalePositionPath 不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.HyperliquidPositionPath))
            {
                failures.Add("CoinGlass.HyperliquidPositionPath 不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.HyperliquidUserPositionPath))
            {
                failures.Add("CoinGlass.HyperliquidUserPositionPath 不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.HyperliquidWalletPositionDistributionPath))
            {
                failures.Add("CoinGlass.HyperliquidWalletPositionDistributionPath 不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.HyperliquidWalletPnlDistributionPath))
            {
                failures.Add("CoinGlass.HyperliquidWalletPnlDistributionPath 不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.GrayscaleHoldingsPath))
            {
                failures.Add("CoinGlass.GrayscaleHoldingsPath 不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.CoinUnlockListPath))
            {
                failures.Add("CoinGlass.CoinUnlockListPath 不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.CoinVestingPath))
            {
                failures.Add("CoinGlass.CoinVestingPath 不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.TopLongShortAccountRatioPath))
            {
                failures.Add("CoinGlass.TopLongShortAccountRatioPath 不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.FuturesFootprintPath))
            {
                failures.Add("CoinGlass.FuturesFootprintPath 不能为空");
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

            if (options.TopLongShortAccountRatioSeriesLimit <= 0)
            {
                failures.Add("CoinGlass.TopLongShortAccountRatioSeriesLimit 必须大于 0");
            }

            if (options.FuturesFootprintSeriesLimit <= 0)
            {
                failures.Add("CoinGlass.FuturesFootprintSeriesLimit 必须大于 0");
            }

            if (options.ExchangeAssetsTopCount <= 0)
            {
                failures.Add("CoinGlass.ExchangeAssetsTopCount 必须大于 0");
            }

            if (options.CoinUnlockListTopCount <= 0)
            {
                failures.Add("CoinGlass.CoinUnlockListTopCount 必须大于 0");
            }

            if (options.CoinVestingAllocationLimit <= 0)
            {
                failures.Add("CoinGlass.CoinVestingAllocationLimit 必须大于 0");
            }

            if (options.CoinVestingScheduleLimit <= 0)
            {
                failures.Add("CoinGlass.CoinVestingScheduleLimit 必须大于 0");
            }

            if (options.ExchangeBalanceListTopCount <= 0)
            {
                failures.Add("CoinGlass.ExchangeBalanceListTopCount 必须大于 0");
            }

            if (options.ExchangeBalanceChartSeriesTopCount <= 0)
            {
                failures.Add("CoinGlass.ExchangeBalanceChartSeriesTopCount 必须大于 0");
            }

            if (options.ExchangeBalanceChartPointLimit <= 0)
            {
                failures.Add("CoinGlass.ExchangeBalanceChartPointLimit 必须大于 0");
            }

            if (options.HyperliquidWhaleAlertTopCount <= 0)
            {
                failures.Add("CoinGlass.HyperliquidWhaleAlertTopCount 必须大于 0");
            }

            if (options.HyperliquidWhalePositionTopCount <= 0)
            {
                failures.Add("CoinGlass.HyperliquidWhalePositionTopCount 必须大于 0");
            }

            if (options.HyperliquidPositionTopCount <= 0)
            {
                failures.Add("CoinGlass.HyperliquidPositionTopCount 必须大于 0");
            }

            if (options.HyperliquidWalletPositionDistributionTopCount <= 0)
            {
                failures.Add("CoinGlass.HyperliquidWalletPositionDistributionTopCount 必须大于 0");
            }

            if (options.HyperliquidWalletPnlDistributionTopCount <= 0)
            {
                failures.Add("CoinGlass.HyperliquidWalletPnlDistributionTopCount 必须大于 0");
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

            if (string.IsNullOrWhiteSpace(options.ExchangeAssetsDefaultExchangeName))
            {
                failures.Add("CoinGlass.ExchangeAssetsDefaultExchangeName 不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.CoinVestingDefaultSymbol))
            {
                failures.Add("CoinGlass.CoinVestingDefaultSymbol 不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.ExchangeBalanceListDefaultSymbol))
            {
                failures.Add("CoinGlass.ExchangeBalanceListDefaultSymbol 不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.ExchangeBalanceChartDefaultSymbol))
            {
                failures.Add("CoinGlass.ExchangeBalanceChartDefaultSymbol 不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.HyperliquidPositionDefaultSymbol))
            {
                failures.Add("CoinGlass.HyperliquidPositionDefaultSymbol 不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.HyperliquidUserPositionDefaultUserAddress))
            {
                failures.Add("CoinGlass.HyperliquidUserPositionDefaultUserAddress 不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.TopLongShortAccountRatioDefaultExchange))
            {
                failures.Add("CoinGlass.TopLongShortAccountRatioDefaultExchange 不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.TopLongShortAccountRatioDefaultInterval))
            {
                failures.Add("CoinGlass.TopLongShortAccountRatioDefaultInterval 不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.FuturesFootprintDefaultExchange))
            {
                failures.Add("CoinGlass.FuturesFootprintDefaultExchange 不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.FuturesFootprintDefaultInterval))
            {
                failures.Add("CoinGlass.FuturesFootprintDefaultInterval 不能为空");
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
