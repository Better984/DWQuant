using Microsoft.Extensions.Options;

namespace ServerTest.Options
{
    public sealed class LiveTradingWorkerOptionsValidator : IValidateOptions<LiveTradingWorkerOptions>
    {
        public ValidateOptionsResult Validate(string? name, LiveTradingWorkerOptions options)
        {
            if (options == null)
            {
                return ValidateOptionsResult.Fail("LiveTradingWorker 配置不能为空");
            }

            var failures = new List<string>();
            if (options.ReconnectDelaySeconds <= 0)
            {
                failures.Add("LiveTradingWorker.ReconnectDelaySeconds 必须大于 0");
            }

            if (options.HeartbeatSeconds <= 0)
            {
                failures.Add("LiveTradingWorker.HeartbeatSeconds 必须大于 0");
            }

            return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
        }
    }
}
