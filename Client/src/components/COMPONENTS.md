# 组件说明

## 命名与资源约定
- 基础 UI 组件（`ui/` 下）使用 CSS 类前缀 `ui-`（如 `ui-dialog`、`ui-button`）。
- 图标与静态资源路径：`src/assets/icons/`，子目录 `crypto`、`cex`、`country`、`head`、`icon`。
- UI 组件测试页：`UIComponentsTest.tsx`，路由 `/ui-test`。
- 组件样式文件采用“同目录同名”约定（如 `AuthPage.tsx` 对应 `auth/AuthPage.css`）。
- `src` 目录仅保留 TS/TSX 源文件，不保留同名 `.js` 副本，避免解析歧义。

## 目录分组与导入约定（迁移后）
- 业务组件按功能拆分到子目录：`auth/`、`user/`、`dialogs/`、`market/`、`indicator/`、`discover/`、`home/`、`chat/`、`strategy/`、`layout/`。
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
- **Dashboard**：主面板布局与各模块切换入口；主内容模块改为 `React.lazy` 懒加载，并将行情页布局切换由 `:has` 选择器改为显式 class，减少样式重算。
- **DiscoverModule**：发现模块入口页，展示市场资讯与快讯列表；支持通过 `focusNewsId` 从首页“新闻精选”跳转到对应新闻项。
- **HomeModule**：首页模块入口，含近期总结、快捷入口、指标精选、新闻精选等；仓位事件使用 `assets/icons/crypto` 币种图标。
- **IndicatorGeneratorSelector**：指标生成/选择弹窗，输出指标配置。
- **IndicatorModule**：指标模块入口页容器。
- **MarketChart**：行情图表视图封装；绘图工具栏图标使用 `assets/KLineCharts` 本地 SVG。
- **MarketModule**：行情模块入口容器。
- **SignIn4**：登录表单页面。
- **StrategyConfigDialog**：策略配置预览弹窗，支持概览/JSON 切换。
- **StrategyDetailDialog**：策略详情弹窗。
- **StrategyEditorShell**：策略编辑器壳结构（头部/主体/底部）。
- **StrategyIndicatorPanel**：已选指标列表与新增指标入口。
- **StrategyModule**：策略创建流程的状态与逻辑编排；支持通过 `initialMenuId` 从其他模块直达指定子页。
- **StrategyModule.types**：策略相关共享类型定义。
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
