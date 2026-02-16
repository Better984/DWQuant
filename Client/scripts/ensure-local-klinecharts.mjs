import { existsSync } from "node:fs";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const vendorDir = path.resolve(__dirname, "../vendors/klinecharts_local");
const distEsmPath = path.join(vendorDir, "dist", "index.esm.js");
const distDtsPath = path.join(vendorDir, "dist", "index.d.ts");
const vendorNodeModulesPath = path.join(vendorDir, "node_modules");
const npmCommand = process.platform === "win32" ? "npm.cmd" : "npm";

function runCommand(command, args, cwd) {
  // 统一封装子命令执行，失败时直接透传退出码。
  const result = spawnSync(command, args, {
    cwd,
    stdio: "inherit",
    shell: false,
    env: process.env,
  });

  if (result.status !== 0) {
    const code = result.status ?? 1;
    process.exit(code);
  }
}

function hasDistArtifacts() {
  // 同时校验运行时和类型声明，避免只生成一半产物。
  return existsSync(distEsmPath) && existsSync(distDtsPath);
}

if (hasDistArtifacts()) {
  console.log("[本地KLineCharts] 已检测到 dist 产物，跳过构建。");
  process.exit(0);
}

console.log("[本地KLineCharts] 未检测到 dist 产物，开始自动同步与构建。");

if (!existsSync(vendorNodeModulesPath)) {
  console.log("[本地KLineCharts] 首次同步依赖，执行 npm install --ignore-scripts。");
  runCommand(npmCommand, ["install", "--ignore-scripts"], vendorDir);
}

console.log("[本地KLineCharts] 开始执行 npm run build。");
runCommand(npmCommand, ["run", "build"], vendorDir);

if (!hasDistArtifacts()) {
  console.error("[本地KLineCharts] 构建完成但未找到 dist 产物，请检查 vendors/klinecharts_local。");
  process.exit(1);
}

console.log("[本地KLineCharts] 本地源码同步完成，dist 产物已就绪。");
