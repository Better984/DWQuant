using Microsoft.Extensions.Logging;
using MySqlConnector;
using ServerTest.Infrastructure.Db;
using ServerTest.Modules.AiAssistant.Domain;

namespace ServerTest.Modules.AiAssistant.Infrastructure
{
    /// <summary>
    /// AI 助手会话与消息仓储。
    /// </summary>
    public sealed class AiAssistantChatRepository
    {
        private readonly IDbManager _db;
        private readonly ILogger<AiAssistantChatRepository> _logger;

        public AiAssistantChatRepository(
            IDbManager db,
            ILogger<AiAssistantChatRepository> logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 确保 AI 会话与消息表存在。
        /// </summary>
        public Task EnsureSchemaAsync(CancellationToken ct = default)
        {
            const string sql = @"
CREATE TABLE IF NOT EXISTS ai_chat_conversation (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  uid BIGINT UNSIGNED NOT NULL COMMENT '用户ID',
  title VARCHAR(128) NOT NULL DEFAULT '新对话' COMMENT '会话标题',
  last_message_preview VARCHAR(255) NULL COMMENT '最近一条助手回复摘要',
  created_at DATETIME(3) NOT NULL COMMENT '创建时间(UTC)',
  updated_at DATETIME(3) NOT NULL COMMENT '更新时间(UTC)',
  last_message_at DATETIME(3) NOT NULL COMMENT '最后消息时间(UTC)',
  deleted_at DATETIME(3) NULL COMMENT '删除时间(UTC)',
  PRIMARY KEY (id),
  INDEX idx_uid_last_message (uid, last_message_at, id),
  INDEX idx_uid_updated (uid, updated_at, id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='AI聊天会话表';

CREATE TABLE IF NOT EXISTS ai_chat_message (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  conversation_id BIGINT UNSIGNED NOT NULL COMMENT '会话ID',
  uid BIGINT UNSIGNED NOT NULL COMMENT '用户ID',
  role VARCHAR(16) NOT NULL COMMENT 'user/assistant/system',
  content TEXT NOT NULL COMMENT '消息内容',
  strategy_config_json LONGTEXT NULL COMMENT '助手返回的策略JSON',
  suggested_questions_json LONGTEXT NULL COMMENT '助手返回的快捷提问JSON',
  created_at DATETIME(3) NOT NULL COMMENT '消息时间(UTC)',
  PRIMARY KEY (id),
  INDEX idx_conversation_created (conversation_id, created_at, id),
  INDEX idx_uid_created (uid, created_at, id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='AI聊天消息表';
";

            return EnsureSchemaInternalAsync(sql, ct);
        }

        /// <summary>
        /// 创建新会话。
        /// </summary>
        public Task<long> CreateConversationAsync(long uid, string title, CancellationToken ct = default)
        {
            const string sql = @"
INSERT INTO ai_chat_conversation
(
  uid,
  title,
  created_at,
  updated_at,
  last_message_at
)
VALUES
(
  @uid,
  @title,
  UTC_TIMESTAMP(3),
  UTC_TIMESTAMP(3),
  UTC_TIMESTAMP(3)
);
SELECT LAST_INSERT_ID();
";
            return _db.ExecuteScalarAsync<long>(sql, new { uid, title }, null, ct);
        }

        /// <summary>
        /// 查询单个会话（按 uid 校验归属）。
        /// </summary>
        public Task<AiAssistantConversationSummary?> GetConversationAsync(
            long uid,
            long conversationId,
            CancellationToken ct = default)
        {
            const string sql = @"
SELECT
  id AS ConversationId,
  title AS Title,
  last_message_preview AS LastMessagePreview,
  created_at AS CreatedAt,
  updated_at AS UpdatedAt,
  last_message_at AS LastMessageAt
FROM ai_chat_conversation
WHERE id = @conversationId
  AND uid = @uid
  AND deleted_at IS NULL
LIMIT 1;
";
            return _db.QuerySingleOrDefaultAsync<AiAssistantConversationSummary>(
                sql,
                new { uid, conversationId },
                null,
                ct);
        }

        /// <summary>
        /// 读取用户会话列表。
        /// </summary>
        public async Task<IReadOnlyList<AiAssistantConversationSummary>> ListConversationsAsync(
            long uid,
            int limit,
            CancellationToken ct = default)
        {
            const string sql = @"
SELECT
  id AS ConversationId,
  title AS Title,
  last_message_preview AS LastMessagePreview,
  created_at AS CreatedAt,
  updated_at AS UpdatedAt,
  last_message_at AS LastMessageAt
FROM ai_chat_conversation
WHERE uid = @uid
  AND deleted_at IS NULL
ORDER BY last_message_at DESC, id DESC
LIMIT @limit;
";
            var rows = await _db.QueryAsync<AiAssistantConversationSummary>(
                    sql,
                    new { uid, limit },
                    null,
                    ct)
                .ConfigureAwait(false);

            return rows.ToList();
        }

        /// <summary>
        /// 读取会话消息（按时间正序）。
        /// </summary>
        public async Task<IReadOnlyList<AiAssistantConversationMessage>> GetConversationMessagesAsync(
            long uid,
            long conversationId,
            int limit,
            CancellationToken ct = default)
        {
            const string sql = @"
SELECT
  m.id AS MessageId,
  m.role AS Role,
  m.content AS Text,
  m.strategy_config_json AS StrategyConfigJson,
  m.suggested_questions_json AS SuggestedQuestionsJson,
  m.created_at AS CreatedAt
FROM ai_chat_message m
JOIN ai_chat_conversation c ON c.id = m.conversation_id
WHERE m.conversation_id = @conversationId
  AND c.uid = @uid
  AND c.deleted_at IS NULL
ORDER BY m.created_at DESC, m.id DESC
LIMIT @limit;
";
            var rows = await _db.QueryAsync<AiAssistantConversationMessage>(
                    sql,
                    new { uid, conversationId, limit },
                    null,
                    ct)
                .ConfigureAwait(false);

            return rows
                .OrderBy(item => item.CreatedAt)
                .ThenBy(item => item.MessageId)
                .ToList();
        }

        /// <summary>
        /// 读取模型上下文历史（user + assistant，按时间正序）。
        /// </summary>
        public async Task<IReadOnlyList<AiAssistantHistoryItem>> GetHistoryMessagesAsync(
            long uid,
            long conversationId,
            int limit,
            CancellationToken ct = default)
        {
            const string sql = @"
SELECT
  m.role AS Role,
  m.content AS Text,
  m.created_at AS CreatedAt
FROM ai_chat_message m
JOIN ai_chat_conversation c ON c.id = m.conversation_id
WHERE m.conversation_id = @conversationId
  AND c.uid = @uid
  AND c.deleted_at IS NULL
  AND m.role IN ('user', 'assistant')
ORDER BY m.created_at DESC, m.id DESC
LIMIT @limit;
";

            var rows = await _db.QueryAsync<AiAssistantHistoryRow>(
                    sql,
                    new { uid, conversationId, limit },
                    null,
                    ct)
                .ConfigureAwait(false);

            return rows
                .OrderBy(item => item.CreatedAt)
                .Select(item => new AiAssistantHistoryItem
                {
                    Role = item.Role ?? string.Empty,
                    Text = item.Text ?? string.Empty
                })
                .ToList();
        }

        /// <summary>
        /// 持久化一轮问答并更新会话摘要。
        /// </summary>
        public async Task SaveChatExchangeAsync(
            long uid,
            long conversationId,
            string userMessage,
            string assistantMessage,
            string? strategyConfigJson,
            string? suggestedQuestionsJson,
            string? suggestedTitle,
            string? lastMessagePreview,
            CancellationToken ct = default)
        {
            await using var uow = await _db.BeginUnitOfWorkAsync(ct).ConfigureAwait(false);
            try
            {
                await InsertMessageAsync(uid, conversationId, "user", userMessage, null, null, uow, ct).ConfigureAwait(false);
                await InsertMessageAsync(uid, conversationId, "assistant", assistantMessage, strategyConfigJson, suggestedQuestionsJson, uow, ct)
                    .ConfigureAwait(false);

                const string updateSql = @"
UPDATE ai_chat_conversation
SET
  title = CASE
            WHEN title IS NULL OR title = '' OR title = '新对话' THEN @suggestedTitle
            ELSE title
          END,
  last_message_preview = @lastMessagePreview,
  updated_at = UTC_TIMESTAMP(3),
  last_message_at = UTC_TIMESTAMP(3)
WHERE id = @conversationId
  AND uid = @uid
  AND deleted_at IS NULL;
";

                var affected = await _db.ExecuteAsync(
                        updateSql,
                        new
                        {
                            uid,
                            conversationId,
                            suggestedTitle,
                            lastMessagePreview
                        },
                        uow,
                        ct)
                    .ConfigureAwait(false);

                if (affected <= 0)
                {
                    throw new InvalidOperationException($"AI 会话不存在或已无权限: conversationId={conversationId}");
                }

                await uow.CommitAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await uow.RollbackAsync(ct).ConfigureAwait(false);
                _logger.LogError(
                    ex,
                    "保存 AI 对话失败: uid={Uid}, conversationId={ConversationId}",
                    uid,
                    conversationId);
                throw;
            }
        }

        private Task<int> InsertMessageAsync(
            long uid,
            long conversationId,
            string role,
            string content,
            string? strategyConfigJson,
            string? suggestedQuestionsJson,
            IUnitOfWork uow,
            CancellationToken ct)
        {
            const string insertSql = @"
INSERT INTO ai_chat_message
(
  conversation_id,
  uid,
  role,
  content,
  strategy_config_json,
  suggested_questions_json,
  created_at
)
VALUES
(
  @conversationId,
  @uid,
  @role,
  @content,
  @strategyConfigJson,
  @suggestedQuestionsJson,
  UTC_TIMESTAMP(3)
);
";
            return _db.ExecuteAsync(
                insertSql,
                new
                {
                    uid,
                    conversationId,
                    role,
                    content,
                    strategyConfigJson,
                    suggestedQuestionsJson
                },
                uow,
                ct);
        }

        private async Task EnsureSchemaInternalAsync(string createSql, CancellationToken ct)
        {
            await _db.ExecuteAsync(createSql, null, null, ct).ConfigureAwait(false);
            await EnsureSuggestedQuestionsColumnAsync(ct).ConfigureAwait(false);
        }

        private async Task EnsureSuggestedQuestionsColumnAsync(CancellationToken ct)
        {
            const string checkColumnSql = @"
SELECT COUNT(*)
FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME = 'ai_chat_message'
  AND COLUMN_NAME = 'suggested_questions_json';";

            var columnCount = await _db.ExecuteScalarAsync<int>(checkColumnSql, null, null, ct).ConfigureAwait(false);
            if (columnCount > 0)
            {
                return;
            }

            const string alterColumnSql = @"
ALTER TABLE ai_chat_message
  ADD COLUMN suggested_questions_json LONGTEXT NULL COMMENT '助手返回的快捷提问JSON' AFTER strategy_config_json;";

            try
            {
                await _db.ExecuteAsync(alterColumnSql, null, null, ct).ConfigureAwait(false);
            }
            catch (MySqlException ex) when (ex.Number == 1060)
            {
                _logger.LogWarning("AI 聊天消息表字段 suggested_questions_json 已存在，跳过重复升级");
            }
        }

        private sealed class AiAssistantHistoryRow
        {
            public string? Role { get; set; }
            public string? Text { get; set; }
            public DateTime CreatedAt { get; set; }
        }
    }
}
