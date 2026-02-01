using Microsoft.Extensions.Logging;
using ServerTest.Models;
using ServerTest.Models.Strategy;
using ServerTest.Modules.ExchangeApiKeys.Domain;
using ServerTest.Modules.ExchangeApiKeys.Infrastructure;
using ServerTest.Modules.StrategyEngine.Application;
using ServerTest.Modules.StrategyRuntime.Domain;
using ServerTest.Modules.StrategyRuntime.Infrastructure;

namespace ServerTest.Modules.StrategyRuntime.Application
{
    /// <summary>
    /// 绛栫暐杩愯鏃跺姞杞藉櫒锛岀粺涓€绛栫暐閰嶇疆瑙ｆ瀽涓?API Key 鏍￠獙閫昏緫
    /// </summary>
    public sealed class StrategyRuntimeLoader
    {
        private readonly StrategyRuntimeRepository _runtimeRepository;
        private readonly UserExchangeApiKeyRepository _apiKeyRepository;
        private readonly StrategyJsonLoader _strategyLoader;
        private readonly ILogger<StrategyRuntimeLoader> _logger;

        public StrategyRuntimeLoader(
            StrategyRuntimeRepository runtimeRepository,
            UserExchangeApiKeyRepository apiKeyRepository,
            StrategyJsonLoader strategyLoader,
            ILogger<StrategyRuntimeLoader> logger)
        {
            _runtimeRepository = runtimeRepository ?? throw new ArgumentNullException(nameof(runtimeRepository));
            _apiKeyRepository = apiKeyRepository ?? throw new ArgumentNullException(nameof(apiKeyRepository));
            _strategyLoader = strategyLoader ?? throw new ArgumentNullException(nameof(strategyLoader));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Strategy?> TryLoadAsync(StrategyRuntimeRow row, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(row.ConfigJson))
            {
                _logger.LogWarning("策略 {UsId} 配置为空，跳过加载", row.UsId);
                return null;
            }

            var config = _strategyLoader.ParseConfig(row.ConfigJson);
            if (config == null)
            {
                _logger.LogWarning("策略 {UsId} 配置解析失败，跳过加载", row.UsId);
                return null;
            }

            var normalizedExchange = MarketDataKeyNormalizer.NormalizeExchange(config.Trade?.Exchange ?? string.Empty);
            if (string.IsNullOrWhiteSpace(normalizedExchange))
            {
                _logger.LogWarning("策略 {UsId} 交易所配置无效，跳过加载", row.UsId);
                return null;
            }

            var exchangeApiKeyId = row.ExchangeApiKeyId;
            UserExchangeApiKeyRecord? apiKey = null;
            if (exchangeApiKeyId.HasValue && exchangeApiKeyId.Value > 0)
            {
                apiKey = await _apiKeyRepository.GetByIdAsync(exchangeApiKeyId.Value, row.Uid, ct).ConfigureAwait(false);
                if (apiKey == null)
                {
                    exchangeApiKeyId = null;
                }
            }

            if (!exchangeApiKeyId.HasValue)
            {
                apiKey = await _apiKeyRepository.GetLatestByUidAsync(row.Uid, normalizedExchange, ct).ConfigureAwait(false);
                if (apiKey != null)
                {
                    exchangeApiKeyId = apiKey.Id;
                    await _runtimeRepository.UpdateExchangeApiKeyAsync(row.UsId, row.Uid, exchangeApiKeyId.Value, ct)
                        .ConfigureAwait(false);
                }
            }

            if (!exchangeApiKeyId.HasValue)
            {
                _logger.LogWarning("策略 {UsId} 未绑定交易所 API Key，跳过加载", row.UsId);
                return null;
            }

            apiKey ??= await _apiKeyRepository.GetByIdAsync(exchangeApiKeyId.Value, row.Uid, ct).ConfigureAwait(false);
            if (apiKey == null)
            {
                _logger.LogWarning("策略 {UsId} 绑定的 API Key 无效，跳过加载", row.UsId);
                return null;
            }

            var apiExchange = MarketDataKeyNormalizer.NormalizeExchange(apiKey.ExchangeType);
            if (!string.IsNullOrWhiteSpace(apiExchange)
                && !string.Equals(apiExchange, normalizedExchange, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "策略 {UsId} API Key 交易所不匹配 strategy={StrategyExchange} api={ApiExchange}",
                    row.UsId,
                    normalizedExchange,
                    apiExchange);
                return null;
            }

            row.ExchangeApiKeyId = exchangeApiKeyId;
            var document = StrategyRuntimeDocumentBuilder.BuildDocument(row, config);
            var runtimeStrategy = _strategyLoader.LoadFromDocument(document);
            if (runtimeStrategy == null)
            {
                _logger.LogWarning("策略 {UsId} 实例加载失败", row.UsId);
                return null;
            }

            return runtimeStrategy;
        }
    }
}
