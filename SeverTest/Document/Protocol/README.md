# 统一网络协议说明

## 目标

- HTTP 与 WebSocket 使用同一套 Envelope 结构
- 强制 `reqId`，避免请求错序列导致前端无感知
- 统一错误码区间

---

## 请求结构（HTTP / WebSocket）

```json
{
  "type": "string",
  "reqId": "string",
  "ts": 1700000000000,
  "data": {}
}
```

字段说明：
- `type`：协议类型，必填
- `reqId`：请求唯一标识，必填（建议 UUID 或业务唯一串）
- `ts`：请求时间戳（毫秒），必填
- `data`：业务内容，可为空

---

## 响应结构（HTTP / WebSocket）

```json
{
  "type": "string",
  "reqId": "string",
  "ts": 1700000000000,
  "code": 0,
  "msg": "ok",
  "data": {},
  "traceId": "string"
}
```

字段说明：
- `type`：响应类型，默认 `请求 type + .ack`，错误时为 `error`
- `reqId`：与请求一致
- `ts`：响应时间戳（毫秒）
- `code`：错误码，0 表示成功
- `msg`：描述信息
- `data`：业务返回内容
- `traceId`：后端链路追踪 ID（可用于排障）

---

## 字段规范

- 文档标记为 `enum` 的字段：允许使用字符串枚举名（推荐使用字符串，名称需与后端枚举一致）

---

## 服务端推送（WebSocket）

推送消息不带 `reqId`：

```json
{
  "type": "mkt.tick",
  "ts": 1700000000000,
  "code": 0,
  "msg": "ok",
  "data": []
}
```

---

## 错误码区间

- 1xxx：参数/格式
- 2xxx：鉴权/权限
- 3xxx：业务
- 5xxx：系统/依赖

常用错误码：
- 1000 InvalidRequest（通用参数错误）
- 1001 MissingField（缺少字段）
- 1002 InvalidFormat（格式错误）
- 1003 OutOfRange（超出限制）
- 1004 Unsupported（不支持）
- 2000 Unauthorized（未授权）
- 2001 TokenInvalid（Token 无效）
- 2002 Forbidden（无权限）
- 3000 NotFound（未找到）
- 3001 Conflict（冲突）
- 3003 LimitExceeded（数量上限）
- 3004 RateLimited（触发限流）
- 5000 InternalError（系统异常）
- 5003 ServiceUnavailable（服务不可用）

---

## HTTP 使用规范

- **所有对外 HTTP 接口统一使用 POST + JSON Body**
- 文件上传为例外，使用 `multipart/form-data`，但表单中仍需包含 `type/reqId/ts`
- Body 必须是统一请求结构（`type/reqId/ts/data`）
- 响应统一为协议 Envelope（`type/reqId/ts/code/msg/data/traceId`）
- 需要登录的接口仍使用 `Authorization: Bearer <token>`
- 服务端会在 HTTP 中间件统一校验 Bearer Token；当 token 已失效或被同类型新登录替换时，统一返回 `401 + code=2001(TokenInvalid)`。
- 为保证中间件阶段也能回传 `reqId`，所有 HTTP 请求必须在 Header 传 `X-Req-Id`

---

## WebSocket 使用规范

### 握手
- 路径：`/ws`
- 必填参数：`system`
- 必须携带 token：
  - 浏览器：`?access_token=...`
  - 非浏览器：`Authorization: Bearer ...`

### 消息
- 请求必须带 `reqId`、`type`、`ts`
- 响应默认 `type = 请求 type + .ack`
- 错误统一 `type = error`

---

## 备注
- 模块级协议详见当前目录下各模块文档。

---

## 前端/工具示例（X-Req-Id 必填）

### 前端 fetch 示例（JSON）

```ts
const reqId = crypto.randomUUID();
const payload = {
  type: "strategy.list",
  reqId,
  ts: Date.now(),
  data: { status: "all" }
};

await fetch("http://localhost:9635/api/strategy/list", {
  method: "POST",
  headers: {
    "Content-Type": "application/json",
    "X-Req-Id": reqId,
    "Authorization": `Bearer ${token}`
  },
  body: JSON.stringify(payload)
});
```

### Postman 示例（JSON）

- Method：`POST`
- URL：`http://localhost:9635/api/strategy/list`
- Headers：
  - `Content-Type: application/json`
  - `X-Req-Id: <uuid>`
  - `Authorization: Bearer <token>`
- Body（raw / JSON）：

```json
{
  "type": "strategy.list",
  "reqId": "3b9b4f14-7f1c-4a8b-9c90-2e5c64e9b6a1",
  "ts": 1700000000000,
  "data": {
    "status": "all"
  }
}
```

### Postman 示例（文件上传）

- Method：`POST`
- URL：`http://localhost:9635/api/media/avatar`
- Headers：
  - `X-Req-Id: <uuid>`
  - `Authorization: Bearer <token>`
- Body（form-data）：
  - `type`: `media.avatar.upload`
  - `reqId`: `<uuid>`
  - `ts`: `1700000000000`
  - `file`: 选择文件
