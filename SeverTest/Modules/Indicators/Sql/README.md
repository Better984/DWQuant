# Indicators SQL 说明

## 文件列表
- `indicators.sql`：指标模块建表与默认数据脚本。

## 表结构说明
- `indicator_definitions`：指标定义与刷新策略。
  - `config_json` 统一存放来源配置（`source.type`）与字段目录（`fields[]`），供 `indicator.meta.list` 对外输出。
- `indicator_snapshots`：每个 `code + scope_key` 的最新快照。
- `coinglass_fear_greed_history`：恐慌贪婪历史点位（按指标独立分表）。
- `coinglass_etf_flow_history`：比特币现货 ETF 净流入历史点位（按指标独立分表）。
- `coinglass_liquidation_heatmap_model1_history`：交易对爆仓热力图（模型1）历史点位（按指标独立分表）。
- `indicator_refresh_logs`：刷新执行日志。

## 历史表命名规范
- 指标历史表统一使用：`coinglass_{指标代码}_history`。
- 示例：`coinglass.fear_greed` 对应 `coinglass_fear_greed_history`，`coinglass.etf_flow` 对应 `coinglass_etf_flow_history`，`coinglass.liquidation_heatmap_model1` 对应 `coinglass_liquidation_heatmap_model1_history`。

## 使用方式
1. 初始化数据库时执行 `indicators.sql`。
2. 生产环境推荐开启后端 `Indicators.AutoCreateSchema=true` 作为兜底，但仍建议通过脚本管理版本。
3. 新增指标时，优先插入 `indicator_definitions`（含 `config_json.fields` 字段目录），再实现采集器逻辑。
