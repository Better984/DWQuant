using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Models.Strategy;
using ServerTest.Modules.AiAssistant.Domain;
using ServerTest.Modules.AiAssistant.Infrastructure;
using ServerTest.Options;

namespace ServerTest.Modules.AiAssistant.Application
{
    /// <summary>
    /// AI 助手应用服务：拼装提示词、调用模型并做结果归一化。
    /// </summary>
    public sealed class AiAssistantService
    {
        private const string WorkbenchContextMarker = "[WORKBENCH_CONTEXT]";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        private readonly TencentYuanqiChatClient _tencentYuanqiClient;
        private readonly AiAssistantOptions _options;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<AiAssistantService> _logger;

        private readonly SemaphoreSlim _knowledgeLock = new(1, 1);
        private string? _knowledgeText;

        public AiAssistantService(
            TencentYuanqiChatClient tencentYuanqiClient,
            IOptions<AiAssistantOptions> options,
            IWebHostEnvironment environment,
            ILogger<AiAssistantService> logger)
        {
            _tencentYuanqiClient = tencentYuanqiClient ?? throw new ArgumentNullException(nameof(tencentYuanqiClient));
            _options = options?.Value ?? new AiAssistantOptions();
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<AiAssistantChatResult> ChatAsync(
            long uid,
            string message,
            IReadOnlyList<AiAssistantHistoryItem>? history,
            CancellationToken ct)
        {
            var userMessage = message?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(userMessage))
            {
                throw new InvalidOperationException("消息不能为空");
            }

            var knowledgeText = await GetKnowledgeTextAsync(ct).ConfigureAwait(false);
            var prompt = BuildSystemPrompt(knowledgeText);

            var messages = new List<AiAssistantPromptMessage>
            {
                new()
                {
                    Role = "user",
                    Text = prompt
                },
                new()
                {
                    Role = "assistant",
                    Text = "好的，我已收到平台规则与知识库，后续会始终使用中文并严格输出要求的 JSON。"
                }
            };

            var historyWindow = (history ?? new List<AiAssistantHistoryItem>())
                .Where(item => item != null)
                .ToList();
            if (_options.MaxHistoryMessages > 0 && historyWindow.Count > _options.MaxHistoryMessages)
            {
                historyWindow = historyWindow
                    .Skip(historyWindow.Count - _options.MaxHistoryMessages)
                    .ToList();
            }
            AppendHistoryMessages(messages, historyWindow);

            messages.Add(new AiAssistantPromptMessage
            {
                Role = "user",
                Text = userMessage
            });

            _logger.LogInformation("AI 助手请求开始: uid={Uid}, 历史条数={HistoryCount}", uid, history?.Count ?? 0);
            var modelContent = await _tencentYuanqiClient.CreateChatCompletionAsync(uid, messages, ct).ConfigureAwait(false);
            var parsed = ParseModelOutput(modelContent);
            var strategyConfig = NormalizeStrategyConfig(parsed.StrategyConfigRaw, userMessage);
            if (strategyConfig == null && ShouldRetryForMissingStrategyConfig(userMessage, historyWindow, parsed))
            {
                _logger.LogWarning("AI 未返回完整策略配置，触发一次补全重试: uid={Uid}", uid);
                var retryMessages = BuildStrategyRetryMessages(messages, parsed, userMessage);
                var retryContent = await _tencentYuanqiClient.CreateChatCompletionAsync(uid, retryMessages, ct).ConfigureAwait(false);
                var retryParsed = ParseModelOutput(retryContent);
                var retryStrategyConfig = NormalizeStrategyConfig(retryParsed.StrategyConfigRaw, userMessage);
                if (retryStrategyConfig != null || !string.IsNullOrWhiteSpace(retryParsed.AssistantReply))
                {
                    parsed = retryParsed;
                    strategyConfig = retryStrategyConfig;
                }
            }

            if (!string.IsNullOrWhiteSpace(parsed.StrategyConfigRaw) && strategyConfig == null)
            {
                _logger.LogWarning("AI 策略配置解析失败，已降级为纯文本回复: uid={Uid}", uid);
            }

            var result = new AiAssistantChatResult
            {
                Reply = string.IsNullOrWhiteSpace(parsed.AssistantReply)
                    ? "已收到你的需求。"
                    : parsed.AssistantReply.Trim(),
                StrategyConfig = strategyConfig,
                SuggestedQuestions = parsed.SuggestedQuestions
            };

            _logger.LogInformation(
                "AI 助手请求完成: uid={Uid}, 是否生成策略={HasConfig}",
                uid,
                result.StrategyConfig != null);

            return result;
        }

        private static void AppendHistoryMessages(
            List<AiAssistantPromptMessage> messages,
            IReadOnlyList<AiAssistantHistoryItem>? history)
        {
            if (history == null || history.Count == 0)
            {
                return;
            }

            foreach (var item in history)
            {
                var role = NormalizeRole(item?.Role);
                if (role == null)
                {
                    continue;
                }

                var text = item?.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                // 历史文本截断，避免单条消息过长导致 token 爆炸。
                if (text.Length > 1000)
                {
                    text = text[..1000];
                }

                messages.Add(new AiAssistantPromptMessage
                {
                    Role = role,
                    Text = text
                });
            }
        }

        private static string? NormalizeRole(string? role)
        {
            var raw = role?.Trim().ToLowerInvariant();
            return raw switch
            {
                "user" => "user",
                "assistant" => "assistant",
                _ => null
            };
        }

        private async Task<string> GetKnowledgeTextAsync(CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(_knowledgeText))
            {
                return _knowledgeText;
            }

            await _knowledgeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!string.IsNullOrWhiteSpace(_knowledgeText))
                {
                    return _knowledgeText;
                }

                var configuredPath = _options.KnowledgeFilePath?.Trim();
                string knowledgePath;
                if (string.IsNullOrWhiteSpace(configuredPath))
                {
                    knowledgePath = Path.Combine(_environment.ContentRootPath, "Document", "AI助手知识库.md");
                }
                else if (Path.IsPathRooted(configuredPath))
                {
                    knowledgePath = configuredPath;
                }
                else
                {
                    knowledgePath = Path.Combine(_environment.ContentRootPath, configuredPath);
                }

                if (File.Exists(knowledgePath))
                {
                    _knowledgeText = await File.ReadAllTextAsync(knowledgePath, ct).ConfigureAwait(false);
                }
                else
                {
                    _knowledgeText = """
                    - 你是多维量化平台 AI 助手。
                    - 需要输出平台可用的策略 JSON 配置，字段遵循 trade / logic / runtime 结构。
                    - 当用户未要求生成策略时，strategyConfig 返回 null。
                    """;
                    _logger.LogWarning("AI 知识库文件不存在，使用内置兜底知识: {Path}", knowledgePath);
                }

                return _knowledgeText;
            }
            finally
            {
                _knowledgeLock.Release();
            }
        }

        private static string BuildSystemPrompt(string knowledgeText)
        {
            return "以下内容是“多维量化”平台 AI 助手的固定规则，不是最终用户问题。请先记住，并在后续用户消息到来后严格遵循。\n\n" +
                   "你必须始终使用中文。\n\n" +
                   "以下是平台知识库，请优先遵循：\n" +
                   knowledgeText +
                   "\n\n" +
                   "你必须输出严格 JSON（禁止 markdown 代码块、禁止额外说明），结构如下：\n" +
                   "{\n" +
                   "  \"assistantReply\": \"给用户看的中文说明\",\n" +
                   "  \"strategyConfig\": 对象或 null,\n" +
                   "  \"suggestedQuestions\": [\"后续可点的快捷提问，最多3条\"]\n" +
                   "}\n\n" +
                   "规则：\n" +
                   "1. 若用户明确要求“生成策略/配置/JSON”，或基于已有策略要求你继续修改/新增条件，strategyConfig 必须为对象。\n" +
                   "2. strategyConfig 需要遵循 trade / logic / runtime 三层结构，并尽量使用平台习惯值：\n" +
                   "   - exchange: binance | okx | bitget\n" +
                   "   - symbol: 例如 BTC/USDT\n" +
                   "   - timeframeSec: 秒，例如 60/300/3600/86400\n" +
                   "   - positionMode: Cross 或 Isolated\n" +
                   "   - openConflictPolicy: GiveUp\n" +
                   "   - 若当前会话里已经有工作台策略上下文，而用户要求你继续修改/新增逻辑，必须返回完整最新 strategyConfig，不能只解释改动\n" +
                   "   - logic.entry.long 与 logic.entry.short 必须包含至少一个可执行条件：containers[].checks.groups[].conditions 不能为空\n" +
                   "   - entry 分支禁止输出 groups: [] 或 conditions: [] 的空条件结构\n" +
                   "   - 条件 method、indicator、params、output 必须严格匹配知识库白名单与参数顺序\n" +
                   "   - MakeTrade 动作方向必须写在 param 中，例如 [\"Long\"]；args 必须为空数组\n" +
                   "3. 若用户只是咨询，不要求生成策略，strategyConfig 必须为 null。\n" +
                   "4. assistantReply 简洁明确，不要超过 220 个中文字符。\n" +
                   "5. suggestedQuestions 返回 0 到 3 条简短中文问题，便于前端渲染快捷提问按钮；若无合适建议请返回空数组。";
        }

        private static AiModelOutput ParseModelOutput(string modelContent)
        {
            var cleaned = StripCodeFence(modelContent);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return new AiModelOutput();
            }

            try
            {
                using var document = JsonDocument.Parse(cleaned);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return new AiModelOutput { AssistantReply = cleaned };
                }

                var output = new AiModelOutput();
                if (root.TryGetProperty("assistantReply", out var replyNode) &&
                    replyNode.ValueKind == JsonValueKind.String)
                {
                    output.AssistantReply = replyNode.GetString();
                }

                if (TryReadStrategyConfigRaw(root, out var strategyConfigRaw))
                {
                    output.StrategyConfigRaw = strategyConfigRaw;
                }

                output.SuggestedQuestions = ReadSuggestedQuestions(root);

                if (string.IsNullOrWhiteSpace(output.AssistantReply))
                {
                    output.AssistantReply = "已生成结果。";
                }

                return output;
            }
            catch (JsonException)
            {
                // 模型偶发未按 JSON 返回时，降级为纯文本回复。
                return new AiModelOutput { AssistantReply = cleaned };
            }
        }

        private static List<string> ReadSuggestedQuestions(JsonElement root)
        {
            foreach (var propertyName in new[] { "suggestedQuestions", "suggestedPrompts", "quickQuestions", "followUpQuestions" })
            {
                if (!root.TryGetProperty(propertyName, out var node) || node.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var questions = new List<string>();
                foreach (var item in node.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var text = item.GetString()?.Trim();
                    if (string.IsNullOrWhiteSpace(text) || questions.Contains(text, StringComparer.Ordinal))
                    {
                        continue;
                    }

                    questions.Add(text);
                    if (questions.Count >= 3)
                    {
                        break;
                    }
                }

                if (questions.Count > 0)
                {
                    return questions;
                }
            }

            return new List<string>();
        }

        private static bool TryReadStrategyConfigRaw(JsonElement root, out string? strategyConfigRaw)
        {
            strategyConfigRaw = null;
            foreach (var propertyName in new[] { "strategyConfig", "strategy_config" })
            {
                if (!root.TryGetProperty(propertyName, out var node))
                {
                    continue;
                }

                if (node.ValueKind == JsonValueKind.Object)
                {
                    strategyConfigRaw = node.GetRawText();
                    return true;
                }

                if (node.ValueKind == JsonValueKind.String)
                {
                    var text = StripCodeFence(node.GetString() ?? string.Empty);
                    if (TryExtractEmbeddedJsonObject(text, out var embeddedJson))
                    {
                        strategyConfigRaw = embeddedJson;
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryExtractEmbeddedJsonObject(string text, out string? json)
        {
            json = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            try
            {
                using var document = JsonDocument.Parse(text);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                json = document.RootElement.GetRawText();
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static string StripCodeFence(string text)
        {
            var raw = text?.Trim() ?? string.Empty;
            if (!raw.StartsWith("```", StringComparison.Ordinal))
            {
                return raw;
            }

            var lines = raw.Split('\n')
                .Select(line => line.TrimEnd('\r'))
                .ToList();
            if (lines.Count == 0)
            {
                return raw;
            }

            if (lines[0].StartsWith("```", StringComparison.Ordinal))
            {
                lines.RemoveAt(0);
            }

            if (lines.Count > 0 && lines[^1].StartsWith("```", StringComparison.Ordinal))
            {
                lines.RemoveAt(lines.Count - 1);
            }

            return string.Join('\n', lines).Trim();
        }

        private static bool ShouldRetryForMissingStrategyConfig(
            string userMessage,
            IReadOnlyList<AiAssistantHistoryItem>? history,
            AiModelOutput output)
        {
            if (!string.IsNullOrWhiteSpace(output.StrategyConfigRaw))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(userMessage) ||
                userMessage.Contains(WorkbenchContextMarker, StringComparison.Ordinal))
            {
                return false;
            }

            var hasWorkbenchContext = history?.Any(item =>
                string.Equals(item?.Role, "user", StringComparison.OrdinalIgnoreCase) &&
                (item?.Text?.Contains(WorkbenchContextMarker, StringComparison.Ordinal) ?? false)) == true;

            if (hasWorkbenchContext && ContainsAny(userMessage, "修改", "新增", "添加", "补充", "优化", "调整", "完善", "改成", "改为"))
            {
                return true;
            }

            if (ContainsAny(userMessage, "生成", "创建", "新增", "添加", "修改", "优化", "调整") &&
                ContainsAny(userMessage, "策略", "逻辑", "条件", "开多", "开空", "做多", "做空", "止盈", "止损"))
            {
                return true;
            }

            return ContainsAny(output.AssistantReply, "已为你", "已为您", "已基于", "已经为你", "已经为您") &&
                   ContainsAny(output.AssistantReply, "新增", "添加", "修改", "优化", "调整", "补充");
        }

        private static List<AiAssistantPromptMessage> BuildStrategyRetryMessages(
            IReadOnlyList<AiAssistantPromptMessage> messages,
            AiModelOutput output,
            string userMessage)
        {
            var retryMessages = messages
                .Select(item => new AiAssistantPromptMessage
                {
                    Role = item.Role,
                    Text = item.Text
                })
                .ToList();

            if (!string.IsNullOrWhiteSpace(output.AssistantReply))
            {
                retryMessages.Add(new AiAssistantPromptMessage
                {
                    Role = "assistant",
                    Text = output.AssistantReply.Trim()
                });
            }

            retryMessages.Add(new AiAssistantPromptMessage
            {
                Role = "user",
                Text = "你上一条回复只有说明文字，没有返回完整 strategyConfig。" +
                       "这次用户明确要求你生成或修改策略，请基于当前会话中最近一次已有的策略上下文，重新输出严格 JSON 顶层对象。" +
                       "要求：1. strategyConfig 必须是完整、最新、可执行的对象，不能只返回差异说明；" +
                       "2. 未被用户要求修改的部分必须保持原样；" +
                       "3. 不要使用 markdown 代码块；" +
                       "4. assistantReply 只简短说明本次改动。用户本次要求：" + userMessage
            });

            return retryMessages;
        }

        private static bool ContainsAny(string? text, params string[] keywords)
        {
            if (string.IsNullOrWhiteSpace(text) || keywords == null || keywords.Length == 0)
            {
                return false;
            }

            foreach (var keyword in keywords)
            {
                if (!string.IsNullOrWhiteSpace(keyword) &&
                    text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static StrategyConfig? NormalizeStrategyConfig(string? rawConfigJson, string userMessage)
        {
            if (string.IsNullOrWhiteSpace(rawConfigJson))
            {
                return null;
            }

            var normalizedRawConfigJson = CanonicalizeStrategyConfigJson(rawConfigJson);
            if (string.IsNullOrWhiteSpace(normalizedRawConfigJson))
            {
                return null;
            }

            StrategyConfig? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<StrategyConfig>(normalizedRawConfigJson, JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }

            if (parsed == null)
            {
                return null;
            }

            var defaults = BuildDefaultStrategyConfig();
            parsed.Trade ??= defaults.Trade;
            parsed.Logic ??= defaults.Logic;
            parsed.Runtime ??= defaults.Runtime;

            NormalizeTrade(parsed.Trade, defaults.Trade);
            NormalizeLogic(parsed.Logic, defaults.Logic);
            NormalizeRuntime(parsed.Runtime, defaults.Runtime);
            EnsureExecutableLogic(parsed, userMessage);
            return parsed;
        }

        private static string? CanonicalizeStrategyConfigJson(string rawConfigJson)
        {
            JsonNode? rootNode;
            try
            {
                rootNode = JsonNode.Parse(rawConfigJson);
            }
            catch (JsonException)
            {
                return rawConfigJson;
            }

            if (rootNode is not JsonObject root)
            {
                return rawConfigJson;
            }

            try
            {
                NormalizeRawTrade(root["trade"] as JsonObject);
                NormalizeRawLogic(root["logic"] as JsonObject);
                NormalizeRawRuntime(root["runtime"] as JsonObject);
            }
            catch (InvalidOperationException)
            {
                // AI 返回了非预期的节点形态时，不让底层 JsonNode 异常直接中断整次对话。
                return rawConfigJson;
            }

            return root.ToJsonString();
        }

        private static void NormalizeRawTrade(JsonObject? trade)
        {
            if (trade == null)
            {
                return;
            }

            if (trade["sizing"] is null)
            {
                trade["sizing"] = new JsonObject();
            }

            if (trade["risk"] is null)
            {
                trade["risk"] = new JsonObject();
            }
        }

        private static void NormalizeRawLogic(JsonObject? logic)
        {
            if (logic == null)
            {
                return;
            }

            NormalizeRawBranch(((logic["entry"] as JsonObject)?["long"] as JsonObject), "Long");
            NormalizeRawBranch(((logic["entry"] as JsonObject)?["short"] as JsonObject), "Short");
            NormalizeRawBranch(((logic["exit"] as JsonObject)?["long"] as JsonObject), "CloseLong");
            NormalizeRawBranch(((logic["exit"] as JsonObject)?["short"] as JsonObject), "CloseShort");
        }

        private static void NormalizeRawBranch(JsonObject? branch, string fallbackAction)
        {
            if (branch == null)
            {
                return;
            }

            if (branch["onPass"] is not JsonObject onPass)
            {
                return;
            }

            if (onPass["conditions"] is not JsonArray conditions)
            {
                return;
            }

            foreach (var node in conditions)
            {
                if (node is JsonObject method)
                {
                    NormalizeRawActionMethod(method, fallbackAction);
                }
            }
        }

        private static void NormalizeRawActionMethod(JsonObject method, string fallbackAction)
        {
            var methodName = ReadJsonNodeText(method["method"]);
            if (!string.Equals(methodName, "MakeTrade", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var paramValues = ExtractStringArray(method["param"]);
            if (paramValues.Count == 0)
            {
                paramValues = ExtractStringArray(method["args"]);
            }

            if (paramValues.Count == 0 && !string.IsNullOrWhiteSpace(fallbackAction))
            {
                paramValues.Add(fallbackAction);
            }

            method["param"] = BuildJsonStringArray(paramValues);
            method["args"] = new JsonArray();
        }

        private static void NormalizeRawRuntime(JsonObject? runtime)
        {
            if (runtime == null)
            {
                return;
            }

            if (string.Equals(ReadJsonNodeText(runtime["scheduleType"]), "Continuous", StringComparison.OrdinalIgnoreCase))
            {
                runtime["scheduleType"] = "Always";
            }

            if (string.Equals(ReadJsonNodeText(runtime["outOfSessionPolicy"]), "Ignore", StringComparison.OrdinalIgnoreCase))
            {
                runtime["outOfSessionPolicy"] = "BlockEntryAllowExit";
            }
        }

        private static string? ReadJsonNodeText(JsonNode? node)
        {
            if (node is not JsonValue value)
            {
                return null;
            }

            var text = value.ToString().Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        private static List<string> ExtractStringArray(JsonNode? node)
        {
            if (node == null)
            {
                return new List<string>();
            }

            if (node is JsonValue singleValue)
            {
                var text = singleValue.ToString().Trim();
                return string.IsNullOrWhiteSpace(text) ? new List<string>() : new List<string> { text };
            }

            if (node is not JsonArray array)
            {
                return new List<string>();
            }

            var values = new List<string>();
            foreach (var item in array)
            {
                if (item == null)
                {
                    continue;
                }

                var text = item.ToString().Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                values.Add(text);
                if (values.Count >= 3)
                {
                    break;
                }
            }

            return values;
        }

        private static JsonArray BuildJsonStringArray(IEnumerable<string> values)
        {
            var array = new JsonArray();
            foreach (var value in values)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                array.Add(value.Trim());
            }

            return array;
        }

        private static void NormalizeTrade(TradeConfig trade, TradeConfig defaults)
        {
            if (string.IsNullOrWhiteSpace(trade.Exchange))
            {
                trade.Exchange = defaults.Exchange;
            }
            else
            {
                trade.Exchange = trade.Exchange.Trim().ToLowerInvariant();
            }

            if (string.IsNullOrWhiteSpace(trade.Symbol))
            {
                trade.Symbol = defaults.Symbol;
            }
            else
            {
                trade.Symbol = trade.Symbol.Trim().ToUpperInvariant();
            }

            if (trade.TimeframeSec <= 0)
            {
                trade.TimeframeSec = defaults.TimeframeSec;
            }

            if (!string.Equals(trade.PositionMode, "Cross", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(trade.PositionMode, "Isolated", StringComparison.OrdinalIgnoreCase))
            {
                trade.PositionMode = defaults.PositionMode;
            }
            else
            {
                trade.PositionMode = char.ToUpperInvariant(trade.PositionMode[0]) +
                                     trade.PositionMode[1..].ToLowerInvariant();
            }

            if (string.IsNullOrWhiteSpace(trade.OpenConflictPolicy))
            {
                trade.OpenConflictPolicy = defaults.OpenConflictPolicy;
            }

            trade.Sizing ??= defaults.Sizing;
            if (trade.Sizing.OrderQty <= 0)
            {
                trade.Sizing.OrderQty = defaults.Sizing.OrderQty;
            }
            if (trade.Sizing.MaxPositionQty <= 0)
            {
                trade.Sizing.MaxPositionQty = defaults.Sizing.MaxPositionQty;
            }
            if (trade.Sizing.Leverage <= 0)
            {
                trade.Sizing.Leverage = defaults.Sizing.Leverage;
            }

            trade.Risk ??= defaults.Risk;
            if (trade.Risk.TakeProfitPct <= 0)
            {
                trade.Risk.TakeProfitPct = defaults.Risk.TakeProfitPct;
            }
            if (trade.Risk.StopLossPct <= 0)
            {
                trade.Risk.StopLossPct = defaults.Risk.StopLossPct;
            }

            trade.Risk.Trailing ??= defaults.Risk.Trailing;
            if (trade.Risk.Trailing.ActivationProfitPct <= 0)
            {
                trade.Risk.Trailing.ActivationProfitPct = defaults.Risk.Trailing.ActivationProfitPct;
            }
            if (trade.Risk.Trailing.CloseOnDrawdownPct <= 0)
            {
                trade.Risk.Trailing.CloseOnDrawdownPct = defaults.Risk.Trailing.CloseOnDrawdownPct;
            }
        }

        private static void NormalizeLogic(StrategyLogic logic, StrategyLogic defaults)
        {
            logic.Entry ??= defaults.Entry;
            logic.Exit ??= defaults.Exit;

            logic.Entry.Long ??= defaults.Entry.Long;
            logic.Entry.Short ??= defaults.Entry.Short;
            logic.Exit.Long ??= defaults.Exit.Long;
            logic.Exit.Short ??= defaults.Exit.Short;

            NormalizeBranch(logic.Entry.Long, defaults.Entry.Long, "Long");
            NormalizeBranch(logic.Entry.Short, defaults.Entry.Short, "Short");
            NormalizeBranch(logic.Exit.Long, defaults.Exit.Long, "CloseLong");
            NormalizeBranch(logic.Exit.Short, defaults.Exit.Short, "CloseShort");
        }

        private static void NormalizeBranch(StrategyLogicBranch branch, StrategyLogicBranch defaults, string action)
        {
            branch.Containers ??= defaults.Containers;
            branch.OnPass ??= defaults.OnPass;

            if (branch.Containers.Count == 0)
            {
                branch.Containers.Add(new ConditionContainer
                {
                    Checks = new ConditionGroupSet
                    {
                        Enabled = true,
                        MinPassGroups = 1,
                        Groups = new List<ConditionGroup>()
                    }
                });
            }

            foreach (var container in branch.Containers)
            {
                container.Checks ??= new ConditionGroupSet
                {
                    Enabled = true,
                    MinPassGroups = 1,
                    Groups = new List<ConditionGroup>()
                };
                container.Checks.Groups ??= new List<ConditionGroup>();
                if (container.Checks.MinPassGroups <= 0)
                {
                    container.Checks.MinPassGroups = 1;
                }
            }

            branch.OnPass.Conditions ??= new List<StrategyMethod>();
            if (branch.OnPass.Conditions.Count == 0)
            {
                branch.OnPass.Conditions.Add(BuildActionMethod(action));
            }
        }

        /// <summary>
        /// 对模型输出做最终可执行兜底：避免 checks.groups 为空导致策略永不触发。
        /// </summary>
        private static void EnsureExecutableLogic(StrategyConfig config, string userMessage)
        {
            if (config.Logic?.Entry == null || config.Logic.Exit == null)
            {
                return;
            }

            if (TryBuildEmaMaCrossRefs(userMessage, out var fastRef, out var slowRef))
            {
                EnsureBranchCondition(config.Logic.Entry.Long, "CrossUp", fastRef, slowRef);
                EnsureBranchCondition(config.Logic.Entry.Short, "CrossDown", fastRef, slowRef);
                EnsureBranchCondition(config.Logic.Exit.Long, "CrossDown", fastRef, slowRef);
                EnsureBranchCondition(config.Logic.Exit.Short, "CrossUp", fastRef, slowRef);
                return;
            }

            // 无法从用户语义提取指标时，退化为价格与 SMA20 的交叉，确保策略至少可触发。
            var closeRef = BuildFieldRef("Close");
            var ma20Ref = BuildIndicatorRef("SMA", 20);
            EnsureBranchCondition(config.Logic.Entry.Long, "CrossUp", closeRef, ma20Ref);
            EnsureBranchCondition(config.Logic.Entry.Short, "CrossDown", closeRef, ma20Ref);
            EnsureBranchCondition(config.Logic.Exit.Long, "CrossDown", closeRef, ma20Ref);
            EnsureBranchCondition(config.Logic.Exit.Short, "CrossUp", closeRef, ma20Ref);
        }

        private static bool TryBuildEmaMaCrossRefs(
            string userMessage,
            out StrategyValueRef fastRef,
            out StrategyValueRef slowRef)
        {
            fastRef = null!;
            slowRef = null!;

            var text = (userMessage ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var emaMatch = Regex.Match(text, @"EMA\s*(\d+)", RegexOptions.IgnoreCase);
            var smaMatch = Regex.Match(text, @"SMA\s*(\d+)", RegexOptions.IgnoreCase);
            var maMatch = Regex.Match(text, @"(?<!E)MA\s*(\d+)", RegexOptions.IgnoreCase);

            var hasSlow = smaMatch.Success || maMatch.Success;
            if (!emaMatch.Success || !hasSlow)
            {
                return false;
            }

            if (!int.TryParse(emaMatch.Groups[1].Value, out var emaPeriod) ||
                !int.TryParse((smaMatch.Success ? smaMatch.Groups[1].Value : maMatch.Groups[1].Value), out var maPeriod))
            {
                return false;
            }

            if (emaPeriod <= 0 || maPeriod <= 0)
            {
                return false;
            }

            fastRef = BuildIndicatorRef("EMA", emaPeriod);
            slowRef = BuildIndicatorRef("SMA", maPeriod);
            return true;
        }

        private static void EnsureBranchCondition(
            StrategyLogicBranch branch,
            string method,
            StrategyValueRef leftRef,
            StrategyValueRef rightRef)
        {
            if (BranchHasExecutableCondition(branch))
            {
                return;
            }

            branch.Containers ??= new List<ConditionContainer>();
            if (branch.Containers.Count == 0)
            {
                branch.Containers.Add(new ConditionContainer
                {
                    Checks = new ConditionGroupSet
                    {
                        Enabled = true,
                        MinPassGroups = 1,
                        Groups = new List<ConditionGroup>()
                    }
                });
            }

            var firstContainer = branch.Containers[0] ?? new ConditionContainer();
            branch.Containers[0] = firstContainer;
            firstContainer.Checks ??= new ConditionGroupSet
            {
                Enabled = true,
                MinPassGroups = 1,
                Groups = new List<ConditionGroup>()
            };
            firstContainer.Checks.Groups ??= new List<ConditionGroup>();
            if (firstContainer.Checks.MinPassGroups <= 0)
            {
                firstContainer.Checks.MinPassGroups = 1;
            }

            var group = new ConditionGroup
            {
                Enabled = true,
                MinPassConditions = 1,
                Conditions = new List<StrategyMethod>
                {
                    BuildCrossMethod(method, leftRef, rightRef)
                }
            };
            firstContainer.Checks.Groups.Add(group);
        }

        private static bool BranchHasExecutableCondition(StrategyLogicBranch branch)
        {
            if (branch?.Containers == null || branch.Containers.Count == 0)
            {
                return false;
            }

            foreach (var container in branch.Containers)
            {
                var groups = container?.Checks?.Groups;
                if (groups == null || groups.Count == 0)
                {
                    continue;
                }

                foreach (var group in groups)
                {
                    var conditions = group?.Conditions;
                    if (conditions == null || conditions.Count == 0)
                    {
                        continue;
                    }

                    if (conditions.Any(item => item != null && !string.IsNullOrWhiteSpace(item.Method)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static StrategyMethod BuildCrossMethod(string method, StrategyValueRef leftRef, StrategyValueRef rightRef)
        {
            return new StrategyMethod
            {
                Enabled = true,
                Required = false,
                Method = method,
                Param = Array.Empty<string>(),
                Args = new List<StrategyValueRef>
                {
                    CloneRef(leftRef),
                    CloneRef(rightRef)
                }
            };
        }

        private static StrategyValueRef BuildFieldRef(string input)
        {
            return new StrategyValueRef
            {
                RefType = "Field",
                Input = input,
                Output = "Value",
                Params = new List<double>(),
                OffsetRange = new[] { 0, 0 },
                CalcMode = "OnBarClose"
            };
        }

        private static StrategyValueRef BuildIndicatorRef(string indicator, int period)
        {
            return new StrategyValueRef
            {
                RefType = "Indicator",
                Indicator = indicator,
                Input = "Close",
                Output = "Real",
                Params = new List<double> { period },
                OffsetRange = new[] { 0, 0 },
                CalcMode = "OnBarClose"
            };
        }

        private static StrategyValueRef CloneRef(StrategyValueRef source)
        {
            return new StrategyValueRef
            {
                RefType = source.RefType,
                Indicator = source.Indicator,
                Timeframe = source.Timeframe,
                Input = source.Input,
                Params = source.Params?.ToList() ?? new List<double>(),
                Output = source.Output,
                OffsetRange = source.OffsetRange?.ToArray() ?? new[] { 0, 0 },
                CalcMode = source.CalcMode
            };
        }

        private static void NormalizeRuntime(StrategyRuntimeConfig runtime, StrategyRuntimeConfig defaults)
        {
            var scheduleType = NormalizeScheduleType(runtime.ScheduleType);
            if (!string.Equals(scheduleType, "Always", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(scheduleType, "Template", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(scheduleType, "Custom", StringComparison.OrdinalIgnoreCase))
            {
                runtime.ScheduleType = defaults.ScheduleType;
            }
            else
            {
                runtime.ScheduleType = scheduleType ?? defaults.ScheduleType;
            }

            var policy = NormalizeOutOfSessionPolicy(runtime.OutOfSessionPolicy);
            if (!string.Equals(policy, "BlockEntryAllowExit", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(policy, "BlockAll", StringComparison.OrdinalIgnoreCase))
            {
                runtime.OutOfSessionPolicy = defaults.OutOfSessionPolicy;
            }
            else
            {
                runtime.OutOfSessionPolicy = policy ?? defaults.OutOfSessionPolicy;
            }

            runtime.TemplateIds ??= new List<string>();
            runtime.Templates ??= new List<StrategyRuntimeTemplateConfig>();
            runtime.Custom ??= defaults.Custom;

            if (string.IsNullOrWhiteSpace(runtime.Custom.Mode))
            {
                runtime.Custom.Mode = defaults.Custom.Mode;
            }

            if (string.IsNullOrWhiteSpace(runtime.Custom.Timezone))
            {
                runtime.Custom.Timezone = defaults.Custom.Timezone;
            }

            runtime.Custom.Days ??= new List<string>();
            runtime.Custom.TimeRanges ??= new List<StrategyRuntimeTimeRange>();
        }

        private static string? NormalizeScheduleType(string? scheduleType)
        {
            var value = scheduleType?.Trim();
            if (string.Equals(value, "Continuous", StringComparison.OrdinalIgnoreCase))
            {
                return "Always";
            }

            return value;
        }

        private static string? NormalizeOutOfSessionPolicy(string? policy)
        {
            var value = policy?.Trim();
            if (string.Equals(value, "Ignore", StringComparison.OrdinalIgnoreCase))
            {
                return "BlockEntryAllowExit";
            }

            return value;
        }

        private static StrategyConfig BuildDefaultStrategyConfig()
        {
            return new StrategyConfig
            {
                Trade = new TradeConfig
                {
                    Exchange = "bitget",
                    Symbol = "BTC/USDT",
                    TimeframeSec = 60,
                    PositionMode = "Cross",
                    OpenConflictPolicy = "GiveUp",
                    Sizing = new TradeSizing
                    {
                        OrderQty = 0.001m,
                        MaxPositionQty = 10m,
                        Leverage = 20
                    },
                    Risk = new TradeRisk
                    {
                        TakeProfitPct = 2m,
                        StopLossPct = 1m,
                        Trailing = new TradeTrailingStop
                        {
                            Enabled = false,
                            ActivationProfitPct = 1m,
                            CloseOnDrawdownPct = 0.2m
                        }
                    }
                },
                Logic = new StrategyLogic
                {
                    Entry = new StrategyLogicSide
                    {
                        Long = BuildDefaultBranch("Long"),
                        Short = BuildDefaultBranch("Short")
                    },
                    Exit = new StrategyLogicSide
                    {
                        Long = BuildDefaultBranch("CloseLong"),
                        Short = BuildDefaultBranch("CloseShort")
                    }
                },
                Runtime = new StrategyRuntimeConfig
                {
                    ScheduleType = "Always",
                    OutOfSessionPolicy = "BlockEntryAllowExit",
                    TemplateIds = new List<string>(),
                    Templates = new List<StrategyRuntimeTemplateConfig>(),
                    Custom = new StrategyRuntimeCustomConfig
                    {
                        Mode = "Deny",
                        Timezone = "Asia/Shanghai",
                        Days = new List<string>(),
                        TimeRanges = new List<StrategyRuntimeTimeRange>()
                    }
                }
            };
        }

        private static StrategyLogicBranch BuildDefaultBranch(string action)
        {
            return new StrategyLogicBranch
            {
                Enabled = true,
                MinPassConditionContainer = 1,
                Containers = new List<ConditionContainer>
                {
                    new()
                    {
                        Checks = new ConditionGroupSet
                        {
                            Enabled = true,
                            MinPassGroups = 1,
                            Groups = new List<ConditionGroup>()
                        }
                    }
                },
                OnPass = new ActionSet
                {
                    Enabled = true,
                    MinPassConditions = 1,
                    Conditions = new List<StrategyMethod>
                    {
                        BuildActionMethod(action)
                    }
                }
            };
        }

        private static StrategyMethod BuildActionMethod(string action)
        {
            return new StrategyMethod
            {
                Enabled = true,
                Required = false,
                Method = "MakeTrade",
                Param = new[] { action },
                Args = new List<StrategyValueRef>()
            };
        }

        private sealed class AiModelOutput
        {
            public string? AssistantReply { get; set; }
            public string? StrategyConfigRaw { get; set; }
            public List<string> SuggestedQuestions { get; set; } = new();
        }
    }
}
