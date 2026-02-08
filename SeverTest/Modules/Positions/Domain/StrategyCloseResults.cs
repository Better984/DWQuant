using System.Collections.Generic;

// 策略平仓结果相关模型
namespace ServerTest.Modules.Positions.Domain
{
    public sealed class StrategyClosePositionsResult
    {
        public int TotalPositions { get; set; }
        public int ClosedPositions { get; set; }
        public List<StrategyCloseGroupResult> FailedGroups { get; set; } = new();
    }

    public sealed class StrategyCloseGroupResult
    {
        public string Exchange { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public string Side { get; set; } = string.Empty;
        public long? ExchangeApiKeyId { get; set; }
        public decimal Qty { get; set; }
        public string Error { get; set; } = string.Empty;
    }

    public sealed class PositionCloseResult
    {
        public long PositionId { get; set; }
        public bool Success { get; set; }
        public string? Error { get; set; }
    }
}
