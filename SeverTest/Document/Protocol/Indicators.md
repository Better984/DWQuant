# Indicators 模块协议

## indicator.meta.list
- 路径：`POST /api/indicator/meta/list`
- data：
  - `includeDisabled` bool（可选，默认 `false`）
- 响应 data：
  - `items[]`
    - `code` string
    - `provider` string
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
