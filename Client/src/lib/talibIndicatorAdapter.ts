import { TAFuncs } from "talib-web";
import type { KLineData } from "klinecharts";

export type TalibCalcResult = Record<string, number | undefined>;

export type TalibRuntimeInput = {
  name: string;
};

export type TalibRuntimeOption = {
  name: string;
  type?: string;
  defaultValue?: number;
};

export type TalibRuntimeOutput = {
  key: string;
};

export type TalibRuntimeCalcSpec = {
  code: string;
  inputSeries: string[];
  inputs: TalibRuntimeInput[];
  options: TalibRuntimeOption[];
  outputs: TalibRuntimeOutput[];
};

type TalibRuntimeExtendData = {
  taInputMap?: Record<string, string>;
};

type TalibFunction = (params: Record<string, unknown>) => Record<string, unknown>;

const TALIB_FUNCTIONS = TAFuncs as unknown as Record<string, TalibFunction>;

function toNumber(value: unknown): number {
  if (typeof value === "number" && Number.isFinite(value)) {
    return value;
  }
  if (typeof value === "string") {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : 0;
  }
  return 0;
}

function toOutputValue(values: number[] | undefined, index: number): number | undefined {
  const value = values?.[index];
  return typeof value === "number" && Number.isFinite(value) ? value : undefined;
}

function normalizeInputKey(value: string): string {
  return value.trim().toUpperCase().replace(/[^A-Z0-9]/g, "");
}

function normalizeInputSource(value: string): string {
  const normalized = normalizeInputKey(value);
  switch (normalized) {
    case "OPEN":
      return "OPEN";
    case "HIGH":
      return "HIGH";
    case "LOW":
      return "LOW";
    case "CLOSE":
      return "CLOSE";
    case "VOLUME":
      return "VOLUME";
    case "HL2":
      return "HL2";
    case "HLC3":
      return "HLC3";
    case "OHLC4":
      return "OHLC4";
    case "OC2":
      return "OC2";
    case "HLCC4":
      return "HLCC4";
    default:
      return "CLOSE";
  }
}

function resolveSeriesValue(item: KLineData, key: string): number {
  const open = toNumber(item.open);
  const high = toNumber(item.high);
  const low = toNumber(item.low);
  const close = toNumber(item.close);
  const volume = toNumber(item.volume);
  switch (key) {
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

function buildSeries(data: KLineData[], key: string): number[] {
  return data.map((item) => resolveSeriesValue(item, key));
}

function resolveConfiguredSource(
  inputName: string,
  inputIndex: number,
  configuredSeries: string[],
  runtimeInputMap: Record<string, string>
): string {
  const normalizedName = normalizeInputKey(inputName);
  const runtimeSource = runtimeInputMap[normalizedName];
  if (typeof runtimeSource === "string" && runtimeSource.length > 0) {
    return normalizeInputSource(runtimeSource);
  }
  if (normalizedName === "OPEN") {
    return "OPEN";
  }
  if (normalizedName === "HIGH") {
    return "HIGH";
  }
  if (normalizedName === "LOW") {
    return "LOW";
  }
  if (normalizedName === "CLOSE") {
    return "CLOSE";
  }
  if (normalizedName === "VOLUME") {
    return "VOLUME";
  }
  if (normalizedName === "INREAL" || normalizedName === "INREAL0" || normalizedName === "INREAL1") {
    const configured = configuredSeries[inputIndex] ?? "Close";
    return normalizeInputSource(configured);
  }
  if (normalizedName === "INPERIODS") {
    const configured = configuredSeries[inputIndex] ?? "Close";
    return normalizeInputSource(configured);
  }
  const configured = configuredSeries[inputIndex] ?? "Close";
  return normalizeInputSource(configured);
}

function resolveOptionValue(paramValue: number | undefined, defaultValue: number | undefined, optionType: string): number {
  const baseValue = Number.isFinite(paramValue) ? Number(paramValue) : Number.isFinite(defaultValue) ? Number(defaultValue) : 0;
  const normalizedType = optionType.trim().toLowerCase();
  if (normalizedType === "integer" || normalizedType === "matype") {
    return Math.round(baseValue);
  }
  return baseValue;
}

function resolveRuntimeInputMap(runtimeExtendData: unknown): Record<string, string> {
  if (!runtimeExtendData || typeof runtimeExtendData !== "object") {
    return {};
  }
  const inputMap = (runtimeExtendData as TalibRuntimeExtendData).taInputMap;
  if (!inputMap || typeof inputMap !== "object") {
    return {};
  }

  const normalizedMap: Record<string, string> = {};
  for (const [rawKey, rawValue] of Object.entries(inputMap)) {
    if (typeof rawValue !== "string") {
      continue;
    }
    const key = normalizeInputKey(rawKey);
    if (!key) {
      continue;
    }
    normalizedMap[key] = normalizeInputSource(rawValue);
  }
  return normalizedMap;
}

export function calcTalibIndicator(
  spec: TalibRuntimeCalcSpec,
  data: KLineData[],
  calcParams: number[],
  runtimeExtendData?: unknown
): TalibCalcResult[] {
  if (data.length === 0) {
    return [];
  }

  const fn = TALIB_FUNCTIONS[spec.code];
  if (!fn) {
    return [];
  }

  try {
    const seriesCache: Record<string, number[]> = {};
    const runtimeInputMap = resolveRuntimeInputMap(runtimeExtendData);
    const getSeries = (sourceKey: string): number[] => {
      const key = sourceKey.length > 0 ? sourceKey : "CLOSE";
      if (!seriesCache[key]) {
        seriesCache[key] = buildSeries(data, key);
      }
      return seriesCache[key];
    };

    const params: Record<string, unknown> = {};
    for (let i = 0; i < spec.inputs.length; i += 1) {
      const input = spec.inputs[i];
      const source = resolveConfiguredSource(input.name, i, spec.inputSeries, runtimeInputMap);
      params[input.name] = getSeries(source);
    }

    for (let i = 0; i < spec.options.length; i += 1) {
      const option = spec.options[i];
      params[option.name] = resolveOptionValue(calcParams[i], option.defaultValue, option.type ?? "");
    }

    const rawOutputs = fn(params);
    const outputArrays = new Map<string, number[]>();
    for (const output of spec.outputs) {
      const raw = rawOutputs[output.key];
      if (Array.isArray(raw)) {
        outputArrays.set(output.key, raw as number[]);
      }
    }
    if (outputArrays.size === 0) {
      for (const [key, raw] of Object.entries(rawOutputs)) {
        if (Array.isArray(raw)) {
          outputArrays.set(key, raw as number[]);
        }
      }
    }
    if (outputArrays.size === 0) {
      return [];
    }

    const result: TalibCalcResult[] = new Array(data.length);
    for (let i = 0; i < data.length; i += 1) {
      const row: TalibCalcResult = {};
      for (const [key, values] of outputArrays.entries()) {
        row[key] = toOutputValue(values, i);
      }
      result[i] = row;
    }

    return result;
  } catch (error) {
    console.error(`[talib] calc indicator failed: ${spec.code}`, error);
    return [];
  }
}
