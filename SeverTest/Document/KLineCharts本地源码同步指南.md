# KLineCharts 本地源码同步指南

> 适用场景：项目使用本地魔改版 `klinecharts`，新设备或清理缓存后出现 `Failed to resolve import "klinecharts"`。

## 1. 目录约定

- 本地源码目录：`Client/vendors/klinecharts_local`
- 运行时依赖产物：`Client/vendors/klinecharts_local/dist/index.esm.js`
- 类型声明产物：`Client/vendors/klinecharts_local/dist/index.d.ts`
- Vite 别名：`Client/vite.config.ts` 中 `klinecharts -> ./vendors/klinecharts_local/dist/index.esm.js`
- TS 类型路径：`Client/tsconfig.app.json` 中 `klinecharts -> ./vendors/klinecharts_local/dist/index.d.ts`

## 2. 自动兜底机制（推荐）

已在前端增加自动同步脚本：

- 脚本：`Client/scripts/ensure-local-klinecharts.mjs`
- 命令：`npm run ensure:klinecharts-local`
- 接入点：
  - `npm run dev` 启动前自动执行
  - `npm run build` 构建前自动执行

逻辑说明：

1. 检查 `dist/index.esm.js` 与 `dist/index.d.ts` 是否存在。
2. 若缺失，且 `vendors/klinecharts_local/node_modules` 不存在，则自动执行 `npm install --ignore-scripts`。
3. 自动执行 `npm run build` 产出 `dist`。

## 3. 手动同步命令（需要时）

在仓库根目录执行：

```powershell
cd Client/vendors/klinecharts_local
npm.cmd install --ignore-scripts
npm.cmd run build
```

说明：

- 使用 `--ignore-scripts` 是为了绕过 `husky install` 在子目录下的 `.git` 检测失败。
- `dist` 为本地构建产物，不纳入仓库版本控制。

## 4. 验证步骤

在 `Client` 目录执行：

```powershell
npm.cmd run ensure:klinecharts-local
```

校验点：

- `Client/vendors/klinecharts_local/dist/index.esm.js` 存在
- `Client/vendors/klinecharts_local/dist/index.d.ts` 存在
- Vite 请求 `src/components/KlineChartsDemo.tsx` 时，`import { ... } from "klinecharts"` 会被解析到 `/vendors/klinecharts_local/dist/index.esm.js`

## 5. 故障排查

- 报错 `Failed to resolve import "klinecharts"`：
  - 优先检查 `dist` 目录是否存在并含上述两个文件。
- 报错 `husky - .git can't be found`：
  - 改用 `npm.cmd install --ignore-scripts`。
- 已有 `dist` 但仍报错：
  - 清理前端缓存后重启：删除 `Client/node_modules/.vite` 再重新 `npm run dev`。
