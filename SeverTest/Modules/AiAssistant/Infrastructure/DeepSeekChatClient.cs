using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Options;

namespace ServerTest.Modules.AiAssistant.Infrastructure
{
    /// <summary>
    /// DeepSeek 对话客户端（OpenAI 兼容接口）。
    /// </summary>
    public sealed class DeepSeekChatClient
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpClient _httpClient;
        private readonly AiAssistantOptions _options;
        private readonly ILogger<DeepSeekChatClient> _logger;

        public DeepSeekChatClient(
            HttpClient httpClient,
            IOptions<AiAssistantOptions> options,
            ILogger<DeepSeekChatClient> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _options = options?.Value ?? new AiAssistantOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<string> CreateChatCompletionAsync(
            IReadOnlyList<DeepSeekChatMessage> messages,
            CancellationToken ct)
        {
            if (!_options.Enabled)
            {
                throw new InvalidOperationException("AI 助手未启用，请在配置中将 AiAssistant.Enabled 设为 true");
            }

            if (string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                throw new InvalidOperationException("AI 助手未配置 API Key，请先在 AiAssistant.ApiKey 中填写密钥");
            }

            if (messages == null || messages.Count == 0)
            {
                throw new InvalidOperationException("AI 请求消息不能为空");
            }

            var request = new DeepSeekChatRequest
            {
                Model = _options.Model,
                Temperature = _options.Temperature,
                ResponseFormat = new DeepSeekResponseFormat { Type = "json_object" },
                Messages = messages.ToList()
            };

            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
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
                    "DeepSeek 请求失败: status={Status}, body={Body}",
                    (int)response.StatusCode,
                    Truncate(payload, 1000));
                throw new InvalidOperationException(BuildHttpFailureMessage((int)response.StatusCode, payload));
            }

            if (string.IsNullOrWhiteSpace(payload))
            {
                throw new InvalidOperationException("AI 服务返回为空");
            }

            DeepSeekChatResponse? data;
            try
            {
                data = JsonSerializer.Deserialize<DeepSeekChatResponse>(payload, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "DeepSeek 返回解析失败: {Body}", Truncate(payload, 1000));
                throw new InvalidOperationException("AI 服务返回格式异常");
            }

            var content = data?.Choices?
                .FirstOrDefault()?
                .Message?
                .Content?
                .Trim();

            if (string.IsNullOrWhiteSpace(content))
            {
                throw new InvalidOperationException("AI 服务未返回有效内容");
            }

            return content;
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
                401 => $"AI 服务调用失败（401）：API Key 无效或未授权{detail}",
                402 => $"AI 服务调用失败（402）：账户余额不足、套餐到期或额度不足{detail}",
                403 => $"AI 服务调用失败（403）：当前账号无模型访问权限{detail}",
                429 => $"AI 服务调用失败（429）：请求频率超限，请稍后重试{detail}",
                500 or 502 or 503 or 504 => $"AI 服务调用失败（{statusCode}）：模型服务暂时不可用{detail}",
                _ => $"AI 服务调用失败，状态码={statusCode}{detail}"
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
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            return $"，详情：{Truncate(text, 240)}";
        }
    }

    public sealed class DeepSeekChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    internal sealed class DeepSeekChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("response_format")]
        public DeepSeekResponseFormat? ResponseFormat { get; set; }

        [JsonPropertyName("messages")]
        public List<DeepSeekChatMessage> Messages { get; set; } = new();
    }

    internal sealed class DeepSeekResponseFormat
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "json_object";
    }

    internal sealed class DeepSeekChatResponse
    {
        [JsonPropertyName("choices")]
        public List<DeepSeekChoice>? Choices { get; set; }
    }

    internal sealed class DeepSeekChoice
    {
        [JsonPropertyName("message")]
        public DeepSeekChoiceMessage? Message { get; set; }
    }

    internal sealed class DeepSeekChoiceMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}
