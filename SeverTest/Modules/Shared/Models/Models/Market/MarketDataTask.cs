namespace ServerTest.Models
{
    public readonly record struct MarketDataTask(
        string Exchange,
        string Symbol,
        string Timeframe,
        long CandleTimestamp,
        int TimeframeSec,
        bool IsBarClose)
    {
        public MarketDataTask(string exchange, string symbol, string timeframe, long candleTimestamp, bool isBarClose = true)
            : this(
                exchange,
                symbol,
                timeframe,
                candleTimestamp,
                (int)(MarketDataConfig.TimeframeToMs(timeframe) / 1000),
                isBarClose)
        {
        }
    }
}
