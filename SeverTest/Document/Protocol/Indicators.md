# Indicators 模块协议

## indicator.meta.list
- 路径：`POST /api/indicator/meta/list`
- data：
  - `includeDisabled` bool（可选，默认 `false`）
- 响应 data：
  - `items[]`
    - `code` string
    - `provider` string
    - `sourceType` string（数据来源类型，例如 `http_pull` / `custom_compute`）
    - `displayName` string
    - `shape` string
    - `unit` string?
    - `description` string?
    - `refreshIntervalSec` int
    - `ttlSec` int
    - `historyRetentionDays` int
    - `defaultScopeKey` string
    - `enabled` bool
    - `sortOrder` int
    - `fields[]`
      - `path` string（字段路径，条件中可作为 `reference.output` 使用）
      - `displayName` string
      - `dataType` string（`number` / `boolean` / `string` / `array` / `object`）
      - `unit` string?
      - `conditionSupported` bool（是否可用于策略条件）
      - `description` string?
  - `total` int

## indicator.latest.get
- 路径：`POST /api/indicator/latest/get`
- data：
  - `code` string（必填）
  - `scope` object?（可选，键值对）
  - `allowStale` bool（可选，默认 `true`）
  - `forceRefresh` bool（可选，默认 `false`）
- 响应 data：
  - `code` string
  - `provider` string
  - `displayName` string
  - `shape` string
  - `unit` string?
  - `description` string?
  - `scopeKey` string
  - `sourceTs` number（毫秒）
  - `fetchedAt` number（毫秒）
  - `expireAt` number（毫秒）
  - `stale` bool（是否过期降级）
  - `origin` string（`cache` / `database` / `provider`）
  - `payload` object（统一指标负载）

## indicator.batch.latest
- 路径：`POST /api/indicator/batch/latest`
- data：
  - `codes` string[]（必填）
  - `scope` object?（可选）
  - `allowStale` bool（可选，默认 `true`）
- 响应 data：
  - `items`：同 `indicator.latest.get` 单项结构数组
  - `total` int

## indicator.history.get
- 路径：`POST /api/indicator/history/get`
- data：
  - `code` string（必填）
  - `scope` object?（可选）
  - `startTime` string?（可选，ISO8601 或 `yyyy-MM-dd HH:mm:ss`）
  - `endTime` string?（可选，ISO8601 或 `yyyy-MM-dd HH:mm:ss`）
  - `limit` int（可选，默认 `200`）
- 响应 data：
  - `code` string
  - `displayName` string
  - `unit` string?
  - `shape` string
  - `scopeKey` string
  - `points[]`
    - `sourceTs` number（毫秒）
    - `payload` object
  - `total` int
- 说明：历史数据在后端按指标独立分表存储（命名规范 `coinglass_{指标代码}_history`），协议层无感知。

## indicator.realtime.channel.get
- 路径：`POST /api/indicator/realtime/channel/get`
- data：
  - `channel` string（必填，例：`funding-rate`）
  - `allowStale` bool（可选，默认 `true`）
- 响应 data：
  - `channel` string
  - `source` string（当前固定 `coinglass.ws.stream`）
  - `receivedAt` number（毫秒）
  - `expireAt` number（毫秒）
  - `stale` bool（是否已过期）
  - `payload` object（频道原始 JSON 消息）
