# 开仓尝试记录表

## 目的
记录每次开仓尝试（成功/失败/上限阻断），并保存信号上下文，支持：
- 查询「连续真实下单失败次数」并触发自动暂停；
- 追溯“信号命中但因最大持仓上限被阻断”的具体时间与价格。

## 表结构

```sql
CREATE TABLE IF NOT EXISTS order_open_attempt (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  uid BIGINT UNSIGNED NOT NULL COMMENT '用户ID',
  us_id BIGINT UNSIGNED NOT NULL COMMENT '策略实例ID',
  exchange VARCHAR(32) NOT NULL COMMENT '交易所',
  symbol VARCHAR(32) NOT NULL COMMENT '交易对',
  side VARCHAR(16) NOT NULL COMMENT '方向: Long/Short',
  success TINYINT(1) NOT NULL COMMENT '1=成功 0=失败',
  error_message VARCHAR(512) NULL COMMENT '失败时错误信息',
  attempt_type VARCHAR(32) NOT NULL DEFAULT 'order_result' COMMENT '事件类型: order_result/blocked_max_position',
  signal_time DATETIME(3) NULL COMMENT '信号命中时间（UTC）',
  signal_price DECIMAL(20,8) NULL COMMENT '信号命中时参考价',
  max_position_qty DECIMAL(20,8) NULL COMMENT '策略配置最大持仓',
  current_open_qty DECIMAL(20,8) NULL COMMENT '阻断时当前同向持仓',
  request_order_qty DECIMAL(20,8) NULL COMMENT '本次计划开仓数量',
  created_at DATETIME(3) NOT NULL COMMENT '尝试时间',
  PRIMARY KEY (id),
  INDEX idx_usid_created (us_id, created_at DESC),
  INDEX idx_usid_type_created (us_id, attempt_type, created_at DESC)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='开仓尝试记录（成功/失败/上限阻断）';
```

## 索引说明
- `idx_usid_created`：按策略实例查最近 N 次尝试，用于统计连续失败数。
- `idx_usid_type_created`：按策略实例 + 事件类型快速读取最近事件，避免阻断事件干扰连续失败统计。

## 字段语义
- `attempt_type=order_result`：真实开仓下单结果（成功或下单失败），用于连续失败统计。
- `attempt_type=blocked_max_position`：命中信号但被 `MaxPositionQty` 阻断，不计入连续失败暂停。
- `signal_time/signal_price`：策略命中信号时的时间与参考价，便于定位“为何未开仓”。
- `max_position_qty/current_open_qty/request_order_qty`：阻断时的上限与持仓快照。

## 自动迁移
服务启动时 `OrderOpenAttemptRepository.EnsureSchemaAsync` 会创建表，并对历史版本自动补列/补索引，无需手动执行。
