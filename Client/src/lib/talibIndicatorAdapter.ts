import { TAFuncs } from "talib-web";
import type { KLineData } from "klinecharts";
import {
  normalizeTalibInputSource,
  resolveTalibSeriesValue,
  roundAwayFromZero,
  type TalibInputSource,
} from "./talibCalcRules";

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
type NumericArrayLike = { length: number; [index: number]: unknown };

const TALIB_FUNCTIONS = TAFuncs as unknown as Record<string, TalibFunction>;

function toOutputValue(values: Array<number | undefined> | undefined, index: number): number | undefined {
  const value = values?.[index];
  return typeof value === "number" && Number.isFinite(value) ? value : undefined;
}

function normalizeInputKey(value: string): string {
  return value.trim().toUpperCase().replace(/[^A-Z0-9]/g, "");
}

function isNumericArrayLike(value: unknown): value is NumericArrayLike {
  if (Array.isArray(value)) {
    return true;
  }
  if (ArrayBuffer.isView(value)) {
    const maybeLength = (value as { length?: unknown }).length;
    return typeof maybeLength === "number";
  }
  return false;
}

function toFiniteOrUndefined(value: unknown): number | undefined {
  if (typeof value === "number") {
    return Number.isFinite(value) ? value : undefined;
  }
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : undefined;
}

function alignOutputTail(rawValues: unknown, expectedLength: number): Array<number | undefined> {
  const aligned: Array<number | undefined> = new Array(expectedLength).fill(undefined);
  if (!isNumericArrayLike(rawValues) || expectedLength <= 0) {
    return aligned;
  }

  const copyCount = Math.min(expectedLength, rawValues.length);
  const startIndex = Math.max(0, expectedLength - copyCount);
  for (let i = 0; i < copyCount; i += 1) {
    aligned[startIndex + i] = toFiniteOrUndefined(rawValues[i]);
  }
  return aligned;
}

function tryGetOutputArray(rawOutputs: Record<string, unknown>, outputKey: string): unknown {
  const direct = rawOutputs[outputKey];
  if (isNumericArrayLike(direct)) {
    return direct;
  }

  const normalized = normalizeInputKey(outputKey);
  for (const [key, value] of Object.entries(rawOutputs)) {
    if (normalizeInputKey(key) === normalized && isNumericArrayLike(value)) {
      return value;
    }
  }
  return undefined;
}

function buildSeries(data: KLineData[], key: string): number[] {
  const source = normalizeTalibInputSource(key);
  return data.map((item) => resolveTalibSeriesValue(item, source));
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
    return roundAwayFromZero(baseValue);
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

function normalizeInputSource(value: string): TalibInputSource {
  return normalizeTalibInputSource(value);
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
    const outputArrays = new Map<string, Array<number | undefined>>();
    let configuredOutputFound = 0;
    for (const output of spec.outputs) {
      const raw = tryGetOutputArray(rawOutputs, output.key);
      if (Array.isArray(raw)) {
        configuredOutputFound += 1;
        outputArrays.set(output.key, alignOutputTail(raw, data.length));
      }
    }
    if (configuredOutputFound === 0) {
      for (const [key, raw] of Object.entries(rawOutputs)) {
        if (Array.isArray(raw)) {
          outputArrays.set(key, alignOutputTail(raw, data.length));
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
