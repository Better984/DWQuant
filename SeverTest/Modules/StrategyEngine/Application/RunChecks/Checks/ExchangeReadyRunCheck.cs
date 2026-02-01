using ServerTest.Models;
using ServerTest.Modules.MarketStreaming.Application;
using ServerTest.Modules.StrategyEngine.Application.RunChecks;

namespace ServerTest.Modules.StrategyEngine.Application.RunChecks.Checks
{
    public sealed class ExchangeReadyRunCheck : IStrategyRunCheck
    {
        private readonly MarketDataEngine _marketDataEngine;

        public ExchangeReadyRunCheck(MarketDataEngine marketDataEngine)
        {
            _marketDataEngine = marketDataEngine ?? throw new ArgumentNullException(nameof(marketDataEngine));
        }

        public Task<StrategyRunCheckItem> CheckAsync(StrategyRunCheckContext context, CancellationToken ct)
        {
            var exchangeKey = MarketDataKeyNormalizer.NormalizeExchange(context.Exchange);
            var ready = _marketDataEngine.IsExchangeReady(exchangeKey);
            return Task.FromResult(new StrategyRunCheckItem
            {
                Code = "exchange_ready",
                Name = "交易所行情就绪",
                Passed = ready,
                Blocker = true,
                Message = ready ? "交易所行情已就绪" : "交易所行情未就绪，请稍后重试"
            });
        }
    }
}
