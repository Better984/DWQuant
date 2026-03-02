# Discover Feed SQL 说明

- 脚本：`coinglass_discover_feed_schema.sql`
- 表：
  - `coinglass_news_articles`
  - `coinglass_news_flashes`

去重键规则：
- `title + release_time + source` 计算 SHA256。
