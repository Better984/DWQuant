# WebSockets 模块协议

## 握手
- 路径：`/ws`
- 必填参数：`system`
- Token：
  - `?access_token=...`
  - 或 `Authorization: Bearer ...`

## 内置消息

### ping
- data：无
- 响应 type：`pong`

### health
- data：无
- 响应 type：`health`
- 响应 data：`{ serverTime, connectionId, userId, system }`

### account.profile.update
- data：
  - `nickname` string
  - `signature` string
- 响应 type：`account.profile.updated`
- 响应 data：`{ success, nickname, signature }`

### kicked（服务端推送）
- 说明：同一用户/系统连接被替换时触发
- reqId：无
- data：`{ reason }`
