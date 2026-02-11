# SQL

本目录用于存放仓位与风控相关的 SQL 脚本，统一按模块管理。

## 脚本清单

- `20260210_position_overview_upgrade.sql`
  - 新增 `strategy_position.strategy_version_id`（开仓时策略版本快照）。
  - 新增索引 `idx_uid_status_opened(uid,status,opened_at)`，优化当前持仓与最近开仓查询。
  - 新增索引 `idx_uid_version_opened(uid,strategy_version_id,opened_at)`，优化版本参与统计查询。
  - 回填历史仓位 `strategy_version_id`（优先按开仓时间推断版本，兜底 pinned 版本）。
  - 脚本为幂等执行：可重复运行。
- `20260210_position_overview_upgrade.md`
  - 与 `.sql` 同步的可审计版本（仓库默认忽略 `*.sql`）。
- `20260210_position_recent_summary_upgrade.md`
  - 新增 `strategy_position.close_price`、`strategy_position.realized_pnl` 字段。
  - 统一平仓链路写入平仓价与已实现盈亏，支持近期总结胜率/盈亏统计。
  - 脚本为幂等执行：可重复运行。
