// 仓位风险配置
namespace ServerTest.Modules.Positions.Domain
{
    public sealed class PositionRiskConfig
    {
        public string Side { get; init; } = "Long";
        public decimal? ActivationPct { get; init; }
        public decimal? DrawdownPct { get; init; }
    }
}
