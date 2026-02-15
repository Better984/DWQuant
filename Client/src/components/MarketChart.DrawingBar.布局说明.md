# MarketChart 绘图栏（klinecharts-pro-drawing-bar）布局与复刻说明

本文档描述行情图左侧**绘图工具栏**的完整结构：DOM、样式、分组、按键、功能、SVG 资源与交互，便于从 UI 到代码一步一复刻。

---

## 1. 在页面中的位置

- **DOM 路径**：`div#root` → `div.dashboard-container` → `main.dashboard-main-content` → `div.market-module` → `div.market-chart-shell` → `div.market-chart-body` → **`div.klinecharts-pro-drawing-bar`**
- **位置**：紧贴 `market-chart-body` 左侧，与 `market-chart-stage`（图表画布）并列。
- **典型尺寸**：`width: 52px`，`height: 100%`（与 `market-chart-body` 同高，如 644px）。

---

## 2. 整体 DOM 结构

```html
<div ref={drawingBarRef} class="klinecharts-pro-drawing-bar">
  <!-- 第 1 组：线段工具 -->
  <div class="item" tabindex="0">
    <span class="icon-overlay" title="当前选中工具名">...</span>
    <div class="icon-arrow">...</div>
    <!-- 展开时 -->
    <ul class="list [is-up]">...</ul>
  </div>
  <!-- 第 2 组：通道工具 -->
  <div class="item">...</div>
  <!-- 第 3 组：绘图（形状） -->
  <div class="item">...</div>
  <!-- 第 4 组：绘图（斐波那契） -->
  <div class="item">...</div>
  <!-- 第 5 组：形态（波浪） -->
  <div class="item">...</div>

  <span class="split-line"></span>

  <!-- 磁吸 -->
  <div class="item">...</div>
  <!-- 锁定 -->
  <div class="item">...</div>
  <!-- 显示/隐藏所有绘图 -->
  <div class="item">...</div>
  <!-- 清除所有绘图 -->
  <div class="item">...</div>
</div>
```

- 每个 **`.item`** 占一行，内部为 **主图标区**（`.icon-overlay`）+ **展开箭头**（`.icon-arrow`），可带一个展开的 **`.list`**（工具子项或磁吸模式）。
- **`.split-line`** 为一条 1px 高的分割线，将「绘图工具组」与「磁吸/锁定/显示/清除」分开。

---

## 3. 分组与功能一览

| 序号 | 分组 id | 显示名 | 功能说明 | 是否有展开列表 |
|------|---------|--------|----------|----------------|
| 1 | singleLine | 线段工具 | 在图表上绘制各类直线/射线/线段/价格线 | 是 |
| 2 | moreLine | 通道工具 | 价格通道线、平行直线 | 是 |
| 3 | polygon | 绘图 | 圆、矩形、平行四边形、三角形、十字星 | 是 |
| 4 | fibonacci | 绘图 | 斐波那契与江恩箱类工具 | 是 |
| 5 | wave | 形态 | 波浪形态（XABCD、ABCD、三/五/八浪等） | 是 |
| — | — | **分割线** | 仅视觉分隔 | 否 |
| 6 | magnet | （磁吸） | 弱磁/强磁吸附到 K 线或关键点 | 是 |
| 7 | — | 锁定 | 锁定/解锁绘图，防止误选误移 | 否 |
| 8 | — | 显示/隐藏 | 一键显示或隐藏所有绘图 | 否 |
| 9 | — | 清除 | 删除图表上全部绘图 | 否 |

---

## 4. 各组按键与子项明细

### 4.1 线段工具（singleLine）

- **默认展示图标**：当前选中工具的图标（默认 `horizontalStraightLine`）。
- **点击主图标**：以当前选中工具开始绘图。
- **点击箭头**：展开/收起子列表；列表向下展开（或向上 `.list.is-up`，由空间计算）。

| 工具 id | 中文名 | overlay 名称 | 对应 SVG 文件名 |
|---------|--------|--------------|-----------------|
| horizontalStraightLine | 水平直线 | horizontalStraightLine | 01_horizontal_line.svg |
| horizontalRayLine | 水平射线 | horizontalRayLine | 02_horizontal_ray.svg |
| horizontalSegment | 水平线段 | horizontalSegment | 03_horizontal_segment.svg |
| verticalStraightLine | 垂直直线 | verticalStraightLine | 04_vertical_line.svg |
| verticalRayLine | 垂直射线 | verticalRayLine | 05_vertical_ray.svg |
| verticalSegment | 垂直线段 | verticalSegment | 06_vertical_segment.svg |
| straightLine | 直线 | straightLine | 07_straight_line.svg |
| rayLine | 射线 | rayLine | 08_ray.svg |
| segment | 线段 | segment | 09_segment.svg |
| priceLine | 价格线 | priceLine | 10_price_line.svg |

### 4.2 通道工具（moreLine）

| 工具 id | 中文名 | overlay | SVG 文件名 |
|---------|--------|---------|-------------|
| priceChannelLine | 价格通道线 | priceChannelLine | 06_price_channel.svg |
| parallelStraightLine | 平行直线 | parallelStraightLine | 05_parallel_lines.svg |

### 4.3 绘图 - 形状（polygon）

| 工具 id | 中文名 | overlay | SVG 文件名 |
|---------|--------|---------|-------------|
| circle | 圆 | circle | 02_circle.svg |
| rect | 矩形 | rect | 01_rectangle.svg |
| parallelogram | 平行四边形 | parallelogram | 04_parallelogram.svg |
| triangle | 三角形 | triangle | 03_triangle.svg |
| crossStar | 十字星 | crossStar | （内联 SVG，无文件） |

### 4.4 绘图 - 斐波那契（fibonacci）

| 工具 id | 中文名 | overlay | SVG 文件名 |
|---------|--------|---------|-------------|
| fibonacciLine | 斐波那契回调直线 | fibonacciLine | 11_fibonacci_retracement_line.svg |
| fibonacciSegment | 斐波那契回调线段 | fibonacciSegment | 12_fibonacci_retracement_segment.svg |
| fibonacciCircle | 斐波那契圆环 | fibonacciCircle | 13_fibonacci_circle.svg |
| fibonacciSpiral | 斐波那契螺旋 | fibonacciSpiral | 14_fibonacci_spiral.svg |
| fibonacciSpeedResistanceFan | 斐波那契速度阻力扇 | fibonacciSpeedResistanceFan | 15_fibonacci_speed_resistance_fan.svg |
| fibonacciExtension | 斐波那契趋势扩展 | fibonacciExtension | 16_fibonacci_extension.svg |
| gannBox | 江恩箱 | gannBox | 17_gann_box.svg |

### 4.5 形态 - 波浪（wave）

| 工具 id | 中文名 | overlay | SVG 文件名 |
|---------|--------|---------|-------------|
| xabcd | XABCD形态 | xabcd | 18_wave_xabcd.svg |
| abcd | ABCD形态 | abcd | 19_wave_abcd.svg |
| threeWaves | 三浪 | threeWaves | 20_wave_three.svg |
| fiveWaves | 五浪 | fiveWaves | 21_wave_five.svg |
| eightWaves | 八浪 | eightWaves | 22_wave_eight.svg |
| anyWaves | 任意浪 | anyWaves | 23_wave_any.svg |

### 4.6 分割线（split-line）

- 仅一个 `<span class="split-line">`，无点击逻辑。
- 样式：宽 100%，高 1px，背景为边框色，上边距 8px。

### 4.7 磁吸（magnet）

- **主图标**：根据 `magnetMode` 与 `isMagnetEnabled` 显示四者之一：
  - 弱磁关：magnet-weak-off.svg
  - 弱磁开：magnet-weak-on.svg
  - 强磁关：magnet-strong-off.svg
  - 强磁开：magnet-strong-on.svg
- **点击主图标**：切换 `isMagnetEnabled`（开/关磁吸）。
- **点击箭头**：展开列表，两项：
  - 弱磁模式（MagnetWeakOffIcon）
  - 强磁模式（MagnetStrongOffIcon）
- 选中项在 `.icon-overlay` 上带 `.selected`。

### 4.8 锁定

- **主图标**：锁定用 drawing-lock-on.svg，解锁用 drawing-lock-off.svg。
- **点击**：切换 `isDrawingLocked`（锁定后不可选/移绘图）。
- 无展开列表。

### 4.9 显示/隐藏所有绘图

- **主图标**：当前为「显示」时用 drawing-hide-all.svg（表示可执行「隐藏」）；当前为「隐藏」时用 drawing-show-all.svg（表示可执行「显示」）。
- **点击**：切换 `isDrawingVisible`，并调用图表 API 显示/隐藏所有绘图。
- 隐藏状态时该按钮带 `.selected`。

### 4.10 清除所有绘图

- **主图标**：drawing-clear-all.svg。
- **点击**：调用 `handleClearDrawings`，删除图表上全部绘图。
- 无展开列表、无选中态。

---

## 5. SVG 资源清单（名称与功能对应）

路径前缀：`Client/src/assets/KLineCharts/`。

### 5.1 线段类（01～10）

| 文件名 | 功能 | 用于工具 id |
|--------|------|-------------|
| 01_horizontal_line.svg | 水平直线 | horizontalStraightLine |
| 02_horizontal_ray.svg | 水平射线 | horizontalRayLine |
| 03_horizontal_segment.svg | 水平线段 | horizontalSegment |
| 04_vertical_line.svg | 垂直直线 | verticalStraightLine |
| 05_vertical_ray.svg | 垂直射线 | verticalRayLine |
| 06_vertical_segment.svg | 垂直线段 | verticalSegment |
| 07_straight_line.svg | 直线 | straightLine |
| 08_ray.svg | 射线 | rayLine |
| 09_segment.svg | 线段 | segment |
| 10_price_line.svg | 价格线 | priceLine |

### 5.2 通道与形状（01～06，与线段不同目录语义）

| 文件名 | 功能 | 用于工具 id |
|--------|------|-------------|
| 01_rectangle.svg | 矩形 | rect |
| 02_circle.svg | 圆 | circle |
| 03_triangle.svg | 三角形 | triangle |
| 04_parallelogram.svg | 平行四边形 | parallelogram |
| 05_parallel_lines.svg | 平行直线 | parallelStraightLine |
| 06_price_channel.svg | 价格通道线 | priceChannelLine |

### 5.3 斐波那契与江恩（11～17）

| 文件名 | 功能 | 用于工具 id |
|--------|------|-------------|
| 11_fibonacci_retracement_line.svg | 斐波那契回调直线 | fibonacciLine |
| 12_fibonacci_retracement_segment.svg | 斐波那契回调线段 | fibonacciSegment |
| 13_fibonacci_circle.svg | 斐波那契圆环 | fibonacciCircle |
| 14_fibonacci_spiral.svg | 斐波那契螺旋 | fibonacciSpiral |
| 15_fibonacci_speed_resistance_fan.svg | 斐波那契速度阻力扇 | fibonacciSpeedResistanceFan |
| 16_fibonacci_extension.svg | 斐波那契趋势扩展 | fibonacciExtension |
| 17_gann_box.svg | 江恩箱 | gannBox |

### 5.4 波浪形态（18～23）

| 文件名 | 功能 | 用于工具 id |
|--------|------|-------------|
| 18_wave_xabcd.svg | XABCD 形态 | xabcd |
| 19_wave_abcd.svg | ABCD 形态 | abcd |
| 20_wave_three.svg | 三浪 | threeWaves |
| 21_wave_five.svg | 五浪 | fiveWaves |
| 22_wave_eight.svg | 八浪 | eightWaves |
| 23_wave_any.svg | 任意浪 | anyWaves |

### 5.5 磁吸与绘图控制

| 文件名 | 功能 |
|--------|------|
| magnet-weak-off.svg | 弱磁吸关闭 |
| magnet-weak-on.svg | 弱磁吸开启 |
| magnet-strong-off.svg | 强磁吸关闭 |
| magnet-strong-on.svg | 强磁吸开启 |
| drawing-lock-off.svg | 绘图未锁定（可编辑） |
| drawing-lock-on.svg | 绘图已锁定 |
| drawing-show-all.svg | 显示所有绘图（当前为隐藏时显示此图标） |
| drawing-hide-all.svg | 隐藏所有绘图（当前为显示时显示此图标） |
| drawing-clear-all.svg | 清除所有绘图 |

**内联 SVG（无文件）**：十字星（crossStar）在 `ToolIcon` 中写死为十字线 + 圆心。

---

## 6. 主要 CSS 类与样式要点

- **`.klinecharts-pro-drawing-bar`**
  - 宽 52px，高 100%，左边框 1px，背景与主题变量一致，`overflow: visible`，`z-index: 60`。
- **`.item`**
  - 横向 flex，居中对齐，每项上边距 8px，光标 pointer，颜色用次要文字色。
- **`.icon-overlay`**
  - 32×32px，圆角 2px；hover 背景高亮；`.selected` 时背景与主色一致，图标填色为主色。
- **`.icon-overlay svg`**
  - 28×28px 显示。
- **`.icon-arrow`**
  - 绝对定位在 item 右侧，32px 高、10px 宽，默认透明，item hover 时显示；内为 4×6 小三角 SVG，展开时加 `.rotate` 旋转 180°。
- **`.list`**
  - 绝对定位在 bar 右侧（left: calc(100% + 1px)），白底、阴影、圆角 2px，最大高度 min(320px, 100vh-16px)，可纵向滚动；列表项 40px 高，左内边距 16px，文字左 padding 8px。
- **`.list.is-up`**
  - 列表从底部对齐（bottom: 0），用于上方空间不足时向上展开。
- **`.split-line`**
  - 宽 100%，高 1px，背景为边框色，上边距 8px。

CSS 变量示例：`--klinecharts-pro-border-color`、`--klinecharts-pro-background-color`、`--klinecharts-pro-text-second-color`、`--klinecharts-pro-primary-color`、`--klinecharts-pro-hover-background-color`、`--klinecharts-pro-popover-background-color`、`--klinecharts-pro-selected-color`、`--klinecharts-pro-text-color`。

---

## 7. 交互与状态

1. **主图标点击**：工具组为「开始对应 overlay 绘图」；磁吸为切换开/关；锁定为切换锁定；显示/隐藏为切换可见；清除为执行清除。
2. **箭头点击**：展开或收起该组的 `.list`；仅一组可展开，展开另一组会收起当前组；列表方向由 `toolListDirection` 决定（down / is-up）。
3. **列表项点击**：工具组为「设为该组当前工具 + 开始绘图 + 收起列表」；磁吸为「切换弱/强磁 + 收起列表」。
4. **选中态**：当前绘图工具在列表里对应项 `.icon-overlay.selected`；磁吸开启时磁吸按钮 `.selected`；绘图隐藏时「显示/隐藏」按钮 `.selected`；锁定开启时锁定按钮 `.selected`。
5. **点击外部**：通过 body 的 click 检测，若点击不在 drawingBar 内且不在展开的 list 内，则收起展开的 list。

---

## 8. 复刻检查清单

- [ ] 容器：52px 宽、全高、左侧边框与背景。
- [ ] 5 个绘图工具组，每组：主图标 + 右箭头，展开后为带标签的列表。
- [ ] 分割线。
- [ ] 磁吸（4 个 SVG 切换）、锁定（2 个 SVG 切换）、显示/隐藏（2 个 SVG 切换）、清除（1 个 SVG）。
- [ ] 线段 10 项、通道 2 项、形状 5 项、斐波 7 项、波浪 6 项，id/overlay/文件名与上表一致。
- [ ] 十字星为内联 SVG，无文件。
- [ ] 列表最大高度与 is-up 方向逻辑。
- [ ] 主题变量与 hover/selected 样式一致。

按上述 DOM、分组、按键、SVG 与 CSS 逐项实现即可完整复刻绘图栏。

---

## TA Indicator Integration (talib-web)

- `Client` installs `talib-web` (`npm install talib-web`).
- WASM is self-hosted at `Client/public/talib.wasm`.
- Runtime init is done by `ensureTalibReady("/talib.wasm")` in `Client/src/lib/talibInit.ts`.
- KLine custom indicators are batch-registered in `Client/src/lib/registerTalibIndicators.ts`.
- Registered indicator names use `ta_` prefix so they are distinguishable from built-ins.
- Registration is metadata-driven:
  - reads `Client/public/talib_indicators_config.json` (backend-aligned 160 indicators)
  - reads `Client/public/talib_web_api_meta.json` (talib-web function metadata)
  - maps backend indicator code/method to talib-web API and registers all resolvable items
- `MarketChart` waits for `registerTalibIndicators()` before creating indicator instances, then uses `ta_` names in the indicator panel.
- Indicator selection UI is now a dedicated dialog in `MarketChart`:
  - open from toolbar `指标` button
  - supports keyword search (`name/code`)
  - supports pane filter (`全部/主图/副图`)
  - supports category filter (grouped by TA indicator group)
  - supports quick `关闭副图`
