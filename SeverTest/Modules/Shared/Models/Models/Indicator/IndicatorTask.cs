namespace ServerTest.Models.Indicator
{
    public sealed class IndicatorTask
    {
        private static readonly IReadOnlyList<IndicatorRequest> EmptyRequests = Array.Empty<IndicatorRequest>();

        internal IndicatorTask()
        {
            MarketTask = default;
            Requests = EmptyRequests;
        }

        public IndicatorTask(MarketDataTask marketTask, IReadOnlyList<IndicatorRequest> requests)
        {
            Reset(marketTask, requests);
        }

        public MarketDataTask MarketTask { get; private set; } = default;
        public IReadOnlyList<IndicatorRequest> Requests { get; private set; } = EmptyRequests;

        /// <summary>
        /// 重置指标任务上下文，供回测对象池复用。
        /// </summary>
        internal void Reset(MarketDataTask marketTask, IReadOnlyList<IndicatorRequest>? requests)
        {
            MarketTask = marketTask;
            Requests = requests ?? EmptyRequests;
        }
    }
}
