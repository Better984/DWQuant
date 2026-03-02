# Discover 模块 SQL 说明（已拆分）

为避免资讯与日历脚本混写，建表脚本已拆分到各自子模块目录：

## 资讯模块（Feed）
- 路径：`../Feed/Sql/coinglass_discover_feed_schema.sql`
- 包含表：
  - `coinglass_news_articles`
  - `coinglass_news_flashes`

## 日历模块（Calendar）
- 路径：`../Calendar/Sql/coinglass_discover_calendar_schema.sql`
- 包含表：
  - `coinglass_calendar_central_bank_activities`
  - `coinglass_calendar_financial_events`
  - `coinglass_calendar_economic_data`

> 说明：所有表统一使用 `coinglass_` 前缀。
