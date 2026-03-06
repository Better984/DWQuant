using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Options;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace ServerTest.Modules.Indicators.Infrastructure
{
    /// <summary>
    /// CoinGlass HTTP 客户端（仅封装鉴权与请求）。
    /// </summary>
    public sealed class CoinGlassClient
    {
        private readonly HttpClient _httpClient;
        private readonly CoinGlassOptions _options;
        private readonly ILogger<CoinGlassClient> _logger;
        private int _piratedSourceWarned;

        public CoinGlassClient(
            HttpClient httpClient,
            IOptions<CoinGlassOptions> options,
            ILogger<CoinGlassClient> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _options = options?.Value ?? new CoinGlassOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<JsonDocument> GetFearGreedHistoryAsync(int limit, CancellationToken ct)
        {
            var path = string.IsNullOrWhiteSpace(_options.FearGreedPath)
                ? "/api/index/fear-greed-history"
                : _options.FearGreedPath;

            Dictionary<string, string?>? query = null;
            if (limit > 0)
            {
                query = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["limit"] = limit.ToString(CultureInfo.InvariantCulture)
                };
            }

            return await GetJsonAsync(path, query, ct, "贪婪恐慌").ConfigureAwait(false);
        }

        public async Task<JsonDocument> GetEtfFlowHistoryAsync(string asset, int limit, CancellationToken ct)
        {
            var normalizedAsset = NormalizeEtfAsset(asset);
            var assetSlug = ResolveEtfAssetSlug(normalizedAsset);
            var path = ResolveEtfFlowPath(assetSlug);

            Dictionary<string, string?>? query = null;
            if (limit > 0)
            {
                query = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["limit"] = limit.ToString(CultureInfo.InvariantCulture)
                };
            }

            return await GetJsonAsync(path, query, ct, $"ETF净流入-{normalizedAsset}").ConfigureAwait(false);
        }

        public Task<JsonDocument> GetTopLongShortAccountRatioHistoryAsync(
            string exchange,
            string symbol,
            string interval,
            int limit,
            long? startTime,
            long? endTime,
            CancellationToken ct)
        {
            var path = string.IsNullOrWhiteSpace(_options.TopLongShortAccountRatioPath)
                ? "/api/futures/top-long-short-account-ratio/history"
                : _options.TopLongShortAccountRatioPath;

            var query = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["exchange"] = exchange,
                ["symbol"] = symbol,
                ["interval"] = interval
            };

            if (limit > 0)
            {
                query["limit"] = limit.ToString(CultureInfo.InvariantCulture);
            }

            if (startTime.HasValue && startTime.Value > 0)
            {
                query["start_time"] = startTime.Value.ToString(CultureInfo.InvariantCulture);
            }

            if (endTime.HasValue && endTime.Value > 0)
            {
                query["end_time"] = endTime.Value.ToString(CultureInfo.InvariantCulture);
            }

            return GetJsonAsync(path, query, ct, $"大户账户数多空比-{symbol}-{interval}");
        }

        public Task<JsonDocument> GetFuturesFootprintHistoryAsync(
            string exchange,
            string symbol,
            string interval,
            int limit,
            long? startTime,
            long? endTime,
            CancellationToken ct)
        {
            var path = string.IsNullOrWhiteSpace(_options.FuturesFootprintPath)
                ? "/api/futures/volume/footprint-history"
                : _options.FuturesFootprintPath;

            var query = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["exchange"] = exchange,
                ["symbol"] = symbol,
                ["interval"] = interval
            };

            if (limit > 0)
            {
                query["limit"] = limit.ToString(CultureInfo.InvariantCulture);
            }

            if (startTime.HasValue && startTime.Value > 0)
            {
                query["start_time"] = startTime.Value.ToString(CultureInfo.InvariantCulture);
            }

            if (endTime.HasValue && endTime.Value > 0)
            {
                query["end_time"] = endTime.Value.ToString(CultureInfo.InvariantCulture);
            }

            return GetJsonAsync(path, query, ct, $"合约足迹图-{symbol}-{interval}");
        }

        public Task<JsonDocument> GetGrayscaleHoldingsAsync(CancellationToken ct)
        {
            var path = string.IsNullOrWhiteSpace(_options.GrayscaleHoldingsPath)
                ? "/api/grayscale/holdings-list"
                : _options.GrayscaleHoldingsPath;

            return GetJsonAsync(path, null, ct, "灰度持仓");
        }

        public Task<JsonDocument> GetCoinUnlockListAsync(CancellationToken ct)
        {
            var path = string.IsNullOrWhiteSpace(_options.CoinUnlockListPath)
                ? "/api/coin/unlock-list"
                : _options.CoinUnlockListPath;

            return GetJsonAsync(path, null, ct, "代币解锁列表");
        }

        public Task<JsonDocument> GetCoinVestingAsync(string symbol, CancellationToken ct)
        {
            var normalizedSymbol = NormalizeCoinSymbol(symbol);
            var path = string.IsNullOrWhiteSpace(_options.CoinVestingPath)
                ? "/api/coin/vesting"
                : _options.CoinVestingPath;

            var query = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["symbol"] = normalizedSymbol
            };

            return GetJsonAsync(path, query, ct, $"代币解锁详情-{normalizedSymbol}");
        }

        public Task<JsonDocument> GetLiquidationHeatmapModel1Async(
            string exchange,
            string symbol,
            string range,
            CancellationToken ct)
        {
            var path = string.IsNullOrWhiteSpace(_options.LiquidationHeatmapModel1Path)
                ? "/api/futures/liquidation/heatmap/model1"
                : _options.LiquidationHeatmapModel1Path;

            var query = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["exchange"] = exchange,
                ["symbol"] = symbol,
                ["range"] = range
            };

            return GetJsonAsync(path, query, ct, "爆仓热力图");
        }

        public Task<JsonDocument> GetExchangeAssetsAsync(string exchangeName, CancellationToken ct)
        {
            var path = string.IsNullOrWhiteSpace(_options.ExchangeAssetsPath)
                ? "/api/exchange/assets"
                : _options.ExchangeAssetsPath;

            var query = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["exchangeName"] = exchangeName
            };

            return GetJsonAsync(path, query, ct, "交易所资产明细");
        }

        public Task<JsonDocument> GetExchangeBalanceListAsync(string symbol, CancellationToken ct)
        {
            var path = string.IsNullOrWhiteSpace(_options.ExchangeBalanceListPath)
                ? "/api/exchange/balance/list"
                : _options.ExchangeBalanceListPath;

            var query = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["symbol"] = symbol
            };

            return GetJsonAsync(path, query, ct, "交易所余额排行");
        }

        public Task<JsonDocument> GetExchangeBalanceChartAsync(string symbol, CancellationToken ct)
        {
            var path = string.IsNullOrWhiteSpace(_options.ExchangeBalanceChartPath)
                ? "/api/exchange/balance/chart"
                : _options.ExchangeBalanceChartPath;

            var query = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["symbol"] = symbol
            };

            return GetJsonAsync(path, query, ct, "交易所余额趋势");
        }

        public Task<JsonDocument> GetHyperliquidWhaleAlertAsync(CancellationToken ct)
        {
            var path = string.IsNullOrWhiteSpace(_options.HyperliquidWhaleAlertPath)
                ? "/api/hyperliquid/whale-alert"
                : _options.HyperliquidWhaleAlertPath;

            return GetJsonAsync(path, null, ct, "Hyperliquid 鲸鱼提醒");
        }

        public Task<JsonDocument> GetHyperliquidWhalePositionAsync(CancellationToken ct)
        {
            var path = string.IsNullOrWhiteSpace(_options.HyperliquidWhalePositionPath)
                ? "/api/hyperliquid/whale-position"
                : _options.HyperliquidWhalePositionPath;

            return GetJsonAsync(path, null, ct, "Hyperliquid 鲸鱼持仓");
        }

        public Task<JsonDocument> GetHyperliquidPositionAsync(string symbol, CancellationToken ct)
        {
            var path = string.IsNullOrWhiteSpace(_options.HyperliquidPositionPath)
                ? "/api/hyperliquid/position"
                : _options.HyperliquidPositionPath;

            var query = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["symbol"] = symbol
            };

            return GetJsonAsync(path, query, ct, $"Hyperliquid 持仓排行-{symbol}");
        }

        public Task<JsonDocument> GetHyperliquidUserPositionAsync(string userAddress, CancellationToken ct)
        {
            var path = string.IsNullOrWhiteSpace(_options.HyperliquidUserPositionPath)
                ? "/api/hyperliquid/user-position"
                : _options.HyperliquidUserPositionPath;

            var query = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["user_address"] = userAddress
            };

            return GetJsonAsync(path, query, ct, "Hyperliquid 用户持仓");
        }

        public Task<JsonDocument> GetHyperliquidWalletPositionDistributionAsync(CancellationToken ct)
        {
            var path = string.IsNullOrWhiteSpace(_options.HyperliquidWalletPositionDistributionPath)
                ? "/api/hyperliquid/wallet/position-distribution"
                : _options.HyperliquidWalletPositionDistributionPath;

            return GetJsonAsync(path, null, ct, "Hyperliquid 钱包持仓分布");
        }

        public Task<JsonDocument> GetHyperliquidWalletPnlDistributionAsync(CancellationToken ct)
        {
            var path = string.IsNullOrWhiteSpace(_options.HyperliquidWalletPnlDistributionPath)
                ? "/api/hyperliquid/wallet/pnl-distribution"
                : _options.HyperliquidWalletPnlDistributionPath;

            return GetJsonAsync(path, null, ct, "Hyperliquid 钱包盈亏分布");
        }

        /// <summary>
        /// 通用 CoinGlass GET JSON 拉取（开发阶段可对接聚合商代理）。
        /// <param name="module">调用模块标识，用于日志区分（如：贪婪恐慌、ETF净流入、爆仓热力图、Discover-Feed、Discover-日历）。</param>
        /// </summary>
        public async Task<JsonDocument> GetJsonAsync(
            string path,
            IReadOnlyDictionary<string, string?>? query,
            CancellationToken ct,
            string? module = null,
            bool quietFailure = false)
        {
            EnsureReady();
            WarnIfUsingPiratedSource();

            var requestPath = BuildRequestPath(path, query);
            var apiKeyHeaderName = string.IsNullOrWhiteSpace(_options.ApiKeyHeaderName)
                ? "CG-API-KEY"
                : _options.ApiKeyHeaderName.Trim();

            using var request = new HttpRequestMessage(HttpMethod.Get, requestPath);
            request.Headers.TryAddWithoutValidation(apiKeyHeaderName, _options.ApiKey);

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            var logTag = string.IsNullOrWhiteSpace(module) ? "[coinglass]" : $"[coinglass][{module}]";

            if (!response.IsSuccessStatusCode)
            {
                if (quietFailure)
                {
                    _logger.LogDebug(
                        "{Tag} 请求失败: status={Status}, path={Path}, response={Response}",
                        logTag,
                        (int)response.StatusCode,
                        requestPath,
                        Truncate(payload, 500));
                }
                else
                {
                    _logger.LogWarning(
                        "{Tag} 请求失败: status={Status}, path={Path}, response={Response}",
                        logTag,
                        (int)response.StatusCode,
                        requestPath,
                        Truncate(payload, 500));
                }

                throw new HttpRequestException(
                    $"CoinGlass 请求失败，状态码={(int)response.StatusCode}",
                    null,
                    response.StatusCode);
            }

            if (string.IsNullOrWhiteSpace(payload))
            {
                throw new InvalidOperationException("CoinGlass 返回空响应");
            }

            var doc = JsonDocument.Parse(payload);
            var rootKind = doc.RootElement.ValueKind.ToString();
            _logger.LogInformation(
                "{Tag} 请求成功: path={Path}, 响应长度={Length}, 根类型={RootKind}",
                logTag,
                requestPath,
                payload.Length,
                rootKind);
            _logger.LogInformation(
                "{Tag} 响应原文: path={Path}, content={Content}",
                logTag,
                requestPath,
                Truncate(payload, 2000));
            return doc;
        }

        private void EnsureReady()
        {
            if (!_options.Enabled)
            {
                throw new InvalidOperationException("CoinGlass 未启用，请在配置中将 CoinGlass.Enabled 设为 true");
            }

            if (string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                throw new InvalidOperationException("CoinGlass.ApiKey 为空，无法拉取指标数据");
            }
        }

        private string BuildRequestPath(string path, IReadOnlyDictionary<string, string?>? query)
        {
            var normalizedPath = NormalizePath(path);
            var routePrefix = (_options.RoutePrefix ?? string.Empty).Trim().Trim('/');
            if (!string.IsNullOrWhiteSpace(routePrefix) &&
                !normalizedPath.StartsWith(routePrefix + "/", StringComparison.OrdinalIgnoreCase))
            {
                normalizedPath = $"{routePrefix}/{normalizedPath}";
            }

            if (query == null || query.Count == 0)
            {
                return normalizedPath;
            }

            var builder = new StringBuilder(normalizedPath);
            var separator = normalizedPath.Contains('?', StringComparison.Ordinal) ? '&' : '?';
            foreach (var entry in query)
            {
                if (string.IsNullOrWhiteSpace(entry.Key) || string.IsNullOrWhiteSpace(entry.Value))
                {
                    continue;
                }

                builder.Append(separator);
                builder.Append(Uri.EscapeDataString(entry.Key));
                builder.Append('=');
                builder.Append(Uri.EscapeDataString(entry.Value));
                separator = '&';
            }

            return builder.ToString();
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "api/index/fear-greed-history";
            }

            return path.Trim().TrimStart('/');
        }

        private string ResolveEtfFlowPath(string assetSlug)
        {
            if (!string.IsNullOrWhiteSpace(_options.EtfFlowPathTemplate))
            {
                return _options.EtfFlowPathTemplate.Replace("{asset}", assetSlug, StringComparison.OrdinalIgnoreCase);
            }

            var basePath = string.IsNullOrWhiteSpace(_options.EtfFlowPath)
                ? "/api/etf/bitcoin/flow-history"
                : _options.EtfFlowPath;

            if (string.Equals(assetSlug, "bitcoin", StringComparison.OrdinalIgnoreCase))
            {
                return basePath;
            }

            if (TryReplaceEtfAssetSegment(basePath, assetSlug, out var replacedPath))
            {
                return replacedPath;
            }

            return $"/api/etf/{assetSlug}/flow-history";
        }

        private static bool TryReplaceEtfAssetSegment(string path, string assetSlug, out string replacedPath)
        {
            replacedPath = string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var normalizedPath = path.Trim();
            var hasLeadingSlash = normalizedPath.StartsWith("/", StringComparison.Ordinal);
            var segments = normalizedPath
                .Trim('/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length == 0)
            {
                return false;
            }

            for (var index = 0; index < segments.Length - 1; index++)
            {
                if (!string.Equals(segments[index], "etf", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                segments[index + 1] = assetSlug;
                replacedPath = (hasLeadingSlash ? "/" : string.Empty) + string.Join('/', segments);
                return true;
            }

            return false;
        }

        private static string NormalizeEtfAsset(string? asset)
        {
            return (asset ?? string.Empty).Trim().ToUpperInvariant() switch
            {
                "" => "BTC",
                "BTC" or "BITCOIN" => "BTC",
                "ETH" or "ETHEREUM" => "ETH",
                "SOL" or "SOLANA" => "SOL",
                "XRP" => "XRP",
                var unsupported => throw new ArgumentOutOfRangeException(nameof(asset), unsupported, "暂不支持的 ETF 资产")
            };
        }

        private static string ResolveEtfAssetSlug(string asset)
        {
            return asset switch
            {
                "BTC" => "bitcoin",
                "ETH" => "ethereum",
                "SOL" => "solana",
                "XRP" => "xrp",
                _ => throw new ArgumentOutOfRangeException(nameof(asset), asset, "暂不支持的 ETF 资产")
            };
        }

        private static string NormalizeCoinSymbol(string? symbol)
        {
            var normalized = (symbol ?? string.Empty).Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new ArgumentException("symbol 不能为空", nameof(symbol));
            }

            return normalized;
        }

        private void WarnIfUsingPiratedSource()
        {
            if (!string.Equals(_options.SourceMode, "pirated_proxy", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // 只打印一次，避免高频请求刷屏。
            if (Interlocked.Exchange(ref _piratedSourceWarned, 1) == 1)
            {
                return;
            }

            _logger.LogWarning(
                "[coinglass][通用] 当前使用第三方非官方聚合商（盗版源）数据，仅用于开发联调；正式上线前必须切换官方数据源。");
        }

        private static string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            {
                return text;
            }

            return text[..maxLength] + "...";
        }
    }
}
