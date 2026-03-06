# AI 聊天会话与消息表

## 目的
- 为 AI 聊天提供持久化能力。
- 支持一个用户维护多个会话。
- 支持退出登录/更换设备后继续历史对话。

## 表结构

```sql
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
```

## 字段说明
- `ai_chat_conversation.title`：会话标题，默认 `新对话`，首轮提问后可自动更新。
- `ai_chat_conversation.last_message_preview`：列表摘要显示字段，减少前端额外拼装。
- `ai_chat_message.strategy_config_json`：助手返回策略 JSON 的原文，便于历史回放展示。
- `ai_chat_message.suggested_questions_json`：助手返回的快捷提问列表 JSON，前端可直接渲染按钮。

## 索引说明
- `idx_uid_last_message`：按用户读取会话列表时使用。
- `idx_conversation_created`：按会话加载历史消息时使用。
- `idx_uid_created`：按用户做消息审计/排查时使用。

## 自动迁移
- 服务启动后首次访问 AI 会话接口时，`AiAssistantConversationService` 会触发 `AiAssistantChatRepository.EnsureSchemaAsync` 自动建表（可通过配置关闭）。
- 若旧库缺少 `suggested_questions_json` 字段，仓储会先查询 `information_schema.COLUMNS`，再执行 `ALTER TABLE` 最小化升级，兼容旧版本 MySQL。
