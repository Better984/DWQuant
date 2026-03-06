# Discover Calendar SQL 说明

- 脚本：`coinglass_discover_calendar_schema.sql`
- 表：
  - `coinglass_calendar_central_bank_activities`
  - `coinglass_calendar_financial_events`
  - `coinglass_calendar_economic_data`

去重键规则：
- `calendar_name + publish_timestamp + country_code(+country_name)` 计算 SHA256。

补充说明：
- “未来一周补齐”仅是上游拉取策略增强，不新增表字段，也不修改现有索引。
- 未来窗口探测返回的额外事件仍复用同一套 `dedupe_key` + `UPSERT` 规则入库。
