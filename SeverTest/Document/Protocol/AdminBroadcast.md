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
