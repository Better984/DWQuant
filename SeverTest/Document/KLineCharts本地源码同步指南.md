# KLineCharts 本地源码同步指南（多设备/多 AI）

> 目标：确保任何设备拉取后都能直接使用当前“本地魔改版 klinecharts + TA 指标能力”，避免出现 `Failed to resolve import "klinecharts"`。

## 1. 当前接入方式（以代码为准）

- 本项目**不是**从 npm 直接使用 `klinecharts`，而是使用仓库内本地源码：
  - `Client/vendors/klinecharts_local`
- Vite 别名（`Client/vite.config.ts`）采用“dist 优先、src 回退”策略：
  - 优先：`Client/vendors/klinecharts_local/dist/index.esm.js`
  - 回退：`Client/vendors/klinecharts_local/src/index.ts`
- TypeScript 路径（`Client/tsconfig.app.json`）同样是“dist 优先、src 回退”：
  - `./vendors/klinecharts_local/dist/index.d.ts`
  - `./vendors/klinecharts_local/src/index.ts`

## 2. 自动兜底机制（推荐）

前端已接入本地同步脚本：

- 脚本：`Client/scripts/ensure-local-klinecharts.mjs`
- 命令：`npm run ensure:klinecharts-local`
- 接入点：
  - `npm run dev` 启动前自动执行
  - `npm run build` 构建前自动执行

脚本逻辑：

1. 检查 `dist/index.esm.js` 与 `dist/index.d.ts` 是否存在。
2. 若缺失，且 `vendors/klinecharts_local/node_modules` 不存在，则自动执行：
   - `npm.cmd install --ignore-scripts`
3. 自动执行 `npm.cmd run build` 产出 `dist`。

说明：

- 使用 `--ignore-scripts` 是为了绕过子目录下 `husky install` 可能触发的 `.git` 检测失败。

## 3. 为什么其他设备会报错

报错示例：

```text
[plugin:vite:import-analysis] Failed to resolve import "klinecharts"
```

常见根因：

1. `Client/vendors/klinecharts_local` 未完整同步。
2. `vite.config.ts` / `tsconfig.app.json` 未保留 `klinecharts` 路径与回退策略。
3. 本地依赖未安装，且未执行自动兜底脚本。

## 4. 推荐同步方案（首选 Git）

在功能完整的机器上提交并推送以下内容：

- `Client/vendors/klinecharts_local/**`
- `Client/vite.config.ts`
- `Client/tsconfig.app.json`
- `Client/scripts/ensure-local-klinecharts.mjs`
- `Client/package.json`
- 图表/指标相关业务代码与配置（如 `Client/src/components/**`、`Client/src/lib/**`、`Client/public/talib_*.json`）

在目标设备执行：

```powershell
cd Client
npm.cmd install
npm.cmd run dev
```

## 5. 手动搬运方案（无法走 Git 时）

至少复制：

1. `Client/vendors/klinecharts_local`（整个目录）
2. `Client/vite.config.ts`
3. `Client/tsconfig.app.json`
4. `Client/scripts/ensure-local-klinecharts.mjs`
5. `Client/package.json`
6. `Client/package-lock.json`（如存在）

复制后执行：

```powershell
cd Client
npm.cmd install
npm.cmd run ensure:klinecharts-local
npm.cmd run dev
```

## 6. 关于 dist 目录

- `Client/.gitignore` 默认忽略 `dist`，因此 `Client/vendors/klinecharts_local/dist/**` 通常不会被 Git 跟踪。
- 由于存在 dist/src 回退策略，即使 `dist` 缺失，也可以通过 src 回退保证可解析。
- 但项目默认会在 `dev/build` 前触发 ensure 脚本，尽量自动补齐 `dist`。

如需手动构建：

```powershell
cd Client/vendors/klinecharts_local
npm.cmd install --ignore-scripts
npm.cmd run build
```

## 7. 5 分钟自检清单

在仓库根目录执行：

```powershell
Test-Path .\Client\vendors\klinecharts_local\src\index.ts
rg -n "klinecharts" .\Client\vite.config.ts
rg -n "\"klinecharts\"" .\Client\tsconfig.app.json
cd .\Client
npm.cmd run ensure:klinecharts-local
```

通过标准：

1. `Test-Path` 返回 `True`。
2. `vite.config.ts` 中 `klinecharts` alias 为 dist/src 回退策略。
3. `tsconfig.app.json` 中存在 `paths.klinecharts` 配置。
4. `ensure:klinecharts-local` 执行后无报错。

## 8. 给其他 AI 的执行约束

1. 不要改回 npm 版 `klinecharts`，统一基于 `Client/vendors/klinecharts_local`。
2. klinecharts 魔改统一改 `Client/vendors/klinecharts_local/src/**`。
3. 保持 `vite.config.ts` 与 `tsconfig.app.json` 的 dist/src 回退策略不变。
4. 改完图表功能后，必须同步更新本文档与相关业务文档（`SeverTest/Document/`）。
5. 风险区绘图（`riskRewardLong` / `riskRewardShort`）若需“真实交易区间自动计算”，创建 overlay 时必须注入 `extendData.getBars`（建议同时带 `extendData.direction`）；缺失会退化为仅固定计划区块，不再实时计算中间真实区间。
