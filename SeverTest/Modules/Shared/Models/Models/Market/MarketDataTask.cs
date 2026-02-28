namespace ServerTest.Models
{
    public readonly record struct MarketDataTask(
        string Exchange,
        string Symbol,
        string Timeframe,
        long CandleTimestamp,
        int TimeframeSec,
        bool IsBarClose,
        string TraceId)
    {
        public MarketDataTask(
            string exchange,
            string symbol,
            string timeframe,
            long candleTimestamp,
            int timeframeSec,
            bool isBarClose)
            : this(
                exchange,
                symbol,
                timeframe,
                candleTimestamp,
                timeframeSec,
                isBarClose,
                CreateTraceId())
        {
        }

        public MarketDataTask(string exchange, string symbol, string timeframe, long candleTimestamp, bool isBarClose = true)
            : this(
                exchange,
                symbol,
                timeframe,
                candleTimestamp,
                (int)(MarketDataConfig.TimeframeToMs(timeframe) / 1000),
                isBarClose,
                CreateTraceId())
        {
        }

        public MarketDataTask(
            string exchange,
            string symbol,
            string timeframe,
            long candleTimestamp,
            bool isBarClose,
            string traceId)
            : this(
                exchange,
                symbol,
                timeframe,
                candleTimestamp,
                (int)(MarketDataConfig.TimeframeToMs(timeframe) / 1000),
                isBarClose,
                NormalizeTraceId(traceId))
        {
        }

        public static string NormalizeTraceId(string? traceId)
        {
            return string.IsNullOrWhiteSpace(traceId)
                ? CreateTraceId()
                : traceId.Trim();
        }

        public static string CreateTraceId()
        {
            return Guid.NewGuid().ToString("N");
        }
    }
}
