using System.Collections.Generic;
using System.Linq;
using ServerTest.Modules.ExchangeApiKeys.Domain;

namespace ServerTest.Modules.StrategyEngine.Application.RunChecks
{
    public sealed class StrategyRunCheckContext
    {
        public long Uid { get; init; }
        public long UsId { get; init; }
        public string Exchange { get; init; } = string.Empty;
        public string Symbol { get; init; } = string.Empty;
        public string PositionMode { get; init; } = string.Empty;
        public int Leverage { get; init; }
        public decimal OrderQty { get; init; }
        public bool RequireBalanceCheck { get; init; }
        public bool RequirePositionModeCheck { get; init; }
        public UserExchangeApiKeyRecord ApiKey { get; init; } = new();
    }

    public sealed class StrategyRunCheckItem
    {
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public bool Passed { get; init; }
        public bool Blocker { get; init; } = true;
        public string Message { get; init; } = string.Empty;
        public Dictionary<string, object>? Detail { get; init; }
    }

    public sealed class StrategyRunCheckResult
    {
        public bool Passed { get; init; }
        public List<StrategyRunCheckItem> Items { get; init; } = new();
        public StrategyRunCheckItem? FirstFailure => Items.FirstOrDefault(item => !item.Passed && item.Blocker);
    }

    public interface IStrategyRunCheck
    {
        Task<StrategyRunCheckItem> CheckAsync(StrategyRunCheckContext context, CancellationToken ct);
    }
}
