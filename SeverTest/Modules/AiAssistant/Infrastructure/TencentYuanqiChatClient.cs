using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.AiAssistant.Domain;
using ServerTest.Options;

namespace ServerTest.Modules.AiAssistant.Infrastructure
{
    /// <summary>
    /// 腾讯元器智能体对话客户端。
    /// </summary>
    public sealed class TencentYuanqiChatClient
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpClient _httpClient;
        private readonly AiAssistantOptions _options;
        private readonly ILogger<TencentYuanqiChatClient> _logger;

        public TencentYuanqiChatClient(
            HttpClient httpClient,
            IOptions<AiAssistantOptions> options,
            ILogger<TencentYuanqiChatClient> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _options = options?.Value ?? new AiAssistantOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<string> CreateChatCompletionAsync(
            long uid,
            IReadOnlyList<AiAssistantPromptMessage> messages,
            CancellationToken ct)
        {
            if (!_options.Enabled)
            {
                throw new InvalidOperationException("AI 助手未启用，请在配置中将 AiAssistant.Enabled 设为 true");
            }

            if (string.IsNullOrWhiteSpace(_options.Token))
            {
                throw new InvalidOperationException("AI 助手未配置腾讯元器 Token，请先在 AiAssistant.Token 中填写");
            }

            if (string.IsNullOrWhiteSpace(_options.AssistantId))
            {
                throw new InvalidOperationException("AI 助手未配置腾讯元器智能体 ID，请先在 AiAssistant.AssistantId 中填写");
            }

            if (messages == null || messages.Count == 0)
            {
                throw new InvalidOperationException("AI 请求消息不能为空");
            }

            var request = new TencentYuanqiChatRequest
            {
                AssistantId = _options.AssistantId.Trim(),
                UserId = uid.ToString(CultureInfo.InvariantCulture),
                Stream = false,
                ChatType = NormalizeChatType(_options.ChatType),
                Messages = messages
                    .Select(MapMessage)
                    .ToList()
            };

            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, "openapi/v1/agent/chat/completions");
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.Token.Trim());
            requestMessage.Content = new StringContent(
                JsonSerializer.Serialize(request, JsonOptions),
                Encoding.UTF8,
                "application/json");

            using var response = await _httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "腾讯元器请求失败: status={Status}, body={Body}",
                    (int)response.StatusCode,
                    Truncate(payload, 1000));
                throw new InvalidOperationException(BuildHttpFailureMessage((int)response.StatusCode, payload));
            }

            if (string.IsNullOrWhiteSpace(payload))
            {
                throw new InvalidOperationException("腾讯元器服务返回为空");
            }

            TencentYuanqiChatResponse? data;
            try
            {
                data = JsonSerializer.Deserialize<TencentYuanqiChatResponse>(payload, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "腾讯元器返回解析失败: {Body}", Truncate(payload, 1000));
                throw new InvalidOperationException("腾讯元器返回格式异常");
            }

            var choice = data?.Choices?.FirstOrDefault();
            if (choice == null)
            {
                throw new InvalidOperationException("腾讯元器未返回有效结果");
            }

            var finishReason = choice.FinishReason?.Trim();
            var content = choice.Message?.Content?.Trim();

            if (string.Equals(finishReason, "sensitive", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("AI 服务审核未通过，请调整问题描述后重试");
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                if (string.Equals(finishReason, "tool_fail", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("AI 服务执行失败：腾讯元器智能体内部工具调用失败");
                }

                throw new InvalidOperationException("腾讯元器未返回有效内容");
            }

            return content;
        }

        private static TencentYuanqiRequestMessage MapMessage(AiAssistantPromptMessage message)
        {
            var role = message?.Role?.Trim().ToLowerInvariant();
            if (!string.Equals(role, "user", StringComparison.Ordinal) &&
                !string.Equals(role, "assistant", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"腾讯元器暂不支持角色: {message?.Role}");
            }

            var text = message?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException("腾讯元器消息内容不能为空");
            }

            return new TencentYuanqiRequestMessage
            {
                Role = role!,
                Content = new List<TencentYuanqiContentItem>
                {
                    new()
                    {
                        Type = "text",
                        Text = text
                    }
                }
            };
        }

        private static string NormalizeChatType(string? chatType)
        {
            var normalized = chatType?.Trim().ToLowerInvariant();
            return normalized switch
            {
                "preview" => "preview",
                _ => "published"
            };
        }

        private static string Truncate(string? text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            {
                return text ?? string.Empty;
            }

            return text[..maxLength] + "...";
        }

        private static string BuildHttpFailureMessage(int statusCode, string? payload)
        {
            var detail = ExtractErrorMessage(payload);
            return statusCode switch
            {
                400 => $"腾讯元器调用失败（400）：请求参数错误{detail}",
                401 => $"腾讯元器调用失败（401）：Token 无效或未授权{detail}",
                403 => $"腾讯元器调用失败（403）：当前账号无智能体访问权限{detail}",
                429 => $"腾讯元器调用失败（429）：请求频率超限，请稍后重试{detail}",
                500 or 502 or 503 or 504 => $"腾讯元器调用失败（{statusCode}）：服务暂时不可用{detail}",
                _ => $"腾讯元器调用失败，状态码={statusCode}{detail}"
            };
        }

        private static string ExtractErrorMessage(string? payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return string.Empty;
            }

            try
            {
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;
                if (root.TryGetProperty("error", out var errorNode))
                {
                    if (errorNode.ValueKind == JsonValueKind.Object &&
                        errorNode.TryGetProperty("message", out var msgNode) &&
                        msgNode.ValueKind == JsonValueKind.String)
                    {
                        var msg = msgNode.GetString()?.Trim();
                        return string.IsNullOrWhiteSpace(msg) ? string.Empty : $"，详情：{msg}";
                    }

                    if (errorNode.ValueKind == JsonValueKind.String)
                    {
                        var msg = errorNode.GetString()?.Trim();
                        return string.IsNullOrWhiteSpace(msg) ? string.Empty : $"，详情：{msg}";
                    }
                }

                if (root.TryGetProperty("message", out var messageNode) &&
                    messageNode.ValueKind == JsonValueKind.String)
                {
                    var msg = messageNode.GetString()?.Trim();
                    return string.IsNullOrWhiteSpace(msg) ? string.Empty : $"，详情：{msg}";
                }
            }
            catch (JsonException)
            {
                // 非 JSON 错误体不影响主流程。
            }

            var text = payload.Trim();
            return string.IsNullOrWhiteSpace(text) ? string.Empty : $"，详情：{Truncate(text, 240)}";
        }
    }

    internal sealed class TencentYuanqiChatRequest
    {
        [JsonPropertyName("assistant_id")]
        public string AssistantId { get; set; } = string.Empty;

        [JsonPropertyName("user_id")]
        public string UserId { get; set; } = string.Empty;

        [JsonPropertyName("stream")]
        public bool Stream { get; set; }

        [JsonPropertyName("chat_type")]
        public string ChatType { get; set; } = "published";

        [JsonPropertyName("messages")]
        public List<TencentYuanqiRequestMessage> Messages { get; set; } = new();
    }

    internal sealed class TencentYuanqiRequestMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public List<TencentYuanqiContentItem> Content { get; set; } = new();
    }

    internal sealed class TencentYuanqiContentItem
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "text";

        [JsonPropertyName("text")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Text { get; set; }
    }

    internal sealed class TencentYuanqiChatResponse
    {
        [JsonPropertyName("choices")]
        public List<TencentYuanqiChoice>? Choices { get; set; }
    }

    internal sealed class TencentYuanqiChoice
    {
        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }

        [JsonPropertyName("message")]
        public TencentYuanqiChoiceMessage? Message { get; set; }
    }

    internal sealed class TencentYuanqiChoiceMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}
