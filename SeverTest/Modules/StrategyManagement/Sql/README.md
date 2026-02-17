# SQL

该目录用于存放本模块的SQL脚本，请按功能接口或功能点拆分文件。

## 脚本清单

- `20260217_strategy_performance_cache.md`
  - 新增策略绩效缓存表 `strategy_performance_cache`。
  - 用于公开策略场景的资金曲线缓存与回测补齐结果复用。
- `20260217_strategy_position_perf_index.md`
  - 新增 `strategy_position(us_id, closed_at)` 组合索引。
  - 用于优化近 N 天仓位/平仓查询，降低资金曲线聚合扫描成本。
