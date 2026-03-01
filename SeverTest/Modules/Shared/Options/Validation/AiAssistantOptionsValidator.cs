using Microsoft.Extensions.Options;

namespace ServerTest.Options
{
    /// <summary>
    /// AI 助手配置校验器。
    /// </summary>
    public sealed class AiAssistantOptionsValidator : IValidateOptions<AiAssistantOptions>
    {
        public ValidateOptionsResult Validate(string? name, AiAssistantOptions options)
        {
            if (options == null)
            {
                return ValidateOptionsResult.Fail("AiAssistant 配置不能为空");
            }

            var failures = new List<string>();

            if (string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                failures.Add("AiAssistant.BaseUrl 不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.Model))
            {
                failures.Add("AiAssistant.Model 不能为空");
            }

            if (options.TimeoutSeconds <= 0)
            {
                failures.Add("AiAssistant.TimeoutSeconds 必须大于 0");
            }

            if (options.MaxHistoryMessages < 0)
            {
                failures.Add("AiAssistant.MaxHistoryMessages 不能小于 0");
            }

            if (options.MaxHistoryMessages > 20)
            {
                failures.Add("AiAssistant.MaxHistoryMessages 建议不超过 20");
            }

            if (options.Temperature < 0 || options.Temperature > 2)
            {
                failures.Add("AiAssistant.Temperature 必须在 0 到 2 之间");
            }

            return failures.Count > 0
                ? ValidateOptionsResult.Fail(failures)
                : ValidateOptionsResult.Success;
        }
    }
}
