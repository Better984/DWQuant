# 2026-02-10 仓位近期总结升级（平仓盈亏字段）

## 变更目标
- 为 `strategy_position` 增加平仓成交价与已实现盈亏字段，支持近期总结中的胜率与已实现盈亏统计。
- 保证平仓链路可回写 `close_price` 与 `realized_pnl`。

## SQL（幂等）

```sql
-- 1) 新增 close_price 字段（幂等）
SET @sql_add_close_price = (
  SELECT IF(
    EXISTS (
      SELECT 1
      FROM information_schema.COLUMNS
      WHERE TABLE_SCHEMA = DATABASE()
        AND TABLE_NAME = 'strategy_position'
        AND COLUMN_NAME = 'close_price'
    ),
    'SELECT 1',
    'ALTER TABLE strategy_position ADD COLUMN close_price DECIMAL(18,8) NULL COMMENT ''平仓成交均价'' AFTER close_reason'
  )
);
PREPARE stmt FROM @sql_add_close_price;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- 2) 新增 realized_pnl 字段（幂等）
SET @sql_add_realized_pnl = (
  SELECT IF(
    EXISTS (
      SELECT 1
      FROM information_schema.COLUMNS
      WHERE TABLE_SCHEMA = DATABASE()
        AND TABLE_NAME = 'strategy_position'
        AND COLUMN_NAME = 'realized_pnl'
    ),
    'SELECT 1',
    'ALTER TABLE strategy_position ADD COLUMN realized_pnl DECIMAL(20,8) NULL COMMENT ''已实现盈亏（按平仓价）'' AFTER close_price'
  )
);
PREPARE stmt FROM @sql_add_realized_pnl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
```

## 说明
- 历史数据若缺少 `close_price`，无法精准回算 `realized_pnl`，可保留 `NULL`。
- 新开平仓链路会在平仓成功后写入 `close_price`，并实时计算 `realized_pnl`。
