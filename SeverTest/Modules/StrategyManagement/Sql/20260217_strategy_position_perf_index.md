# 20260217 strategy_position 性能索引

## 变更目标
- 优化策略绩效缓存在近 30/90 天窗口内读取仓位明细与平仓时间过滤的查询性能。

## SQL

```sql
ALTER TABLE strategy_position
ADD INDEX idx_usid_closed_time (us_id, closed_at);
```

## 说明
- 该索引与已有 `idx_usid_time(us_id, opened_at)` 互补：
  - 开仓时间过滤走 `idx_usid_time`；
  - 平仓时间过滤走 `idx_usid_closed_time`。
- 适用场景：
  - `StrategyPerformanceCacheService` 的实盘指纹统计、日志快照构建。
