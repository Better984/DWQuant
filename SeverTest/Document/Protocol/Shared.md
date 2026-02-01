# Shared 模块协议

## 媒体

### media.avatar.upload
- 路径：`POST /api/media/avatar`
- Content-Type：`multipart/form-data`
- 表单字段：
  - `type` string
  - `reqId` string
  - `ts` long
  - `file` 文件
- Header 必须同步携带 `X-Req-Id`
- 响应 data：`{ avatarUrl }`

### media.avatar.get
- 路径：`POST /api/media/avatar/get`
- data：无
- 响应 data：`{ avatarUrl }`

---

## 健康检查

### system.health
- 路径：`POST /api/health/get`
- data：无
- 响应 data：系统状态详情
