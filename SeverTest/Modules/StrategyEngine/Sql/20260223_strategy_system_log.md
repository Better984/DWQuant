# 策略系统事件日志表

## 目的
记录系统触发的策略事件（如连续开仓失败自动暂停），便于前端展示与审计。

## 表结构

```sql
CREATE TABLE IF NOT EXISTS strategy_system_log (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  us_id BIGINT UNSIGNED NOT NULL COMMENT '策略实例ID',
  uid BIGINT UNSIGNED NOT NULL COMMENT '用户ID',
  event_type VARCHAR(64) NOT NULL COMMENT '事件类型: paused_open_fail 等',
  message VARCHAR(512) NOT NULL COMMENT '事件描述',
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) COMMENT '创建时间',
  PRIMARY KEY (id),
  INDEX idx_usid_created (us_id, created_at DESC)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='策略系统事件日志（如连续开仓失败暂停）';
```

## 自动迁移
服务启动时 `StrategySystemLogRepository.EnsureSchemaAsync` 会创建表，无需手动执行。

## MySQL MCP 手动执行（可选）
若需通过 MySQL MCP 直接执行：

```sql
CREATE TABLE IF NOT EXISTS strategy_system_log (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  us_id BIGINT UNSIGNED NOT NULL COMMENT '策略实例ID',
  uid BIGINT UNSIGNED NOT NULL COMMENT '用户ID',
  event_type VARCHAR(64) NOT NULL COMMENT '事件类型: paused_open_fail 等',
  message VARCHAR(512) NOT NULL COMMENT '事件描述',
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) COMMENT '创建时间',
  PRIMARY KEY (id),
  INDEX idx_usid_created (us_id, created_at DESC)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='策略系统事件日志（如连续开仓失败暂停）';
```
