#!/usr/bin/env node
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { init, TAFuncs } from "talib-web";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const DEFAULT_CASES_PATH = path.resolve(__dirname, "../../SeverTest/Config/ta_alignment_cases.json");
const DEFAULT_META_PATH = path.resolve(__dirname, "../public/talib_web_api_meta.json");
const DEFAULT_WASM_PATH = path.resolve(__dirname, "../public/talib.wasm");
const DEFAULT_OUTPUT_PATH = path.resolve(
  __dirname,
  "../../SeverTest/Config/ta_alignment_baseline.frontend.json"
);

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

function normalizeSource(value) {
  const normalized = normalizeKey(value);
  switch (normalized) {
    case "OPEN":
    case "HIGH":
    case "LOW":
    case "CLOSE":
    case "VOLUME":
    case "HL2":
    case "HLC3":
    case "OHLC4":
    case "OC2":
    case "HLCC4":
      return normalized;
    default:
      return "CLOSE";
  }
}

function resolveSourceValue(series, source, index) {
  const open = series.open[index];
  const high = series.high[index];
  const low = series.low[index];
  const close = series.close[index];
  const volume = series.volume[index];
  switch (source) {
    case "OPEN":
      return open;
    case "HIGH":
      return high;
    case "LOW":
      return low;
    case "CLOSE":
      return close;
    case "VOLUME":
      return volume;
    case "HL2":
      return (high + low) / 2;
    case "HLC3":
      return (high + low + close) / 3;
    case "OHLC4":
      return (open + high + low + close) / 4;
    case "OC2":
      return (open + close) / 2;
    case "HLCC4":
      return (high + low + close + close) / 4;
    default:
      return close;
  }
}

function buildDerivedSeries(series, source) {
  const length = series.open.length;
  const result = new Array(length);
  for (let i = 0; i < length; i += 1) {
    result[i] = resolveSourceValue(series, source, i);
  }
  return result;
}

function generateSeries(generator) {
  const length = Number(generator.length ?? 360);
  const basePrice = Number(generator.basePrice ?? 100);
  const trendStep = Number(generator.trendStep ?? 0.18);
  const openAmp = Number(generator.openWaveAmplitude ?? 2.4);
  const closeAmp = Number(generator.closeWaveAmplitude ?? 1.1);
  const highPadding = Number(generator.highPadding ?? 1.35);
  const lowPadding = Number(generator.lowPadding ?? 1.25);
  const volumeBase = Number(generator.volumeBase ?? 1200);
  const volumeAmp = Number(generator.volumeWaveAmplitude ?? 95);

  const open = new Array(length);
  const high = new Array(length);
  const low = new Array(length);
  const close = new Array(length);
  const volume = new Array(length);

  for (let i = 0; i < length; i += 1) {
    const trend = basePrice + i * trendStep;
    const o = trend + Math.sin(i * 0.17) * openAmp;
    const c = trend + Math.cos(i * 0.11) * closeAmp;
    let h = Math.max(o, c) + highPadding + Math.sin(i * 0.07) * 0.25;
    let l = Math.min(o, c) - lowPadding - Math.cos(i * 0.09) * 0.22;
    if (h <= l) {
      h = l + 0.01;
    }

    let v = volumeBase + ((i % 37) - 18) * 8 + Math.cos(i * 0.13) * volumeAmp;
    if (!Number.isFinite(v) || v < 1) {
      v = 1;
    }

    open[i] = o;
    high[i] = h;
    low[i] = l;
    close[i] = c;
    volume[i] = v;
  }

  return { open, high, low, close, volume };
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

function toJsonArray(values, expectedLength) {
  const output = new Array(expectedLength).fill(null);
  if (!isNumericArrayLike(values)) {
    return output;
  }

  const limit = Math.min(expectedLength, values.length);
  const startIndex = Math.max(0, expectedLength - limit);
  for (let i = 0; i < limit; i += 1) {
    const value = values[i];
    output[startIndex + i] = Number.isFinite(value) ? Number(value) : null;
  }
  return output;
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

function resolveInputSource(inputName, realSources, realIndexRef) {
  const normalized = normalizeKey(inputName);
  const canonical = normalized.startsWith("IN") && normalized.length > 2 ? normalized.slice(2) : normalized;

  if (["OPEN", "HIGH", "LOW", "CLOSE", "VOLUME"].includes(canonical)) {
    return canonical;
  }

  const source = realSources[realIndexRef.value] ?? "CLOSE";
  realIndexRef.value += 1;
  return normalizeSource(source);
}

function resolveOptionValue(rawValue, optionType) {
  const num = Number(rawValue);
  const value = Number.isFinite(num) ? num : 0;
  const normalizedType = String(optionType ?? "").trim().toLowerCase();
  if (normalizedType === "integer" || normalizedType === "matype") {
    return roundAwayFromZero(value);
  }
  return value;
}

function parseArgs(argv) {
  const args = {
    cases: DEFAULT_CASES_PATH,
    meta: DEFAULT_META_PATH,
    wasm: DEFAULT_WASM_PATH,
    output: DEFAULT_OUTPUT_PATH,
  };

  for (let i = 0; i < argv.length; i += 1) {
    const token = argv[i];
    if (token === "--cases" && argv[i + 1]) {
      args.cases = path.resolve(process.cwd(), argv[i + 1]);
      i += 1;
      continue;
    }
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
    if (token === "--output" && argv[i + 1]) {
      args.output = path.resolve(process.cwd(), argv[i + 1]);
      i += 1;
      continue;
    }
  }

  return args;
}

function loadJsonFile(filePath) {
  const raw = fs.readFileSync(filePath, "utf-8");
  const sanitized = raw.replace(/^\uFEFF/, "");
  return JSON.parse(sanitized);
}

async function main() {
  const args = parseArgs(process.argv.slice(2));

  const casesRoot = loadJsonFile(args.cases);
  const metaRoot = loadJsonFile(args.meta);

  const wasmRuntimePath = path.resolve(args.wasm);
  console.log(`[对齐基线] 初始化 talib-web wasm: ${wasmRuntimePath}`);
  // talib-web 在 Node 18+ 会优先走 fetch 分支，本地文件路径会触发 URL 解析失败。
  // 这里临时禁用 fetch，强制走 fs 读取分支，确保可稳定加载本地 wasm。
  const originalFetch = globalThis.fetch;
  let fetchOverridden = false;
  if (typeof originalFetch === "function") {
    globalThis.fetch = undefined;
    fetchOverridden = true;
  }

  try {
    await init(wasmRuntimePath);
  } finally {
    if (fetchOverridden) {
      globalThis.fetch = originalFetch;
    }
  }

  const generatedSeries = generateSeries(casesRoot.generator ?? {});
  const length = generatedSeries.open.length;

  const baselineCases = [];
  for (const caseDef of casesRoot.cases ?? []) {
    const indicator = String(caseDef.indicator ?? "").trim().toUpperCase();
    const caseName = String(caseDef.name ?? indicator);
    const metaDef = metaRoot[indicator];
    const fn = TAFuncs[indicator];

    if (!metaDef || typeof fn !== "function") {
      throw new Error(`基线生成失败，未找到指标定义: ${caseName} (${indicator})`);
    }

    const params = {};
    const realSources = Array.isArray(caseDef.realSources) ? caseDef.realSources : [];
    const realIndexRef = { value: 0 };

    for (const input of metaDef.inputs ?? []) {
      const inputName = String(input.name ?? "");
      const source = resolveInputSource(inputName, realSources, realIndexRef);
      params[inputName] = buildDerivedSeries(generatedSeries, source);
    }

    const providedParams = Array.isArray(caseDef.parameters) ? caseDef.parameters : [];
    for (let i = 0; i < (metaDef.options ?? []).length; i += 1) {
      const option = metaDef.options[i];
      const optionName = String(option.name ?? "");
      const rawValue = i < providedParams.length ? providedParams[i] : option.defaultValue;
      params[optionName] = resolveOptionValue(rawValue, option.type);
    }

    const rawOutputs = fn(params);
    const outputNames = [];
    const outputs = [];

    for (const outputDef of metaDef.outputs ?? []) {
      const outputName = String(outputDef.name ?? "");
      outputNames.push(outputName);
      outputs.push(toJsonArray(resolveRawOutput(rawOutputs, outputName), length));
    }

    baselineCases.push({
      name: caseName,
      indicator,
      outputNames,
      outputs,
    });

    console.log(`[对齐基线] 已生成: ${caseName} (${indicator})`);
  }

  const result = {
    schemaVersion: "1.0.0",
    generatedAtUtc: new Date().toISOString(),
    source: {
      casesPath: args.cases,
      metaPath: args.meta,
      wasmPath: args.wasm,
    },
    length,
    caseCount: baselineCases.length,
    cases: baselineCases,
  };

  fs.mkdirSync(path.dirname(args.output), { recursive: true });
  fs.writeFileSync(args.output, `${JSON.stringify(result, null, 2)}\n`, "utf-8");
  console.log(`[对齐基线] 输出完成: ${args.output}`);
}

main().catch((error) => {
  console.error("[对齐基线] 执行失败:", error);
  process.exitCode = 1;
});
