# 管理员 WebSocket 实时推送功能说明

## 功能概述

实现了管理员后台管理系统的实时 WebSocket 推送功能，包括：
1. 连接统计实时推送（`admin.connection.stats`）
2. 在线用户列表实时推送（`admin.connection.users`）
3. 日志消息实时推送（`admin.log`）

## 实现内容

### 1. 后端实现

#### 1.1 AdminWebSocketBroadcastService
**文件**: `SeverTest/Modules/AdminBroadcast/Application/AdminWebSocketBroadcastService.cs`

**功能**:
- 推送连接统计信息给所有管理员
- 推送在线用户列表给所有管理员
- 推送日志消息给所有管理员
- 使用缓存机制优化管理员角色检查性能

**关键方法**:
- `BroadcastConnectionStatsAsync()` - 推送连接统计
- `BroadcastOnlineUsersAsync()` - 推送在线用户列表
- `BroadcastLogAsync()` - 推送日志消息

#### 1.2 AdminLogBroadcastProvider
**文件**: `SeverTest/Modules/AdminBroadcast/Infrastructure/AdminLogBroadcastProvider.cs`

**功能**:
- 拦截系统日志（Warning、Error、Critical 级别）
- 自动推送给所有管理员用户
- 异步处理，不阻塞日志记录

#### 1.3 Program.cs 集成
**修改内容**:
- 注册 `AdminWebSocketBroadcastService` 服务
- 在 WebSocket 连接建立/断开时触发推送
- 注册 `AdminLogBroadcastProvider` 日志提供者

**推送时机**:
- WebSocket 连接建立后 200ms 推送统计信息
- WebSocket 连接断开后 200ms 推送统计信息
- 系统日志记录时自动推送（Warning/Error/Critical）

### 2. 前端修复

#### 2.1 NetworkStatus.tsx
**修复内容**:
- ✅ 修复 WebSocket 状态监听（使用 `onWsStatusChange`）
- ✅ 改进错误处理（首次加载失败时显示错误）
- ✅ 保留 WebSocket 实时推送监听
- ✅ 调整轮询间隔为 30 秒（因为有实时推送）

#### 2.2 LogConsole.tsx
**功能**:
- ✅ 监听 `admin.log` WebSocket 消息
- ✅ 自动显示 Warning、Error、Critical 级别的日志
- ✅ 日志进入动画效果

## WebSocket 消息格式

### admin.connection.stats
```json
{
  "type": "admin.connection.stats",
  "code": 0,
  "msg": "ok",
  "ts": 1234567890,
  "data": {
    "totalConnections": 10,
    "totalUsers": 5,
    "connectionsBySystem": {
      "web": 8,
      "admin": 2
    }
  }
}
```

### admin.connection.users
```json
{
  "type": "admin.connection.users",
  "code": 0,
  "msg": "ok",
  "ts": 1234567890,
  "data": {
    "users": [
      {
        "userId": "123",
        "system": "web",
        "connectionId": "guid",
        "connectedAt": "2025-01-01T00:00:00Z",
        "remoteIp": "127.0.0.1"
      }
    ]
  }
}
```

### admin.log
```json
{
  "type": "admin.log",
  "code": 0,
  "msg": "ok",
  "ts": 1234567890,
  "data": {
    "level": "ERROR",
    "message": "错误消息",
    "category": "System",
    "timestamp": 1234567890
  }
}
```

## 性能优化

1. **管理员角色缓存**: 使用 `ConcurrentDictionary` 缓存管理员角色，减少数据库查询
2. **缓存刷新策略**: 每 60 秒刷新一次缓存，自动清理离线用户缓存
3. **异步推送**: 所有推送操作异步执行，不阻塞主流程
4. **连接状态检查**: 发送前检查 WebSocket 连接状态，避免无效发送

## 注意事项

1. **权限检查**: 只有角色为 255（超级管理员）的用户才能收到推送
2. **日志级别**: 只推送 Warning、Error、Critical 级别的日志
3. **错误处理**: 推送失败不会影响主流程，只记录警告日志
4. **循环依赖**: 使用 `IServiceProvider` 延迟获取服务，避免循环依赖

## 测试建议

1. 测试连接建立/断开时的推送
2. 测试多个管理员同时在线时的推送
3. 测试日志推送功能（触发 Warning/Error 级别日志）
4. 测试非管理员用户是否不会收到推送
5. 测试网络断开重连后的推送恢复
