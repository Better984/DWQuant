# Positions 模块协议

### position.list
- 路径：`POST /api/positions/list`
- data：
  - `from` string?
  - `to` string?
  - `status` string?
- 响应 data：`PositionListResponse`

### position.list.by_strategy
- 路径：`POST /api/positions/by-strategy`
- data：
  - `usId` long
  - `from` string?
  - `to` string?
  - `status` string?
- 响应 data：`PositionListResponse`

### position.close.by_strategy
- 路径：`POST /api/positions/close-by-strategy`
- data：
  - `usId` long
- 响应 data：`StrategyClosePositionsResult`
