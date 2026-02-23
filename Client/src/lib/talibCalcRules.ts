import type { KLineData } from "klinecharts";

export type TalibInputSource =
  | "OPEN"
  | "HIGH"
  | "LOW"
  | "CLOSE"
  | "VOLUME"
  | "HL2"
  | "HLC3"
  | "OHLC4"
  | "OC2"
  | "HLCC4";

/**
 * 统一规范输入源名称，保证前后端使用一致的映射规则。
 */
export function normalizeTalibInputSource(value: string | null | undefined): TalibInputSource {
  const normalized = (value ?? "").trim().toUpperCase().replace(/[^A-Z0-9]/g, "");
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

/**
 * 统一数值解析：无法解析为有限数字时保留 NaN，避免被静默改写为 0。
 */
export function toFiniteNumberOrNaN(value: unknown): number {
  if (typeof value === "number" && Number.isFinite(value)) {
    return value;
  }
  if (typeof value === "string") {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : Number.NaN;
  }
  return Number.NaN;
}

/**
 * 对齐 C# MidpointRounding.AwayFromZero 的整数取整行为。
 */
export function roundAwayFromZero(value: number): number {
  if (!Number.isFinite(value) || value === 0) {
    return 0;
  }
  return Math.sign(value) * Math.round(Math.abs(value));
}

/**
 * 统一行情输入字段解析，所有派生源都基于同一套数学定义。
 */
export function resolveTalibSeriesValue(item: KLineData, source: TalibInputSource): number {
  const open = toFiniteNumberOrNaN(item.open);
  const high = toFiniteNumberOrNaN(item.high);
  const low = toFiniteNumberOrNaN(item.low);
  const close = toFiniteNumberOrNaN(item.close);
  const volume = toFiniteNumberOrNaN(item.volume);

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
