#!/usr/bin/env node
import fs from "node:fs";
import path from "node:path";
import readline from "node:readline";
import { fileURLToPath } from "node:url";
import { init, TAFuncs } from "talib-web";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

function log(message) {
  process.stderr.write(`[TalibNodeBridge] ${message}\n`);
}

function normalizeKey(value) {
  return String(value ?? "")
    .trim()
    .toUpperCase()
    .replace(/[^A-Z0-9]/g, "");
}

function roundAwayFromZero(value) {
  const num = Number(value);
  if (!Number.isFinite(num) || num === 0) {
    return 0;
  }
  return Math.sign(num) * Math.round(Math.abs(num));
}

function toFiniteNumberOrNaN(value) {
  const num = Number(value);
  return Number.isFinite(num) ? num : Number.NaN;
}

function parseArgs(argv) {
  const args = {
    meta: path.resolve(__dirname, "../public/talib_web_api_meta.json"),
    wasm: path.resolve(__dirname, "../public/talib.wasm"),
  };

  for (let i = 0; i < argv.length; i += 1) {
    const token = argv[i];
    if (token === "--meta" && argv[i + 1]) {
      args.meta = path.resolve(process.cwd(), argv[i + 1]);
      i += 1;
      continue;
    }
    if (token === "--wasm" && argv[i + 1]) {
      args.wasm = path.resolve(process.cwd(), argv[i + 1]);
      i += 1;
      continue;
    }
  }

  return args;
}

function loadJson(filePath) {
  const raw = fs.readFileSync(filePath, "utf-8");
  return JSON.parse(raw.replace(/^\uFEFF/, ""));
}

function isNumericArrayLike(value) {
  if (Array.isArray(value)) {
    return true;
  }
  if (ArrayBuffer.isView(value)) {
    return typeof value.length === "number";
  }
  return false;
}

function alignOutput(rawValues, expectedLength) {
  const length = Number.isFinite(expectedLength) && expectedLength > 0 ? Math.floor(expectedLength) : 0;
  if (length <= 0) {
    return [];
  }

  const output = new Array(length).fill(null);
  if (!isNumericArrayLike(rawValues) || rawValues.length === 0) {
    return output;
  }

  const limit = Math.min(length, rawValues.length);
  const startIndex = Math.max(0, length - limit);
  for (let i = 0; i < limit; i += 1) {
    const value = rawValues[i];
    output[startIndex + i] = Number.isFinite(value) ? Number(value) : null;
  }
  return output;
}

function resolveOptionValue(rawValue, optionDef) {
  const normalizedType = String(optionDef?.type ?? "").trim().toLowerCase();
  const numeric = Number(rawValue);
  const value = Number.isFinite(numeric) ? numeric : Number(optionDef?.defaultValue ?? 0);
  if (normalizedType === "integer" || normalizedType === "matype") {
    return roundAwayFromZero(value);
  }
  return value;
}

function resolveRawOutput(rawOutputs, outputName) {
  if (rawOutputs && isNumericArrayLike(rawOutputs[outputName])) {
    return rawOutputs[outputName];
  }

  const normalizedTarget = normalizeKey(outputName);
  for (const [key, value] of Object.entries(rawOutputs ?? {})) {
    if (normalizeKey(key) === normalizedTarget && isNumericArrayLike(value)) {
      return value;
    }
  }
  return undefined;
}

function safeWrite(response) {
  process.stdout.write(`${JSON.stringify(response)}\n`);
}

function buildErrorResponse(id, message) {
  return {
    id,
    ok: false,
    error: message,
    outputs: null,
  };
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  const meta = loadJson(args.meta);
  const wasmPath = path.resolve(args.wasm);

  // Node 18+ 默认有 fetch。talib-web 在 Node 环境会优先走 fetch 分支，
  // 本地磁盘路径会触发 URL 解析异常，这里强制走 fs 读取分支。
  const originalFetch = globalThis.fetch;
  let fetchOverridden = false;
  if (typeof originalFetch === "function") {
    globalThis.fetch = undefined;
    fetchOverridden = true;
  }

  try {
    await init(wasmPath);
  } finally {
    if (fetchOverridden) {
      globalThis.fetch = originalFetch;
    }
  }

  log(`已初始化 talib-web wasm: ${wasmPath}`);

  const rl = readline.createInterface({
    input: process.stdin,
    crlfDelay: Infinity,
  });

  rl.on("line", (line) => {
    const trimmed = String(line ?? "").trim();
    if (!trimmed) {
      return;
    }

    let request;
    try {
      request = JSON.parse(trimmed);
    } catch (error) {
      safeWrite(buildErrorResponse(0, `请求 JSON 解析失败: ${error?.message ?? error}`));
      return;
    }

    const id = Number(request?.id ?? 0);
    const type = normalizeKey(request?.type);

    if (type === "PING") {
      safeWrite({
        id,
        ok: true,
        type: "pong",
        error: null,
        outputs: null,
      });
      return;
    }

    const indicator = normalizeKey(request?.indicator);
    const fn = TAFuncs[indicator];
    const metaDef = meta[indicator];
    if (!metaDef || typeof fn !== "function") {
      safeWrite(buildErrorResponse(id, `未知指标: ${indicator}`));
      return;
    }

    try {
      const inputArrays = Array.isArray(request?.inputs) ? request.inputs : [];
      const optionValues = Array.isArray(request?.options) ? request.options : [];
      const expectedLength = Number(request?.expectedLength ?? 0);
      const params = {};

      for (let i = 0; i < (metaDef.inputs ?? []).length; i += 1) {
        const inputDef = metaDef.inputs[i];
        const inputName = String(inputDef?.name ?? "");
        const sourceArray = Array.isArray(inputArrays[i]) ? inputArrays[i] : [];
        params[inputName] = sourceArray.map(toFiniteNumberOrNaN);
      }

      for (let i = 0; i < (metaDef.options ?? []).length; i += 1) {
        const optionDef = metaDef.options[i];
        const optionName = String(optionDef?.name ?? "");
        const rawValue = i < optionValues.length ? optionValues[i] : optionDef?.defaultValue;
        params[optionName] = resolveOptionValue(rawValue, optionDef);
      }

      const rawOutputs = fn(params);
      const outputs = [];
      for (const outputDef of metaDef.outputs ?? []) {
        const outputName = String(outputDef?.name ?? "");
        outputs.push(alignOutput(resolveRawOutput(rawOutputs, outputName), expectedLength));
      }

      safeWrite({
        id,
        ok: true,
        type: "computeResult",
        error: null,
        outputs,
      });
    } catch (error) {
      safeWrite(buildErrorResponse(id, `计算失败: ${error?.message ?? error}`));
    }
  });
}

main().catch((error) => {
  log(`启动失败: ${error?.stack ?? error?.message ?? error}`);
  process.exitCode = 1;
});
