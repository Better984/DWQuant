# strategy_task_trace_log 建表脚本

```sql
CREATE TABLE IF NOT EXISTS strategy_task_trace_log (
  id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  trace_id VARCHAR(64) NOT NULL COMMENT '根链路追踪ID',
  parent_trace_id VARCHAR(64) NULL COMMENT '父链路ID（动作子链路等）',
  event_stage VARCHAR(64) NOT NULL COMMENT '阶段标识',
  event_status VARCHAR(32) NOT NULL COMMENT '阶段状态: start/success/fail/skip/degraded',
  actor_module VARCHAR(64) NOT NULL COMMENT '处理模块',
  actor_instance VARCHAR(128) NOT NULL COMMENT '处理实例（机器:进程:实例ID）',
  uid BIGINT UNSIGNED NULL COMMENT '用户ID',
  us_id BIGINT UNSIGNED NULL COMMENT '策略实例ID',
  strategy_uid VARCHAR(64) NULL COMMENT '策略字符串UID',
  exchange VARCHAR(32) NULL COMMENT '交易所',
  symbol VARCHAR(32) NULL COMMENT '交易对',
  timeframe VARCHAR(16) NULL COMMENT '周期',
  candle_timestamp BIGINT NULL COMMENT 'K线时间戳(毫秒)',
  is_bar_close TINYINT(1) NULL COMMENT '是否收线',
  method VARCHAR(64) NULL COMMENT '方法/动作',
  flow VARCHAR(32) NULL COMMENT '流程分支',
  duration_ms INT NULL COMMENT '阶段耗时(毫秒)',
  metrics_json LONGTEXT NULL COMMENT '阶段明细JSON',
  error_message VARCHAR(1024) NULL COMMENT '失败原因',
  created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) COMMENT '创建时间',
  PRIMARY KEY (id),
  INDEX idx_trace_stage (trace_id, event_stage, id),
  INDEX idx_created (created_at DESC),
  INDEX idx_market (exchange, symbol, timeframe, candle_timestamp),
  INDEX idx_usid_created (us_id, created_at DESC),
  INDEX idx_actor_created (actor_instance, created_at DESC)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='策略任务链路追踪日志';
```
