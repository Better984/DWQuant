# AdminBroadcast 模块协议

### admin.broadcast.send
- 路径：`POST /api/admin/broadcast`
- data：
  - `category` string
  - `severity` string
  - `template` string
  - `payload` object?
  - `target` string（默认 AllUsers）
- 响应 data：`{ notificationId, recipients }`

### admin.position.risk.index
- 路径：`POST /api/admin/position-risk/index`
- data：
  - `exchange` string?（可选，交易所过滤）
  - `symbol` string?（可选，交易对过滤）
- 响应 data：
  - `generatedAt` string（快照时间，UTC）
  - `items` array（交易对维度的风控索引快照，含树结构与仓位详情）

### admin.strategy.running.list
- 路径：`POST /api/admin/strategy/running/list`
- 说明：超级管理员专用，获取运行中策略（running/paused_open_position/testing）分页列表，用于服务器实盘情况展示。
- data：
  - `page` int?（页码，从 1 开始，默认 1）
  - `pageSize` int?（每页条数，默认 100，最大 100）
- 响应 data：
  - `total` int（总条数）
  - `items` array（当前页策略数组，每项含 `uid/usId/defId/defName/aliasName/description/state/versionNo/updatedAt/exchangeApiKeyId`）

### admin.strategy.running.by-market
- 路径：`POST /api/admin/strategy/running/by-market`
- 说明：超级管理员专用，按市场聚合运行中策略。为避免大表扫描，不再查询 `strategy_task_trace_log`。
- data：
  - `machineName` string（必填，兼容字段，当前不参与聚合计算）
- 响应 data：array（市场列表，每项含 `exchange/symbol/timeframe/strategyCount/lastRunAt`；`lastRunAt` 可能为空）

### admin.strategy.running.list-by-market
- 路径：`POST /api/admin/strategy/running/list-by-market`
- 说明：超级管理员专用，按市场筛选运行中策略，支持分页和搜索。
- data：
  - `exchange` string（必填）
  - `symbol` string（必填）
  - `timeframe` string（必填，如 1m、5m）
  - `page` int?（默认 1）
  - `pageSize` int?（默认 100，最大 100）
  - `search` string?（可选，搜索 aliasName/defName/usId）
- 响应 data：
  - `total` int（总条数）
  - `items` array（策略数组，同 admin.strategy.running.list 的 items 结构）

### admin.strategy.task.trace.market-summary
- 路径：`POST /api/admin/strategy/task-trace/market-summary`
- 说明：超级管理员专用，获取指定市场的任务执行报告（已切换为 `strategy_engine_run_log` 主记录模式，避免依赖 `strategy_task_trace_log` 大表聚合）。
- data：
  - `machineName` string（必填）
  - `exchange` string（必填）
  - `symbol` string（必填）
  - `timeframe` string（必填）
- 响应 data：`exchange/symbol/timeframe/taskCount/avgDurationMs/successRatePct/stageStats/recentOrders/recentTasks`
  - `stageStats`：分段统计（`strategy.lookup`、`strategy.indicator`、`strategy.execute`）
  - `recentOrders`：最近订单样本（从主记录内 `openOrderIds` 解析）
  - `recentTasks`：最近任务样本（默认 5 条，包含 `traceId/runStatus/lookupMs/indicatorMs/executeMs/matchedCount/executedCount/openTaskCount/openTaskTraceIds/openOrderIds` 等字段）

### admin.strategy.run.metrics.recent
- 路径：`POST /api/admin/strategy/run-metrics/recent`
- 说明：超级管理员专用，获取指定策略实例最近运行画像（默认最近 5 条，用于策略详情弹窗）。
- data：
  - `machineName` string（必填）
  - `usId` long（必填，策略实例ID）
  - `limit` int?（可选，默认 5，最大 20）
- 响应 data：array（每项含 `runAt/exchange/symbol/timeframe/isBarClose/durationMs/matchedCount/executedCount/skippedCount/conditionEvalCount/actionExecCount/openTaskCount/successRatePct/openTaskRatePct/runStatus/traceId/engineInstance/lookupMs/indicatorMs/executeMs/perStrategySamples/perStrategyAvgMs/perStrategyMaxMs`）

### admin.strategy.task.trace.list
- 路径：`POST /api/admin/strategy/task-trace/list`
- 说明：超级管理员专用，获取指定服务器（按 machineName）的实盘任务按 trace_id 聚合分页列表。每个任务一行：交易所、周期、币对、策略数、总耗时等。
- data：
  - `machineName` string（必填，机器名，用于匹配 actor_instance 前缀）
  - `page` int?（页码，从 1 开始，默认 1）
  - `pageSize` int?（每页条数，默认 100，最大 100）
- 响应 data：
  - `total` int（任务总数）
  - `items` array（当前页任务摘要，每项含 `traceId/exchange/symbol/timeframe/candleTimestamp/strategyCount/totalDurationMs/firstCreatedAt/lastCreatedAt`）

### admin.strategy.task.trace.layout
- 路径：`POST /api/admin/strategy/task-trace/layout`
- 说明：超级管理员专用，获取指定服务器（按 machineName）的实盘布局，按 exchange/symbol/timeframe 聚合。数据源为 `strategy_engine_run_log` 主记录（不再扫描 `strategy_task_trace_log`）。每个市场一行：交易所、币对、周期、策略数、任务数、总耗时、最后执行时间。
- data：
  - `machineName` string（必填）
- 响应 data：array（布局项，每项含 `exchange/symbol/timeframe/strategyCount/taskCount/totalDurationMs/lastExecutedAt`）

### admin.strategy.task.trace.detail
- 路径：`POST /api/admin/strategy/task-trace/detail`
- 说明：超级管理员专用，获取指定 trace_id 的完整链路明细，用于点击任务后的详情弹窗。
- data：
  - `traceId` string（必填）
- 响应 data：array（该任务的全链路追踪记录，每项含 `id/traceId/eventStage/eventStatus/actorModule/actorInstance/uid/usId/exchange/symbol/timeframe/method/flow/durationMs/metricsJson/errorMessage/createdAt` 等）

### admin.user.test.capability
- 路径：`POST /api/admin/user/test/capability`
- 说明：查询超级测试台是否启用。
- 响应 data：
  - `enabled` bool

### admin.user.test.create-user
- 路径：`POST /api/admin/user/test/create-user`
- 说明：创建测试用户。
- data：
  - `email` string
  - `password` string
  - `nickname` string?
- 响应 data：
  - `uid` long
  - `email` string
  - `nickname` string
  - `role` int
  - `status` int

### admin.user.test.create-random-strategy
- 路径：`POST /api/admin/user/test/create-random-strategy`
- 说明：给目标用户创建随机策略，可选切换到 `testing`。
- data：
  - `targetUid` long
  - `autoSwitchToTesting` bool?
  - `conditionCount` int?（开多/开空条件数量，默认 5，范围 1～20）
  - `preferredExchange` string?
  - `preferredSymbol` string?
  - `preferredTimeframeSec` int?
- 响应 data：
  - `strategy`：`defId/usId/versionId/versionNo/name/state`
  - `random`：`entryMethod/exitMethod/leftIndicator/rightIndicator/timeframe/exchange/symbol`
  - `testing`：`requested/success/message/state`

### admin.user.test.delete-test-strategies
- 路径：`POST /api/admin/user/test/delete-test-strategies`
- 说明：一键删除目标用户下所有超级测试台创建的测试策略。
- data：
  - `targetUid` long（必填）
- 响应 data：
  - `targetUid` long
  - `deleted` int（成功删除数量）
  - `failed` int（删除失败数量）
  - `elapsedMs` long
  - `failures` array（失败样本，最多 10 条）

### admin.user.test.batch-create-testing-strategies
- 路径：`POST /api/admin/user/test/batch-create-testing-strategies`
- 说明：批量创建随机策略并切换 testing，用于快速生成测试策略。
- data：
  - `targetUid` long（必填）
  - `strategyCount` int（默认 10，最大 1000）
  - `conditionCount` int?（开多/开空条件数量，默认 5，范围 1～20）
  - `preferredExchange` string?
  - `preferredSymbol` string?
  - `preferredTimeframeSec` int?
- 响应 data：
  - `targetUid` long
  - `requested` int
  - `created` int
  - `failed` int
  - `switchSuccess` int
  - `switchFailed` int
  - `elapsedMs` long
  - `failures` array（失败样本，最多 10 条）

### admin.user.test.backtest-stress
- 路径：`POST /api/admin/user/test/backtest-stress`
- 说明：超级测试台回测压测入口，批量创建随机策略并并发提交回测任务。
- data：
  - `targetUid` long（必填）
  - `strategyCount` int（默认 20，最大 500）
  - `conditionCount` int?（开多/开空条件数量，默认 5，范围 1～20）
  - `tasksPerStrategy` int（默认 1，最大 20）
  - `submitParallelism` int（默认 4，最大 64）
  - `barCount` int（默认 1500，范围 100~500000）
  - `initialCapital` decimal（默认 10000）
  - `executionMode` string?（`batch_open_close`/`timeline`）
  - `includeDetailedOutput` bool（默认 false）
  - `pollAfterSubmitSeconds` int（默认 5，最大 120）
  - `preferredExchange` string?
  - `preferredSymbol` string?
  - `preferredTimeframeSec` int?
- 响应 data：
  - `requested`：压测参数与总任务数。
  - `strategies`：策略创建统计、创建耗时分位与失败样本。
  - `submissions`：任务提交统计、提交耗时分位（P50/P95/P99）、线程参与信息与失败样本。
  - `taskStatus`：即时状态分布与等待后状态分布。
  - `runtime`：压测前/提交后/等待后运行时快照（线程池、内存、进程线程数）。
