namespace ServerTest.Modules.TradingExecution.Domain
{
    /// <summary>
    /// 交易恢复任务类型定义。
    /// </summary>
    public static class TradeRecoveryTaskTypes
    {
        public const string CloseWrite = "close_write";
        public const string OpenCompensation = "open_compensation";
    }

    /// <summary>
    /// 交易恢复任务状态定义。
    /// </summary>
    public static class TradeRecoveryTaskStatuses
    {
        public const string Pending = "pending";
        public const string Processing = "processing";
        public const string Succeeded = "succeeded";
        public const string Failed = "failed";
    }

    /// <summary>
    /// 交易恢复任务持久化实体。
    /// </summary>
    public sealed class TradeRecoveryTaskEntity
    {
        public long TaskId { get; set; }
        public string TaskType { get; set; } = string.Empty;
        public long? Uid { get; set; }
        public long? UsId { get; set; }
        public long? OrderRequestId { get; set; }
        public long PositionId { get; set; }
        public long? ExchangeApiKeyId { get; set; }
        public string Exchange { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public string Side { get; set; } = string.Empty;
        public decimal Qty { get; set; }
        public decimal? ClosePrice { get; set; }
        public DateTime? ClosedAtUtc { get; set; }
        public int Attempt { get; set; }
        public int MaxAttempts { get; set; }
        public string Status { get; set; } = TradeRecoveryTaskStatuses.Pending;
        public DateTime NextRetryAtUtc { get; set; } = DateTime.UtcNow;
        public string? ProcessingToken { get; set; }
        public DateTime? ProcessingAtUtc { get; set; }
        public string? LastError { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
    }
}
