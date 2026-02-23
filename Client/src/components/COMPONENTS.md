# 组件说明

## 命名与资源约定
- 基础 UI 组件（`ui/` 下）使用 CSS 类前缀 `ui-`（如 `ui-dialog`、`ui-button`）。
- 图标与静态资源路径：`src/assets/icons/`，子目录 `crypto`、`cex`、`country`、`head`、`icon`。
- UI 组件测试页：`UIComponentsTest.tsx`，路由 `/ui-test`。
- 组件样式文件采用“同目录同名”约定（如 `AuthPage.tsx` 对应 `auth/AuthPage.css`）。
- `src` 目录仅保留 TS/TSX 源文件，不保留同名 `.js` 副本，避免解析歧义。

## 目录分组与导入约定（迁移后）
- 业务组件按功能拆分到子目录：`auth/`、`user/`、`dialogs/`、`market/`、`indicator/`、`discover/`、`planet/`、`home/`、`chat/`、`strategy/`、`layout/`。
- 同目录组件互相引用使用 `./`（例如 `strategy` 目录内组件互引）。
- 跨组件分组引用使用 `../<group>/...`（例如 `strategy` 引用 `dialogs`/`market`/`indicator`）。
- 组件子目录访问应用层目录统一使用 `../../` 前缀；聚合入口使用显式 TS 后缀：`../../network/index.ts`、`../../auth/profileStore.ts`、`../../assets/...`、`../../lib/...`。
- 组件子目录访问通用 UI 组件统一使用 `../ui/index.ts`（根目录组件使用 `./ui/index.ts`），避免在开发态出现 `index.js` 解析 404。

## 业务组件（按功能概览）
- **AlertDialog**：通用提示弹窗，带确认/取消等基础操作。
- **AuthPage**：登录/认证入口页面容器。
- **ChangePassword**：修改密码表单页面。
- **ChatModule**：聊天模块入口与布局。
- **ConditionContainerList**：策略条件容器/条件组/条件项的列表渲染与操作区。
- **ConditionEditorDialog**：条件编辑弹窗，用于新增/编辑触发条件。
- **CryptoMarketPanel**：行情面板布局与数据展示容器；行情推送更新采用帧内合并，降低高频 tick 导致的重渲染抖动。
- **Dashboard**：主面板布局与各模块切换入口；左侧导航已包含 `星球` 入口并支持切换到社区页面。主内容模块改为 `React.lazy` 懒加载，并将行情页布局切换由 `:has` 选择器改为显式 class，减少样式重算。
- **DiscoverModule**：发现模块入口页，展示市场资讯与快讯列表；支持通过 `focusNewsId` 从首页“新闻精选”跳转到对应新闻项。
- **HomeModule**：首页模块入口，含近期总结、快捷入口、指标精选、新闻精选等；仓位事件使用 `assets/icons/crypto` 币种图标。近期总结时间窗口切换支持“立即高亮 + 切换中提示 + 失败回退”，避免交互无反馈。
- **IndicatorGeneratorSelector**：指标生成/选择弹窗，输出指标配置。
- **IndicatorModule**：指标模块入口页容器。
- **MarketChart**：行情图表视图封装；绘图工具栏图标使用 `assets/KLineCharts` 本地 SVG。
- **MarketModule**：行情模块入口容器。
- **PlanetModule**：星球社区模块入口，支持发帖、图片上传、策略绑定、帖子状态管理（正常/隐藏/删除）、策略30日曲线展示（实盘/回测来源标记）、评论区内联展开分页（首批5条、更多10条）与作者互动统计；帖子互动（点赞/踩/收藏/评论/状态切换）采用局部动态刷新与失败回滚，避免每次操作全量重拉列表。
- **SignIn4**：登录表单页面。
- **StrategyConfigDialog**：策略配置预览弹窗，支持概览/JSON 切换。
- **StrategyCurveSparkline**：策略卡片轻量曲线组件（SVG 实现），统一展示 30 日资金曲线与“实盘/回测”来源标签。
- **StrategyDetailDialog**：策略详情弹窗。
- **StrategyList**：策略列表页，策略状态/发布/同步/移除/删除等操作改为单条记录局部更新，编辑器提交通过 `strategy:changed` 事件的 `skipReload` 标记避免触发全量重拉。
- **StrategyEditorShell**：策略编辑器壳结构（头部/主体/底部）。
- **StrategyIndicatorPanel**：已选指标列表与新增指标入口。
- **StrategyModule**：策略创建流程的状态与逻辑编排；支持通过 `initialMenuId` 从其他模块直达指定子页。
- **StrategyModule.types**：策略相关共享类型定义。
- **StrategyRecommend / OfficialStrategyList / StrategyMarketList**：统一透传并展示 `pnlSeries30d`、`curveSource`、`isBacktestCurve`，确保官方/市场/推荐三处表现一致。
- **StrategyTemplateOptions**：策略创建入口选项（自定义/模板/导入）。
- **TestPage**：测试页容器。
- **TradeConfigForm**：交易规则表单（交易所/币对/风控等）。
- **TradingViewDatafeed**：TradingView 数据源适配器。
- **UIComponentsTest**：UI 组件测试/演示页面（路由 `/ui-test`）。
- **UserSettings**：用户设置模块。
- **WhatsOnRoadPanel**：资讯/路况面板模块。
- **WsNotificationBridge**：WebSocket 通知桥接层。

## UI 子目录（`ui/`）
通用基础组件：Button、Dialog、Select、SelectItem、SelectCard、TextInput、SearchInput、Slider、Avatar、AvatarGroup、PeopleList、StatusBadge、Notification、NotificationToast 等，统一从 `components/ui` 导出。

## 全局滚动条样式
- 可滚动容器统一添加 `ui-scrollable` 类，使用 `index.css` 中定义的全局滚动条样式（4px 宽度、透明轨道、统一拇指颜色与 hover 效果）。
- 所有带 `overflow-y: auto` 或 `overflow: auto` 的列表、面板、弹窗内容区等，均应添加 `ui-scrollable`，保证全站滚动条视觉一致。
- 已适配：Dashboard（主内容、左右侧栏）、PlanetModule、StrategyModule/StrategyConfigDialog/StrategyDetailDialog/StrategyHistoryDialog、MarketChart、UserSettings、WhatsOnRoadPanel、HomeModule、ChatModule、CryptoMarketPanel、IndicatorGeneratorSelector、HistoricalDataCacheDialog、Select、Dialog 等。
