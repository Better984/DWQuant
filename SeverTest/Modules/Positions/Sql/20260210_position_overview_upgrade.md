# 2026-02-10 仓位总览升级 SQL

> 说明：仓库默认忽略 `*.sql` 文件，本说明同步保留可执行 SQL，便于版本管理与审计。

## 变更目标

1. 给 `strategy_position` 增加 `strategy_version_id`（开仓版本快照）。
2. 增加高频查询索引，优化仓位总览接口性能。
3. 回填历史数据，减少历史版本口径误差。

## 可执行 SQL

```sql
SET @db := DATABASE();

SET @has_col := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = @db
      AND TABLE_NAME = 'strategy_position'
      AND COLUMN_NAME = 'strategy_version_id'
);

SET @sql_col := IF(
    @has_col = 0,
    'ALTER TABLE strategy_position ADD COLUMN strategy_version_id BIGINT UNSIGNED NULL COMMENT ''开仓时策略版本ID快照'' AFTER us_id',
    'SELECT ''skip:add column strategy_version_id'' AS message'
);

PREPARE stmt_col FROM @sql_col;
EXECUTE stmt_col;
DEALLOCATE PREPARE stmt_col;

SET @has_idx_uid_status_opened := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = @db
      AND TABLE_NAME = 'strategy_position'
      AND INDEX_NAME = 'idx_uid_status_opened'
);

SET @sql_idx_uid_status_opened := IF(
    @has_idx_uid_status_opened = 0,
    'ALTER TABLE strategy_position ADD INDEX idx_uid_status_opened (uid, status, opened_at)',
    'SELECT ''skip:add index idx_uid_status_opened'' AS message'
);

PREPARE stmt_idx_uid_status_opened FROM @sql_idx_uid_status_opened;
EXECUTE stmt_idx_uid_status_opened;
DEALLOCATE PREPARE stmt_idx_uid_status_opened;

SET @has_idx_uid_version_opened := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = @db
      AND TABLE_NAME = 'strategy_position'
      AND INDEX_NAME = 'idx_uid_version_opened'
);

SET @sql_idx_uid_version_opened := IF(
    @has_idx_uid_version_opened = 0,
    'ALTER TABLE strategy_position ADD INDEX idx_uid_version_opened (uid, strategy_version_id, opened_at)',
    'SELECT ''skip:add index idx_uid_version_opened'' AS message'
);

PREPARE stmt_idx_uid_version_opened FROM @sql_idx_uid_version_opened;
EXECUTE stmt_idx_uid_version_opened;
DEALLOCATE PREPARE stmt_idx_uid_version_opened;

UPDATE strategy_position p
JOIN user_strategy us ON us.us_id = p.us_id AND us.uid = p.uid
SET p.strategy_version_id = COALESCE(
    (
        SELECT sv2.version_id
        FROM strategy_version sv2
        WHERE sv2.def_id = us.def_id
          AND sv2.created_at <= p.opened_at
        ORDER BY sv2.created_at DESC
        LIMIT 1
    ),
    us.pinned_version_id
)
WHERE p.strategy_version_id IS NULL;
```

