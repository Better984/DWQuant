using System;
using System.Collections.Generic;

namespace ServerTest.Modules.Positions.Application
{
    public sealed class PositionRiskIndexSnapshot
    {
        public string Exchange { get; init; } = string.Empty;
        public string Symbol { get; init; } = string.Empty;
        public int TotalPositions { get; init; }
        public DateTime GeneratedAt { get; init; }
        public List<RiskPositionSnapshot> Positions { get; init; } = new();
        public List<IndexTreeSnapshot> IndexTrees { get; init; } = new();
    }

    public sealed class RiskPositionSnapshot
    {
        public long PositionId { get; init; }
        public long Uid { get; init; }
        public long UsId { get; init; }
        public long? ExchangeApiKeyId { get; init; }
        public string Exchange { get; init; } = string.Empty;
        public string Symbol { get; init; } = string.Empty;
        public string Side { get; init; } = string.Empty;
        public decimal EntryPrice { get; init; }
        public decimal Qty { get; init; }
        public decimal? StopLossPrice { get; init; }
        public decimal? TakeProfitPrice { get; init; }
        public bool TrailingEnabled { get; init; }
        public decimal? TrailingStopPrice { get; init; }
        public bool TrailingTriggered { get; init; }
        public decimal? TrailingActivationPct { get; init; }
        public decimal? TrailingDrawdownPct { get; init; }
        public decimal? TrailingActivationPrice { get; init; }
        public decimal? TrailingUpdateThresholdPrice { get; init; }
        public string Status { get; init; } = string.Empty;
    }

    public sealed class IndexTreeSnapshot
    {
        public string IndexType { get; init; } = string.Empty;
        public List<ScaleTreeSnapshot> Scales { get; init; } = new();
        public int Count { get; set; }
    }

    public sealed class ScaleTreeSnapshot
    {
        public int Scale { get; init; }
        public decimal Step1 { get; init; }
        public decimal Step2 { get; init; }
        public decimal Step3 { get; init; }
        public List<Level1TreeSnapshot> Level1 { get; init; } = new();
        public int Count { get; set; }
    }

    public sealed class Level1TreeSnapshot
    {
        public long Key { get; init; }
        public decimal Low { get; init; }
        public decimal High { get; init; }
        public List<Level2TreeSnapshot> Level2 { get; init; } = new();
        public int Count { get; set; }
    }

    public sealed class Level2TreeSnapshot
    {
        public long Key { get; init; }
        public decimal Low { get; init; }
        public decimal High { get; init; }
        public List<Level3TreeSnapshot> Level3 { get; init; } = new();
        public int Count { get; set; }
    }

    public sealed class Level3TreeSnapshot
    {
        public long Key { get; init; }
        public decimal Low { get; init; }
        public decimal High { get; init; }
        public List<long> PositionIds { get; init; } = new();
        public int Count { get; set; }
    }
}
