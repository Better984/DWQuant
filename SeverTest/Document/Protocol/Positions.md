# Positions 模块协议

### position.list
- 路径：`POST /api/positions/list`
- data：
  - `from` string?
  - `to` string?
  - `status` string?
- 响应 data：`PositionListResponse`
  - `items[].closeReason` string? 平仓原因（ManualSingle/ManualBatch/StopLoss/TakeProfit/TrailingStop）

### position.list.by_strategy
- 路径：`POST /api/positions/by-strategy`
- data：
  - `usId` long
  - `from` string?
  - `to` string?
  - `status` string?
- 响应 data：`PositionListResponse`
  - `items[].closeReason` string? 平仓原因（ManualSingle/ManualBatch/StopLoss/TakeProfit/TrailingStop）

### position.close.by_strategy
- 路径：`POST /api/positions/close-by-strategy`
- data：
  - `usId` long
- 响应 data：`StrategyClosePositionsResult`

### position.close.by_id
- 路径：`POST /api/positions/close-by-id`
- data：
  - `positionId` long
- 响应 data：`PositionCloseResult`
