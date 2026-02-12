using Microsoft.Extensions.Options;
using ServerTest.Options;

namespace ServerTest.Options
{
    public sealed class BacktestWorkerOptionsValidator : IValidateOptions<BacktestWorkerOptions>
    {
        public ValidateOptionsResult Validate(string? name, BacktestWorkerOptions options)
        {
            if (options == null)
            {
                return ValidateOptionsResult.Fail("BacktestWorker 配置不能为空");
            }

            var failures = new List<string>();
            if (options.ReconnectDelaySeconds <= 0)
            {
                failures.Add("BacktestWorker.ReconnectDelaySeconds 必须大于 0");
            }

            if (options.DispatchPollingIntervalMs <= 0)
            {
                failures.Add("BacktestWorker.DispatchPollingIntervalMs 必须大于 0");
            }

            if (options.HeartbeatSeconds <= 0)
            {
                failures.Add("BacktestWorker.HeartbeatSeconds 必须大于 0");
            }

            if (options.MaxParallelTasksPerWorker <= 0)
            {
                failures.Add("BacktestWorker.MaxParallelTasksPerWorker 必须大于 0");
            }

            return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
        }
    }
}
