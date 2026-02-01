using ServerTest.Modules.StrategyEngine.Application.RunChecks;

namespace ServerTest.Modules.StrategyEngine.Application.RunChecks.Checks
{
    public sealed class ApiKeyRunCheck : IStrategyRunCheck
    {
        public Task<StrategyRunCheckItem> CheckAsync(StrategyRunCheckContext context, CancellationToken ct)
        {
            var passed = context.ApiKey != null && context.ApiKey.Id > 0;
            return Task.FromResult(new StrategyRunCheckItem
            {
                Code = "api_key",
                Name = "交易所API校验",
                Passed = passed,
                Blocker = true,
                Message = passed ? "已绑定交易所API" : "未绑定交易所API"
            });
        }
    }
}
