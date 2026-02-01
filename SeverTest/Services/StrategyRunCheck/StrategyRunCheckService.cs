using System.Collections.Generic;
using System.Linq;

namespace ServerTest.Services.StrategyRunCheck
{
    public sealed class StrategyRunCheckService
    {
        private readonly IReadOnlyList<IStrategyRunCheck> _checks;

        public StrategyRunCheckService(IEnumerable<IStrategyRunCheck> checks)
        {
            _checks = checks?.ToList() ?? throw new ArgumentNullException(nameof(checks));
        }

        public async Task<StrategyRunCheckResult> RunAsync(StrategyRunCheckContext context, CancellationToken ct)
        {
            var items = new List<StrategyRunCheckItem>();
            foreach (var check in _checks)
            {
                var item = await check.CheckAsync(context, ct).ConfigureAwait(false);
                items.Add(item);
                if (!item.Passed && item.Blocker)
                {
                    break;
                }
            }

            var passed = items.All(item => item.Passed || !item.Blocker);
            return new StrategyRunCheckResult
            {
                Passed = passed,
                Items = items
            };
        }
    }
}
