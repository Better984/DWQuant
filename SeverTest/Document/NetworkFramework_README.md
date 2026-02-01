# 网络框架说明文档

## 概述

当前服务对外提供 **HTTP + WebSocket** 两类入口，统一使用一套协议 Envelope。
- HTTP：统一 POST + JSON Envelope（文件上传例外，使用 multipart/form-data）
- WebSocket：统一 Envelope 消息（reqId 强制）

协议细节与模块说明请查看：`Document/NetworkProtocol/README.md` 与对应模块文档。

---

## 管道结构（简述）

```
HTTP Pipeline
  ExceptionHandlingMiddleware
  SystemReadinessMiddleware
  CORS / Authentication / Authorization
  HttpRateLimitMiddleware
  Controllers

WebSocket Pipeline (/ws)
  握手参数校验 (system + token)
  JWT + Redis 会话校验
  RedisConnectionManager 连接管理
  WebSocketHandler 消息接收/分发
```

---

## 关键要点

- **统一协议**：所有 HTTP/WS 请求必须携带 `type/reqId/ts`，序列化规则保持一致（camelCase + 枚举字符串）。
- **reqId 回传**：HTTP 请求必须在 Header 传 `X-Req-Id`，确保中间件阶段也能回传。
- **错误码统一**：按 1xxx/2xxx/3xxx/5xxx 区间划分。
- **限流**：HTTP/WS 均使用 Redis 令牌桶限流。
- **连接管理**：WebSocket 使用 Redis 维护同用户/系统的连接上限，心跳刷新 TTL；分布式场景通过 Redis Pub/Sub 踢下线。

---

## 文档入口

- 统一协议说明：`Document/NetworkProtocol/README.md`
- 各模块协议：`Document/NetworkProtocol/*.md`
