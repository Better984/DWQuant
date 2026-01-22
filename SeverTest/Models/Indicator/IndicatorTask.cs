namespace ServerTest.Models.Indicator
{
    public sealed class IndicatorTask
    {
        public IndicatorTask(MarketDataTask marketTask, IReadOnlyList<IndicatorRequest> requests)
        {
            MarketTask = marketTask;
            Requests = requests;
        }

        public MarketDataTask MarketTask { get; }
        public IReadOnlyList<IndicatorRequest> Requests { get; }
    }
}
