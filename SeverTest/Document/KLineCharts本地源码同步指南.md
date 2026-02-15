# KLineCharts 本地源码同步指南（多设备/多 AI）

> 目标：确保任何设备或 AI 拉取后都能直接使用当前“本地魔改版 klinecharts + TA 指标能力”，避免出现 `Failed to resolve import "klinecharts"`。

## 1. 当前接入方式（必须保持）

- 本项目**不是**从 npm 直接使用 `klinecharts` 包，而是使用仓库内本地源码：
  - `Client/vendors/klinecharts_local`
- Vite 别名固定为 `klinecharts`，解析策略：
  - 优先 `Client/vendors/klinecharts_local/dist/index.esm.js`
  - 若 `dist` 不存在，自动回退 `Client/vendors/klinecharts_local/src/index.ts`
- TypeScript 路径同样是 dist 优先、src 回退：
  - `Client/tsconfig.app.json` 中 `paths.klinecharts`

## 2. 为什么其他设备会报错

报错示例：

```text
[plugin:vite:import-analysis] Failed to resolve import "klinecharts"
```

常见根因：

1. `Client/vendors/klinecharts_local` 没有同步过去。
2. 只同步了 `src`，但本地 `vite.config.ts` 还写死到 `dist`（没有回退逻辑）。
3. `Client/tsconfig.app.json` 没有配置 `klinecharts` 路径。

## 3. 推荐同步方案（首选）

直接用 Git 同步整个仓库（最稳，不漏文件）：

1. 在功能完整的机器上提交并推送：
   - `Client/vendors/klinecharts_local/**`
   - `Client/vite.config.ts`
   - `Client/tsconfig.app.json`
   - 与图表/指标相关的业务代码与配置文件（如 `Client/src/components/**`、`Client/src/lib/**`、`Client/public/talib_*.json`、`Client/package.json`）
2. 在目标设备拉取最新代码：
   - `git pull`
3. 安装前端依赖：
   - `cd Client`
   - `npm install`
4. 启动验证：
   - `npm run dev`

## 4. 手动搬运方案（无法走 Git 时）

如果你打算自己手动复制文件，至少复制这些：

1. `Client/vendors/klinecharts_local`（整个目录）
2. `Client/vite.config.ts`
3. `Client/tsconfig.app.json`
4. `Client/package.json`
5. `Client/package-lock.json`（如存在）
6. 图表功能相关目录（建议整目录复制，避免遗漏）：
   - `Client/src/components`
   - `Client/src/lib`
   - `Client/public/talib_indicators_config.json`
   - `Client/public/talib_web_api_meta.json`

复制后在目标设备执行：

```powershell
cd Client
npm install
npm run dev
```

## 5. 关于 dist 目录的说明

- `Client/.gitignore` 默认忽略 `dist`，所以 `Client/vendors/klinecharts_local/dist/**` 通常不会被 Git 跟踪。
- 当前项目已支持 `dist` 缺失时回退到 `src`，所以**即使没有 dist 也可以跑起来**。
- 如果你希望在本地生成 dist，可执行：

```powershell
cd Client/vendors/klinecharts_local
npm install
npm run build
```

## 6. 5 分钟自检清单

在目标设备执行以下检查：

```powershell
Test-Path .\Client\vendors\klinecharts_local\src\index.ts
rg -n "klinecharts" .\Client\vite.config.ts
rg -n "\"klinecharts\"" .\Client\tsconfig.app.json
cd .\Client
npx vite build
```

通过标准：

1. `Test-Path` 返回 `True`
2. `vite.config.ts` 中有 `klinecharts` alias，且带 dist/src 回退
3. `tsconfig.app.json` 中有 `paths.klinecharts`
4. `npx vite build` 不再出现 `Failed to resolve import "klinecharts"`

## 7. 给其他 AI 的执行约束

如果后续让其他 AI 继续开发，请附加以下要求：

1. 不要改回 npm 版 `klinecharts`，统一基于 `Client/vendors/klinecharts_local`。
2. klinecharts 魔改统一改 `Client/vendors/klinecharts_local/src/**`。
3. 保持 `vite.config.ts` 与 `tsconfig.app.json` 的 dist/src 回退策略不变。
4. 改完图表功能后，必须同步更新本文档与相关业务文档（`SeverTest/Document/`）。
