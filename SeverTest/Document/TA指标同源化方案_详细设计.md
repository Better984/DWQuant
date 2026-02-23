# TA指标同源化方案（详细设计）

## 1. 文档目的

本文档用于指导 `DWQuant` 在以下目标下进行可实施改造：

1. 前端 `Client` 独立完成指标计算，不依赖后端计算接口。
2. 后端 `SeverTest` 继续具备指标计算能力，用于策略运行、回测、风控。
3. 前后端指标结果可验证一致，避免业务行为偏差。

## 2. 现状分析

### 2.1 前端现状

- 使用 `talib-web` + `talib.wasm` 本地计算指标。
- `registerTalibIndicators.ts` 动态注册指标，读取：
  - `/talib_indicators_config.json`
  - `/talib_web_api_meta.json`
- 支持输入映射（如 `MA` 的 `real` 可映射到 `Open/High/Low/Close/Volume/派生价`）。

### 2.2 后端现状

- 使用 `TALib.NETCore` + `TALib.Abstract` 执行指标函数。
- `TalibIndicatorCalculator` 已具备：
  - 动态函数解析
  - Lookback 计算
  - 输入序列构建
  - 输出索引映射
- `IndicatorEngine` 已具备实时任务队列与句柄缓存。

### 2.3 已具备的同源基础

- 前后端 `talib_indicators_config.json` 已一致（指标集合一致）。
- 两端都在 TA-Lib 生态内，不需要重写全部公式。

### 2.4 当前主要风险点

1. 数值预处理差异：前端在部分路径将异常值归零，后端更多保留 NaN。
2. 参数取整规则差异：前端 `Math.round`，后端 `AwayFromZero`。
3. 版本漂移风险：`talib-web` 和 `TALib.NETCore` 底层版本可能逐步偏离。

## 3. 设计目标

### 3.1 必达目标

- G1：前端计算不依赖后端指标接口。
- G2：后端计算用于策略与回测，不被迫改成“前端上传结果”。
- G3：定义并落地“可测试的一致性规则”，使两端结果稳定对齐。

### 3.2 质量目标

- Q1：全量指标可配置、可发现、可回归。
- Q2：新增指标不需要前后端手工各改一套逻辑。
- Q3：文档与配置具备单一真源，减少长期漂移。

## 4. 总体架构

采用“**同源规范层 + 双端执行适配层 + 对齐测试层**”。

### 4.1 同源规范层（Single Source of Truth）

统一维护一份规范数据，建议命名：`ta_indicator_manifest.json`。

建议字段：

- `indicator`：如 `MA`、`MACD`。
- `inputs`：输入名与顺序，如 `inReal`, `inHigh`, `inLow`。
- `parameters`：参数名、类型（`int/float/matype`）、默认值。
- `outputs`：输出名与顺序。
- `input_map_supported`：可映射输入（如 `real`）。
- `rules`：取整规则、缺失值规则、输出对齐规则。

来源建议：

1. 以 `talib_indicators_config.json` 为业务展示配置主源。
2. 以 `talib_web_api_meta.json`/后端反射信息补齐函数签名。
3. 生成后的 `manifest` 同步给前后端。

### 4.2 前端执行适配层（React）

#### 4.2.1 执行模型

- 使用 `talib-web + talib.wasm`。
- 指标计算放到 `Web Worker`，避免阻塞主线程渲染。
- 主线程仅负责图表渲染与参数交互。

#### 4.2.2 规则实现

- 输入映射：严格按 `manifest.inputs` 和 `input_map` 解析。
- 参数处理：
  - `int/matype` 按“远离零取整”实现（不要直接 `Math.round`）。
  - `float` 保持双精度。
- 缺失值处理：统一保留 `NaN`，展示层再决定显示为空。

#### 4.2.3 用户体验

- 用户无额外编译步骤。
- 打开网页自动下载 wasm 后即可使用。
- 通过缓存策略降低重复下载开销。

### 4.3 后端执行适配层（C#）

#### 4.3.1 执行模型

- 继续使用 `TALib.NETCore`。
- 在 `TalibIndicatorCalculator` 中对齐前端规则：
  - 输入映射规则
  - 参数取整规则
  - NaN 与输出索引规则

#### 4.3.2 运行边界

- 后端指标计算服务于策略/回测内部流程。
- 不再承担前端图表指标的在线计算请求（减压目标）。

### 4.4 对齐测试层

建立 `Golden Dataset`（黄金样本）对齐机制：

- 输入：同一组 OHLCV、同一组参数、同一 input_map。
- 执行：前端 runner 与后端 runner 各算一遍。
- 比对：逐点比对输出数组，记录 max/mean diff。
- 门禁：超过阈值阻断发布。

推荐阈值：

- 浮点输出：`abs(a-b) <= 1e-10`
- 整型输出：必须完全相等

## 5. 关键规则定义（必须统一）

### R1 输入映射规则

- 支持标准字段：`OPEN/HIGH/LOW/CLOSE/VOLUME`。
- 支持派生字段：`HL2/HLC3/OHLC4/OC2/HLCC4`。
- `inReal` 系列必须可映射；多 `inReal` 指标按顺序一一映射。

### R2 参数规则

- `int/matype` 统一远离零取整。
- 参数缺失使用 `manifest` 默认值。
- 禁止未知参数静默注入（建议严格模式报错）。

### R3 缺失值规则

- 计算层保留 NaN，不做 0 填充。
- 展示层可将 NaN 显示为空白，不改计算结果。

### R4 输出对齐规则

- 遵循 TA-Lib `outRange` 语义。
- 输出数组映射回原始时间索引后再参与比较/渲染。

### R5 版本冻结规则

- 冻结 `talib-web` 与 `TALib.NETCore` 的版本组合。
- 升级任一端版本时必须通过全量对齐测试。

## 6. 实施步骤（建议）

### 第1步：建立统一规范层

- 抽取当前配置生成 `manifest`。
- 生成脚本纳入仓库并作为 CI 任务。

### 第2步：双端适配对齐

- 前端改造 adapter：去除隐式归零，改为统一 NaN 语义。
- 后端改造 calculator：与前端共用同一输入映射/参数规则。

### 第3步：搭建自动化对齐测试

- 构建指标样本矩阵（覆盖 160 指标、常见参数组合、边界数据）。
- 产出对齐报告（按指标分组输出误差统计）。

### 第4步：分阶段上线

- 灰度阶段只比较，不切断旧逻辑。
- 误差稳定后切到统一逻辑。
- 保留回滚开关。

## 7. 性能与容量建议

### 前端

- Worker 并行数量建议受 CPU 核数限制。
- 长序列计算采用窗口化（按可视区 + lookback 补齐）。
- 对不变参数和不变输入启用结果缓存。

### 后端

- 保留现有 `IndicatorEngine` 的任务队列与句柄缓存。
- 避免为前端展示提供重复计算接口，专注策略运行链路。

## 8. 风险与应对

### 风险1：版本升级导致结果漂移

- 应对：版本冻结 + 升级门禁测试。

### 风险2：边界行为不一致（NaN、索引、取整）

- 应对：规则文档化 + 单元测试固化。

### 风险3：前端高频计算导致卡顿

- 应对：Worker 化 + 分批计算 + 缓存。

## 9. 验收标准

满足以下条件视为改造完成：

1. 前端关闭后端指标接口后仍可完整显示并计算指标。
2. 后端策略/回测指标计算不受影响。
3. 全量指标对齐测试通过，误差在阈值内。
4. 配置单源化，新增指标仅需更新规范并自动生效。

## 10. 结论

该改造方向正确且必要：

- 前端独立计算可显著降低服务器压力。
- 后端保留计算能力可保障策略可靠运行。
- 通过“同源规范层 + 自动化对齐测试”，可以把“一致性”从口头要求变成工程可验证结果。

建议立即进入“规则对齐 + 自动化对齐测试”阶段，这是决定该方案成败的关键路径。

## 11. 实施记录（阶段A/B已落地）

### 11.1 第一阶段：规则对齐（已完成）

1. 前端统一规则模块落地  
   文件：`D:\UGit\DWQuant\Client\src\lib\talibCalcRules.ts`  
   内容：
   - 输入源标准化（`OPEN/HIGH/LOW/CLOSE/VOLUME/HL2/HLC3/OHLC4/OC2/HLCC4`）。
   - 数值解析统一（无法解析时保留 `NaN`）。
   - 参数整数取整统一为“远离零”。
   - 派生价计算规则统一。

2. 前端适配层规则接入  
   文件：`D:\UGit\DWQuant\Client\src\lib\talibIndicatorAdapter.ts`  
   内容：
   - 接入 `talibCalcRules.ts`；
   - 移除“异常值置 0”行为；
   - `integer/matype` 参数改为统一取整规则。

3. 后端统一规则模块落地  
   文件：`D:\UGit\DWQuant\SeverTest\Modules\StrategyEngine\Application\TalibCalcRules.cs`  
   内容：
   - 输入源标准化；
   - 参数取整统一为 `MidpointRounding.AwayFromZero`；
   - 价格源解析统一。

4. 后端计算器规则接入  
   文件：`D:\UGit\DWQuant\SeverTest\Modules\StrategyEngine\Application\TalibIndicatorCalculator.cs`  
   内容：
   - `GetLookback` 使用统一取整规则；
   - `SplitRealInputs` 输入源标准化；
   - `BuildSeries/ResolveValue` 统一委托规则模块。

### 11.2 第二阶段：黄金样本对齐（已完成）

1. 黄金样本配置落地  
   文件：`D:\UGit\DWQuant\SeverTest\Config\ta_alignment_cases.json`  
   内容：
   - 统一 `generator`（确定性 OHLCV 生成参数）；
   - 统一 `tolerance=1e-10`；
   - 首批 14 个核心指标案例（`MA/EMA/MACD/RSI/BBANDS/STOCH/STOCHRSI/ATR/ADX/OBV/ADOSC/BETA/CORREL/ROC`）。

2. 前端基线生成脚本落地  
   文件：`D:\UGit\DWQuant\Client\scripts\generate-talib-alignment-baseline.mjs`  
   配套脚本：`D:\UGit\DWQuant\Client\package.json` 中 `talib:baseline`  
   关键点：
   - Node 环境下临时禁用 `fetch`，强制 `talib-web` 从本地文件加载 wasm，避免 URL 解析失败；
   - 输出数组按 `lookback` 前缀补 `null`，与后端 `outRange` 语义一致；
   - 生成文件：`D:\UGit\DWQuant\SeverTest\Config\ta_alignment_baseline.frontend.json`。

3. 后端对齐校验工具落地  
   文件：
   - `D:\UGit\DWQuant\SeverTest\Tools\TalibAlignmentVerifier\TalibAlignmentVerifier.csproj`
   - `D:\UGit\DWQuant\SeverTest\Tools\TalibAlignmentVerifier\Program.cs`  
   能力：
   - 读取样本配置与前端基线；
   - 生成同一组 OHLCV；
   - 使用 `TALib.Abstract` 计算并逐点比较；
   - 输出每个案例最大误差、总体通过率与全局最大误差。

4. 第二阶段执行结果（2026-02-20）  
   执行命令：
   - `npm.cmd run talib:baseline`（工作目录 `D:\UGit\DWQuant\Client`）
   - `dotnet run --project D:\UGit\DWQuant\SeverTest\Tools\TalibAlignmentVerifier\TalibAlignmentVerifier.csproj`  
   结果：
   - 总案例：14
   - 通过：14
   - 失败：0
   - 全局最大误差：`9.094947e-13`（小于 `1e-10` 门限）

### 11.3 第三阶段：单核心接入（阶段C-1已完成）

1. 后端同核心调用器落地  
   文件：`D:\UGit\DWQuant\SeverTest\Modules\StrategyEngine\Application\TalibWasmNodeInvoker.cs`  
   说明：
   - 后端通过 Node 子进程调用 `talib-web + talib.wasm`；
   - 使用 JSON 行协议（`ping/compute`）进行请求应答；
   - 支持自动探测 `Client/scripts`、`Client/public` 路径；
   - 支持 `StrictWasmCore` 严格模式（失败是否允许回退）。

2. 单核心桥接脚本落地  
   文件：`D:\UGit\DWQuant\Client\scripts\talib-node-bridge.mjs`  
   说明：
   - 与前端一致使用 `talib-web` 运行时；
   - 统一整数参数“远离零取整”；
   - 输出按 `lookback` 前缀补 `null`，与后端索引语义对齐；
   - Node 环境下禁用 `fetch`，避免本地 wasm URL 解析失败。

3. 实时/回测链路接入同核心入口  
   文件：
   - `D:\UGit\DWQuant\SeverTest\Modules\StrategyEngine\Application\TalibIndicatorCalculator.cs`
   - `D:\UGit\DWQuant\SeverTest\Modules\StrategyEngine\Application\IndicatorEngine.cs`
   - `D:\UGit\DWQuant\SeverTest\Modules\Backtest\Application\BacktestMainLoop.cs`
   - `D:\UGit\DWQuant\SeverTest\Program.cs`
   - `D:\UGit\DWQuant\SeverTest\Modules\Shared\Options\Options\TalibCoreOptions.cs`
   - `D:\UGit\DWQuant\SeverTest\appsettings.json`  
   说明：
   - 默认 `TalibCore.Mode=TalibWasmNode`；
   - 计算优先同核心，失败后按配置决定是否回退 `TALib.NETCore`。

4. 构建验证（2026-02-20）  
   命令：`dotnet build D:\UGit\DWQuant\SeverTest\ServerTest.csproj -p:OutDir=D:\UGit\DWQuant\SeverTest\_build\verify_wasm_core\`  
   结果：构建成功（0 error）。

5. 严格同核心配置已启用（2026-02-20）  
   文件：`D:\UGit\DWQuant\SeverTest\appsettings.json`  
   配置：`TalibCore.StrictWasmCore=true`  
   说明：同核心调用失败时不再回退 `TALib.NETCore`，直接报错并阻断，确保运行期只走统一核心。

6. 对齐工具新增引擎模式并完成验证（2026-02-20）  
   文件：`D:\UGit\DWQuant\SeverTest\Tools\TalibAlignmentVerifier\Program.cs`  
   能力：
   - `--engine talibnet`：走 `TALib.NETCore`；
   - `--engine wasm-core`：走 `Node + talib-web + talib.wasm`。  
   结果：
   - `talibnet`：14/14 通过，最大误差 `9.094947e-13`；
   - `wasm-core`：14/14 通过，最大误差 `9.094947e-13`。

### 11.4 下一阶段执行项（阶段C-2）

1. 将对齐校验工具纳入 CI 门禁，发布前强制执行。
2. 扩展样本矩阵至全量指标与边界参数组合（按批次覆盖约 160 指标）。
3. 在 `StrictWasmCore=true` 环境完成回测与实时链路压测，输出容量与延迟报告。
4. 增加桥接进程可观测性指标（启动耗时、请求耗时、异常计数）并接入告警。

### 11.5 风险提示

1. 即使规则对齐完成，若底层库版本变化仍可能导致细微漂移。
2. 因此必须把“版本冻结 + 对齐回归”作为上线前置条件。

### 11.6 随机一致性测试入口（已完成，2026-02-21）

1. 后端新增随机对比接口  
   文件：`D:\UGit\DWQuant\SeverTest\Modules\MarketData\Controllers\MarketDataTalibCompareController.cs`  
   协议：`marketdata.ta.random.compare`  
   路由：`POST /api/MarketData/ta-random-compare`  
   关键点：
   - 从 `HistoricalMarketDataCache` 快照随机选择组合；
   - 从缓存中随机截取连续 2000 根 K 线；
   - 逐指标调用 `TalibWasmNodeInvoker`（同核心）计算；
   - 返回：样本信息、K 线、指标输入源/参数、后端输出数组。

2. 前端新增测试弹窗  
   文件：
   - `D:\UGit\DWQuant\Client\src\components\dialogs\TalibRandomParityTestDialog.tsx`
   - `D:\UGit\DWQuant\Client\src\components\dialogs\TalibRandomParityTestDialog.css`
   - `D:\UGit\DWQuant\Client\src\components\layout\Dashboard.tsx`（入口按钮接入）  
   关键点：
   - `sidebar-test-section` 下新增“随机一致性测试”按钮；
   - 点击后请求后端随机样本；
   - 前端用同一份 K 线、本地 `talib-web` 重算全部指标；
   - 按输出逐项比对并展示：
     - 后端有效点数
     - 前端有效点数
     - 最大绝对误差
     - 首差异索引
     - 通过/不一致状态
   - 支持查看单一输出的 2000 根全量逐行明细。

3. 验证与构建  
   - 后端：`dotnet build D:\UGit\DWQuant\SeverTest\ServerTest.csproj -p:OutDir=D:\UGit\DWQuant\SeverTest\_build\verify_parity_dialog\` 通过。  
   - 前端：`npm.cmd run build` 已执行，当前仓库存在历史 TypeScript 错误（与本次改动无关），导致整体构建未通过；本次新增文件未产生额外编译报错记录。

4. 说明  
   - 该功能用于“界面内随机抽检一致性”，不改变线上策略执行路径。
   - 默认固定窗口长度 2000，满足本阶段抽检要求；后续可扩展为可配置窗口长度与参数随机化模式。

### 11.7 同源包装层收敛修复（2026-02-21）

1. 前端生产计算链路补齐“输出尾对齐”  
   文件：`D:\UGit\DWQuant\Client\src\lib\talibIndicatorAdapter.ts`  
   变更：
   - 新增 `alignOutputTail`，对 talib 短输出按 K 线长度做前补空值对齐；
   - 输出取值改为“对齐后按原索引读取”，避免 `MA/EMA/MACD` 等 lookback 指标整体错位。

2. 输出键匹配规则统一（大小写/符号无关）  
   文件：
   - `D:\UGit\DWQuant\Client\src\components\dialogs\TalibRandomParityTestDialog.tsx`
   - `D:\UGit\DWQuant\Client\scripts\talib-node-bridge.mjs`
   - `D:\UGit\DWQuant\Client/scripts/generate-talib-alignment-baseline.mjs`  
   变更：
   - 新增输出键标准化匹配逻辑（如 `MACDSignal` 与大小写变体可互认）；
   - 避免因输出键命名差异导致“取不到输出而误判不一致”。

3. 新增随机一致性命令行回归工具  
   文件：`D:\UGit\DWQuant\Client\scripts\verify-random-parity.mjs`  
   能力：
   - 直接调用 `POST /api/MarketData/ta-random-compare` 拉取随机样本；
   - 本地使用 `talib-web + talib.wasm` 重算并逐输出对比；
   - 输出通过率与失败明细，失败时返回非 0 退出码，可接入自动化。

4. 本地复测结果  
   命令：`node D:\UGit\DWQuant\Client\scripts\verify-random-parity.mjs --seed 12345 --bars 2000`  
   结果：指标 `95/95` 后端计算成功，输出比对 `112/112` 通过（无不一致项）。
