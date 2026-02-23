#!/usr/bin/env node
import path from "node:path";
import { fileURLToPath } from "node:url";
import { init, TAFuncs } from "talib-web";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const DEFAULT_URL = "http://localhost:9635/api/MarketData/ta-random-compare";
const DEFAULT_BARS = 2000;
const DEFAULT_TIMEOUT = 120000;
const DIFF_TOLERANCE = 1e-10;

function normalizeKey(value) {
  return String(value ?? "")
    .trim()
    .toUpperCase()
    .replace(/[^A-Z0-9]/g, "");
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

function roundAwayFromZero(value) {
  const num = Number(value);
  if (!Number.isFinite(num) || num === 0) {
    return 0;
  }
  return Math.sign(num) * Math.round(Math.abs(num));
}

function toFiniteOrNull(value) {
  if (value === null || value === undefined) {
    return null;
  }
  if (typeof value === "number") {
    return Number.isFinite(value) ? value : null;
  }
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
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

function alignSeries(values, expectedLength) {
  const result = new Array(expectedLength).fill(null);
  if (!isNumericArrayLike(values) || expectedLength <= 0) {
    return result;
  }

  const copyCount = Math.min(expectedLength, values.length);
  const startIndex = Math.max(0, expectedLength - copyCount);
  for (let i = 0; i < copyCount; i += 1) {
    result[startIndex + i] = toFiniteOrNull(values[i]);
  }
  return result;
}

function resolveOutputValues(rawOutputs, outputName) {
  if (
    rawOutputs &&
    Object.prototype.hasOwnProperty.call(rawOutputs, outputName) &&
    isNumericArrayLike(rawOutputs[outputName])
  ) {
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

function resolveSourceValue(kline, source) {
  const open = Number.isFinite(kline.open) ? Number(kline.open) : Number.NaN;
  const high = Number.isFinite(kline.high) ? Number(kline.high) : Number.NaN;
  const low = Number.isFinite(kline.low) ? Number(kline.low) : Number.NaN;
  const close = Number.isFinite(kline.close) ? Number(kline.close) : Number.NaN;
  const volume = Number.isFinite(kline.volume) ? Number(kline.volume) : Number.NaN;

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

function parseArgs(argv) {
  const args = {
    url: DEFAULT_URL,
    bars: DEFAULT_BARS,
    seed: null,
    timeout: DEFAULT_TIMEOUT,
  };

  for (let i = 0; i < argv.length; i += 1) {
    const token = argv[i];
    if (token === "--url" && argv[i + 1]) {
      args.url = String(argv[i + 1]).trim();
      i += 1;
      continue;
    }
    if (token === "--bars" && argv[i + 1]) {
      const bars = Number(argv[i + 1]);
      if (Number.isFinite(bars) && bars > 0) {
        args.bars = Math.floor(bars);
      }
      i += 1;
      continue;
    }
    if (token === "--seed" && argv[i + 1]) {
      const seed = Number(argv[i + 1]);
      args.seed = Number.isFinite(seed) ? Math.floor(seed) : null;
      i += 1;
      continue;
    }
    if (token === "--timeout" && argv[i + 1]) {
      const timeout = Number(argv[i + 1]);
      if (Number.isFinite(timeout) && timeout > 0) {
        args.timeout = Math.floor(timeout);
      }
      i += 1;
      continue;
    }
  }

  return args;
}

async function fetchRandomSample(args) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), args.timeout);
  try {
    const body = {
      type: "marketdata.ta.random.compare",
      reqId: `verify-${Date.now()}`,
      ts: Date.now(),
      data: {
        bars: args.bars,
        seed: args.seed,
      },
    };

    const response = await fetch(args.url, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify(body),
      signal: controller.signal,
    });
    const envelope = await response.json();
    if (!response.ok || envelope?.code !== 0 || !envelope?.data) {
      throw new Error(`接口返回异常: status=${response.status}, code=${envelope?.code}, msg=${envelope?.msg}`);
    }
    return envelope.data;
  } finally {
    clearTimeout(timer);
  }
}

async function ensureTalibReady() {
  const wasmPath = path.resolve(__dirname, "../public/talib.wasm");
  const originalFetch = globalThis.fetch;
  let overridden = false;
  if (typeof originalFetch === "function") {
    globalThis.fetch = undefined;
    overridden = true;
  }

  try {
    await init(wasmPath);
  } finally {
    if (overridden) {
      globalThis.fetch = originalFetch;
    }
  }
}

function compareSample(sample) {
  const rows = [];
  const candleLength = Array.isArray(sample.klines) ? sample.klines.length : 0;
  const seriesCache = new Map();

  const getSeries = (sourceText) => {
    const source = normalizeSource(sourceText);
    if (seriesCache.has(source)) {
      return seriesCache.get(source);
    }
    const built = sample.klines.map((kline) => resolveSourceValue(kline, source));
    seriesCache.set(source, built);
    return built;
  };

  for (const indicator of sample.indicators ?? []) {
    const frontendOutputs = [];
    let frontendError = null;

    if (!indicator.error) {
      const fn = TAFuncs[indicator.talibCode];
      if (typeof fn !== "function") {
        frontendError = `前端缺少函数: ${indicator.talibCode}`;
      } else {
        try {
          const params = {};
          for (const input of indicator.inputs ?? []) {
            params[input.name] = getSeries(input.source);
          }
          for (const option of indicator.options ?? []) {
            const optionType = String(option.type ?? "").trim().toLowerCase();
            const value = Number(option.value);
            params[option.name] =
              optionType === "integer" || optionType === "matype"
                ? roundAwayFromZero(value)
                : value;
          }

          const rawOutputs = fn(params);
          const outputNames =
            Array.isArray(indicator.outputNames) && indicator.outputNames.length > 0
              ? indicator.outputNames
              : Object.keys(rawOutputs);
          for (const outputName of outputNames) {
            frontendOutputs.push(resolveOutputValues(rawOutputs, outputName));
          }
        } catch (error) {
          frontendError = error instanceof Error ? error.message : "前端计算异常";
        }
      }
    }

    const outputCount = Math.max(
      indicator.outputNames?.length ?? 0,
      indicator.outputs?.length ?? 0,
      frontendOutputs.length,
      1
    );

    for (let outputIndex = 0; outputIndex < outputCount; outputIndex += 1) {
      const outputName = indicator.outputNames?.[outputIndex] ?? `output_${outputIndex + 1}`;
      const backendValues = alignSeries(indicator.outputs?.[outputIndex], candleLength);
      const frontendValues = alignSeries(frontendOutputs[outputIndex], candleLength);

      let mismatchCount = 0;
      let firstMismatchIndex = null;
      let maxAbsDiff = 0;

      for (let i = 0; i < candleLength; i += 1) {
        const backendValue = backendValues[i];
        const frontendValue = frontendValues[i];

        if (backendValue === null && frontendValue === null) {
          continue;
        }
        if (backendValue === null || frontendValue === null) {
          mismatchCount += 1;
          if (firstMismatchIndex === null) {
            firstMismatchIndex = i;
          }
          continue;
        }

        const diff = Math.abs(backendValue - frontendValue);
        if (diff > maxAbsDiff) {
          maxAbsDiff = diff;
        }
        if (diff > DIFF_TOLERANCE) {
          mismatchCount += 1;
          if (firstMismatchIndex === null) {
            firstMismatchIndex = i;
          }
        }
      }

      rows.push({
        indicatorCode: indicator.indicatorCode,
        talibCode: indicator.talibCode,
        outputName,
        pass: !indicator.error && !frontendError && mismatchCount === 0,
        mismatchCount,
        firstMismatchIndex,
        maxAbsDiff,
        backendError: indicator.error ?? null,
        frontendError,
      });
    }
  }

  return rows;
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  const startedAt = Date.now();

  await ensureTalibReady();
  const sample = await fetchRandomSample(args);
  const rows = compareSample(sample);

  const passCount = rows.filter((row) => row.pass).length;
  const failRows = rows.filter((row) => !row.pass);
  const elapsed = Date.now() - startedAt;

  console.log(`样本: ${sample.sample.exchange} / ${sample.sample.symbol} / ${sample.sample.timeframe}`);
  console.log(`窗口: ${sample.sample.windowStartIndex + 1} / ${sample.sample.totalCachedBars}, bars=${sample.sample.bars}`);
  console.log(`输出通过: ${passCount} / ${rows.length}, 耗时: ${elapsed} ms`);

  if (failRows.length > 0) {
    console.log("失败样例(最多前10条):");
    for (const row of failRows.slice(0, 10)) {
      console.log(
        `- ${row.indicatorCode}(${row.talibCode})/${row.outputName} mismatch=${row.mismatchCount} maxAbsDiff=${row.maxAbsDiff.toExponential(
          6
        )} first=${row.firstMismatchIndex ?? "-"}`
      );
    }
    process.exitCode = 1;
    return;
  }

  console.log("随机一致性校验通过。");
}

main().catch((error) => {
  console.error("随机一致性校验失败:", error);
  process.exitCode = 1;
});
