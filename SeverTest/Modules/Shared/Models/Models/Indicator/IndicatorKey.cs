namespace ServerTest.Models.Indicator
{
    public readonly record struct IndicatorKey(
        string Exchange,
        string Symbol,
        string Timeframe,
        string Indicator,
        string Input,
        string Output,
        string CalcMode,
        string ParamsKey)
    {
        public int TimeframeSec => (int)(MarketDataConfig.TimeframeToMs(Timeframe) / 1000);

        public override string ToString()
        {
            return $"{Exchange}|{Symbol}|{Timeframe}|{Indicator}|{Input}|{Output}|{CalcMode}|{ParamsKey}";
        }
    }
}
