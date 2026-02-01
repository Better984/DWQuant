# MarketDataEngine 行情引擎使用文档

## 目录

- [概述](#概述)
- [工作原理](#工作原理)
- [架构设计](#架构设计)
- [数据流程](#数据流程)
- [公共方法](#公共方法)
- [使用示例](#使用示例)
- [配置说明](#配置说明)
- [注意事项](#注意事项)

---

## 概述

`MarketDataEngine` 是一个实时行情数据引擎，用于订阅和管理多个交易所、多个交易对的 K 线数据。引擎通过 WebSocket 实时订阅 1 分钟 K 线，并自动聚合生成其他周期的 K 线数据，同时输出实时行情任务（MarketDataTask）供实时策略引擎消费。

### 核心特性

- ✅ **实时订阅**：通过 WebSocket 实时订阅 1 分钟 K 线数据
- ✅ **多周期支持**：自动从 1 分钟 K 线聚合生成 3m、5m、15m、30m、1h、4h、1d、1w 等周期
- ✅ **多交易所支持**：支持 Binance、OKX、Bitget（当前默认初始化 Bitget）
- ✅ **增量聚合**：使用增量算法，避免全表扫描，性能优异
- ✅ **行情任务通道**：1m 收线与周期收线生成 MarketDataTask，1m 收线额外驱动其他周期 OnBarUpdate 任务
- ✅ **并发安全**：每个交易对独立锁，支持高并发访问
- ✅ **历史数据缓存**：自动加载并缓存最近 2000 根 K 线数据

---

## 工作原理

### 1. 初始化流程

```
启动应用
  ↓
创建 MarketDataEngine 实例
  ↓
异步初始化交易所（REST API + WebSocket）
  ↓
初始化缓存结构（为每个交易所/交易对/周期创建缓存）
  ↓
启动 WebSocket 订阅（订阅所有交易对的 1m K 线）
  ↓
加载历史数据（通过 REST API 拉取各周期历史数据）
  ↓
初始化完成，引擎就绪
```

### 2. 实时数据更新流程

```
WebSocket 接收 1m K 线更新
  ↓
判断是否为新 K 线（收线）
  ├─ 是：写入 1m 缓存，触发周期聚合
  │   ├─ 跨周期时 finalize 上一个 bucket，并入队该周期收线任务
  │   ├─ 1m 收线入队 MarketDataTask（IsBarClose=true）
  │   └─ 1m 收线驱动其他周期 OnBarUpdate 任务入队（IsBarClose=false）
  └─ 否：更新当前 1m K 线，同步更新其他周期的当前 bucket（不入队）
```

### 3. 周期聚合机制

引擎使用**增量聚合**算法，维护每个周期的**当前 bucket**（正在构建的 K 线）：

- **1m 收线时**：
  - 如果当前 bucket 存在且属于同一周期 → 更新 bucket（累加 volume）
  - 如果跨周期 → finalize 上一个 bucket（添加到历史列表）并入队该周期收线任务，创建新的 bucket

- **1m 更新时（未收线）**：
  - 只更新当前 bucket 的 high/low/close，**不累加 volume**
  - 避免同一根 1m K 线多次更新时重复累加 volume

### 4. 数据结构

```
Exchange (交易所)
  └─ Symbol (交易对)
      └─ SymbolCache
          ├─ Lock (独立锁，保证并发安全)
          ├─ Timeframes (各周期的历史 K 线列表)
          │   ├─ "1m": List<OHLCV>
          │   ├─ "5m": List<OHLCV>
          │   └─ ...
          └─ CurrentBuckets (各周期当前正在构建的 K 线)
              ├─ "5m": OHLCV? (未收线的 5m K 线)
              └─ ...
```

### 5. 行情任务通道（MarketDataTask）

引擎通过 `Channel<MarketDataTask>` 向外输出行情任务，供实时策略引擎/指标引擎消费。

**MarketDataTask 字段**：
- `Exchange` / `Symbol` / `Timeframe`：定位策略执行范围
- `CandleTimestamp`：K 线时间戳（毫秒）
- `TimeframeSec`：周期秒数
- `IsBarClose`：`true` 表示收线任务（OnBarClose），`false` 表示更新任务（OnBarUpdate）

**触发规则**：
- 1m 收线：入队 1m 收线任务
- 其他周期跨周期收线：入队对应周期收线任务
- 1m 收线：同时为其他周期入队 OnBarUpdate 任务（用于每分钟刷新指标）

---

## 架构设计

### 核心组件

1. **SymbolCache**：每个交易对的缓存容器
   - 包含独立锁，保证并发安全
   - 存储各周期的历史 K 线列表
   - 维护各周期的当前 bucket

2. **缓存结构**：`Exchange -> Symbol -> SymbolCache`
   - 使用 `ConcurrentDictionary` 保证线程安全
   - 每个交易对独立锁，不同交易对完全并行

3. **WebSocket 订阅**：每个交易对一个独立的订阅任务
   - 使用 `ccxt.pro` 库进行 WebSocket 订阅
   - 自动重连和错误处理

4. **限流机制**：使用 `SemaphoreSlim` 限制并发请求数
   - 最多 5 个并发 REST API 请求
   - 避免触发交易所 API 限流

5. **MarketDataTask 通道**：`Channel<MarketDataTask>` 输出实时行情任务
   - 提供给实时策略引擎/指标引擎消费
   - 支持阻塞/非阻塞读取任务

---

## 数据流程

### 启动阶段

1. **交易所初始化**
   - 为每个交易所创建 REST API 和 WebSocket 实例
   - 加载 markets 数据

2. **缓存初始化**
   - 为每个交易所/交易对/周期创建空的缓存结构

3. **WebSocket 订阅**
   - 为每个交易对启动独立的 WebSocket 订阅任务
   - 订阅 1 分钟 K 线数据

4. **历史数据加载**
   - 通过 REST API 拉取各周期最近 2000 根 K 线
   - 从 1m 历史数据初始化其他周期的当前 bucket

### 运行阶段

1. **实时更新**
   - WebSocket 持续接收 1m K 线更新
   - 判断是否收线，更新缓存和聚合其他周期

2. **数据查询**
   - 通过公共方法查询实时或历史数据
   - 每个交易对使用独立锁，保证并发安全

3. **行情任务输出**
   - 1m 收线入队 `MarketDataTask`（`IsBarClose=true`）
   - 其他周期收线入队对应 `MarketDataTask`
   - 1m 收线额外入队其他周期 OnBarUpdate 任务（`IsBarClose=false`）

---

## 公共方法

### 1. WaitForInitializationAsync

等待引擎初始化完成。

```csharp
public async Task WaitForInitializationAsync()
```

**使用场景**：在应用启动时，确保引擎完全初始化后再开始使用。

**示例**：
```csharp
var engine = serviceProvider.GetRequiredService<MarketDataEngine>();
await engine.WaitForInitializationAsync();
// 现在可以安全地使用引擎了
```

---

### 2. TryDequeueMarketTask

尝试读取一条最新行情任务（非阻塞）。

```csharp
public bool TryDequeueMarketTask(out MarketDataTask task)
```

**使用场景**：轮询式消费任务或与其他事件循环结合。

**说明**：
- 当没有任务时返回 `false`，不会阻塞线程
- `task.IsBarClose` 用于区分收线与更新

**示例**：
```csharp
if (engine.TryDequeueMarketTask(out var task))
{
    var mode = task.IsBarClose ? "OnBarClose" : "OnBarUpdate";
    Console.WriteLine($"收到任务: {task.Exchange} {task.Symbol} {task.Timeframe} {mode}");
}
```

---

### 3. ReadMarketTaskAsync

阻塞读取一条行情任务（用于实时策略引擎）。

```csharp
public ValueTask<MarketDataTask> ReadMarketTaskAsync(CancellationToken cancellationToken)
```

**使用场景**：单任务阻塞式消费。

**示例**：
```csharp
var task = await engine.ReadMarketTaskAsync(stoppingToken);
```

---

### 4. ReadAllMarketTasksAsync

持续读取行情任务流（用于后台消费）。

```csharp
public IAsyncEnumerable<MarketDataTask> ReadAllMarketTasksAsync(CancellationToken cancellationToken)
```

**示例**：
```csharp
await foreach (var task in engine.ReadAllMarketTasksAsync(stoppingToken))
{
    var mode = task.IsBarClose ? "OnBarClose" : "OnBarUpdate";
    Console.WriteLine($"收到任务: {task.Exchange} {task.Symbol} {task.Timeframe} {mode}");
}
```

---

### 5. GetLatestKline

获取最新的 1 根 K 线数据。

```csharp
public OHLCV? GetLatestKline(
    MarketDataConfig.ExchangeEnum exchange,
    MarketDataConfig.TimeframeEnum timeframe,
    MarketDataConfig.SymbolEnum symbol)
```

**字符串版本**：
```csharp
public OHLCV? GetLatestKline(string exchangeId, string timeframe, string symbol)
```

**补充**：字符串版本会自动标准化交易所/交易对/周期格式（大小写、分隔符）。

**参数说明**：
- `exchange`：交易所枚举（Binance、OKX、Bitget）
- `timeframe`：周期枚举（m1、m5、h1 等）
- `symbol`：交易对枚举（BTC_USDT）

**返回值**：
- `OHLCV?`：最新的 K 线数据，如果不存在则返回 `null`
- 对于非 1m 周期，如果存在未收线的 bucket，会返回当前 bucket

**示例**：
```csharp
var latest = engine.GetLatestKline(
    MarketDataConfig.ExchangeEnum.Bitget,
    MarketDataConfig.TimeframeEnum.m5,
    MarketDataConfig.SymbolEnum.BTC_USDT
);

if (latest.HasValue)
{
    var kline = latest.Value;
    Console.WriteLine($"最新 5m K线: 时间={kline.timestamp}, 收盘价={kline.close}");
}
```

---

### 6. GetHistoryKlines

获取历史 K 线数据。

```csharp
public List<OHLCV> GetHistoryKlines(
    MarketDataConfig.ExchangeEnum exchange,
    MarketDataConfig.TimeframeEnum timeframe,
    MarketDataConfig.SymbolEnum symbol,
    DateTime? endTime,
    int count)
```

**字符串版本**：
```csharp
public List<OHLCV> GetHistoryKlines(
    string exchangeId,
    string timeframe,
    string symbol,
    long? endTimestamp,
    int count)
```

**参数说明**：
- `exchange`：交易所枚举（Binance、OKX、Bitget）
- `timeframe`：周期枚举
- `symbol`：交易对枚举（BTC_USDT）
- `endTime`：结束时间（可选），如果指定，只返回该时间之前的数据
- `count`：需要返回的 K 线数量（最多 2000 根）

**补充**：字符串版本会自动标准化交易所/交易对/周期格式（大小写、分隔符），并使用 `endTimestamp`（毫秒）作为结束时间。

**返回值**：
- `List<OHLCV>`：历史 K 线列表，按时间顺序排列
- 对于非 1m 周期，会自动包含当前未收线的 bucket（如果存在）

**示例**：
```csharp
// 获取最近 100 根 5m K 线
var klines = engine.GetHistoryKlines(
    MarketDataConfig.ExchangeEnum.Bitget,
    MarketDataConfig.TimeframeEnum.m5,
    MarketDataConfig.SymbolEnum.BTC_USDT,
    endTime: null,
    count: 100
);

// 获取指定时间之前的 50 根 K 线
var endTime = new DateTime(2024, 1, 1, 12, 0, 0);
var klines2 = engine.GetHistoryKlines(
    MarketDataConfig.ExchangeEnum.Bitget,
    MarketDataConfig.TimeframeEnum.h1,
    MarketDataConfig.SymbolEnum.BTC_USDT,
    endTime: endTime,
    count: 50
);
```

---

### 7. Dispose

释放引擎资源。

```csharp
public void Dispose()
```

**说明**：
- 取消所有 WebSocket 订阅任务
- 释放交易所资源
- 不等待任务完成，直接释放（避免卡住）

**使用场景**：应用关闭时调用，通常在 `IHostApplicationLifetime` 事件中调用。

**示例**：
```csharp
app.Lifetime.ApplicationStopping.Register(() =>
{
    engine.Dispose();
});
```

---

## 使用示例

### 示例 1：在 Controller 中使用

```csharp
[ApiController]
[Route("api/[controller]")]
public class MarketDataController : ControllerBase
{
    private readonly MarketDataEngine _engine;

    public MarketDataController(MarketDataEngine engine)
    {
        _engine = engine;
    }

    [HttpGet("latest")]
    public IActionResult GetLatest(
        [FromQuery] MarketDataConfig.ExchangeEnum exchange,
        [FromQuery] MarketDataConfig.TimeframeEnum timeframe,
        [FromQuery] MarketDataConfig.SymbolEnum symbol)
    {
        var kline = _engine.GetLatestKline(exchange, timeframe, symbol);
        
        if (!kline.HasValue)
        {
            return NotFound("未找到K线数据");
        }

        return Ok(kline.Value);
    }
}
```

### 示例 2：在后台服务中使用

```csharp
public class TradingService : BackgroundService
{
    private readonly MarketDataEngine _engine;

    public TradingService(MarketDataEngine engine)
    {
        _engine = engine;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 等待引擎初始化
        await _engine.WaitForInitializationAsync();

        while (!stoppingToken.IsCancellationRequested)
        {
            // 获取最新 5m K 线
            var latest = _engine.GetLatestKline(
                MarketDataConfig.ExchangeEnum.Bitget,
                MarketDataConfig.TimeframeEnum.m5,
                MarketDataConfig.SymbolEnum.BTC_USDT
            );

            if (latest.HasValue)
            {
                // 执行交易逻辑
                ProcessKline(latest.Value);
            }

            await Task.Delay(1000, stoppingToken);
        }
    }
}
```

### 示例 3：获取历史数据进行技术分析

```csharp
public void AnalyzeMarket(MarketDataEngine engine)
{
    // 获取最近 200 根 1h K 线
    var klines = engine.GetHistoryKlines(
        MarketDataConfig.ExchangeEnum.Bitget,
        MarketDataConfig.TimeframeEnum.h1,
        MarketDataConfig.SymbolEnum.BTC_USDT,
        endTime: null,
        count: 200
    );

    if (klines.Count < 200)
    {
        Console.WriteLine("历史数据不足");
        return;
    }

    // 计算移动平均线
    var closes = klines.Select(k => k.close ?? 0).ToList();
    var ma20 = closes.Skip(klines.Count - 20).Average();
    var ma50 = closes.Skip(klines.Count - 50).Average();

    Console.WriteLine($"MA20: {ma20}, MA50: {ma50}");
}
```

### 示例 4：消费实时行情任务

```csharp
public class StrategyTaskConsumer : BackgroundService
{
    private readonly MarketDataEngine _engine;

    public StrategyTaskConsumer(MarketDataEngine engine)
    {
        _engine = engine;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _engine.WaitForInitializationAsync();

        await foreach (var task in _engine.ReadAllMarketTasksAsync(stoppingToken))
        {
            var mode = task.IsBarClose ? "OnBarClose" : "OnBarUpdate";
            var timeText = MarketDataEngine.FormatTimestamp(task.CandleTimestamp);
            Console.WriteLine($"收到任务: {task.Exchange} {task.Symbol} {task.Timeframe} {mode} time={timeText}");
            // 交给实时策略引擎/指标引擎处理
        }
    }
}
```

---

## 配置说明

### 支持的交易所

在 `MarketDataConfig.ExchangeEnum` 中定义：
- `Binance`：币安合约（默认未初始化）
- `OKX`：OKX 合约（默认未初始化）
- `Bitget`：Bitget 合约（当前默认初始化）

### 支持的交易对

在 `MarketDataConfig.SymbolEnum` 中定义：
- `BTC_USDT`：BTC/USDT

### 支持的周期

在 `MarketDataConfig.TimeframeEnum` 中定义：
- `m1`：1 分钟
- `m3`：3 分钟
- `m5`：5 分钟
- `m15`：15 分钟
- `m30`：30 分钟
- `h1`：1 小时
- `h4`：4 小时
- `d1`：1 天
- `w1`：1 周

### 缓存配置

- `CacheHistoryLength`：每个周期缓存的历史数据长度，默认 2000 根（对应配置 `MarketDataQuery:CacheHistoryLength`）

---

## 注意事项

### 1. 初始化时机

引擎在构造函数中自动启动初始化，但这是异步的。如果需要在初始化完成后使用，请调用 `WaitForInitializationAsync()`：

```csharp
var engine = serviceProvider.GetRequiredService<MarketDataEngine>();
await engine.WaitForInitializationAsync();
// 现在可以安全使用
```

### 2. 数据可用性

- 引擎启动后需要一些时间加载历史数据
- 如果查询时数据还未加载完成，可能返回空列表或 null
- 建议在查询前检查数据是否存在

### 3. 周期聚合说明

- **1m 周期**：直接从 WebSocket 获取，数据最准确
- **其他周期**：从 1m K 线聚合生成
  - 聚合算法：open = 第一个 1m 的 open，close = 最后一个 1m 的 close
  - high = 所有 1m 的 high 的最大值，low = 所有 1m 的 low 的最小值
  - volume = 所有 1m 的 volume 之和

### 4. 行情任务触发时机

- `IsBarClose=true` 任务仅在 1m 收线或周期跨界 finalize 时产生
- `IsBarClose=false` 任务仅在 1m 收线时为其他周期生成（用于每分钟刷新指标）
- 1m 未收线（同一根 1m K 线多次更新）只更新缓存，不会入队任务

### 5. 当前 Bucket（未收线 K 线）

- 对于非 1m 周期，引擎会维护一个**当前 bucket**（正在构建的 K 线）
- `GetLatestKline` 和 `GetHistoryKlines` 会自动包含当前 bucket
- 当前 bucket 会在周期收线时 finalize 并添加到历史列表

### 6. 并发安全

- 每个交易对使用独立锁，不同交易对的查询完全并行
- 同一交易对的查询会串行化，但锁粒度很小，性能影响可忽略
- 所有公共方法都是线程安全的

### 7. 资源释放

- 引擎实现了 `Dispose` 方法，应用关闭时应调用
- `Dispose` 会取消所有 WebSocket 订阅，但不等待任务完成（避免卡住）
- 建议在 `IHostApplicationLifetime.ApplicationStopping` 事件中调用

### 8. 错误处理

- WebSocket 连接断开时会自动重连
- REST API 请求失败会记录日志，但不影响其他数据
- 如果某个交易对不存在，会跳过订阅，不影响其他交易对

### 9. 性能考虑

- 引擎使用增量聚合算法，性能优异
- 缓存了周期列表和合约符号查找结果，避免重复计算
- 历史数据查询是内存操作，速度很快

---

## 常见问题

### Q1: 为什么查询返回 null 或空列表？

**A**: 可能的原因：
1. 引擎还在初始化中，数据还未加载完成
2. 该交易对在交易所不存在
3. 该周期还没有数据

**解决方案**：
- 等待初始化完成：`await engine.WaitForInitializationAsync()`
- 检查日志，确认交易对是否存在
- 等待一段时间后再查询

### Q2: 如何知道引擎是否初始化完成？

**A**: 调用 `WaitForInitializationAsync()` 方法：

```csharp
await engine.WaitForInitializationAsync();
// 初始化完成
```

### Q3: 为什么非 1m 周期的数据可能不准确？

**A**: 非 1m 周期是从 1m K 线聚合生成的，如果 1m 数据有延迟或缺失，聚合结果可能不准确。建议：
- 优先使用 1m 周期进行实时交易
- 其他周期主要用于历史数据分析

### Q4: 如何添加新的交易所或交易对？

**A**: 在 `MarketDataConfig` 类中添加对应的枚举值，并在 `MarketDataEngine` 的初始化代码中添加交易所创建逻辑。

### Q5: 引擎会占用多少内存？

**A**: 取决于配置的交易对和周期数量：
- 每个周期缓存 2000 根 K 线
- 每根 K 线约 100 字节
- 估算：`交易所数 × 交易对数 × 周期数 × 2000 × 100 字节`

例如：2 个交易所 × 2 个交易对 × 9 个周期 × 2000 × 100 = 约 7.2 MB

---

## 更新日志

### v1.1.0 (2024-XX-XX)
- 增加 MarketDataTask 通道（收线/更新任务）
- 1m 收线驱动其他周期 OnBarUpdate 任务
- 补充阻塞/非阻塞的任务读取接口
- 支持 Bitget 合约初始化流程

### v1.0.0 (2024-01-XX)
- 初始版本
- 支持 Binance 和 OKX 交易所
- 支持 9 个周期
- 增量聚合算法
- 并发安全设计

---

## 技术支持

如有问题或建议，请联系开发团队。
