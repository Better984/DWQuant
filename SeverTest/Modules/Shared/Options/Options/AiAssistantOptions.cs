namespace ServerTest.Options
{
    /// <summary>
    /// AI 助手配置。
    /// </summary>
    public sealed class AiAssistantOptions
    {
        /// <summary>
        /// 是否启用 AI 助手。
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 是否自动建表（会话与消息持久化）。
        /// </summary>
        public bool AutoCreateSchema { get; set; } = true;

        /// <summary>
        /// 腾讯元器开放平台地址。
        /// </summary>
        public string BaseUrl { get; set; } = "https://open.hunyuan.tencent.com";

        /// <summary>
        /// 腾讯元器调用 Token。
        /// </summary>
        public string Token { get; set; } = string.Empty;

        /// <summary>
        /// 腾讯元器智能体 ID。
        /// </summary>
        public string AssistantId { get; set; } = string.Empty;

        /// <summary>
        /// 智能体态，默认使用已发布版本。
        /// </summary>
        public string ChatType { get; set; } = "published";

        /// <summary>
        /// HTTP 超时秒数。
        /// </summary>
        public int TimeoutSeconds { get; set; } = 60;

        /// <summary>
        /// 知识库文档路径（相对路径基于 SeverTest 根目录）。
        /// </summary>
        public string KnowledgeFilePath { get; set; } = "Document/AI助手知识库.md";

        /// <summary>
        /// 发送给模型的最大历史消息条数。
        /// </summary>
        public int MaxHistoryMessages { get; set; } = 8;
    }
}
