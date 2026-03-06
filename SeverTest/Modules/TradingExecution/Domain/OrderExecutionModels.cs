namespace ServerTest.Modules.TradingExecution.Domain
{
    public sealed class OrderExecutionRequest
    {
        public long? OrderRequestId { get; init; }
        public string TargetId { get; init; } = string.Empty;
        public long Uid { get; init; }
        public long? ExchangeApiKeyId { get; init; }
        public string Exchange { get; init; } = string.Empty;
        public string Symbol { get; init; } = string.Empty;
        public string Side { get; init; } = string.Empty; // 买/卖
        public string PositionSide { get; init; } = string.Empty;
        public string TargetType { get; init; } = string.Empty;
        public decimal Qty { get; init; }
        public bool ReduceOnly { get; init; }
    }

    public sealed class OrderExecutionResult
    {
        public bool Success { get; init; }
        public string? ExchangeOrderId { get; init; }
        public decimal? AveragePrice { get; init; }
        public decimal? ExecutedQty { get; init; }
        public string? ErrorMessage { get; init; }
    }

    public interface IOrderExecutor
    {
        Task<OrderExecutionResult> PlaceMarketOrderAsync(OrderExecutionRequest request, CancellationToken ct);
    }
}
