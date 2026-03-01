using ServerTest.Models.Strategy;

namespace ServerTest.Modules.AiAssistant.Domain
{
    /// <summary>
    /// AI 助手单条历史消息。
    /// </summary>
    public sealed class AiAssistantHistoryItem
    {
        public string Role { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }

    /// <summary>
    /// AI 助手聊天请求。
    /// </summary>
    public sealed class AiAssistantChatRequest
    {
        /// <summary>
        /// 会话ID。为空时由服务端自动创建新会话。
        /// </summary>
        public long? ConversationId { get; set; }

        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 兼容旧版前端透传历史字段，新版由服务端持久化历史生成上下文。
        /// </summary>
        public List<AiAssistantHistoryItem>? History { get; set; }
    }

    /// <summary>
    /// AI 助手聊天结果。
    /// </summary>
    public sealed class AiAssistantChatResult
    {
        public long ConversationId { get; set; }
        public string ConversationTitle { get; set; } = "新对话";
        public string Reply { get; set; } = string.Empty;
        public StrategyConfig? StrategyConfig { get; set; }
    }

    /// <summary>
    /// 创建会话请求。
    /// </summary>
    public sealed class AiAssistantConversationCreateRequest
    {
        public string? Title { get; set; }
    }

    /// <summary>
    /// 会话列表请求。
    /// </summary>
    public sealed class AiAssistantConversationListRequest
    {
        public int? Limit { get; set; }
    }

    /// <summary>
    /// 会话消息查询请求。
    /// </summary>
    public sealed class AiAssistantConversationMessagesRequest
    {
        public long ConversationId { get; set; }
        public int? Limit { get; set; }
    }

    /// <summary>
    /// 会话摘要。
    /// </summary>
    public sealed class AiAssistantConversationSummary
    {
        public long ConversationId { get; set; }
        public string Title { get; set; } = "新对话";
        public string? LastMessagePreview { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime LastMessageAt { get; set; }
    }

    /// <summary>
    /// 会话消息。
    /// </summary>
    public sealed class AiAssistantConversationMessage
    {
        public long MessageId { get; set; }
        public string Role { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string? StrategyConfigJson { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// 会话详情。
    /// </summary>
    public sealed class AiAssistantConversationMessagesResult
    {
        public AiAssistantConversationSummary Conversation { get; set; } = new();
        public List<AiAssistantConversationMessage> Messages { get; set; } = new();
    }
}
