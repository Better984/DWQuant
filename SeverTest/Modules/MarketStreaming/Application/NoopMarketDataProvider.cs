using ccxt;
using ServerTest.Models;

namespace ServerTest.Modules.MarketStreaming.Application
{
    /// <summary>
    /// 兜底行情提供器：在回测算力节点未启用实时行情引擎时返回空结果。
    /// </summary>
    public sealed class NoopMarketDataProvider : IMarketDataProvider
    {
        public List<OHLCV> GetHistoryKlines(string exchangeId, string timeframe, string symbol, long? endTimestamp, int count)
        {
            return new List<OHLCV>();
        }
    }
}
