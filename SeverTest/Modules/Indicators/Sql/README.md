# Indicators SQL 说明

## 文件列表
- `indicators.sql`：指标模块建表与默认数据脚本。

## 表结构说明
- `indicator_definitions`：指标定义与刷新策略。
  - `config_json` 统一存放来源配置（`source.type`）与字段目录（`fields[]`），供 `indicator.meta.list` 对外输出。
- `indicator_snapshots`：每个 `code + scope_key` 的最新快照。
- `coinglass_fear_greed_history`：恐慌贪婪历史点位（按指标独立分表）。
- `coinglass_etf_flow_history`：现货 ETF 净流入历史点位（按指标独立分表，`scope_key` 区分 `BTC / ETH / SOL / XRP`）。
- `coinglass_futures_footprint_history`：合约足迹图历史点位（按指标独立分表，`scope_key` 区分 `BTC / ETH`）。
- `coinglass_top_long_short_account_ratio_history`：大户账户数多空比历史点位（按指标独立分表，`scope_key` 区分 `BTC / ETH`）。
- `coinglass_grayscale_holdings_history`：灰度持仓历史快照（按指标独立分表）。
- `coinglass_coin_unlock_list_history`：代币解锁列表历史快照（按指标独立分表）。
- `coinglass_coin_vesting_history`：代币解锁详情历史快照（按指标独立分表，`scope_key` 区分 `symbol`）。
- `coinglass_exchange_assets_history`：交易所资产明细历史快照（按指标独立分表，`scope_key` 区分 `exchangeName`）。
- `coinglass_exchange_balance_list_history`：交易所余额排行历史快照（按指标独立分表，`scope_key` 区分 `symbol`）。
- `coinglass_exchange_balance_chart_history`：交易所余额趋势历史点位（按指标独立分表，`scope_key` 区分 `symbol`）。
- `coinglass_hyperliquid_whale_alert_history`：Hyperliquid 鲸鱼提醒历史快照（按指标独立分表）。
- `coinglass_hyperliquid_whale_position_history`：Hyperliquid 鲸鱼持仓历史快照（按指标独立分表）。
- `coinglass_hyperliquid_position_history`：Hyperliquid 持仓排行历史快照（按指标独立分表，`scope_key` 区分 `symbol`）。
- `coinglass_hyperliquid_user_position_history`：Hyperliquid 用户持仓历史快照（按指标独立分表，`scope_key` 区分 `userAddress`）。
- `coinglass_hyperliquid_wallet_position_distribution_history`：Hyperliquid 钱包持仓分布历史快照（按指标独立分表）。
- `coinglass_hyperliquid_wallet_pnl_distribution_history`：Hyperliquid 钱包盈亏分布历史快照（按指标独立分表）。
- `coinglass_liquidation_heatmap_model1_history`：交易对爆仓热力图（模型1）历史点位（按指标独立分表）。
- `indicator_refresh_logs`：刷新执行日志。

## 历史表命名规范
- 指标历史表统一使用：`coinglass_{指标代码}_history`。
- 示例：`coinglass.fear_greed` 对应 `coinglass_fear_greed_history`，`coinglass.etf_flow` 对应 `coinglass_etf_flow_history`（不同 ETF 资产通过 `scope_key` 区分），`coinglass.futures_footprint` 对应 `coinglass_futures_footprint_history`（不同币种通过 `scope_key` 区分），`coinglass.top_long_short_account_ratio` 对应 `coinglass_top_long_short_account_ratio_history`（不同币种通过 `scope_key` 区分），`coinglass.grayscale_holdings` 对应 `coinglass_grayscale_holdings_history`，`coinglass.coin_unlock_list` 对应 `coinglass_coin_unlock_list_history`，`coinglass.coin_vesting` 对应 `coinglass_coin_vesting_history`，`coinglass.exchange_assets` 对应 `coinglass_exchange_assets_history`，`coinglass.exchange_balance_list` 对应 `coinglass_exchange_balance_list_history`，`coinglass.exchange_balance_chart` 对应 `coinglass_exchange_balance_chart_history`，`coinglass.hyperliquid_whale_alert` 对应 `coinglass_hyperliquid_whale_alert_history`，`coinglass.hyperliquid_whale_position` 对应 `coinglass_hyperliquid_whale_position_history`，`coinglass.hyperliquid_position` 对应 `coinglass_hyperliquid_position_history`，`coinglass.hyperliquid_user_position` 对应 `coinglass_hyperliquid_user_position_history`，`coinglass.hyperliquid_wallet_position_distribution` 对应 `coinglass_hyperliquid_wallet_position_distribution_history`，`coinglass.hyperliquid_wallet_pnl_distribution` 对应 `coinglass_hyperliquid_wallet_pnl_distribution_history`，`coinglass.liquidation_heatmap_model1` 对应 `coinglass_liquidation_heatmap_model1_history`。

## 默认指标定义
- `coinglass.fear_greed`：默认 `scope_key=global`，默认关闭，刷新周期 1200 秒。
- `coinglass.etf_flow`：默认 `scope_key=asset=BTC`，刷新周期 20 秒。
- `coinglass.futures_footprint`：默认 `scope_key=asset=BTC`，刷新周期 60 秒。
- `coinglass.top_long_short_account_ratio`：默认 `scope_key=asset=BTC`，刷新周期 60 秒。
- `coinglass.grayscale_holdings`：默认 `scope_key=global`，刷新周期 60 秒。
- `coinglass.coin_unlock_list`：默认 `scope_key=global`，刷新周期 21600 秒（6 小时），优先复用数据库快照。
- `coinglass.coin_vesting`：默认 `scope_key=symbol=HYPE`，刷新周期 21600 秒（6 小时），按币种落库缓存详情。
- `coinglass.exchange_assets`：默认 `scope_key=exchangeName=Binance`，刷新周期 20 秒。
- `coinglass.exchange_balance_list`：默认 `scope_key=symbol=BTC`，刷新周期 20 秒。
- `coinglass.exchange_balance_chart`：默认 `scope_key=symbol=BTC`，刷新周期 20 秒。
- `coinglass.hyperliquid_whale_alert`：默认 `scope_key=global`，刷新周期 20 秒。
- `coinglass.hyperliquid_whale_position`：默认 `scope_key=global`，刷新周期 20 秒。
- `coinglass.hyperliquid_position`：默认 `scope_key=symbol=BTC`，刷新周期 20 秒。
- `coinglass.hyperliquid_user_position`：默认 `scope_key=userAddress=0xa5b0edf6b55128e0ddae8e51ac538c3188401d41`，刷新周期 20 秒。
- `coinglass.hyperliquid_wallet_position_distribution`：默认 `scope_key=global`，刷新周期 20 秒。
- `coinglass.hyperliquid_wallet_pnl_distribution`：默认 `scope_key=global`，刷新周期 20 秒。
- `coinglass.liquidation_heatmap_model1`：默认 `scope_key=exchange=Binance&symbol=BTCUSDT&range=3d`，刷新周期 20 秒。

## 配置说明
- `indicators.sql` 中的 `config_json` 使用初始化占位值；运行后端 `EnsureSeedDefinitionsAsync` 时，会按 `IndicatorRepository` 中的 `Build*ConfigJson()` 自动补齐当前字段目录与来源配置。
- 新增交易所资产相关指标后，前端单卡片会组合读取 `coinglass.exchange_assets`、`coinglass.exchange_balance_list`、`coinglass.exchange_balance_chart` 三组数据，并在卡片内切换展示。
- 新增 Hyperliquid 相关指标后，前端单卡片会组合读取 `coinglass.hyperliquid_whale_alert`、`coinglass.hyperliquid_whale_position`、`coinglass.hyperliquid_position`、`coinglass.hyperliquid_user_position`、`coinglass.hyperliquid_wallet_position_distribution`、`coinglass.hyperliquid_wallet_pnl_distribution` 六组数据，并在卡片内切换展示。
- 新增合约足迹图后，前端单卡片会读取 `coinglass.futures_footprint`，并在卡片内切换 `BTC / ETH` 两个资产视图。
- 新增大户账户数多空比后，前端单卡片会读取 `coinglass.top_long_short_account_ratio`，并在卡片内切换 `BTC / ETH` 两个资产视图。
- 新增代币解锁相关指标后，前端单卡片会组合读取 `coinglass.coin_unlock_list` 与 `coinglass.coin_vesting`，左侧展示解锁列表，右侧展示选中币种的锁仓与未来解锁详情。

## 使用方式
1. 初始化数据库时执行 `indicators.sql`。
2. 生产环境推荐开启后端 `Indicators.AutoCreateSchema=true` 作为兜底，但仍建议通过脚本管理版本。
3. 新增指标时，优先插入 `indicator_definitions`（含 `config_json.fields` 字段目录），再实现采集器逻辑。
