# Indicators SQL 说明

## 文件列表
- `indicators.sql`：指标模块建表与默认数据脚本。

## 表结构说明
- `indicator_definitions`：指标定义与刷新策略。
- `indicator_snapshots`：每个 `code + scope_key` 的最新快照。
- `indicator_history`：指标历史点位。
- `indicator_refresh_logs`：刷新执行日志。

## 使用方式
1. 初始化数据库时执行 `indicators.sql`。
2. 生产环境推荐开启后端 `Indicators.AutoCreateSchema=true` 作为兜底，但仍建议通过脚本管理版本。
3. 新增指标时，优先插入 `indicator_definitions`，再实现采集器逻辑。
