# 价格订阅服务使用说明

## 功能概述

本服务使用 CCXT 库订阅三个交易所（币安、OKX、Bitget）的 BTC/USDT 合约实时价格，并每3秒在控制台输出一次最新价格。

## 已实现的功能

1. **多交易所价格订阅**
   - 币安 (Binance) - BTC/USDT 合约
   - OKX - BTC/USDT 合约
   - Bitget - BTC/USDT 合约

2. **实时价格缓存**
   - 价格数据存储在内存中，每秒更新一次
   - 包含价格、24小时最高价、24小时最低价、成交量等信息

3. **控制台输出**
   - 每3秒自动清屏并输出三个交易所的最新价格
   - 显示格式化的价格信息

4. **REST API 接口**
   - `GET /api/price/all` - 获取所有交易所的价格
   - `GET /api/price/{exchange}` - 获取指定交易所的价格（Binance/OKX/Bitget）

## 运行方式

### 方式一：直接运行服务器（推荐）

```bash
dotnet run
```

服务器启动后，价格订阅服务会自动启动，控制台会每3秒输出一次价格信息。

### 方式二：构建后运行

```bash
dotnet build
dotnet bin/Debug/net8.0/ServerTest.dll
```

## 控制台输出示例

```
================================================================================
BTC/USDT 合约实时价格 - 2024-01-15 14:30:25
================================================================================

Binance   | 价格:       43250.50 USDT | 24h高:    43500.00 | 24h低:    42800.00 | 更新时间: 14:30:24
OKX       | 价格:       43248.75 USDT | 24h高:    43480.00 | 24h低:    42820.00 | 更新时间: 14:30:24
Bitget    | 价格:       43249.25 USDT | 24h高:    43490.00 | 24h低:    42810.00 | 更新时间: 14:30:24

================================================================================
```

## API 使用示例

### 获取所有交易所价格

```bash
curl http://localhost:5000/api/price/all
```

响应示例：
```json
{
  "success": true,
  "message": "操作成功",
  "data": {
    "Binance": {
      "exchange": "Binance",
      "symbol": "BTC/USDT",
      "price": 43250.50,
      "timestamp": "2024-01-15T14:30:24Z",
      "volume": 1234567.89,
      "high24h": 43500.00,
      "low24h": 42800.00
    },
    "OKX": {
      "exchange": "OKX",
      "symbol": "BTC/USDT",
      "price": 43248.75,
      "timestamp": "2024-01-15T14:30:24Z",
      "volume": 987654.32,
      "high24h": 43480.00,
      "low24h": 42820.00
    },
    "Bitget": {
      "exchange": "Bitget",
      "symbol": "BTC/USDT",
      "price": 43249.25,
      "timestamp": "2024-01-15T14:30:24Z",
      "volume": 567890.12,
      "high24h": 43490.00,
      "low24h": 42810.00
    }
  },
  "timestamp": "2024-01-15T14:30:25Z"
}
```

### 获取指定交易所价格

```bash
curl http://localhost:5000/api/price/Binance
```

## 技术说明

### 实现方式

本服务使用 **CCXT Pro WebSocket** 实时订阅价格数据：
- 使用 `ccxt.pro` 命名空间中的交易所类
- 通过 `WatchTicker()` 方法建立 WebSocket 连接
- 实时接收价格更新，无需轮询
- 自动处理连接、重连、心跳等机制

### 服务架构

- **ExchangePriceService**: 核心服务类，负责价格订阅和缓存
- **PriceController**: REST API 控制器，提供 HTTP 接口
- **PriceData**: 价格数据模型

### 配置说明

服务在 `Program.cs` 中自动注册为单例服务，启动时自动初始化。

### WebSocket 实现细节

- **币安**: 使用 `ccxt.pro.binanceusdm` 用于 USDT 永续合约
- **OKX**: 使用 `ccxt.pro.okx`，设置 `defaultType` 为 `swap`
- **Bitget**: 使用 `ccxt.pro.bitget`，设置 `defaultType` 为 `swap`
- **数据解析**: 使用动态类型和反射机制解析 ticker 对象

## 注意事项

1. **网络连接**: 确保能够访问币安、OKX、Bitget 的 API
2. **API 限流**: 当前设置为每秒请求一次，避免触发交易所限流
3. **错误处理**: 如果某个交易所连接失败，会每2秒重试一次
4. **合约类型**: 
   - 币安使用 `future` 类型
   - OKX 和 Bitget 使用 `swap` 类型

## 故障排查

### 价格显示为 0 或连接中

1. 检查网络连接
2. 查看控制台日志中的错误信息
3. 确认交易所 API 是否可访问

### 编译错误

确保已安装 CCXT 包：
```bash
dotnet add package ccxt
```

### 运行时错误

检查日志输出，查看具体错误信息。常见问题：
- 网络连接问题
- 交易所 API 变更
- CCXT 版本兼容性问题

## 扩展功能

可以轻松扩展以下功能：
1. 添加更多交易所
2. 订阅更多交易对
3. 将价格数据存储到数据库
4. 添加 WebSocket 推送功能
5. 实现价格差异提醒
