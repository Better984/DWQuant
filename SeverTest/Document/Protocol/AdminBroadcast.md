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
