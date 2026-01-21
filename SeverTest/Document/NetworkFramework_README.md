# 网络框架说明文档

## 目录

- [概述](#概述)
- [整体结构](#整体结构)
- [连接与鉴权](#连接与鉴权)
- [连接管理与连接上限](#连接管理与连接上限)
- [限流策略](#限流策略)
- [消息协议](#消息协议)
- [错误处理](#错误处理)
- [日志与监控点](#日志与监控点)
- [配置说明](#配置说明)
- [Redis 缓存使用示例](#redis-缓存使用示例)
- [令牌验证工具类](#令牌验证工具类)
- [本地验证](#本地验证)

---

## 概述

本网络框架在同一进程内同时支持 HTTP 与 WebSocket，并使用 Redis 作为限流与连接管理的存储后端：
- 登录/注册走 HTTP（允许匿名）
- 业务交互优先通过 WebSocket
- WebSocket 握手阶段强制 JWT 校验
- Redis 版连接管理与限流（支持多实例一致性）

---

## 整体结构

```
HTTP Pipeline
  ExceptionHandlingMiddleware
  Authentication / Authorization
  HttpRateLimitMiddleware
  Controllers

WebSocket Pipeline (/ws)
  握手前参数校验 (system + token)
  JWT 验证 -> userId
  RedisConnectionManager 连接管理
  WebSocketHandler 消息循环与分发
```

关键组件：
- `JwtService`: token 验证与 userId 解析
- `HttpRateLimitMiddleware`: HTTP 令牌桶限流（Redis）
- `IConnectionManager`: 连接表管理（Redis，userId+system 唯一）
- `WebSocketHandler`: WS 消息接收、解析、路由与回包

---

## 连接与鉴权

### WebSocket 连接要求
- 必须传 `system` 参数：`web/pc/phone/...`
- 必须携带 token：
  - 浏览器：`?access_token=...`
  - 非浏览器：`Authorization: Bearer ...`

握手阶段执行：
1. 校验 `system` 参数
2. 读取 token
3. `JwtService.ValidateToken` 校验
4. 从 `NameIdentifier` 或 `sub` 解析 `userId`
5. 鉴权通过才接受连接

---

## 连接管理与连接上限

规则：
- 允许同一 user 多设备同时在线
- 同一 user + system 最多 `MaxConnectionsPerSystem` 条连接（可配置）

策略：
- 超过 3 条连接：直接拒绝连接

说明：
- Redis 仅负责连接占用关系的全局一致性

---

## 限流策略

### HTTP 限流
- 按 userId 令牌桶限流
- 登录/注册不参与限流
- 超限返回：HTTP 429 + `{code,message,traceId}`

### WebSocket 限流
- 按 userId 令牌桶限流
- 以“业务消息数”计数，ping/pong 不计
- 超限返回：`rate_limit` 错误 Envelope

---

## 消息协议

统一 Envelope：
```json
{
  "type": "string",
  "reqId": "string|nullable",
  "ts": 1700000000000,
  "payload": {},
  "err": { "code":"string", "message":"string" }
}
```

内置消息：
- `ping` -> `pong`
- `health` -> 返回服务器时间与连接信息

解析失败：
- `bad_request`

未知 type：
- `unknown_type`

超大消息：
- `message_too_large`

---

## 错误处理

### HTTP
统一格式：
```json
{ "code": "...", "message": "...", "traceId": "..." }
```

### WebSocket
通过 Envelope.err 返回：
```json
{
  "type": "error",
  "reqId": "...",
  "ts": 1700000000000,
  "payload": null,
  "err": { "code": "...", "message": "..." }
}
```

---

## 日志与监控点

建议关注：
- WS 连接建立/断开
- 鉴权失败（缺 token、无 userId、token 无效）
- 限流命中（HTTP/WS）
- 消息解析失败与 handler 异常

---

## 配置说明

`SeverTest/appsettings.json`：
```json
{
  "RateLimit": {
    "HttpRps": 5,
    "WsRps": 20
  },
  "WebSocket": {
    "Path": "/ws",
    "MaxMessageBytes": 1048576,
    "KickPolicy": "KickOld",
    "KeepAliveSeconds": 30,
    "MaxConnectionsPerSystem": 3
  },
  "Redis": {
    "ConnectionString": "127.0.0.1:6379"
  }
}
```

---

## Redis 缓存使用示例

可直接注入 `IDistributedCache`，或使用封装类 `RedisCacheService`：

```csharp
public class RedisCacheService
{
    private readonly IDistributedCache _cache;

    public RedisCacheService(IDistributedCache cache) => _cache = cache;

    public Task SetAsync(string key, string value, TimeSpan? ttl = null)
    {
        var opts = new DistributedCacheEntryOptions();
        if (ttl.HasValue) opts.SetAbsoluteExpiration(ttl.Value);
        return _cache.SetStringAsync(key, value, opts);
    }

    public Task<string?> GetAsync(string key) => _cache.GetStringAsync(key);
}
```

---

## 令牌验证工具类

`AuthTokenService` 负责统一 JWT 校验与 Redis 会话校验，避免重复代码：

```csharp
public class AuthTokenService
{
    public Task StoreTokenAsync(string userId, string token, TimeSpan? ttl = null);
    public Task<(bool IsValid, ClaimsPrincipal? Principal, string? UserId)> ValidateTokenAsync(string token);
    public Task RemoveTokenAsync(string token);
}
```

使用要点：
- 登录/注册成功后调用 `StoreTokenAsync` 保存会话
- WebSocket 握手时调用 `ValidateTokenAsync`
- 注销或失效时调用 `RemoveTokenAsync`
 - Redis 中键格式为 `auth:token:{token}`，值为 `userId`

---

## 本地验证

1) 启动服务

2) WebSocket 连接
```
ws://localhost:5000/ws?access_token=YOUR_TOKEN&system=web
```

3) 测试 ping
```json
{"type":"ping","reqId":"1","ts":1700000000000,"payload":null,"err":null}
```

4) 测试 health
```json
{"type":"health","reqId":"2","ts":1700000000000,"payload":null,"err":null}
```
