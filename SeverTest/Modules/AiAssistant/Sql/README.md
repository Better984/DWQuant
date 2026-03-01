该目录用于存放 AiAssistant 模块相关 SQL 说明与建表脚本。

## 当前脚本
- `20260301_ai_chat_schema.md`
  - AI 聊天会话与消息持久化表结构。
  - 支持多会话、历史消息加载、跨设备续聊。
  - 脚本为幂等写法（`CREATE TABLE IF NOT EXISTS`），可重复执行。

## 说明
- 服务端默认可通过 `AiAssistant.AutoCreateSchema=true` 自动建表。
- 生产环境建议仍通过脚本管理版本变更，便于审计与回滚。
