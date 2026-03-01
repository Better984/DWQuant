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
        /// 模型服务地址（DeepSeek OpenAI 兼容地址）。
        /// </summary>
        public string BaseUrl { get; set; } = "https://api.deepseek.com";

        /// <summary>
        /// 模型服务密钥。
        /// </summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// 对话模型名称。
        /// </summary>
        public string Model { get; set; } = "deepseek-chat";

        /// <summary>
        /// HTTP 超时秒数。
        /// </summary>
        public int TimeoutSeconds { get; set; } = 60;

        /// <summary>
        /// 采样温度，越低越稳定。
        /// </summary>
        public double Temperature { get; set; } = 0.2;

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
