# 追踪止损参数持久化

## 目的
重启后从 DB 恢复 trailing 参数，配合 K 线回放重算未激活仓位的 trailing 状态。

## 自动迁移
服务启动时，`PositionRiskEngine` 会调用 `StrategyPositionRepository.EnsureTrailingColumnsAsync`，若列不存在则自动执行 ALTER TABLE，无需手动执行 SQL。

## 变更
- 新增 `strategy_position.trailing_activation_pct` DECIMAL(10,6) NULL
- 新增 `strategy_position.trailing_drawdown_pct` DECIMAL(10,6) NULL

## SQL（幂等）

```sql
-- 1) 新增 trailing_activation_pct
SET @sql_add = (
  SELECT IF(
    EXISTS (
      SELECT 1 FROM information_schema.COLUMNS
      WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'strategy_position' AND COLUMN_NAME = 'trailing_activation_pct'
    ),
    'SELECT 1',
    'ALTER TABLE strategy_position ADD COLUMN trailing_activation_pct DECIMAL(10,6) NULL COMMENT ''追踪止损激活百分比'' AFTER trailing_triggered'
  )
);
PREPARE stmt FROM @sql_add;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- 2) 新增 trailing_drawdown_pct
SET @sql_add = (
  SELECT IF(
    EXISTS (
      SELECT 1 FROM information_schema.COLUMNS
      WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'strategy_position' AND COLUMN_NAME = 'trailing_drawdown_pct'
    ),
    'SELECT 1',
    'ALTER TABLE strategy_position ADD COLUMN trailing_drawdown_pct DECIMAL(10,6) NULL COMMENT ''追踪止损回撤百分比'' AFTER trailing_activation_pct'
  )
);
PREPARE stmt FROM @sql_add;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
```
