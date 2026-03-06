namespace ServerTest.Modules.TradingExecution.Domain
{
    public static class TradingOrderStatuses
    {
        public const string Pending = "pending";
        public const string Validated = "validated";
        public const string Submitting = "submitting";
        public const string Submitted = "submitted";
        public const string Completed = "completed";
        public const string Rejected = "rejected";
        public const string Failed = "failed";
        public const string RecoveryPending = "recovery_pending";
        public const string Recovered = "recovered";
    }

    public static class TradingOrderEventTypes
    {
        public const string Created = "created";
        public const string Validated = "validated";
        public const string Rejected = "rejected";
        public const string Submitting = "submitting";
        public const string Submitted = "submitted";
        public const string Completed = "completed";
        public const string Failed = "failed";
        public const string RecoveryQueued = "recovery_queued";
        public const string Recovered = "recovered";
        public const string Skipped = "skipped";
    }

    public sealed class TradingOrderRequestEntity
    {
        public long RequestId { get; set; }
        public string TargetId { get; set; } = string.Empty;
        public string StrategyUid { get; set; } = string.Empty;
        public long? Uid { get; set; }
        public long? UsId { get; set; }
        public long? StrategyVersionId { get; set; }
        public int? StrategyVersionNo { get; set; }
        public long? ExchangeApiKeyId { get; set; }
        public string Exchange { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public string TargetType { get; set; } = string.Empty;
        public string PositionSide { get; set; } = string.Empty;
        public string OrderSide { get; set; } = string.Empty;
        public bool ReduceOnly { get; set; }
        public bool IsTesting { get; set; }
        public string Stage { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
        public decimal RequestedQty { get; set; }
        public decimal? NormalizedQty { get; set; }
        public decimal MaxPositionQty { get; set; }
        public int Leverage { get; set; }
        public DateTime SignalTimeUtc { get; set; } = DateTime.UtcNow;
        public string TriggerResultsJson { get; set; } = "[]";
        public string RiskChecksJson { get; set; } = "[]";
        public string LatestStatus { get; set; } = TradingOrderStatuses.Pending;
        public string? StatusMessage { get; set; }
        public string? ExchangeOrderId { get; set; }
        public decimal? AveragePrice { get; set; }
        public long? RecoveryTaskId { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
    }

    public sealed class TradingOrderEventEntity
    {
        public long EventId { get; set; }
        public long RequestId { get; set; }
        public string EventType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string DetailJson { get; set; } = "{}";
        public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    }
}
