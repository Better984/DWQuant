# Backtest 模块协议

---

## 同步回测（兼容旧接口）

### backtest.run
- 路径：`POST /api/backtest/run`
- 说明：同步执行回测，等待结果返回。适用于小规模回测或调试场景。
- data：
  - `usId` long?（策略实例 ID，可选）
  - `configJson` string?（策略配置 JSON，优先于 usId）
  - `exchange` string?（交易所，默认取策略）
  - `symbols` string[]?（标的列表，默认取策略）
  - `timeframe` string?（周期，如 1m/5m，默认取策略）
  - `startTime` string?（yyyy-MM-dd HH:mm:ss，需与 endTime 同时提供）
  - `endTime` string?（yyyy-MM-dd HH:mm:ss，需与 startTime 同时提供）
  - `barCount` int?（不传时间范围时使用）
  - `initialCapital` decimal（初始资金）
  - `orderQtyOverride` decimal?（覆盖 trade.sizing.orderQty）
  - `leverageOverride` int?（覆盖杠杆）
  - `takeProfitPctOverride` decimal?（覆盖止盈百分比）
  - `stopLossPctOverride` decimal?（覆盖止损百分比）
  - `feeRate` decimal（默认 0.0004）
  - `fundingRate` decimal（资金费率，默认 0，每 8 小时结算一次）
  - `slippageBps` int（固定滑点 bps）
  - `autoReverse` bool（是否自动反向）
  - `runtime` StrategyRuntimeConfig?（运行时间配置，可覆盖策略）
  - `useStrategyRuntime` bool（是否启用运行时间门禁）
  - `executionMode` string?（执行模式，可选：`batch_open_close`/`timeline`，默认由服务端配置决定）
  - `output` BacktestOutputOptions?（输出裁剪选项）
    - `includeTrades` bool
    - `includeEquityCurve` bool
    - `includeEvents` bool
    - `equityCurveGranularity` string（资金曲线输出粒度：`1m/15m/1h/4h/1d/3d/7d`，默认 `1m`）

- 响应 data：`BacktestRunResult`
  - `exchange` / `timeframe`
  - `equityCurveGranularity`（资金曲线实际输出粒度）
  - `startTimestamp` / `endTimestamp`
  - `totalBars`
  - `durationMs`（回测耗时，毫秒）
  - `totalStats`（汇总统计，字段见下方"统计指标"）
  - `symbols[]`：`BacktestSymbolResult`
    - `symbol`
    - `bars`
    - `initialCapital`
    - `stats`（统计指标，字段见下方"统计指标"）
    - `tradeSummary`（交易明细关键指标）
      - `totalCount` / `winCount` / `lossCount`
      - `maxProfit` / `maxLoss` / `totalFee`
      - `firstEntryTime` / `lastExitTime`
    - `equitySummary`（资金曲线关键指标）
      - `pointCount`
      - `maxEquity` / `maxEquityAt`
      - `minEquity` / `minEquityAt`
      - `maxPeriodProfit` / `maxPeriodProfitAt`
      - `maxPeriodLoss` / `maxPeriodLossAt`
    - `eventSummary`（事件日志关键指标）
      - `totalCount`
      - `firstTimestamp` / `lastTimestamp`
      - `typeCounts`（按类型统计）
    - `tradesRaw` string[]（可裁剪，单条交易 JSON 字符串，前端按需解析）
    - `equityCurveRaw` string[]（可裁剪，单条曲线点 JSON 字符串，前端按需解析）
      - 字段同 `BacktestEquityPoint`：`timestamp` / `equity` / `realizedPnl` / `unrealizedPnl` / `periodRealizedPnl` / `periodUnrealizedPnl`
    - `eventsRaw` string[]（可裁剪，单条事件 JSON 字符串，前端按需解析）

> 说明：回测结束会按最后一根 K 线收盘价强制平仓，便于统计输出。
> 为避免前端全量反序列化卡顿，交易明细/资金曲线/事件日志改为字符串数组输出，前端仅在需要展示的行进行解析。
> 服务端执行模型为“时间轴串行 + 同时间点多 symbol 并行”，确保时序正确的同时提升单任务吞吐。
> 当执行模式为 `batch_open_close` 时，服务端采用“批量开仓检测 + 并行统一平仓”的高速链路，适用于大样本快速回测。

### 统计指标（`BacktestStats`）

#### 基础指标
| 字段 | 类型 | 说明 |
|---|---|---|
| `totalProfit` | decimal | 总收益（交易 PnL 求和，已扣手续费） |
| `totalReturn` | decimal | 总收益率（TotalProfit / 初始资金） |
| `maxDrawdown` | decimal | 最大回撤（权益曲线峰值回撤比例） |
| `winRate` | decimal | 胜率（盈利交易数 / 交易总数） |
| `tradeCount` | int | 交易次数 |
| `avgProfit` | decimal | 平均盈亏（TotalProfit / TradeCount） |
| `profitFactor` | decimal | 盈亏比（盈利总和 / |亏损总和|） |
| `avgWin` | decimal | 平均盈利 |
| `avgLoss` | decimal | 平均亏损 |

#### 高级指标
| 字段 | 类型 | 说明 |
|---|---|---|
| `sharpeRatio` | decimal | 夏普比率（年化，无风险利率 = 0） |
| `sortinoRatio` | decimal | Sortino 比率（年化，仅下行波动率） |
| `annualizedReturn` | decimal | 年化收益率 |
| `maxConsecutiveLosses` | int | 最大连续亏损次数 |
| `maxConsecutiveWins` | int | 最大连续盈利次数 |
| `avgHoldingMs` | long | 平均持仓时间（毫秒） |
| `maxDrawdownDurationMs` | long | 最大回撤持续时间（毫秒） |
| `calmarRatio` | decimal | Calmar 比率（年化收益率 / 最大回撤） |

---

## 异步任务队列

### backtest.submit
- 路径：`POST /api/backtest/submit`
- 说明：提交回测任务到异步队列，立即返回 `taskId`。前端通过轮询或 WebSocket 跟踪进度。
- 鉴权：需要 Bearer Token。
- data：同 `backtest.run` 的请求参数。
- 响应 data：`BacktestTaskSummary`
  - `taskId` long（任务唯一标识）
  - `status` string（`queued`）
  - `progress` decimal（0）
  - `stage` string?
  - `stageName` string?
  - `message` string?
  - `errorMessage` string?
  - `exchange` string
  - `timeframe` string
  - `symbols` string（逗号分隔）
  - `barCount` int
  - `tradeCount` int
  - `durationMs` long
  - `createdAt` datetime
  - `startedAt` datetime?
  - `completedAt` datetime?

- 错误响应：
  - 超出单用户并发限制：`当前有 N 个回测任务进行中，单用户最多 M 个`
  - 队列已满：`回测队列已满（N/M），请稍后重试`
  - 超出 bar 数量限制：`请求 barCount (N) 超过系统限制 M`

### backtest.task.status
- 路径：`GET /api/backtest/task/{taskId}/status`
- 说明：查询回测任务当前状态与进度，用于 HTTP 轮询。
- 鉴权：需要 Bearer Token，仅能查看自己的任务。
- 响应 data：`BacktestTaskSummary`（字段同上）
- 错误响应：
  - 任务不存在：404

### backtest.task.result
- 路径：`GET /api/backtest/task/{taskId}/result`
- 说明：获取已完成回测任务的完整结果（`BacktestRunResult` JSON）。
- 鉴权：需要 Bearer Token，仅能查看自己的任务。
- 响应：`BacktestRunResult` 原始 JSON（Content-Type: application/json）
- 错误响应：
  - 任务不存在：404
  - 任务尚未完成：`任务尚未完成，当前状态: {status}`

### backtest.tasks
- 路径：`GET /api/backtest/tasks?limit=20`
- 说明：列出用户的回测任务历史（简要信息，不含 result_json）。
- 鉴权：需要 Bearer Token。
- 参数：
  - `limit` int（可选，默认 20）
- 响应 data：`BacktestTaskSummary[]`

### backtest.tasks.active
- 路径：`GET /api/backtest/tasks/active?limit=20`
- 说明：仅返回当前用户进行中的回测任务（`queued/running`），用于页面初始化时快速恢复任务状态。
- 鉴权：需要 Bearer Token。
- 参数：
  - `limit` int（可选，默认 20）
- 响应 data：`BacktestTaskSummary[]`

### backtest.task.cancel
- 路径：`POST /api/backtest/task/{taskId}/cancel`
- 说明：取消排队中或运行中的回测任务。
- 鉴权：需要 Bearer Token，仅能取消自己的任务。
- 响应 data：`"已取消"`
- 错误响应：
  - 任务不存在或无法取消：`任务不存在或无法取消`

---

## 任务状态流转

```
queued → running → completed
                 → failed
       → cancelled
```

| 状态 | 说明 |
|---|---|
| `queued` | 已入队，等待 Worker 取出执行 |
| `running` | Worker 已开始执行回测 |
| `completed` | 回测成功完成，结果已写入 `result_json` |
| `failed` | 回测执行失败，`error_message` 包含错误信息 |
| `cancelled` | 用户手动取消 |

---

## WebSocket 推送

### backtest.progress
- type：`backtest.progress`
- 触发条件：`backtest.run` 或异步任务执行期间，且用户存在可用 WebSocket 连接时。
- 关联方式：
  - 推送消息与 HTTP 请求共用 `reqId`；
  - 前端应按 `reqId` 过滤，只消费当前回测任务的进度流。
- data：`BacktestProgressMessage`
  - `eventKind` string：`stage` 或 `positions`
  - `stage` string：阶段编码（如 `parse_request`、`execution_mode`、`main_loop`、`batch_open_phase`、`batch_close_phase`、`collect_positions`、`completed`、`failed`）
  - `stageName` string：阶段名称（用于前端展示）
  - `message` string?：阶段说明
  - `processedBars` int?：已处理 bar 数
  - `totalBars` int?：总 bar 数
  - `progress` decimal?：进度比例（0~1）
  - `elapsedMs` long?：当前阶段耗时（毫秒）
  - `foundPositions` int?：已汇总仓位/交易数量
  - `totalPositions` int?：预估总仓位/交易数量
  - `chunkCount` int?：本次增量仓位条数
  - `winCount` int?：当前已平仓胜场数（平仓阶段）
  - `lossCount` int?：当前已平仓负场数（平仓阶段）
  - `winRate` decimal?：当前胜率（0~1，平仓阶段）
  - `completed` bool?：当前阶段是否完成
  - `symbol` string?：增量所属标的
  - `positions` BacktestTrade[]?：增量仓位列表（测试阶段用于前端快速预览）
  - `replacePositions` bool?：前端是否应使用本次 `positions` 覆盖当前预览（用于“最近100条窗口”）

> 说明：仓位汇总阶段采用 0.1 秒节流推送，长耗时时前端可先显示部分仓位，再等待最终完整结果。
> 
> 批量模式新增体验：
> - `batch_open_phase`：实时推送“正在检测开仓”进度，并附带已获取开仓数量；
> - `batch_open_phase` 完成：消息会明确“开仓数量检测完毕，共 N 个仓位”；
> - `batch_close_phase`：优先检测最近仓位，推送平仓进度与实时胜率；
> - `batch_close_phase` 可携带最近 100 条仓位预览，`replacePositions=true` 时前端应覆盖显示该窗口。
> - `processedBars` 在 `batch_open_phase` 表示已检测开仓检查点数量；在 `batch_close_phase` 表示已处理候选仓位数量。
> - `foundPositions` 在 `batch_close_phase` 表示当前已完成平仓并纳入统计的仓位数量。

---

## 前端对接建议

### HTTP 轮询模式
1. 调用 `POST /api/backtest/submit` 获取 `taskId`。
2. 定时调用 `GET /api/backtest/task/{taskId}/status`（建议 2~5 秒间隔）。
3. 当 `status == "completed"` 时，调用 `GET /api/backtest/task/{taskId}/result` 获取完整结果。
4. 当 `status == "failed"` 时，展示 `errorMessage`。

### WebSocket 模式
1. 调用 `POST /api/backtest/submit` 获取 `taskId` 和 `reqId`。
2. 监听 `backtest.progress` 消息，按 `reqId` 过滤。
3. 收到 `stage == "completed"` 时，调用 `GET /api/backtest/task/{taskId}/result` 获取完整结果。

### 混合模式（推荐）
- 优先使用 WebSocket 实时获取进度。
- 页面重新加载时回退到 HTTP 轮询恢复任务状态。
- 调用 `GET /api/backtest/tasks` 展示历史任务列表。
