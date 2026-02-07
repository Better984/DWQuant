# Backtest 模块协议

### backtest.run
- 路径：`POST /api/backtest/run`
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
  - `fundingRate` decimal（默认 0）
  - `slippageBps` int（固定滑点 bps）
  - `autoReverse` bool（是否自动反向）
  - `runtime` StrategyRuntimeConfig?（运行时间配置，可覆盖策略）
  - `useStrategyRuntime` bool（是否启用运行时间门禁）
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
  - `totalStats`（汇总统计）
  - `symbols[]`：`BacktestSymbolResult`
    - `symbol`
    - `bars`
    - `initialCapital`
    - `stats`
    - `trades`（可裁剪）
    - `equityCurve`（可裁剪）
      - `timestamp`
      - `equity`
      - `realizedPnl`
      - `unrealizedPnl`
      - `periodRealizedPnl`（当前粒度周期内已实现盈亏增量）
      - `periodUnrealizedPnl`（当前粒度周期内未实现盈亏增量）
    - `events`（可裁剪）

> 说明：回测结束会按最后一根 K 线收盘价强制平仓，便于统计输出。

### backtest.progress（WebSocket 推送）
- type：`backtest.progress`
- 触发条件：`backtest.run` 执行期间，且用户存在可用 WebSocket 连接时。
- 关联方式：
  - 推送消息与 HTTP 请求共用 `reqId`；
  - 前端应按 `reqId` 过滤，只消费当前回测任务的进度流。
- data：`BacktestProgressMessage`
  - `eventKind` string：`stage` 或 `positions`
  - `stage` string：阶段编码（如 `parse_request`、`main_loop`、`collect_positions`、`completed`、`failed`）
  - `stageName` string：阶段名称（用于前端展示）
  - `message` string?：阶段说明
  - `processedBars` int?：已处理 bar 数
  - `totalBars` int?：总 bar 数
  - `progress` decimal?：进度比例（0~1）
  - `elapsedMs` long?：当前阶段耗时（毫秒）
  - `foundPositions` int?：已汇总仓位/交易数量
  - `totalPositions` int?：预估总仓位/交易数量
  - `chunkCount` int?：本次增量仓位条数
  - `completed` bool?：当前阶段是否完成
  - `symbol` string?：增量所属标的
  - `positions` BacktestTrade[]?：增量仓位列表（测试阶段用于前端快速预览）

> 说明：仓位汇总阶段采用 0.1 秒节流推送，长耗时时前端可先显示部分仓位，再等待最终完整结果。
