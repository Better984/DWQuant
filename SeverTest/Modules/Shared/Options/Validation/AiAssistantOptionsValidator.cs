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

            if (string.IsNullOrWhiteSpace(options.Token))
            {
                failures.Add("AiAssistant.Token 不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.AssistantId))
            {
                failures.Add("AiAssistant.AssistantId 不能为空");
            }

            if (string.IsNullOrWhiteSpace(options.ChatType))
            {
                failures.Add("AiAssistant.ChatType 不能为空");
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

            if (!string.Equals(options.ChatType, "published", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(options.ChatType, "preview", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add("AiAssistant.ChatType 仅支持 published 或 preview");
            }

            return failures.Count > 0
                ? ValidateOptionsResult.Fail(failures)
                : ValidateOptionsResult.Success;
        }
    }
}
