# Backtest 模块 SQL 说明

## 表：`backtest_task`

用于持久化回测异步任务、执行进度与结果。

### 核心字段
- `task_id`：任务主键。
- `user_id`：任务所属用户。
- `req_id`：关联协议请求 ID。
- `status`：任务状态（`queued` / `running` / `completed` / `failed` / `cancelled`）。
- `progress` / `stage` / `stage_name` / `message`：执行进度信息。
- `request_json` / `result_json`：请求与结果 JSON。
- `assigned_worker_id`：当前执行该任务的算力节点 ID（分布式回测新增）。

### 分布式回测相关索引
- `idx_assigned_worker (assigned_worker_id, status)`：用于按 worker 维度排查与回收任务。
- `idx_status (status)`：用于队列任务筛选。

## 本次变更

### 1. 新增字段
- `assigned_worker_id VARCHAR(128) NULL`
  - 含义：记录任务当前绑定的算力节点，便于观测、追踪和故障回收。
  - 任务完成/失败/取消时会清空该字段。

### 2. 兼容升级 SQL

```sql
-- 先检查字段是否存在（兼容旧版本 MySQL，不使用 ADD COLUMN IF NOT EXISTS）
SELECT COUNT(*)
FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME = 'backtest_task'
  AND COLUMN_NAME = 'assigned_worker_id';

ALTER TABLE backtest_task
  ADD COLUMN assigned_worker_id VARCHAR(128) NULL COMMENT '当前处理该任务的算力节点ID' AFTER req_id;

-- 再检查索引是否存在
SELECT COUNT(*)
FROM information_schema.STATISTICS
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME = 'backtest_task'
  AND INDEX_NAME = 'idx_assigned_worker';

ALTER TABLE backtest_task
  ADD INDEX idx_assigned_worker (assigned_worker_id, status);
```

### 3. 抢占执行语义
- 核心节点与本地 worker 均通过“原子抢占 queued 任务”的方式执行，避免多实例重复消费。
- 在支持 `FOR UPDATE SKIP LOCKED` 的 MySQL 版本上优先使用该机制。
