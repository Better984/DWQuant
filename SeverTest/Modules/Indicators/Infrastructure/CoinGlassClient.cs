using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Options;
using System.Net;
using System.Text;
using System.Text.Json;

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
            if (!_options.Enabled)
            {
                throw new InvalidOperationException("CoinGlass 未启用，请在配置中将 CoinGlass.Enabled 设为 true");
            }

            if (string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                throw new InvalidOperationException("CoinGlass.ApiKey 为空，无法拉取指标数据");
            }

            var path = string.IsNullOrWhiteSpace(_options.FearGreedPath)
                ? "/api/index/fear-greed-history"
                : _options.FearGreedPath;
            var requestPath = BuildRequestPath(path, limit);

            using var request = new HttpRequestMessage(HttpMethod.Get, requestPath);
            request.Headers.TryAddWithoutValidation("CG-API-KEY", _options.ApiKey);

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "CoinGlass 请求失败: status={Status}, path={Path}, response={Response}",
                    (int)response.StatusCode,
                    requestPath,
                    Truncate(payload, 500));

                throw new HttpRequestException(
                    $"CoinGlass 请求失败，状态码={(int)response.StatusCode}",
                    null,
                    response.StatusCode);
            }

            if (string.IsNullOrWhiteSpace(payload))
            {
                throw new InvalidOperationException("CoinGlass 返回空响应");
            }

            return JsonDocument.Parse(payload);
        }

        private static string BuildRequestPath(string path, int limit)
        {
            if (limit <= 0)
            {
                return path;
            }

            var separator = path.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            return $"{path}{separator}limit={limit}";
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
