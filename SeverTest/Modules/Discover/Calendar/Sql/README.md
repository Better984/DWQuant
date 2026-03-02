# Discover Calendar SQL 说明

- 脚本：`coinglass_discover_calendar_schema.sql`
- 表：
  - `coinglass_calendar_central_bank_activities`
  - `coinglass_calendar_financial_events`
  - `coinglass_calendar_economic_data`

去重键规则：
- `calendar_name + publish_timestamp + country_code(+country_name)` 计算 SHA256。
