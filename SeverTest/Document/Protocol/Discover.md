# Discover 模块协议

## 说明
- Discover 分为两个独立模块：
  - 新闻（Article）
  - 快讯（Newsflash）
- 另包含三类日历模块：
  - 央行活动（Central Bank Activities）
  - 财经事件（Financial Events）
  - 经济数据（Economic Data）
- 两个模块各自独立自增 ID（数据库独立表），互不共用。
- 当前数据源为开发联调用第三方聚合商（非官方源）；正式上线前需切换官方数据源。
- 服务端对上游使用 `language/page/per_page` 参数拉取，新闻默认中文（`zh`）。
- 日历服务端同样使用 `language/page/per_page` 参数拉取，默认中文（`zh`）；后台刷新时会在同一条上游请求中附带 `start_time/end_time`，直接拉取“当天 + 未来 N 天”的区间数据并入库。

## discover.article.pull
- 路径：`POST /api/discover/article/pull`
- data：
  - `latestId` number?（可选）
  - `beforeId` number?（可选）
  - `limit` int?（可选）

## discover.newsflash.pull
- 路径：`POST /api/discover/newsflash/pull`
- data：
  - `latestId` number?（可选）
  - `beforeId` number?（可选）
  - `limit` int?（可选）

## discover.calendar.central-bank.pull
- 路径：`POST /api/discover/calendar/central-bank/pull`
- data：
  - `latestId` number?（可选）
  - `beforeId` number?（可选）
  - `startTime` number?（可选，毫秒）
  - `endTime` number?（可选，毫秒）
  - `limit` int?（可选）

## discover.calendar.financial-events.pull
- 路径：`POST /api/discover/calendar/financial-events/pull`
- data：
  - `latestId` number?（可选）
  - `beforeId` number?（可选）
  - `startTime` number?（可选，毫秒）
  - `endTime` number?（可选，毫秒）
  - `limit` int?（可选）

## discover.calendar.economic-data.pull
- 路径：`POST /api/discover/calendar/economic-data/pull`
- data：
  - `latestId` number?（可选）
  - `beforeId` number?（可选）
  - `startTime` number?（可选，毫秒）
  - `endTime` number?（可选，毫秒）
  - `limit` int?（可选）

## 拉取规则（两接口一致）
- `latestId` 与 `beforeId` 不能同时传。
- 首次拉取（`latestId` 和 `beforeId` 都不传）：
  - 返回最新 N 条（默认 20，受服务端配置限制）。
  - 排序：按 ID 降序（新 -> 旧）。
- 增量拉取（传 `latestId`）：
  - 返回 `id > latestId` 的新数据。
  - 排序：按 ID 升序（旧 -> 新），便于前端顺序追加。
- 下拉历史（传 `beforeId`）：
  - 返回 `id < beforeId` 的更早数据。
  - 排序：按 ID 降序（新 -> 旧），便于列表向下追加。
- 时间区间（传 `startTime` 或 `endTime`）：
  - 返回发布时间落在区间内的数据。
  - 排序：按发布时间降序（新 -> 旧）。

## 服务端补齐策略
- Discover Calendar 后台刷新会在单次上游请求中附带 `start_time/end_time`，默认拉取当天 00:00 到未来 7 天 23:59:59.999 的区间数据。
- 返回数据按去重键统一入库与刷新内存缓存，避免为了补齐未来事件重复请求上游。
- 初始化补历史数据时，仍可继续按分页请求更早记录；常规刷新不再为未来窗口打多次探测请求。

## 响应 data
- `mode` string：`latest` / `incremental` / `history` / `range`
- `latestServerId` number：当前服务端此模块最大 ID
- `hasMore` bool：当前分页方向是否还有更多数据
- `total` int：本次返回条数
- `items[]`：
  - `id` number：服务端唯一 ID（模块内唯一）
  - `title` string：标题
  - `summary` string：摘要（纯文本）
  - `contentHtml` string：正文 HTML
  - `source` string：来源
  - `sourceLogo` string?：来源 logo
  - `pictureUrl` string?：配图
  - `releaseTime` number：发布时间（毫秒）
  - `createdAt` number：服务端入库时间（毫秒）

日历 `items[]` 字段：
- `id` number：服务端唯一 ID（各日历模块内唯一）
- `calendarName` string：事件名称
- `countryCode` string：国家代码
- `countryName` string：国家名称
- `publishTimestamp` number：发布时间（毫秒）
- `importanceLevel` number：重要等级
- `hasExactPublishTime` bool：是否有精确发布时间
- `dataEffect` string?：数据影响（主要经济数据）
- `forecastValue` string?：预测值
- `previousValue` string?：前值
- `revisedPreviousValue` string?：修正前值
- `publishedValue` string?：公布值
- `createdAt` number：服务端入库时间（毫秒）
- `updatedAt` number：服务端更新时间（毫秒）

## 鉴权
- 必须携带：`Authorization: Bearer <token>`
- 复用现有 JWT 校验链路（`AuthTokenService.ValidateTokenAsync`）。
