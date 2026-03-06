using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.AiAssistant.Domain;
using ServerTest.Modules.AiAssistant.Infrastructure;
using ServerTest.Options;

namespace ServerTest.Modules.AiAssistant.Application
{
    /// <summary>
    /// AI 助手会话编排服务：负责会话持久化、历史读取与模型调用联动。
    /// </summary>
    public sealed class AiAssistantConversationService
    {
        private const string WorkbenchContextMarker = "[WORKBENCH_CONTEXT]";
        private readonly AiAssistantService _aiAssistantService;
        private readonly AiAssistantChatRepository _repository;
        private readonly AiAssistantOptions _options;
        private readonly ILogger<AiAssistantConversationService> _logger;

        private readonly SemaphoreSlim _schemaLock = new(1, 1);
        private volatile bool _schemaReady;

        public AiAssistantConversationService(
            AiAssistantService aiAssistantService,
            AiAssistantChatRepository repository,
            IOptions<AiAssistantOptions> options,
            ILogger<AiAssistantConversationService> logger)
        {
            _aiAssistantService = aiAssistantService ?? throw new ArgumentNullException(nameof(aiAssistantService));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _options = options?.Value ?? new AiAssistantOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IReadOnlyList<AiAssistantConversationSummary>> ListConversationsAsync(
            long uid,
            int? limit,
            CancellationToken ct)
        {
            await EnsureSchemaReadyAsync(ct).ConfigureAwait(false);
            var safeLimit = Math.Clamp(limit ?? 30, 1, 100);
            return await _repository.ListConversationsAsync(uid, safeLimit, ct).ConfigureAwait(false);
        }

        public async Task<AiAssistantConversationSummary> CreateConversationAsync(
            long uid,
            string? title,
            CancellationToken ct)
        {
            await EnsureSchemaReadyAsync(ct).ConfigureAwait(false);

            var normalizedTitle = NormalizeTitle(title);
            var conversationId = await _repository.CreateConversationAsync(uid, normalizedTitle, ct).ConfigureAwait(false);
            var created = await _repository.GetConversationAsync(uid, conversationId, ct).ConfigureAwait(false);
            if (created == null)
            {
                throw new InvalidOperationException("会话创建成功但读取失败");
            }

            return created;
        }

        public async Task<AiAssistantConversationMessagesResult> GetConversationMessagesAsync(
            long uid,
            long conversationId,
            int? limit,
            CancellationToken ct)
        {
            await EnsureSchemaReadyAsync(ct).ConfigureAwait(false);

            var safeLimit = Math.Clamp(limit ?? 100, 1, 300);
            var conversation = await _repository.GetConversationAsync(uid, conversationId, ct).ConfigureAwait(false);
            if (conversation == null)
            {
                throw new InvalidOperationException("会话不存在或无权限");
            }

            var messages = await _repository
                .GetConversationMessagesAsync(uid, conversationId, safeLimit, ct)
                .ConfigureAwait(false);

            return new AiAssistantConversationMessagesResult
            {
                Conversation = conversation,
                Messages = messages.ToList()
            };
        }

        public async Task<AiAssistantChatResult> ChatAsync(
            long uid,
            long? conversationId,
            string message,
            CancellationToken ct)
        {
            await EnsureSchemaReadyAsync(ct).ConfigureAwait(false);

            var userMessage = message?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(userMessage))
            {
                throw new InvalidOperationException("消息不能为空");
            }

            var currentConversation = await EnsureConversationAsync(uid, conversationId, userMessage, ct).ConfigureAwait(false);
            var historyLimit = Math.Max(_options.MaxHistoryMessages, 0);
            var history = historyLimit == 0
                ? Array.Empty<AiAssistantHistoryItem>()
                : await _repository.GetHistoryMessagesAsync(uid, currentConversation.ConversationId, historyLimit, ct)
                    .ConfigureAwait(false);

            var aiResult = await _aiAssistantService
                .ChatAsync(uid, userMessage, history, ct)
                .ConfigureAwait(false);

            var strategyConfigJson = aiResult.StrategyConfig == null
                ? null
                : JsonSerializer.Serialize(aiResult.StrategyConfig);
            var suggestedQuestionsJson = aiResult.SuggestedQuestions.Count == 0
                ? null
                : JsonSerializer.Serialize(aiResult.SuggestedQuestions);

            await _repository.SaveChatExchangeAsync(
                    uid,
                    currentConversation.ConversationId,
                    userMessage,
                    aiResult.Reply,
                    strategyConfigJson,
                    suggestedQuestionsJson,
                    suggestedTitle: BuildConversationTitle(userMessage),
                    lastMessagePreview: BuildPreview(aiResult.Reply),
                    ct)
                .ConfigureAwait(false);

            var latestConversation = await _repository
                .GetConversationAsync(uid, currentConversation.ConversationId, ct)
                .ConfigureAwait(false);

            return new AiAssistantChatResult
            {
                ConversationId = currentConversation.ConversationId,
                ConversationTitle = latestConversation?.Title ?? currentConversation.Title,
                Reply = aiResult.Reply,
                StrategyConfig = aiResult.StrategyConfig,
                SuggestedQuestions = aiResult.SuggestedQuestions
            };
        }

        private async Task<AiAssistantConversationSummary> EnsureConversationAsync(
            long uid,
            long? conversationId,
            string firstUserMessage,
            CancellationToken ct)
        {
            if (conversationId.HasValue && conversationId.Value > 0)
            {
                var current = await _repository.GetConversationAsync(uid, conversationId.Value, ct).ConfigureAwait(false);
                if (current != null)
                {
                    return current;
                }

                throw new InvalidOperationException("会话不存在或无权限");
            }

            var createdConversationId = await _repository
                .CreateConversationAsync(uid, BuildConversationTitle(firstUserMessage), ct)
                .ConfigureAwait(false);
            var created = await _repository.GetConversationAsync(uid, createdConversationId, ct).ConfigureAwait(false);
            if (created == null)
            {
                throw new InvalidOperationException("会话创建成功但读取失败");
            }

            return created;
        }

        private async Task EnsureSchemaReadyAsync(CancellationToken ct)
        {
            if (_schemaReady)
            {
                return;
            }

            await _schemaLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_schemaReady)
                {
                    return;
                }

                if (_options.AutoCreateSchema)
                {
                    await _repository.EnsureSchemaAsync(ct).ConfigureAwait(false);
                    _logger.LogInformation("AI 助手会话表结构检查完成");
                }

                _schemaReady = true;
            }
            finally
            {
                _schemaLock.Release();
            }
        }

        private static string NormalizeTitle(string? title)
        {
            var value = title?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                return "新对话";
            }

            return value.Length > 128 ? value[..128] : value;
        }

        private static string BuildConversationTitle(string message)
        {
            var text = (message ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return "新对话";
            }

            if (text.Contains(WorkbenchContextMarker, StringComparison.Ordinal))
            {
                return "当前策略状态同步";
            }

            const int maxLength = 30;
            if (text.Length <= maxLength)
            {
                return text;
            }

            return text[..maxLength];
        }

        private static string? BuildPreview(string text)
        {
            var value = text?.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            const int maxLength = 255;
            if (value.Length <= maxLength)
            {
                return value;
            }

            return value[..maxLength];
        }
    }
}
