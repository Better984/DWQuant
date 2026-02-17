# SQL

本目录用于存放 Planet（星球）模块相关的 SQL 结构变更与说明。

## 脚本清单

- `20260216_planet_schema.md`
  - 新增帖子主表、图片表、策略绑定表、点赞/踩表、收藏表、评论表。
  - 约束与索引：
    - 评论表增加唯一键 `uk_planet_post_comment_post_uid`，保证“同一用户同一帖子仅一条评论”。
    - 帖子状态默认值调整为 `normal`，并保留 `hidden` / `deleted` 管理语义。
    - 帖子表增加管理员隐藏追踪字段：`hidden_by_uid` / `hidden_by_admin` / `hidden_at`。
  - 新增绩效缓存表 `strategy_performance_cache`：
    - 用于公开策略场景的 30 日资金曲线缓存（实盘/回测）
    - 支持回测补齐、过期控制、命中统计与多场景复用（星球/分享码/公开市场/官方策略）。
  - 包含兼容迁移 SQL：将历史 `active` 状态统一迁移为 `normal`。
  - 脚本为幂等写法（`CREATE TABLE IF NOT EXISTS`），可重复执行。
