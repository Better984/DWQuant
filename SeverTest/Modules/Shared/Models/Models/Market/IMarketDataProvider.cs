using ccxt;
using System.Collections.Generic;

namespace ServerTest.Models
{
    /// <summary>
    /// 行情数据提供接口（用于指标/条件统一取数）
    /// </summary>
    public interface IMarketDataProvider
    {
        /// <summary>
        /// 获取历史K线：返回最新的 count 根K线（按时间升序）
        /// </summary>
        List<OHLCV> GetHistoryKlines(
            string exchangeId,
            string timeframe,
            string symbol,
            long? endTimestamp,
            int count);
    }
}
