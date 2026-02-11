# 组件说明

- AlertDialog.tsx: 通用提示弹窗，带确认/取消等基础操作。
- AuthPage.tsx: 登录/认证入口页面容器。
- ChangePassword.tsx: 修改密码表单页面。
- ChatModule.tsx: 聊天模块入口与布局。
- ConditionContainerList.tsx: 策略条件容器/条件组/条件项的列表渲染与操作区。
- ConditionEditorDialog.tsx: 条件编辑弹窗，用于新增/编辑触发条件。
- CryptoMarketPanel.tsx: 行情面板布局与数据展示容器。
- Dashboard.tsx: 主面板布局与各模块切换入口。
// 资讯 / 发现模块
- DiscoverModule.tsx: 发现模块入口页容器，展示市场资讯与快讯列表，并支持通过 `focusNewsId` 属性从首页“新闻精选”轮播跳转到对应新闻项（自动滚动并高亮）。
// Home 模块：首页与快捷入口
- HomeModule.tsx: 首页模块入口容器，含“近期总结”双栏卡片（左侧统计、右侧最近操作日志：开仓/平仓/告警）；仓位事件使用 `assets/SnowUI/cryptoicon` 的币种 SVG 图标，右侧列表超出时滚动显示；首页“快捷入口”按钮已与 `Dashboard` 左侧菜单联动，可一键跳转到“策略创建（策略模块-创建页）”“策略列表（我的）”“分享码导入弹窗”“行情”模块；新增“指标精选”“新闻精选”轮播卡片，分别支持一键跳转到“指标中心（按指标 ID 聚焦卡片）”与“市场资讯（按资讯 ID 聚焦新闻项）”；`HomeModule.css` 已补充响应式网格优化（卡片取消同排强制等高、近期总结固定整行、快捷入口按钮自适应列数、窄屏轮播操作区自动换行），以减少窄屏空白和卡片高度异常。
- IndicatorGeneratorSelector.tsx: 指标生成/选择弹窗，输出指标配置。
- IndicatorModule.tsx: 指标模块入口页容器。
- MarketChart.tsx: 行情图表视图封装，绘图工具栏线段类、通道类与形状类图标使用 `assets/KLineCharts` 本地 SVG。
- MarketModule.tsx: 行情模块入口容器。
- SignIn4.tsx: 登录表单页面。
- SnowUITest.tsx: UI 组件展示/调试页面。
- StrategyConfigDialog.tsx: 策略配置预览弹窗，支持概览/JSON 切换。
- StrategyEditorShell.tsx: 策略编辑器壳结构（头部/主体/底部）。
- StrategyIndicatorPanel.tsx: 已选指标列表与新增指标入口。
- StrategyModule.tsx: 策略创建流程的状态与逻辑编排，支持通过 `initialMenuId` 属性从其他模块（如首页快捷入口）直达指定子页（如 `create` 创建页）。
- StrategyModule.types.ts: 策略相关共享类型定义。
- StrategyTemplateOptions.tsx: 策略创建入口选项（自定义/模板/导入）。
- TestPage.tsx: 测试页容器。
- TradeConfigForm.tsx: 交易规则表单（交易所/币对/风控等）。
- TradingViewDatafeed.ts: TradingView 数据源适配器。
- UIComponentsTest.tsx: UI 组件测试/演示页面。
- UserSettings.tsx: 用户设置模块。
- WhatsOnRoadPanel.tsx: 资讯/路况面板模块。
- WsNotificationBridge.tsx: WebSocket 通知桥接层。

## UI 子目录

`Client/src/components/ui` 下是通用 UI 基础组件（按钮、弹窗、输入控件等）。
