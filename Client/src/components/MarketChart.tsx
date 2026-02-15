import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  ActionType,
  CandleType,
  LineType,
  LoadDataType,
  OverlayMode,
  TooltipIconPosition,
  TooltipShowRule,
  YAxisType,
  dispose,
  init,
  registerLocale,
  type Chart,
  type KLineData,
} from "klinecharts";
import HorizontalLineIcon from "../assets/KLineCharts/01_horizontal_line.svg?react";
import HorizontalRayIcon from "../assets/KLineCharts/02_horizontal_ray.svg?react";
import HorizontalSegmentIcon from "../assets/KLineCharts/03_horizontal_segment.svg?react";
import VerticalLineIcon from "../assets/KLineCharts/04_vertical_line.svg?react";
import VerticalRayIcon from "../assets/KLineCharts/05_vertical_ray.svg?react";
import VerticalSegmentIcon from "../assets/KLineCharts/06_vertical_segment.svg?react";
import StraightLineIcon from "../assets/KLineCharts/07_straight_line.svg?react";
import RayLineIcon from "../assets/KLineCharts/08_ray.svg?react";
import SegmentLineIcon from "../assets/KLineCharts/09_segment.svg?react";
import PriceLineIcon from "../assets/KLineCharts/10_price_line.svg?react";
import RectangleIcon from "../assets/KLineCharts/01_rectangle.svg?react";
import CircleIcon from "../assets/KLineCharts/02_circle.svg?react";
import TriangleIcon from "../assets/KLineCharts/03_triangle.svg?react";
import ParallelogramIcon from "../assets/KLineCharts/04_parallelogram.svg?react";
import ParallelLineIcon from "../assets/KLineCharts/05_parallel_lines.svg?react";
import PriceChannelIcon from "../assets/KLineCharts/06_price_channel.svg?react";
import FibonacciLineIcon from "../assets/KLineCharts/11_fibonacci_retracement_line.svg?react";
import FibonacciSegmentIcon from "../assets/KLineCharts/12_fibonacci_retracement_segment.svg?react";
import FibonacciCircleIcon from "../assets/KLineCharts/13_fibonacci_circle.svg?react";
import FibonacciSpiralIcon from "../assets/KLineCharts/14_fibonacci_spiral.svg?react";
import FibonacciFanIcon from "../assets/KLineCharts/15_fibonacci_speed_resistance_fan.svg?react";
import FibonacciExtensionIcon from "../assets/KLineCharts/16_fibonacci_extension.svg?react";
import GannBoxIcon from "../assets/KLineCharts/17_gann_box.svg?react";
import WaveXabcdIcon from "../assets/KLineCharts/18_wave_xabcd.svg?react";
import WaveAbcdIcon from "../assets/KLineCharts/19_wave_abcd.svg?react";
import WaveThreeIcon from "../assets/KLineCharts/20_wave_three.svg?react";
import WaveFiveIcon from "../assets/KLineCharts/21_wave_five.svg?react";
import WaveEightIcon from "../assets/KLineCharts/22_wave_eight.svg?react";
import WaveAnyIcon from "../assets/KLineCharts/23_wave_any.svg?react";
import MagnetWeakOffIcon from "../assets/KLineCharts/magnet-weak-off.svg?react";
import MagnetWeakOnIcon from "../assets/KLineCharts/magnet-weak-on.svg?react";
import MagnetStrongOffIcon from "../assets/KLineCharts/magnet-strong-off.svg?react";
import MagnetStrongOnIcon from "../assets/KLineCharts/magnet-strong-on.svg?react";
import DrawingLockOffIcon from "../assets/KLineCharts/drawing-lock-off.svg?react";
import DrawingLockOnIcon from "../assets/KLineCharts/drawing-lock-on.svg?react";
import DrawingShowAllIcon from "../assets/KLineCharts/drawing-show-all.svg?react";
import DrawingHideAllIcon from "../assets/KLineCharts/drawing-hide-all.svg?react";
import DrawingClearAllIcon from "../assets/KLineCharts/drawing-clear-all.svg?react";
import {
  TA_INDICATOR_DEFAULT_PARAMS,
  TA_INDICATOR_PARAM_LABELS,
  TA_MAIN_INDICATORS,
  getTalibIndicatorEditorSchema,
  getTalibIndicatorMetaList,
  normalizeTalibInputSource,
  registerTalibIndicators,
  type TalibIndicatorInputSlot,
  type TalibIndicatorParamDefinition,
} from "../lib/registerTalibIndicators";
import { HttpClient, getToken, subscribeMarket } from "../network";
import { registerCustomOverlays } from "./customOverlays";
import { Dialog } from "./ui";
import "./MarketChart.css";

registerLocale("zh-HK", {
  time: "Time: ",
  open: "Open: ",
  high: "High: ",
  low: "Low: ",
  close: "Close: ",
  volume: "Volume: ",
  change: "Change: ",
  turnover: "Turnover: ",
});

type MarketChartProps = {
  symbol?: string;
  interval?: string;
  height?: string | number;
  theme?: "light" | "dark";
  focusRange?: MarketChartFocusRange | null;
};

export type MarketChartFocusRange = {
  id: string;
  chartSymbol?: string;
  chartInterval?: string;
  startTime: number;
  endTime: number;
  side?: string;
  exitReason?: string;
  entryPrice?: number;
  exitPrice?: number;
  stopLossPrice?: number;
  takeProfitPrice?: number;
};

type SymbolOption = {
  label: string;
  value: string;
};

type IntervalOption = {
  label: string;
  value: string;
};

type ToolIconComponent = React.ComponentType<React.SVGProps<SVGSVGElement>>;
type DrawingToolGroupId = "singleLine" | "moreLine" | "polygon" | "fibonacci" | "wave";
type ExpandableDrawingMenuId = DrawingToolGroupId | "magnet";
type DrawingListDirection = "down" | "up";
type MagnetMode = "weak" | "strong";
type ToolbarPanelId = "timezone" | "settings";
type IndicatorFilterPane = "all" | "main" | "sub";

type DrawingToolItem = {
  id: string;
  label: string;
  overlay: string;
  icon: ToolIconComponent;
};

type DrawingToolGroup = {
  id: DrawingToolGroupId;
  label: string;
  tools: DrawingToolItem[];
};

type IndicatorPane = "main" | "sub";

type IndicatorChip = {
  key: string;
  name: string;
  pane: IndicatorPane;
  hidden: boolean;
};

type EditingIndicator = {
  key: string;
  name: string;
  pane: IndicatorPane;
  paramDefinitions: TalibIndicatorParamDefinition[];
  paramDrafts: string[];
  inputSlots: TalibIndicatorInputSlot[];
  inputDrafts: string[];
};

type IndicatorInputMap = Record<string, string>;

type OHLCV = {
  timestamp?: number | null;
  open?: number | null;
  high?: number | null;
  low?: number | null;
  close?: number | null;
  volume?: number | null;
};

type CrosshairPayload = {
  kLineData?: KLineData | null;
};

const DEFAULT_SYMBOL = "Binance:BTC/USDT";
const DEFAULT_INTERVAL = "1";
const HISTORY_PAGE_SIZE = 500;
const CANDLE_PANE_ID = "candle_pane";

const SYMBOL_OPTIONS: SymbolOption[] = [
  { label: "BTC/USDT", value: "Binance:BTC/USDT" },
  { label: "ETH/USDT", value: "Binance:ETH/USDT" },
  { label: "BNB/USDT", value: "Binance:BNB/USDT" },
  { label: "SOL/USDT", value: "Binance:SOL/USDT" },
  { label: "XRP/USDT", value: "Binance:XRP/USDT" },
  { label: "DOGE/USDT", value: "Binance:DOGE/USDT" },
];

const INTERVAL_OPTIONS: IntervalOption[] = [
  { label: "1m", value: "1" },
  { label: "3m", value: "3" },
  { label: "5m", value: "5" },
  { label: "15m", value: "15" },
  { label: "30m", value: "30" },
  { label: "1H", value: "60" },
  { label: "4H", value: "240" },
  { label: "D", value: "D" },
  { label: "W", value: "W" },
];

const INTERVAL_CATEGORIES: Array<{ id: string; label: string; options: IntervalOption[] }> = [
  {
    id: "minute",
    label: "Minute",
    options: [
      { label: "1m", value: "1" },
      { label: "3m", value: "3" },
      { label: "5m", value: "5" },
      { label: "15m", value: "15" },
      { label: "30m", value: "30" },
    ],
  },
  {
    id: "hour",
    label: "Hour",
    options: [
      { label: "1h", value: "60" },
      { label: "2h", value: "120" },
      { label: "4h", value: "240" },
    ],
  },
  {
    id: "day",
    label: "Day/Week",
    options: [
      { label: "1D", value: "D" },
      { label: "1W", value: "W" },
    ],
  },
];

const DEFAULT_SELECTED_INTERVALS = ["1", "5", "15", "60", "240"];
const MAX_SELECTED_INTERVALS = 8;

const CANDLE_TYPE_OPTIONS = [
  { label: "Solid", value: CandleType.CandleSolid },
  { label: "Hollow", value: CandleType.CandleStroke },
  { label: "Up Hollow", value: CandleType.CandleUpStroke },
  { label: "Down Hollow", value: CandleType.CandleDownStroke },
  { label: "OHLC", value: CandleType.Ohlc },
  { label: "Area", value: CandleType.Area },
] as const;

const Y_AXIS_OPTIONS = [
  { label: "Linear", value: YAxisType.Normal },
  { label: "Percent", value: YAxisType.Percentage },
  { label: "Log", value: YAxisType.Log },
] as const;

const TIMEZONE_OPTIONS = [
  { label: "本地", value: "local" },
  { label: "UTC", value: "UTC" },
  { label: "Shanghai", value: "Asia/Shanghai" },
  { label: "Berlin", value: "Europe/Berlin" },
  { label: "Chicago", value: "America/Chicago" },
] as const;

const MAIN_INDICATORS = TA_MAIN_INDICATORS;

const INDICATOR_GROUP_LABELS: Record<string, string> = {
  "Overlap Studies": "Overlay",
  "Price Transform": "Price Transform",
  "Momentum Indicators": "Momentum",
  "Volume Indicators": "Volume",
  "Volatility Indicators": "Volatility",
  "Cycle Indicators": "Cycle",
  "Pattern Recognition": "Pattern",
  "Math Transform": "Math Transform",
  "Math Operators": "Math Operators",
  "Statistic Functions": "Statistics",
};

function normalizeIndicatorName(name: string): string {
  return name.replace(/^ta_/, "");
}

function normalizeIndicatorGroupLabel(group: string): string {
  const trimmed = group.trim();
  return INDICATOR_GROUP_LABELS[trimmed] ?? (trimmed || "Other");
}

const CrossStarIcon: ToolIconComponent = (props) => (
  <svg viewBox="0 0 22 22" {...props}>
    <line x1="11" y1="4" x2="11" y2="18" strokeWidth="1.6" />
    <line x1="4" y1="11" x2="18" y2="11" strokeWidth="1.6" />
    <circle cx="11" cy="11" r="1.8" />
  </svg>
);

const DrawingMenuArrow: React.FC<{ expanded: boolean }> = ({ expanded }) => (
  <svg viewBox="0 0 8 12" className={expanded ? "rotate" : ""} aria-hidden="true">
    <path d="M2 2L6 6L2 10" />
  </svg>
);

const SINGLE_LINE_TOOLS: DrawingToolItem[] = [
  { id: "horizontalStraightLine", label: "Horizontal Line", overlay: "horizontalStraightLine", icon: HorizontalLineIcon },
  { id: "horizontalRayLine", label: "Horizontal Ray", overlay: "horizontalRayLine", icon: HorizontalRayIcon },
  { id: "horizontalSegment", label: "Horizontal Segment", overlay: "horizontalSegment", icon: HorizontalSegmentIcon },
  { id: "verticalStraightLine", label: "Vertical Line", overlay: "verticalStraightLine", icon: VerticalLineIcon },
  { id: "verticalRayLine", label: "Vertical Ray", overlay: "verticalRayLine", icon: VerticalRayIcon },
  { id: "verticalSegment", label: "Vertical Segment", overlay: "verticalSegment", icon: VerticalSegmentIcon },
  { id: "straightLine", label: "Straight Line", overlay: "straightLine", icon: StraightLineIcon },
  { id: "rayLine", label: "Ray Line", overlay: "rayLine", icon: RayLineIcon },
  { id: "segment", label: "Segment", overlay: "segment", icon: SegmentLineIcon },
  { id: "priceLine", label: "Price Line", overlay: "priceLine", icon: PriceLineIcon },
];

const MORE_LINE_TOOLS: DrawingToolItem[] = [
  { id: "priceChannelLine", label: "Price Channel", overlay: "priceChannelLine", icon: PriceChannelIcon },
  { id: "parallelStraightLine", label: "Parallel Line", overlay: "parallelStraightLine", icon: ParallelLineIcon },
];

const POLYGON_TOOLS: DrawingToolItem[] = [
  { id: "circle", label: "Circle", overlay: "circle", icon: CircleIcon },
  { id: "rect", label: "Rectangle", overlay: "rect", icon: RectangleIcon },
  { id: "parallelogram", label: "Parallelogram", overlay: "parallelogram", icon: ParallelogramIcon },
  { id: "triangle", label: "Triangle", overlay: "triangle", icon: TriangleIcon },
  { id: "crossStar", label: "Cross Star", overlay: "crossStar", icon: CrossStarIcon },
];

const FIBONACCI_TOOLS: DrawingToolItem[] = [
  { id: "fibonacciLine", label: "斐波那契回撤直线", overlay: "fibonacciLine", icon: FibonacciLineIcon },
  { id: "fibonacciSegment", label: "斐波那契回撤线段", overlay: "fibonacciSegment", icon: FibonacciSegmentIcon },
  { id: "fibonacciCircle", label: "斐波那契圆环", overlay: "fibonacciCircle", icon: FibonacciCircleIcon },
  { id: "fibonacciSpiral", label: "斐波那契螺旋", overlay: "fibonacciSpiral", icon: FibonacciSpiralIcon },
  {
    id: "fibonacciSpeedResistanceFan",
    label: "Fibonacci Fan",
    overlay: "fibonacciSpeedResistanceFan",
    icon: FibonacciFanIcon,
  },
  { id: "fibonacciExtension", label: "Fibonacci Extension", overlay: "fibonacciExtension", icon: FibonacciExtensionIcon },
  { id: "gannBox", label: "Gann Box", overlay: "gannBox", icon: GannBoxIcon },
];

const WAVE_TOOLS: DrawingToolItem[] = [
  { id: "xabcd", label: "XABCD", overlay: "xabcd", icon: WaveXabcdIcon },
  { id: "abcd", label: "ABCD", overlay: "abcd", icon: WaveAbcdIcon },
  { id: "threeWaves", label: "3 Waves", overlay: "threeWaves", icon: WaveThreeIcon },
  { id: "fiveWaves", label: "5 Waves", overlay: "fiveWaves", icon: WaveFiveIcon },
  { id: "eightWaves", label: "8 Waves", overlay: "eightWaves", icon: WaveEightIcon },
  { id: "anyWaves", label: "Any Waves", overlay: "anyWaves", icon: WaveAnyIcon },
];

const DRAWING_TOOL_GROUPS: DrawingToolGroup[] = [
  { id: "singleLine", label: "Line Tools", tools: SINGLE_LINE_TOOLS },
  { id: "moreLine", label: "Channel Tools", tools: MORE_LINE_TOOLS },
  { id: "polygon", label: "Shapes", tools: POLYGON_TOOLS },
  { id: "fibonacci", label: "Fibonacci", tools: FIBONACCI_TOOLS },
  { id: "wave", label: "Waves", tools: WAVE_TOOLS },
];

const DRAWING_TOOL_GROUP_BY_ID: Record<DrawingToolGroupId, DrawingToolGroup> = {
  singleLine: DRAWING_TOOL_GROUPS[0],
  moreLine: DRAWING_TOOL_GROUPS[1],
  polygon: DRAWING_TOOL_GROUPS[2],
  fibonacci: DRAWING_TOOL_GROUPS[3],
  wave: DRAWING_TOOL_GROUPS[4],
};

const DRAWING_GROUP_DEFAULT_TOOL_ID: Record<DrawingToolGroupId, string> = {
  singleLine: SINGLE_LINE_TOOLS[0].id,
  moreLine: MORE_LINE_TOOLS[0].id,
  polygon: POLYGON_TOOLS[0].id,
  fibonacci: FIBONACCI_TOOLS[0].id,
  wave: WAVE_TOOLS[0].id,
};

const MAGNET_OPTIONS: Array<{ mode: MagnetMode; label: string; icon: ToolIconComponent }> = [
  { mode: "weak", label: "Weak Magnet", icon: MagnetWeakOffIcon },
  { mode: "strong", label: "Strong Magnet", icon: MagnetStrongOffIcon },
];

const DEFAULT_DRAWING_OVERLAY = SINGLE_LINE_TOOLS[0].overlay;
const DEFAULT_SELECTED_DRAWING_TOOLS: Record<DrawingToolGroupId, string> = {
  singleLine: DRAWING_GROUP_DEFAULT_TOOL_ID.singleLine,
  moreLine: DRAWING_GROUP_DEFAULT_TOOL_ID.moreLine,
  polygon: DRAWING_GROUP_DEFAULT_TOOL_ID.polygon,
  fibonacci: DRAWING_GROUP_DEFAULT_TOOL_ID.fibonacci,
  wave: DRAWING_GROUP_DEFAULT_TOOL_ID.wave,
};
const DRAWING_TOOL_ITEM_BY_ID = new Map<string, DrawingToolItem>();
for (const group of DRAWING_TOOL_GROUPS) {
  for (const tool of group.tools) {
    DRAWING_TOOL_ITEM_BY_ID.set(tool.id, tool);
  }
}
let customOverlaysRegistered = false;

const INDICATOR_DEFAULT_PARAMS: Record<string, number[]> = {
  MA: [5, 10, 30],
  EMA: [6, 12, 20],
  BOLL: [20, 2],
  SAR: [2, 2, 20],
  VOL: [5, 10, 20],
  MACD: [12, 26, 9],
  KDJ: [9, 3, 3],
  RSI: [6, 12, 24],
};

const INDICATOR_PARAM_LABELS: Record<string, string[]> = {
  MA: ["周期1", "周期2", "周期3"],
  EMA: ["周期1", "周期2", "周期3"],
  BOLL: ["周期", "倍数"],
  SAR: ["初始", "步长", "上限"],
  VOL: ["均线1", "均线2", "均线3"],
  MACD: ["快线", "慢线", "信号"],
  KDJ: ["周期", "K", "D"],
  RSI: ["周期1", "周期2", "周期3"],
};

const RESOLUTION_TO_TIMEFRAME: Record<string, string> = {
  "1": "m1",
  "3": "m3",
  "5": "m5",
  "15": "m15",
  "30": "m30",
  "60": "h1",
  "120": "h2",
  "240": "h4",
  D: "d1",
  W: "w1",
};

const RESOLUTION_TO_MS: Record<string, number> = {
  "1": 60_000,
  "3": 180_000,
  "5": 300_000,
  "15": 900_000,
  "30": 1_800_000,
  "60": 3_600_000,
  "120": 7_200_000,
  "240": 14_400_000,
  D: 86_400_000,
  W: 604_800_000,
};

const RESOLUTION_ALIASES: Record<string, string> = {
  "1m": "1",
  "3m": "3",
  "5m": "5",
  "15m": "15",
  "30m": "30",
  "1h": "60",
  h1: "60",
  "2h": "120",
  h2: "120",
  "4h": "240",
  h4: "240",
  d: "D",
  "1d": "D",
  w: "W",
  "1w": "W",
};

function parseSymbolName(symbolName: string): { exchange: string; symbol: string } | null {
  if (!symbolName) {
    return null;
  }
  if (symbolName.includes(":")) {
    const [exchange, symbol] = symbolName.split(":");
    if (!exchange || !symbol) {
      return null;
    }
    return { exchange, symbol };
  }
  return { exchange: "Binance", symbol: symbolName };
}

function normalizeResolution(value?: string | null): string | undefined {
  if (!value) {
    return undefined;
  }
  const trimmed = value.trim();
  if (!trimmed) {
    return undefined;
  }
  if (RESOLUTION_TO_TIMEFRAME[trimmed]) {
    return trimmed;
  }
  const lower = trimmed.toLowerCase();
  return RESOLUTION_ALIASES[lower] ?? undefined;
}

function toSymbolEnum(symbol: string): string {
  return symbol.replace("/", "_").replace("-", "_").toUpperCase();
}

function toTimeframe(resolution: string): string | null {
  return RESOLUTION_TO_TIMEFRAME[resolution] ?? null;
}

function toIntervalMs(resolution: string): number | null {
  return RESOLUTION_TO_MS[resolution] ?? null;
}

function formatDateTime(ms: number): string {
  const date = new Date(ms);
  const pad = (value: number) => String(value).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(
    date.getMinutes()
  )}:${pad(date.getSeconds())}`;
}

function clamp(value: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, value));
}

function toFinitePrice(value?: number): number | null {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    return null;
  }
  return value;
}

function getFocusPriceBand(dataList: KLineData[], focusRange: MarketChartFocusRange): [number, number] {
  const start = Math.min(focusRange.startTime, focusRange.endTime);
  const end = Math.max(focusRange.startTime, focusRange.endTime);

  let min = Number.POSITIVE_INFINITY;
  let max = Number.NEGATIVE_INFINITY;

  for (const item of dataList) {
    if (item.timestamp < start || item.timestamp > end) {
      continue;
    }
    min = Math.min(min, item.low);
    max = Math.max(max, item.high);
  }

  const priceCandidates = [
    toFinitePrice(focusRange.entryPrice),
    toFinitePrice(focusRange.exitPrice),
    toFinitePrice(focusRange.stopLossPrice),
    toFinitePrice(focusRange.takeProfitPrice),
  ];

  for (const price of priceCandidates) {
    if (price === null) {
      continue;
    }
    min = Math.min(min, price);
    max = Math.max(max, price);
  }

  if (!Number.isFinite(min) || !Number.isFinite(max) || max <= min) {
    const fallback = priceCandidates.find((value): value is number => value !== null) ?? 1;
    const pad = Math.max(fallback * 0.01, 0.5);
    return [fallback - pad, fallback + pad];
  }

  const pad = Math.max((max - min) * 0.15, max * 0.003, 0.5);
  return [min - pad, max + pad];
}

function safeCreateOverlay(chart: Chart, value: Record<string, unknown>): void {
  try {
    chart.createOverlay(value as never);
  } catch {
    // Ignore unsupported overlay definitions.
  }
}

function toKLine(item: OHLCV): KLineData | null {
  if (!item.timestamp || !Number.isFinite(item.timestamp)) {
    return null;
  }
  const open = item.open ?? item.close ?? 0;
  const high = item.high ?? open;
  const low = item.low ?? open;
  const close = item.close ?? open;
  return {
    timestamp: item.timestamp,
    open,
    high,
    low,
    close,
    volume: item.volume ?? 0,
  };
}

function updateBar(lastBar: KLineData | null, intervalMs: number, price: number, ts: number): KLineData | null {
  const barTime = Math.floor(ts / intervalMs) * intervalMs;
  if (!lastBar || barTime > lastBar.timestamp) {
    return {
      timestamp: barTime,
      open: price,
      high: price,
      low: price,
      close: price,
      volume: 0,
    };
  }
  if (barTime < lastBar.timestamp) {
    return null;
  }
  return {
    ...lastBar,
    high: Math.max(lastBar.high, price),
    low: Math.min(lastBar.low, price),
    close: price,
  };
}

async function fetchHistory(options: {
  http: HttpClient;
  exchange: string;
  symbol: string;
  timeframe: string;
  intervalMs: number;
  count: number;
  signal: AbortSignal;
  endTime?: number;
  startTime?: number;
}): Promise<KLineData[]> {
  const endTime = options.endTime ?? Date.now();
  const startTime = options.startTime ?? endTime - options.intervalMs * (options.count - 1);
  const payload = {
    exchange: options.exchange,
    timeframe: options.timeframe,
    symbol: toSymbolEnum(options.symbol),
    count: options.count,
    startTime: formatDateTime(startTime),
    endTime: formatDateTime(endTime),
  };

  const data = await options.http.postProtocol<OHLCV[]>(
    "/api/marketdata/history",
    "marketdata.kline.history",
    payload,
    { signal: options.signal }
  );

  const bars = data
    .map((item) => toKLine(item))
    .filter((bar): bar is KLineData => bar !== null)
    .sort((a, b) => a.timestamp - b.timestamp);

  const deduped: KLineData[] = [];
  const timestampSet = new Set<number>();
  for (const bar of bars) {
    if (timestampSet.has(bar.timestamp)) {
      continue;
    }
    timestampSet.add(bar.timestamp);
    deduped.push(bar);
  }

  return deduped;
}

function resolveTimezone(value: string): string {
  if (value === "local") {
    return Intl.DateTimeFormat().resolvedOptions().timeZone;
  }
  return value;
}

function buildIndicatorKey(pane: IndicatorPane, name: string): string {
  return `${pane}:${name}`;
}

function getIndicatorParamLabels(name: string, count: number): string[] {
  const labels = TA_INDICATOR_PARAM_LABELS[name] ?? INDICATOR_PARAM_LABELS[name] ?? [];
  if (count <= 0) {
    return [];
  }
  return Array.from({ length: count }, (_, index) => labels[index] ?? `参数${index + 1}`);
}

function buildFallbackParamDefinitions(name: string, values: number[]): TalibIndicatorParamDefinition[] {
  const labels = getIndicatorParamLabels(name, values.length);
  return values.map((value, index) => ({
    key: `param_${index}`,
    label: labels[index] ?? `Param ${index + 1}`,
    type: "number",
    valueType: "double",
    defaultValue: Number.isFinite(value) ? value : 0,
    enumOptions: [],
  }));
}

function normalizeInputMapBySlots(slots: TalibIndicatorInputSlot[], inputMap: IndicatorInputMap | undefined): IndicatorInputMap {
  const normalized: IndicatorInputMap = {};
  for (const slot of slots) {
    const rawValue = inputMap?.[slot.key] ?? slot.defaultValue;
    const normalizedValue = normalizeTalibInputSource(rawValue);
    const hasOption = slot.options.some((option) => option.value === normalizedValue);
    normalized[slot.key] = hasOption ? normalizedValue : slot.defaultValue;
  }
  return normalized;
}

function readInputMapFromExtendData(extendData: unknown): IndicatorInputMap | undefined {
  if (!extendData || typeof extendData !== "object") {
    return undefined;
  }
  const taInputMap = (extendData as { taInputMap?: unknown }).taInputMap;
  if (!taInputMap || typeof taInputMap !== "object") {
    return undefined;
  }
  const map: IndicatorInputMap = {};
  for (const [rawKey, rawValue] of Object.entries(taInputMap as Record<string, unknown>)) {
    if (typeof rawValue !== "string") {
      continue;
    }
    map[rawKey] = normalizeTalibInputSource(rawValue);
  }
  return Object.keys(map).length > 0 ? map : undefined;
}

function buildIndicatorPayload(
  name: string,
  hidden: boolean,
  params: number[] | undefined,
  inputMap: IndicatorInputMap | undefined
): {
  name: string;
  visible: boolean;
  calcParams?: number[];
  extendData?: { taInputMap: IndicatorInputMap };
} {
  const payload: {
    name: string;
    visible: boolean;
    calcParams?: number[];
    extendData?: { taInputMap: IndicatorInputMap };
  } = {
    name,
    visible: !hidden,
  };
  if (Array.isArray(params) && params.length > 0) {
    payload.calcParams = params;
  }
  if (inputMap && Object.keys(inputMap).length > 0) {
    payload.extendData = {
      taInputMap: { ...inputMap },
    };
  }
  return payload;
}

function resolveParamDraftValue(rawValue: string, definition: TalibIndicatorParamDefinition): number {
  const fallback = Number.isFinite(definition.defaultValue) ? definition.defaultValue : 0;
  const parsed = Number(rawValue);
  const base = Number.isFinite(parsed) ? parsed : fallback;
  if (definition.valueType === "integer" || definition.valueType === "matype") {
    return Math.round(base);
  }
  return base;
}

function ensureCustomOverlaysRegistered(): void {
  if (customOverlaysRegistered) {
    return;
  }
  registerCustomOverlays();
  customOverlaysRegistered = true;
}

function normalizeOverlayIds(ids: string | null | Array<string | null>): string[] {
  if (Array.isArray(ids)) {
    return ids.filter((id): id is string => typeof id === "string" && id.length > 0);
  }
  return typeof ids === "string" && ids.length > 0 ? [ids] : [];
}

function createIndicatorTooltipIcons(textColor: string) {
  const buildIcon = (id: string, icon: string, danger = false) => ({
    id,
    position: TooltipIconPosition.Right,
    color: danger ? "#ef4444" : textColor,
    activeColor: danger ? "#b91c1c" : "#1677ff",
    size: 12,
    fontFamily: "PingFang SC, Microsoft YaHei, Helvetica Neue, Arial, sans-serif",
    icon,
    backgroundColor: "transparent",
    activeBackgroundColor: "rgba(22, 119, 255, 0.12)",
    paddingLeft: 6,
    paddingRight: 6,
    paddingTop: 4,
    paddingBottom: 4,
    marginRight: 2,
    marginLeft: 2,
    marginTop: 0,
    marginBottom: 0,
  });

  return [
    buildIcon("indicator_hide", "svg:hide"),
    buildIcon("indicator_settings", "svg:settings"),
    buildIcon("indicator_delete", "svg:delete", true),
  ];
}

const MarketChart: React.FC<MarketChartProps> = ({
  symbol,
  interval,
  height = "100%",
  theme = "light",
  focusRange = null,
}) => {
  const wrapperRef = useRef<HTMLDivElement | null>(null);
  const toolbarRef = useRef<HTMLDivElement | null>(null);
  const containerRef = useRef<HTMLDivElement | null>(null);
  const drawingBarRef = useRef<HTMLDivElement | null>(null);
  const chartRef = useRef<Chart | null>(null);
  const httpRef = useRef<HttpClient>(new HttpClient({ tokenProvider: getToken }));
  const unsubscribeRef = useRef<(() => void) | null>(null);
  const resizeObserverRef = useRef<ResizeObserver | null>(null);
  const dataVersionRef = useRef(0);
  const lastBarRef = useRef<KLineData | null>(null);
  const drawingOverlayIdsRef = useRef<Set<string>>(new Set());
  const subPaneIdMapRef = useRef<Map<string, string>>(new Map());
  const focusOverlayGroupIdRef = useRef<string | null>(null);

  const [chartReady, setChartReady] = useState(false);
  const [talibReady, setTalibReady] = useState(false);
  const [activeSymbol, setActiveSymbol] = useState(symbol ?? DEFAULT_SYMBOL);
  const [activeInterval, setActiveInterval] = useState(normalizeResolution(interval) ?? DEFAULT_INTERVAL);
  const [selectedIntervals, setSelectedIntervals] = useState<string[]>(() => {
    const normalized = normalizeResolution(interval);
    if (normalized && DEFAULT_SELECTED_INTERVALS.includes(normalized)) {
      return DEFAULT_SELECTED_INTERVALS;
    }
    return normalized
      ? [...new Set([...DEFAULT_SELECTED_INTERVALS.slice(0, 4), normalized])]
      : DEFAULT_SELECTED_INTERVALS;
  });
  const [activeTheme, setActiveTheme] = useState<"light" | "dark">(theme);
  const [candleType, setCandleType] = useState<CandleType>(CandleType.CandleSolid);
  const [yAxisType, setYAxisType] = useState<YAxisType>(YAxisType.Normal);
  const [language] = useState("zh-CN");
  const [timezone, setTimezone] = useState("local");
  const [mainIndicators, setMainIndicators] = useState<string[]>(["ta_MA"]);
  const [subIndicators, setSubIndicators] = useState<string[]>(["ta_MACD"]);
  const [hiddenIndicatorKeys, setHiddenIndicatorKeys] = useState<string[]>([]);
  const [indicatorParams, setIndicatorParams] = useState<Record<string, number[]>>({});
  const [indicatorInputMaps, setIndicatorInputMaps] = useState<Record<string, IndicatorInputMap>>({});
  const [editingIndicator, setEditingIndicator] = useState<EditingIndicator | null>(null);
  const [hoverBar, setHoverBar] = useState<KLineData | null>(null);
  const [latestBar, setLatestBar] = useState<KLineData | null>(null);
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [activeDrawingTool, setActiveDrawingTool] = useState<string>(DEFAULT_DRAWING_OVERLAY);
  const [selectedDrawingTools, setSelectedDrawingTools] = useState<Record<DrawingToolGroupId, string>>(
    DEFAULT_SELECTED_DRAWING_TOOLS
  );
  const [expandedDrawingMenu, setExpandedDrawingMenu] = useState<ExpandableDrawingMenuId | null>(null);
  const [drawingListDirection, setDrawingListDirection] = useState<DrawingListDirection>("down");
  const [isMagnetEnabled, setIsMagnetEnabled] = useState(false);
  const [magnetMode, setMagnetMode] = useState<MagnetMode>("weak");
  const [isDrawingLocked, setIsDrawingLocked] = useState(false);
  const [isDrawingVisible, setIsDrawingVisible] = useState(true);
  const [isFullscreen, setIsFullscreen] = useState(false);
  const [historyNonce, setHistoryNonce] = useState(0);
  const [activeToolbarPanel, setActiveToolbarPanel] = useState<ToolbarPanelId | null>(null);
  const [indicatorDialogOpen, setIndicatorDialogOpen] = useState(false);
  const [indicatorSearchKeyword, setIndicatorSearchKeyword] = useState("");
  const [indicatorPaneFilter, setIndicatorPaneFilter] = useState<IndicatorFilterPane>("all");
  const [indicatorGroupFilter, setIndicatorGroupFilter] = useState<string>("ALL");
  const [showLatestValue, setShowLatestValue] = useState(true);
  const [showHighValue, setShowHighValue] = useState(true);
  const [showLowValue, setShowLowValue] = useState(true);
  const [showIndicatorLastValue, setShowIndicatorLastValue] = useState(true);
  const [showGridLine, setShowGridLine] = useState(true);
  const [intervalMoreOpen, setIntervalMoreOpen] = useState(false);
  const [rightMoreOpen, setRightMoreOpen] = useState(false);
  const [toolbarWidth, setToolbarWidth] = useState(0);

  const hiddenIndicatorSet = useMemo(() => new Set(hiddenIndicatorKeys), [hiddenIndicatorKeys]);
  const indicatorCatalog = useMemo(() => {
    if (!talibReady) {
      return [];
    }
    return getTalibIndicatorMetaList();
  }, [talibReady]);

  const indicatorGroupOptions = useMemo(() => {
    const groups = new Set<string>();
    for (const item of indicatorCatalog) {
      if (indicatorPaneFilter === "main" && item.pane !== "main") {
        continue;
      }
      if (indicatorPaneFilter === "sub" && item.pane !== "sub") {
        continue;
      }
      groups.add(normalizeIndicatorGroupLabel(item.group));
    }
    return Array.from(groups).sort((a, b) => a.localeCompare(b));
  }, [indicatorCatalog, indicatorPaneFilter]);

  const filteredIndicatorGroups = useMemo(() => {
    const keyword = indicatorSearchKeyword.trim().toLowerCase();
    const grouped = new Map<string, Array<(typeof indicatorCatalog)[number]>>();
    for (const item of indicatorCatalog) {
      if (indicatorPaneFilter === "main" && item.pane !== "main") {
        continue;
      }
      if (indicatorPaneFilter === "sub" && item.pane !== "sub") {
        continue;
      }
      const groupLabel = normalizeIndicatorGroupLabel(item.group);
      if (indicatorGroupFilter !== "ALL" && groupLabel !== indicatorGroupFilter) {
        continue;
      }
      if (keyword.length > 0) {
        const haystack = `${item.name} ${item.code} ${item.talibCode} ${groupLabel}`.toLowerCase();
        if (!haystack.includes(keyword)) {
          continue;
        }
      }
      if (!grouped.has(groupLabel)) {
        grouped.set(groupLabel, []);
      }
      grouped.get(groupLabel)?.push(item);
    }
    const entries = Array.from(grouped.entries()).sort(([a], [b]) => a.localeCompare(b));
    entries.forEach(([, items]) => {
      items.sort((a, b) => a.name.localeCompare(b.name));
    });
    return entries;
  }, [indicatorCatalog, indicatorGroupFilter, indicatorPaneFilter, indicatorSearchKeyword]);

  const selectedIntervalOptions = useMemo(() => {
    const order = ["1", "3", "5", "15", "30", "60", "120", "240", "D", "W"];
    return selectedIntervals
      .slice()
      .sort((a, b) => order.indexOf(a) - order.indexOf(b))
      .map((v) => INTERVAL_OPTIONS.find((o) => o.value === v))
      .filter((o): o is IntervalOption => Boolean(o));
  }, [selectedIntervals]);

  useEffect(() => {
    if (indicatorGroupFilter === "ALL") {
      return;
    }
    if (!indicatorGroupOptions.includes(indicatorGroupFilter)) {
      setIndicatorGroupFilter("ALL");
    }
  }, [indicatorGroupFilter, indicatorGroupOptions]);

  const handleToggleIntervalSelection = useCallback((value: string) => {
    setSelectedIntervals((prev) => {
      if (prev.includes(value)) {
        const next = prev.filter((v) => v !== value);
        if (next.length === 0) return prev;
        if (activeInterval === value) {
          setActiveInterval(next[0]);
        }
        return next;
      }
      if (prev.length >= MAX_SELECTED_INTERVALS) return prev;
      return [...prev, value];
    });
  }, [activeInterval]);

  const toolbarRightCollapsed = useMemo(() => toolbarWidth > 0 && toolbarWidth < 520, [toolbarWidth]);
  const drawingMode = useMemo<OverlayMode>(() => {
    if (!isMagnetEnabled) {
      return OverlayMode.Normal;
    }
    return magnetMode === "strong" ? OverlayMode.StrongMagnet : OverlayMode.WeakMagnet;
  }, [isMagnetEnabled, magnetMode]);

  const getTrackedOverlayIds = useCallback((): string[] => {
    const chart = chartRef.current;
    if (!chart) {
      return [];
    }
    const ids = Array.from(drawingOverlayIdsRef.current);
    const validIds = ids.filter((id) => Boolean(chart.getOverlayById(id)));
    drawingOverlayIdsRef.current = new Set(validIds);
    return validIds;
  }, []);

  const clearFocusOverlays = useCallback(() => {
    const chart = chartRef.current;
    if (!chart) {
      return;
    }
    if (focusOverlayGroupIdRef.current) {
      chart.removeOverlay({ groupId: focusOverlayGroupIdRef.current });
      focusOverlayGroupIdRef.current = null;
    }
  }, []);

  useEffect(() => {
    if (symbol && symbol !== activeSymbol) {
      setActiveSymbol(symbol);
    }
  }, [symbol, activeSymbol]);

  useEffect(() => {
    const normalized = normalizeResolution(interval);
    if (normalized && normalized !== activeInterval) {
      setActiveInterval(normalized);
      setSelectedIntervals((prev) => {
        if (prev.includes(normalized)) return prev;
        if (prev.length >= MAX_SELECTED_INTERVALS) {
          return [...prev.slice(0, -1), normalized];
        }
        return [...prev, normalized];
      });
    }
  }, [interval, activeInterval]);

  useEffect(() => {
    if (theme !== activeTheme) {
      setActiveTheme(theme);
    }
  }, [theme, activeTheme]);

  const symbolOptions = useMemo(() => {
    const map = new Map<string, SymbolOption>();
    for (const option of SYMBOL_OPTIONS) {
      map.set(option.value, option);
    }
    if (!map.has(activeSymbol)) {
      map.set(activeSymbol, { label: activeSymbol.split(":").pop() ?? activeSymbol, value: activeSymbol });
    }
    return Array.from(map.values());
  }, [activeSymbol]);

  const displayBar = hoverBar ?? latestBar;
  void displayBar;

  useEffect(() => {
    let cancelled = false;
    const loadTalibIndicators = async () => {
      try {
        await registerTalibIndicators();
        if (!cancelled) {
          setTalibReady(true);
        }
      } catch (error) {
        console.error("[talib] register indicator failed", error);
        if (!cancelled) {
          setTalibReady(false);
        }
      }
    };
    loadTalibIndicators();
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    const element = containerRef.current;
    if (!element) {
      return;
    }

    ensureCustomOverlaysRegistered();

    const chart = init(element, {
      styles: {
        grid: {
          horizontal: { style: LineType.Dashed },
          vertical: { style: LineType.Dashed },
        },
      },
    });

    if (!chart) {
      return;
    }

    chartRef.current = chart;
    chart.setPriceVolumePrecision(4, 2);
    chart.setStyles(activeTheme);
    chart.setLocale(language);
    chart.setTimezone(resolveTimezone(timezone));
    const indicatorTextColor = activeTheme === "dark" ? "#9ca3af" : "#64748b";
    chart.setStyles({
      candle: {
        tooltip: {
          showRule: TooltipShowRule.Always,
        },
        priceMark: {
          high: { show: showHighValue },
          low: { show: showLowValue },
          last: { show: showLatestValue },
        },
      },
      grid: {
        show: showGridLine,
        horizontal: { show: showGridLine, style: LineType.Dashed },
        vertical: { show: showGridLine, style: LineType.Dashed },
      },
      indicator: {
        lastValueMark: {
          show: showIndicatorLastValue,
        },
        tooltip: {
          showRule: TooltipShowRule.Always,
          showName: true,
          showParams: true,
          icons: createIndicatorTooltipIcons(indicatorTextColor),
        },
      },
    });

    const handleCrosshairChange = (data?: unknown) => {
      const payload = data as CrosshairPayload | undefined;
      setHoverBar(payload?.kLineData ?? null);
    };

    chart.subscribeAction(ActionType.OnCrosshairChange, handleCrosshairChange);

    const resizeObserver = new ResizeObserver(() => {
      chart.resize();
    });
    resizeObserver.observe(element);

    resizeObserverRef.current = resizeObserver;
    setChartReady(true);

    return () => {
      setChartReady(false);
      setHoverBar(null);
      setLatestBar(null);
      lastBarRef.current = null;
      clearFocusOverlays();
      chart.unsubscribeAction(ActionType.OnCrosshairChange, handleCrosshairChange);
      resizeObserver.disconnect();
      resizeObserverRef.current = null;
      unsubscribeRef.current?.();
      unsubscribeRef.current = null;
      subPaneIdMapRef.current.clear();
      chartRef.current = null;
      dispose(chart);
    };
  // 图表实例只初始化一次，后续通过独立 effect 同步样式/语言/时区，避免重复销毁重建
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [clearFocusOverlays]);

  useEffect(() => {
    const handleFullscreenChange = () => {
      const element = wrapperRef.current;
      if (!element) {
        setIsFullscreen(false);
        return;
      }
      setIsFullscreen(document.fullscreenElement === element);
    };

    document.addEventListener("fullscreenchange", handleFullscreenChange);
    return () => {
      document.removeEventListener("fullscreenchange", handleFullscreenChange);
    };
  }, []);

  useEffect(() => {
    const el = toolbarRef.current;
    if (!el) return;
    const ro = new ResizeObserver((entries) => {
      const entry = entries[0];
      if (entry) {
        setToolbarWidth(entry.contentRect.width);
      }
    });
    ro.observe(el);
    setToolbarWidth(el.getBoundingClientRect().width);
    return () => ro.disconnect();
  }, []);

  useEffect(() => {
    if (!chartReady || !talibReady) {
      return;
    }
    const chart = chartRef.current;
    if (!chart) {
      return;
    }
    chart.setStyles(activeTheme);
    const indicatorTextColor = activeTheme === "dark" ? "#9ca3af" : "#64748b";
    chart.setStyles({
      candle: {
        priceMark: {
          high: { show: showHighValue },
          low: { show: showLowValue },
          last: { show: showLatestValue },
        },
      },
      grid: {
        show: showGridLine,
        horizontal: { show: showGridLine, style: LineType.Dashed },
        vertical: { show: showGridLine, style: LineType.Dashed },
      },
      indicator: {
        lastValueMark: {
          show: showIndicatorLastValue,
        },
        tooltip: {
          icons: createIndicatorTooltipIcons(indicatorTextColor),
        },
      },
    });
  }, [chartReady, activeTheme, showGridLine, showHighValue, showIndicatorLastValue, showLatestValue, showLowValue]);

  useEffect(() => {
    if (!chartReady) {
      return;
    }
    const chart = chartRef.current;
    if (!chart) {
      return;
    }
    chart.setStyles({
      candle: {
        type: candleType,
      },
    });
  }, [chartReady, candleType]);

  useEffect(() => {
    if (!chartReady) {
      return;
    }
    const chart = chartRef.current;
    if (!chart) {
      return;
    }
    chart.setStyles({
      yAxis: {
        type: yAxisType,
      },
    });
  }, [chartReady, yAxisType]);

  useEffect(() => {
    if (!chartReady) {
      return;
    }
    chartRef.current?.setLocale(language);
  }, [chartReady, language]);

  useEffect(() => {
    if (!chartReady) {
      return;
    }
    chartRef.current?.setTimezone(resolveTimezone(timezone));
  }, [chartReady, timezone]);

  useEffect(() => {
    if (!chartReady) {
      return;
    }
    const chart = chartRef.current;
    if (!chart) {
      return;
    }

    for (const indicatorName of MAIN_INDICATORS) {
      const indicatorKey = buildIndicatorKey("main", indicatorName);
      const hidden = hiddenIndicatorSet.has(indicatorKey);
      const shouldExist = mainIndicators.includes(indicatorName);
      const exists = Boolean(chart.getIndicatorByPaneId(CANDLE_PANE_ID, indicatorName));
      const params = indicatorParams[indicatorKey];
      const inputMap = indicatorInputMaps[indicatorKey];
      const createValue = buildIndicatorPayload(indicatorName, hidden, params, inputMap);
      if (shouldExist && !exists) {
        chart.createIndicator(createValue, true, { id: CANDLE_PANE_ID });
      }
      if (!shouldExist && exists) {
        chart.removeIndicator(CANDLE_PANE_ID, indicatorName);
      }
      if (shouldExist && exists) {
        const overrideValue = buildIndicatorPayload(indicatorName, hidden, params, inputMap);
        chart.overrideIndicator(overrideValue, CANDLE_PANE_ID);
      }
    }
  }, [chartReady, hiddenIndicatorSet, indicatorInputMaps, indicatorParams, mainIndicators, talibReady]);

  useEffect(() => {
    if (!chartReady || !talibReady) {
      return;
    }
    const chart = chartRef.current;
    if (!chart) {
      return;
    }

    const paneMap = subPaneIdMapRef.current;
    for (const [indicatorName, paneId] of Array.from(paneMap.entries())) {
      if (!subIndicators.includes(indicatorName)) {
        chart.removeIndicator(paneId);
        paneMap.delete(indicatorName);
      }
    }

    for (const indicatorName of subIndicators) {
      const indicatorKey = buildIndicatorKey("sub", indicatorName);
      const hidden = hiddenIndicatorSet.has(indicatorKey);
      const params = indicatorParams[indicatorKey];
      const inputMap = indicatorInputMaps[indicatorKey];
      const value = buildIndicatorPayload(indicatorName, hidden, params, inputMap);

      const paneId = paneMap.get(indicatorName);
      if (!paneId) {
        const createdPaneId = chart.createIndicator(value, false, { height: 100 });
        if (createdPaneId) {
          paneMap.set(indicatorName, createdPaneId);
        }
        continue;
      }

      const exists = Boolean(chart.getIndicatorByPaneId(paneId, indicatorName));
      if (!exists) {
        paneMap.delete(indicatorName);
        const recreatedPaneId = chart.createIndicator(value, false, { height: 100 });
        if (recreatedPaneId) {
          paneMap.set(indicatorName, recreatedPaneId);
        }
        continue;
      }

      chart.overrideIndicator(value, paneId);
    }
  }, [chartReady, hiddenIndicatorSet, indicatorInputMaps, indicatorParams, subIndicators, talibReady]);

  useEffect(() => {
    if (!chartReady) {
      return;
    }

    const chart = chartRef.current;
    const parsed = parseSymbolName(activeSymbol);
    const timeframe = toTimeframe(activeInterval);
    const intervalMs = toIntervalMs(activeInterval);

    if (!chart || !parsed || !timeframe || !intervalMs) {
      return;
    }

    const version = dataVersionRef.current + 1;
    dataVersionRef.current = version;

    let disposed = false;
    const controller = new AbortController();

    setLoading(true);
    setLoadError(null);
    setHoverBar(null);
    setLatestBar(null);
    lastBarRef.current = null;
    clearFocusOverlays();
    chart.clearData();

    const loadInitial = async () => {
      try {
        const bars = await fetchHistory({
          http: httpRef.current,
          exchange: parsed.exchange,
          symbol: parsed.symbol,
          timeframe,
          intervalMs,
          count: HISTORY_PAGE_SIZE,
          signal: controller.signal,
        });

        if (disposed || version !== dataVersionRef.current) {
          return;
        }

        chart.applyNewData(bars, true);
        if (bars.length > 0) {
          const lastBar = bars[bars.length - 1];
          lastBarRef.current = lastBar;
          setLatestBar(lastBar);
        }
        setHistoryNonce((value) => value + 1);
      } catch (error) {
        if (disposed || controller.signal.aborted) {
          return;
        }
        const message = error instanceof Error ? error.message : "鍔犺浇鍘嗗彶琛屾儏澶辫触";
        setLoadError(message || "鍔犺浇鍘嗗彶琛屾儏澶辫触");
      } finally {
        if (!disposed && version === dataVersionRef.current) {
          setLoading(false);
        }
      }
    };

    loadInitial();

    chart.setLoadDataCallback(async (params) => {
      if (disposed || version !== dataVersionRef.current) {
        params.callback([], false);
        return;
      }
      if (params.type !== LoadDataType.Forward) {
        params.callback([], false);
        return;
      }
      if (!params.data?.timestamp) {
        params.callback([], false);
        return;
      }

      const endTime = params.data.timestamp - intervalMs;
      if (!Number.isFinite(endTime) || endTime <= 0) {
        params.callback([], false);
        return;
      }

      try {
        const bars = await fetchHistory({
          http: httpRef.current,
          exchange: parsed.exchange,
          symbol: parsed.symbol,
          timeframe,
          intervalMs,
          count: HISTORY_PAGE_SIZE,
          endTime,
          signal: controller.signal,
        });

        if (disposed || version !== dataVersionRef.current) {
          params.callback([], false);
          return;
        }

        params.callback(bars, bars.length === HISTORY_PAGE_SIZE);
      } catch {
        params.callback([], false);
      }
    });

    unsubscribeRef.current?.();
    unsubscribeRef.current = subscribeMarket([parsed.symbol], (ticks) => {
      if (disposed || version !== dataVersionRef.current) {
        return;
      }
      for (const tick of ticks) {
        if (tick.symbol !== parsed.symbol) {
          continue;
        }
        const nextBar = updateBar(lastBarRef.current, intervalMs, tick.price, tick.ts);
        if (!nextBar) {
          continue;
        }
        lastBarRef.current = nextBar;
        chart.updateData(nextBar);
        setLatestBar(nextBar);
      }
    });

    return () => {
      disposed = true;
      controller.abort();
      unsubscribeRef.current?.();
      unsubscribeRef.current = null;
    };
  }, [chartReady, activeSymbol, activeInterval, clearFocusOverlays]);

  useEffect(() => {
    if (!chartReady) {
      return;
    }

    const chart = chartRef.current;
    if (!chart) {
      return;
    }

    if (!focusRange) {
      clearFocusOverlays();
      return;
    }

    const targetSymbol = focusRange.chartSymbol?.trim();
    if (targetSymbol && targetSymbol !== activeSymbol) {
      setActiveSymbol(targetSymbol);
      return;
    }

    const targetInterval = normalizeResolution(focusRange.chartInterval);
    if (targetInterval && targetInterval !== activeInterval) {
      setActiveInterval(targetInterval);
      return;
    }

    const startTime = Math.min(focusRange.startTime, focusRange.endTime);
    const endTime = Math.max(focusRange.startTime, focusRange.endTime);
    if (!Number.isFinite(startTime) || !Number.isFinite(endTime) || startTime <= 0 || endTime <= 0) {
      return;
    }

    clearFocusOverlays();

    const groupId = `focus-${focusRange.id}-${Date.now()}`;
    focusOverlayGroupIdRef.current = groupId;

    const [minPrice, maxPrice] = getFocusPriceBand(chart.getDataList(), focusRange);
    const side = focusRange.side?.trim().toLowerCase();
    const color = side === "long" ? "#16a34a" : side === "short" ? "#dc2626" : "#2563eb";

    safeCreateOverlay(chart, {
      name: "rect",
      groupId,
      points: [
        { timestamp: startTime, value: minPrice },
        { timestamp: endTime, value: maxPrice },
      ],
      styles: {
        polygon: {
          color: `${color}22`,
          borderColor: color,
          borderSize: 1,
          style: "stroke_fill",
        },
      },
    });

    safeCreateOverlay(chart, {
      name: "verticalStraightLine",
      groupId,
      points: [{ timestamp: startTime }],
      styles: { line: { color, size: 1 } },
    });

    safeCreateOverlay(chart, {
      name: "verticalStraightLine",
      groupId,
      points: [{ timestamp: endTime }],
      styles: { line: { color, size: 1 } },
    });

    const entryPrice = toFinitePrice(focusRange.entryPrice);
    const exitPrice = toFinitePrice(focusRange.exitPrice);
    const stopLossPrice = toFinitePrice(focusRange.stopLossPrice);
    const takeProfitPrice = toFinitePrice(focusRange.takeProfitPrice);

    if (entryPrice !== null) {
      safeCreateOverlay(chart, {
        name: "priceLine",
        groupId,
        points: [{ value: entryPrice }],
        extendData: "Entry",
      });
    }

    if (exitPrice !== null) {
      safeCreateOverlay(chart, {
        name: "priceLine",
        groupId,
        points: [{ value: exitPrice }],
        extendData: "Exit",
      });
    }

    if (stopLossPrice !== null) {
      safeCreateOverlay(chart, {
        name: "priceLine",
        groupId,
        points: [{ value: stopLossPrice }],
        extendData: "SL",
      });
    }

    if (takeProfitPrice !== null) {
      safeCreateOverlay(chart, {
        name: "priceLine",
        groupId,
        points: [{ value: takeProfitPrice }],
        extendData: "TP",
      });
    }

    const centerTimestamp = Math.floor((startTime + endTime) / 2);
    chart.scrollToTimestamp(centerTimestamp, 240);

    const intervalMs = toIntervalMs(activeInterval) ?? 60_000;
    const targetVisibleBars = clamp(Math.ceil((endTime - startTime) / intervalMs) + 20, 20, 600);
    const visibleRange = chart.getVisibleRange();
    const currentVisibleBars = Math.max(1, visibleRange.to - visibleRange.from);
    const scale = clamp(currentVisibleBars / targetVisibleBars, 0.2, 5);
    chart.zoomAtTimestamp(scale, centerTimestamp, 240);
  }, [chartReady, activeInterval, activeSymbol, clearFocusOverlays, focusRange, historyNonce]);

  const activeIndicatorChips = useMemo<IndicatorChip[]>(() => {
    const chips: IndicatorChip[] = mainIndicators.map((name) => {
      const key = buildIndicatorKey("main", name);
      return {
        key,
        name,
        pane: "main",
        hidden: hiddenIndicatorSet.has(key),
      };
    });

    for (const name of subIndicators) {
      const key = buildIndicatorKey("sub", name);
      chips.push({
        key,
        name,
        pane: "sub",
        hidden: hiddenIndicatorSet.has(key),
      });
    }

    return chips;
  }, [hiddenIndicatorSet, mainIndicators, subIndicators]);

  const hasInvalidEditingParams = useMemo(() => {
    if (!editingIndicator) {
      return false;
    }
    return editingIndicator.paramDefinitions.some((definition, index) => {
      const raw = editingIndicator.paramDrafts[index] ?? "";
      if (definition.type === "enum") {
        return !definition.enumOptions.some((option) => String(option.value) === raw);
      }
      if (raw.trim().length === 0) {
        return true;
      }
      return !Number.isFinite(Number(raw));
    });
  }, [editingIndicator]);

  const findIndicatorChipByTooltip = useCallback(
    (paneId?: string, indicatorName?: string): IndicatorChip | null => {
      if (!paneId || !indicatorName) {
        return null;
      }
      const pane: IndicatorPane = paneId === CANDLE_PANE_ID ? "main" : "sub";
      const key = buildIndicatorKey(pane, indicatorName);
      return activeIndicatorChips.find((chip) => chip.key === key) ?? null;
    },
    [activeIndicatorChips]
  );

  const toggleMainIndicator = useCallback((name: string) => {
    const key = buildIndicatorKey("main", name);
    setMainIndicators((prev) => {
      if (prev.includes(name)) {
        return prev.filter((item) => item !== name);
      }
      return [...prev, name];
    });
    setHiddenIndicatorKeys((prev) => prev.filter((item) => item !== key));
  }, []);

  const handleSetSubIndicator = useCallback((next: string) => {
    if (next === "NONE") {
      setSubIndicators([]);
      return;
    }
    setSubIndicators((prev) => {
      if (prev.includes(next)) {
        return prev.filter((name) => name !== next);
      }
      return [...prev, next];
    });
    const key = buildIndicatorKey("sub", next);
    setHiddenIndicatorKeys((prev) => prev.filter((item) => item !== key));
  }, []);

  const handleToggleIndicatorHidden = useCallback((chip: IndicatorChip) => {
    setHiddenIndicatorKeys((prev) => {
      if (prev.includes(chip.key)) {
        return prev.filter((item) => item !== chip.key);
      }
      return [...prev, chip.key];
    });
  }, []);

  const handleDeleteIndicator = useCallback((chip: IndicatorChip) => {
    setHiddenIndicatorKeys((prev) => prev.filter((item) => item !== chip.key));
    setIndicatorParams((prev) => {
      if (!(chip.key in prev)) {
        return prev;
      }
      const next = { ...prev };
      delete next[chip.key];
      return next;
    });
    setIndicatorInputMaps((prev) => {
      if (!(chip.key in prev)) {
        return prev;
      }
      const next = { ...prev };
      delete next[chip.key];
      return next;
    });
    if (chip.pane === "main") {
      setMainIndicators((prev) => prev.filter((name) => name !== chip.name));
      return;
    }
    setSubIndicators((prev) => prev.filter((name) => name !== chip.name));
  }, []);

  const openIndicatorSettings = useCallback(
    (chip: IndicatorChip) => {
      const chart = chartRef.current;
      const editorSchema = getTalibIndicatorEditorSchema(chip.name);
      const fallbackValues =
        indicatorParams[chip.key] ??
        editorSchema?.defaultParams ??
        TA_INDICATOR_DEFAULT_PARAMS[chip.name] ??
        INDICATOR_DEFAULT_PARAMS[chip.name] ??
        [];
      let values = [...fallbackValues];
      let inputMap = normalizeInputMapBySlots(editorSchema?.inputSlots ?? [], indicatorInputMaps[chip.key]);

      if (chart) {
        const paneId = chip.pane === "main" ? CANDLE_PANE_ID : subPaneIdMapRef.current.get(chip.name);
        if (paneId) {
          const indicator = chart.getIndicatorByPaneId(paneId, chip.name) as
            | { calcParams?: number[]; extendData?: unknown }
            | null;
          if (Array.isArray(indicator?.calcParams)) {
            values = [...indicator.calcParams];
          }
          if (editorSchema) {
            const fromExtendData = readInputMapFromExtendData(indicator?.extendData);
            inputMap = normalizeInputMapBySlots(editorSchema.inputSlots, fromExtendData ?? inputMap);
          }
        }
      }

      const paramDefinitions =
        editorSchema?.paramDefinitions.length && editorSchema.paramDefinitions.length > 0
          ? editorSchema.paramDefinitions
          : buildFallbackParamDefinitions(chip.name, values);
      const paramDrafts = paramDefinitions.map((definition, index) => {
        const raw = values[index];
        if (Number.isFinite(raw)) {
          return String(raw);
        }
        return String(definition.defaultValue);
      });
      const inputSlots = editorSchema?.inputSlots ?? [];
      const inputDrafts = inputSlots.map((slot) => inputMap[slot.key] ?? slot.defaultValue);

      setEditingIndicator({
        key: chip.key,
        name: chip.name,
        pane: chip.pane,
        paramDefinitions,
        paramDrafts,
        inputSlots,
        inputDrafts,
      });
    },
    [indicatorInputMaps, indicatorParams]
  );

  const handleEditIndicatorParam = useCallback((index: number, rawValue: string) => {
    setEditingIndicator((prev) => {
      if (!prev) {
        return null;
      }
      const nextValues = [...prev.paramDrafts];
      nextValues[index] = rawValue;
      return {
        ...prev,
        paramDrafts: nextValues,
      };
    });
  }, []);

  const handleEditIndicatorInput = useCallback((index: number, value: string) => {
    setEditingIndicator((prev) => {
      if (!prev) {
        return null;
      }
      const nextInputs = [...prev.inputDrafts];
      nextInputs[index] = value;
      return {
        ...prev,
        inputDrafts: nextInputs,
      };
    });
  }, []);

  const handleSaveIndicatorSettings = useCallback(() => {
    if (!editingIndicator) {
      return;
    }

    const values = editingIndicator.paramDefinitions.map((definition, index) =>
      resolveParamDraftValue(editingIndicator.paramDrafts[index] ?? "", definition)
    );

    const nextInputMap: IndicatorInputMap = {};
    for (let i = 0; i < editingIndicator.inputSlots.length; i += 1) {
      const slot = editingIndicator.inputSlots[i];
      const rawValue = editingIndicator.inputDrafts[i] ?? slot.defaultValue;
      const normalizedValue = normalizeTalibInputSource(rawValue);
      const hasOption = slot.options.some((option) => option.value === normalizedValue);
      nextInputMap[slot.key] = hasOption ? normalizedValue : slot.defaultValue;
    }

    setIndicatorParams((prev) => ({
      ...prev,
      [editingIndicator.key]: values,
    }));
    setIndicatorInputMaps((prev) => {
      if (Object.keys(nextInputMap).length === 0) {
        if (!(editingIndicator.key in prev)) {
          return prev;
        }
        const next = { ...prev };
        delete next[editingIndicator.key];
        return next;
      }
      return {
        ...prev,
        [editingIndicator.key]: nextInputMap,
      };
    });
    setEditingIndicator(null);
  }, [editingIndicator]);

  const handleNativeIndicatorIconClick = useCallback(
    (data?: unknown) => {
      const payload = data as { paneId?: string; indicatorName?: string; iconId?: string } | undefined;
      const chip = findIndicatorChipByTooltip(payload?.paneId, payload?.indicatorName);
      if (!chip || !payload?.iconId) {
        return;
      }

      if (payload.iconId === "indicator_hide") {
        handleToggleIndicatorHidden(chip);
        return;
      }
      if (payload.iconId === "indicator_settings") {
        openIndicatorSettings(chip);
        return;
      }
      if (payload.iconId === "indicator_delete") {
        handleDeleteIndicator(chip);
      }
    },
    [findIndicatorChipByTooltip, handleDeleteIndicator, handleToggleIndicatorHidden, openIndicatorSettings]
  );

  useEffect(() => {
    if (!chartReady) {
      return;
    }
    const chart = chartRef.current;
    if (!chart) {
      return;
    }
    chart.subscribeAction(ActionType.OnTooltipIconClick, handleNativeIndicatorIconClick);
    return () => {
      chart.unsubscribeAction(ActionType.OnTooltipIconClick, handleNativeIndicatorIconClick);
    };
  }, [chartReady, handleNativeIndicatorIconClick]);

  const handleStartDrawing = useCallback(
    (overlay: string) => {
      const chart = chartRef.current;
      if (!chart) {
        return;
      }
      const created = chart.createOverlay({
        name: overlay,
        lock: isDrawingLocked,
        visible: isDrawingVisible,
        mode: drawingMode,
      });
      for (const id of normalizeOverlayIds(created as string | null | Array<string | null>)) {
        drawingOverlayIdsRef.current.add(id);
      }
      setActiveDrawingTool(overlay);
    },
    [drawingMode, isDrawingLocked, isDrawingVisible]
  );

  const handleSelectDrawingTool = useCallback(
    (groupId: DrawingToolGroupId, toolId: string) => {
      const group = DRAWING_TOOL_GROUP_BY_ID[groupId];
      const tool = group.tools.find((item) => item.id === toolId) ?? group.tools[0];
      setSelectedDrawingTools((prev) => ({ ...prev, [groupId]: tool.id }));
      setExpandedDrawingMenu(null);
      handleStartDrawing(tool.overlay);
    },
    [handleStartDrawing]
  );

  const handleUseGroupDrawingTool = useCallback(
    (groupId: DrawingToolGroupId) => {
      const toolId = selectedDrawingTools[groupId] ?? DRAWING_GROUP_DEFAULT_TOOL_ID[groupId];
      handleSelectDrawingTool(groupId, toolId);
    },
    [handleSelectDrawingTool, selectedDrawingTools]
  );

  const updateDrawingListDirection = useCallback((menuId: ExpandableDrawingMenuId): void => {
    if (!drawingBarRef.current) {
      setDrawingListDirection("down");
      return;
    }
    const selector = `.list[data-menu-id="${menuId}"]`;
    const listElement = drawingBarRef.current.querySelector(selector);
    if (!(listElement instanceof HTMLElement)) {
      setDrawingListDirection("down");
      return;
    }
    const rect = listElement.getBoundingClientRect();
    if (rect.bottom > window.innerHeight - 8 && rect.top > 8) {
      setDrawingListDirection("up");
      return;
    }
    setDrawingListDirection("down");
  }, []);

  const handleToggleDrawingMenu = useCallback(
    (menuId: ExpandableDrawingMenuId) => {
      setExpandedDrawingMenu((prev) => {
        if (prev === menuId) {
          return null;
        }
        return menuId;
      });
      setDrawingListDirection("down");
    },
    []
  );

  useEffect(() => {
    if (!expandedDrawingMenu) {
      return;
    }
    const frameId = window.requestAnimationFrame(() => updateDrawingListDirection(expandedDrawingMenu));
    return () => window.cancelAnimationFrame(frameId);
  }, [expandedDrawingMenu, updateDrawingListDirection]);

  useEffect(() => {
    const handleOutsidePointer = (event: PointerEvent) => {
      if (!drawingBarRef.current) {
        return;
      }
      const target = event.target;
      if (target instanceof Node && !drawingBarRef.current.contains(target)) {
        setExpandedDrawingMenu(null);
      }
    };
    document.addEventListener("pointerdown", handleOutsidePointer);
    return () => {
      document.removeEventListener("pointerdown", handleOutsidePointer);
    };
  }, []);

  useEffect(() => {
    if (!chartReady) {
      return;
    }
    const chart = chartRef.current;
    if (!chart) {
      return;
    }
    for (const id of getTrackedOverlayIds()) {
      chart.overrideOverlay({
        id,
        mode: drawingMode,
        lock: isDrawingLocked,
        visible: isDrawingVisible,
      });
    }
  }, [chartReady, drawingMode, getTrackedOverlayIds, isDrawingLocked, isDrawingVisible]);

  const handleToggleMagnetEnabled = useCallback(() => {
    setIsMagnetEnabled((prev) => !prev);
  }, []);

  const handleSelectMagnetMode = useCallback((mode: MagnetMode) => {
    setMagnetMode(mode);
    setIsMagnetEnabled(true);
    setExpandedDrawingMenu(null);
  }, []);

  const handleToggleDrawingLock = useCallback(() => {
    setIsDrawingLocked((prev) => !prev);
  }, []);

  const handleToggleDrawingVisible = useCallback(() => {
    setIsDrawingVisible((prev) => !prev);
  }, []);

  const handleClearDrawings = useCallback(() => {
    chartRef.current?.removeOverlay();
    focusOverlayGroupIdRef.current = null;
    drawingOverlayIdsRef.current.clear();
    setIsDrawingVisible(true);
    setExpandedDrawingMenu(null);
  }, []);

  const selectedDrawingGroupTools = useMemo<Record<DrawingToolGroupId, DrawingToolItem>>(
    () => ({
      singleLine:
        DRAWING_TOOL_ITEM_BY_ID.get(selectedDrawingTools.singleLine) ??
        DRAWING_TOOL_GROUP_BY_ID.singleLine.tools[0],
      moreLine:
        DRAWING_TOOL_ITEM_BY_ID.get(selectedDrawingTools.moreLine) ?? DRAWING_TOOL_GROUP_BY_ID.moreLine.tools[0],
      polygon:
        DRAWING_TOOL_ITEM_BY_ID.get(selectedDrawingTools.polygon) ?? DRAWING_TOOL_GROUP_BY_ID.polygon.tools[0],
      fibonacci:
        DRAWING_TOOL_ITEM_BY_ID.get(selectedDrawingTools.fibonacci) ??
        DRAWING_TOOL_GROUP_BY_ID.fibonacci.tools[0],
      wave: DRAWING_TOOL_ITEM_BY_ID.get(selectedDrawingTools.wave) ?? DRAWING_TOOL_GROUP_BY_ID.wave.tools[0],
    }),
    [selectedDrawingTools]
  );

  const MagnetIcon = useMemo<ToolIconComponent>(() => {
    if (magnetMode === "strong") {
      return isMagnetEnabled ? MagnetStrongOnIcon : MagnetStrongOffIcon;
    }
    return isMagnetEnabled ? MagnetWeakOnIcon : MagnetWeakOffIcon;
  }, [isMagnetEnabled, magnetMode]);
  const DrawingLockIcon = isDrawingLocked ? DrawingLockOnIcon : DrawingLockOffIcon;
  const DrawingVisibilityIcon = isDrawingVisible ? DrawingHideAllIcon : DrawingShowAllIcon;

  useEffect(() => {
    const handleOutsidePointer = (event: PointerEvent) => {
      if (!toolbarRef.current) {
        return;
      }
      const target = event.target;
      if (target instanceof Node && !toolbarRef.current.contains(target)) {
        setActiveToolbarPanel(null);
        setIntervalMoreOpen(false);
        setRightMoreOpen(false);
      }
    };
    document.addEventListener("pointerdown", handleOutsidePointer);
    return () => {
      document.removeEventListener("pointerdown", handleOutsidePointer);
    };
  }, []);

  const handleToggleToolbarPanel = useCallback((panel: ToolbarPanelId) => {
    setActiveToolbarPanel((prev) => (prev === panel ? null : panel));
  }, []);

  const openIndicatorDialog = useCallback(() => {
    setIndicatorDialogOpen((prev) => !prev);
    setActiveToolbarPanel(null);
    setRightMoreOpen(false);
  }, []);

  const closeIndicatorDialog = useCallback(() => {
    setIndicatorDialogOpen(false);
  }, []);

  const handleSelectTimezone = useCallback((next: string) => {
    setTimezone(next);
    setActiveToolbarPanel(null);
  }, []);

  const handleToggleMainIndicatorFromPanel = useCallback(
    (name: string) => {
      toggleMainIndicator(name);
    },
    [toggleMainIndicator]
  );

  const handleSelectSubIndicatorFromPanel = useCallback(
    (next: string) => {
      handleSetSubIndicator(next);
    },
    [handleSetSubIndicator]
  );

  const handleSelectIndicatorFromDialog = useCallback(
    (name: string, pane: IndicatorPane) => {
      if (pane === "main") {
        handleToggleMainIndicatorFromPanel(name);
        return;
      }
      handleSelectSubIndicatorFromPanel(name);
    },
    [handleSelectSubIndicatorFromPanel, handleToggleMainIndicatorFromPanel]
  );

  const handleDisableSubIndicatorFromDialog = useCallback(() => {
    handleSelectSubIndicatorFromPanel("NONE");
  }, [handleSelectSubIndicatorFromPanel]);

  const handleToggleShowLatest = useCallback(() => {
    setShowLatestValue((prev) => !prev);
  }, []);

  const handleToggleShowHigh = useCallback(() => {
    setShowHighValue((prev) => !prev);
  }, []);

  const handleToggleShowLow = useCallback(() => {
    setShowLowValue((prev) => !prev);
  }, []);

  const handleToggleShowIndicatorLastValue = useCallback(() => {
    setShowIndicatorLastValue((prev) => !prev);
  }, []);

  const handleToggleShowGridLine = useCallback(() => {
    setShowGridLine((prev) => !prev);
  }, []);

  const handleScreenshot = useCallback(() => {
    const chart = chartRef.current;
    if (!chart) {
      return;
    }
    const url = chart.getConvertPictureUrl(true, "png");
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = `market-chart-${Date.now()}.png`;
    anchor.click();
  }, []);

  const handleToggleFullscreen = useCallback(async () => {
    const element = wrapperRef.current;
    if (!element) {
      return;
    }
    try {
      if (document.fullscreenElement === element) {
        await document.exitFullscreen();
      } else {
        await element.requestFullscreen();
      }
    } catch {
      // Ignore fullscreen exceptions in restricted environments.
    }
  }, []);

  return (
    <div
      ref={wrapperRef}
      className={`market-chart-shell ${activeTheme === "dark" ? "theme-dark" : "theme-light"} ${
        isFullscreen ? "is-fullscreen" : ""
      }`}
    >
      <div ref={toolbarRef} className="market-chart-toolbar market-chart-toolbar-unified">
        <div className="market-chart-toolbar-left">
          <select
            className="market-chart-select"
            value={activeSymbol}
            onChange={(event) => setActiveSymbol(event.target.value)}
          >
            {symbolOptions.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>

          <div className="market-chart-intervals">
            {selectedIntervalOptions.map((option) => (
              <button
                key={option.value}
                type="button"
                className={`market-chart-btn ${activeInterval === option.value ? "is-active" : ""}`}
                onClick={() => setActiveInterval(option.value)}
              >
                {option.label}
              </button>
            ))}
            <div className="market-chart-popover-wrap">
              <button
                type="button"
                className={`market-chart-btn market-chart-btn-period-trigger ${intervalMoreOpen ? "is-active" : ""}`}
                onClick={() => setIntervalMoreOpen((prev) => !prev)}
                title="选择周期"
              >
                周期
              </button>
              {intervalMoreOpen && (
                <div className="market-chart-popover-panel market-chart-period-selector">
                  <div className="market-chart-period-selector-title">选择周期</div>
                  {INTERVAL_CATEGORIES.map((cat) => (
                    <div key={cat.id} className="market-chart-period-category">
                      <span className="market-chart-period-category-label">{cat.label}</span>
                      <div className="market-chart-period-category-options">
                        {cat.options.map((opt) => {
                          const selected = selectedIntervals.includes(opt.value);
                          return (
                            <button
                              key={opt.value}
                              type="button"
                              className={`market-chart-period-option ${selected ? "is-selected" : ""}`}
                              onClick={() => {
                                if (selected) {
                                  handleToggleIntervalSelection(opt.value);
                                } else {
                                  if (selectedIntervals.length >= MAX_SELECTED_INTERVALS) return;
                                  handleToggleIntervalSelection(opt.value);
                                  setActiveInterval(opt.value);
                                }
                              }}
                            >
                              {opt.label}
                            </button>
                          );
                        })}
                      </div>
                    </div>
                  ))}
                  {selectedIntervals.length >= MAX_SELECTED_INTERVALS && (
                    <div className="market-chart-period-hint">最多选择五个</div>
                  )}
                </div>
              )}
            </div>
          </div>

        </div>

        <div className="market-chart-toolbar-right">
          {toolbarRightCollapsed ? (
            <div className="market-chart-popover-wrap">
              <button
                type="button"
                className={`market-chart-btn market-chart-btn-more ${rightMoreOpen ? "is-active" : ""}`}
                onClick={() => setRightMoreOpen((prev) => !prev)}
              >
                更多
              </button>
              {rightMoreOpen && (
                <div className="market-chart-popover-panel market-chart-popover-panel-sm market-chart-right-more">
                  <button
                    type="button"
                    className={`market-chart-btn ${indicatorDialogOpen ? "is-active" : ""}`}
                    onClick={() => {
                      openIndicatorDialog();
                    }}
                  >
                    指标
                  </button>
                  <button
                    type="button"
                    className={`market-chart-btn ${activeToolbarPanel === "timezone" ? "is-active" : ""}`}
                    onClick={() => {
                      handleToggleToolbarPanel("timezone");
                      setRightMoreOpen(false);
                    }}
                  >
                    时区
                  </button>
                  <button
                    type="button"
                    className={`market-chart-btn ${activeToolbarPanel === "settings" ? "is-active" : ""}`}
                    onClick={() => {
                      handleToggleToolbarPanel("settings");
                      setRightMoreOpen(false);
                    }}
                  >
                    设置
                  </button>
                  <button
                    type="button"
                    className="market-chart-btn"
                    onClick={() => {
                      handleScreenshot();
                      setRightMoreOpen(false);
                    }}
                  >
                    截图
                  </button>
                  <button
                    type="button"
                    className="market-chart-btn"
                    onClick={() => {
                      handleToggleFullscreen();
                      setRightMoreOpen(false);
                    }}
                  >
                    {isFullscreen ? "退出全屏" : "全屏"}
                  </button>
                </div>
              )}
            </div>
          ) : (
            <>
              <div className="market-chart-popover-wrap">
                <button
                  type="button"
                  className={`market-chart-btn ${indicatorDialogOpen ? "is-active" : ""}`}
                  onClick={openIndicatorDialog}
                >
                  指标
                </button>
              </div>

              <div className="market-chart-popover-wrap">
                <button
                  type="button"
                  className={`market-chart-btn ${activeToolbarPanel === "timezone" ? "is-active" : ""}`}
                  onClick={() => handleToggleToolbarPanel("timezone")}
                >
                  时区
                </button>
              </div>

              <div className="market-chart-popover-wrap">
                <button
                  type="button"
                  className={`market-chart-btn ${activeToolbarPanel === "settings" ? "is-active" : ""}`}
                  onClick={() => handleToggleToolbarPanel("settings")}
                >
                  设置
                </button>
              </div>

              <button type="button" className="market-chart-btn" onClick={handleScreenshot}>
                截图
              </button>
              <button type="button" className="market-chart-btn" onClick={handleToggleFullscreen}>
                {isFullscreen ? "退出全屏" : "全屏"}
              </button>
            </>
          )}

          <div className="market-chart-popover-wrap market-chart-panel-anchor">
            {activeToolbarPanel === "timezone" && (
              <div className="market-chart-popover-panel market-chart-popover-panel-sm">
                <div className="market-chart-panel-list">
                  {TIMEZONE_OPTIONS.map((option) => (
                    <button
                      key={option.value}
                      type="button"
                      className={`market-chart-btn ${timezone === option.value ? "is-active" : ""}`}
                      onClick={() => handleSelectTimezone(option.value)}
                    >
                      {option.label}
                    </button>
                  ))}
                </div>
              </div>
            )}
            {activeToolbarPanel === "settings" && (
              <div className="market-chart-popover-panel market-chart-popover-panel-settings">
                <div className="market-chart-panel-section">
                  <div className="market-chart-panel-title">显示设置</div>
                  <div className="market-chart-toggle-list">
                    <div className="market-chart-toggle-row" onClick={handleToggleShowLatest} role="button" tabIndex={0}>
                      <span className="market-chart-toggle-label">最新价</span>
                      <button
                        type="button"
                        role="switch"
                        aria-checked={showLatestValue}
                        className={`market-chart-toggle ${showLatestValue ? "is-on" : ""}`}
                        onClick={(e) => {
                          e.stopPropagation();
                          handleToggleShowLatest();
                        }}
                      >
                        <span className="market-chart-toggle-thumb" />
                      </button>
                    </div>
                    <div className="market-chart-toggle-row" onClick={handleToggleShowHigh} role="button" tabIndex={0}>
                      <span className="market-chart-toggle-label">最高价</span>
                      <button
                        type="button"
                        role="switch"
                        aria-checked={showHighValue}
                        className={`market-chart-toggle ${showHighValue ? "is-on" : ""}`}
                        onClick={(e) => {
                          e.stopPropagation();
                          handleToggleShowHigh();
                        }}
                      >
                        <span className="market-chart-toggle-thumb" />
                      </button>
                    </div>
                    <div className="market-chart-toggle-row" onClick={handleToggleShowLow} role="button" tabIndex={0}>
                      <span className="market-chart-toggle-label">最低价</span>
                      <button
                        type="button"
                        role="switch"
                        aria-checked={showLowValue}
                        className={`market-chart-toggle ${showLowValue ? "is-on" : ""}`}
                        onClick={(e) => {
                          e.stopPropagation();
                          handleToggleShowLow();
                        }}
                      >
                        <span className="market-chart-toggle-thumb" />
                      </button>
                    </div>
                    <div
                      className="market-chart-toggle-row"
                      onClick={handleToggleShowIndicatorLastValue}
                      role="button"
                      tabIndex={0}
                    >
                      <span className="market-chart-toggle-label">指标最新值</span>
                      <button
                        type="button"
                        role="switch"
                        aria-checked={showIndicatorLastValue}
                        className={`market-chart-toggle ${showIndicatorLastValue ? "is-on" : ""}`}
                        onClick={(e) => {
                          e.stopPropagation();
                          handleToggleShowIndicatorLastValue();
                        }}
                      >
                        <span className="market-chart-toggle-thumb" />
                      </button>
                    </div>
                    <div className="market-chart-toggle-row" onClick={handleToggleShowGridLine} role="button" tabIndex={0}>
                      <span className="market-chart-toggle-label">网格线</span>
                      <button
                        type="button"
                        role="switch"
                        aria-checked={showGridLine}
                        className={`market-chart-toggle ${showGridLine ? "is-on" : ""}`}
                        onClick={(e) => {
                          e.stopPropagation();
                          handleToggleShowGridLine();
                        }}
                      >
                        <span className="market-chart-toggle-thumb" />
                      </button>
                    </div>
                  </div>
                </div>
                <div className="market-chart-panel-section">
                  <div className="market-chart-panel-title">蜡烛图类型</div>
                  <div className="market-chart-panel-grid">
                    {CANDLE_TYPE_OPTIONS.map((option) => (
                      <button
                        key={option.value}
                        type="button"
                        className={`market-chart-btn ${candleType === option.value ? "is-active" : ""}`}
                        onClick={() => setCandleType(option.value)}
                      >
                        {option.label}
                      </button>
                    ))}
                  </div>
                </div>
                <div className="market-chart-panel-section">
                  <div className="market-chart-panel-title">价格轴类型</div>
                  <div className="market-chart-panel-grid">
                    {Y_AXIS_OPTIONS.map((option) => (
                      <button
                        key={option.value}
                        type="button"
                        className={`market-chart-btn ${yAxisType === option.value ? "is-active" : ""}`}
                        onClick={() => setYAxisType(option.value)}
                      >
                        {option.label}
                      </button>
                    ))}
                  </div>
                </div>
              </div>
            )}
          </div>
        </div>
      </div>

      <div className="market-chart-body" style={{ height }}>
        <div ref={drawingBarRef} className="klinecharts-pro-drawing-bar">
          {DRAWING_TOOL_GROUPS.map((group) => {
            const selectedTool = selectedDrawingGroupTools[group.id];
            const ToolIcon = selectedTool.icon;
            const expanded = expandedDrawingMenu === group.id;
            return (
              <div key={group.id} className={`item ${expanded ? "is-expanded" : ""}`}>
                <span
                  className={`icon-overlay ${activeDrawingTool === selectedTool.overlay ? "selected" : ""}`}
                  title={selectedTool.label}
                  onClick={() => handleUseGroupDrawingTool(group.id)}
                >
                  <ToolIcon />
                </span>
                <button
                  type="button"
                  className="icon-arrow"
                  aria-label={`展开${group.label}`}
                  onClick={() => handleToggleDrawingMenu(group.id)}
                >
                  <DrawingMenuArrow expanded={expanded} />
                </button>
                {expanded && (
                  <ul className={`list ${drawingListDirection === "up" ? "is-up" : ""}`} data-menu-id={group.id}>
                    {group.tools.map((tool) => {
                      const ListIcon = tool.icon;
                      const selected = activeDrawingTool === tool.overlay;
                      return (
                        <li key={tool.id} className="list-item" onClick={() => handleSelectDrawingTool(group.id, tool.id)}>
                          <span className={`icon-overlay ${selected ? "selected" : ""}`}>
                            <ListIcon />
                          </span>
                          <span className="list-label">{tool.label}</span>
                        </li>
                      );
                    })}
                  </ul>
                )}
              </div>
            );
          })}

          <span className="split-line" />

          <div className={`item ${expandedDrawingMenu === "magnet" ? "is-expanded" : ""}`}>
            <span
              className={`icon-overlay ${isMagnetEnabled ? "selected" : ""}`}
              title="磁吸"
              onClick={handleToggleMagnetEnabled}
            >
              <MagnetIcon />
            </span>
            <button
              type="button"
              className="icon-arrow"
              aria-label="展开磁吸模式"
              onClick={() => handleToggleDrawingMenu("magnet")}
            >
              <DrawingMenuArrow expanded={expandedDrawingMenu === "magnet"} />
            </button>
            {expandedDrawingMenu === "magnet" && (
              <ul className={`list ${drawingListDirection === "up" ? "is-up" : ""}`} data-menu-id="magnet">
                {MAGNET_OPTIONS.map((option) => {
                  const OptionIcon = option.icon;
                  const selected = magnetMode === option.mode;
                  return (
                    <li key={option.mode} className="list-item" onClick={() => handleSelectMagnetMode(option.mode)}>
                      <span className={`icon-overlay ${selected ? "selected" : ""}`}>
                        <OptionIcon />
                      </span>
                      <span className="list-label">{option.label}</span>
                    </li>
                  );
                })}
              </ul>
            )}
          </div>

          <div className="item">
            <span
              className={`icon-overlay ${isDrawingLocked ? "selected" : ""}`}
              title={isDrawingLocked ? "解锁绘图" : "锁定绘图"}
              onClick={handleToggleDrawingLock}
            >
              <DrawingLockIcon />
            </span>
          </div>

          <div className="item">
            <span
              className={`icon-overlay ${!isDrawingVisible ? "selected" : ""}`}
              title={isDrawingVisible ? "隐藏全部绘图" : "显示全部绘图"}
              onClick={handleToggleDrawingVisible}
            >
              <DrawingVisibilityIcon />
            </span>
          </div>

          <div className="item">
            <span className="icon-overlay" title="清空绘图" onClick={handleClearDrawings}>
              <DrawingClearAllIcon />
            </span>
          </div>
        </div>

        <div className="market-chart-stage">
          <div ref={containerRef} className="market-chart-container" />
          {loading && <div className="market-chart-state">鍔犺浇鍘嗗彶琛屾儏...</div>}
          {!loading && loadError && <div className="market-chart-state is-error">{loadError}</div>}
        </div>
      </div>

      <Dialog
        open={indicatorDialogOpen}
        onClose={closeIndicatorDialog}
        title="指标选择"
        cancelText="关闭"
        className="market-chart-indicator-dialog"
      >
        <div className="market-chart-indicator-picker">
          <div className="market-chart-indicator-picker-toolbar">
            <input
              type="text"
              className="market-chart-indicator-search"
              value={indicatorSearchKeyword}
              onChange={(event) => setIndicatorSearchKeyword(event.target.value)}
              placeholder="搜索指标名称 / 代码（如 MA, RSI, CDL）"
            />
            <span className="market-chart-indicator-picker-stat">
              {filteredIndicatorGroups.reduce((sum, [, items]) => sum + items.length, 0)} 个
            </span>
          </div>

          <div className="market-chart-indicator-filter-row">
            <button
              type="button"
              className={`market-chart-btn ${indicatorPaneFilter === "all" ? "is-active" : ""}`}
              onClick={() => setIndicatorPaneFilter("all")}
            >
              全部
            </button>
            <button
              type="button"
              className={`market-chart-btn ${indicatorPaneFilter === "main" ? "is-active" : ""}`}
              onClick={() => setIndicatorPaneFilter("main")}
            >
              主图
            </button>
            <button
              type="button"
              className={`market-chart-btn ${indicatorPaneFilter === "sub" ? "is-active" : ""}`}
              onClick={() => setIndicatorPaneFilter("sub")}
            >
              副图
            </button>
            <button
              type="button"
              className={`market-chart-btn ${subIndicators.length === 0 ? "is-active" : ""}`}
              onClick={handleDisableSubIndicatorFromDialog}
            >
              关闭副图
            </button>
          </div>

          <div className="market-chart-indicator-filter-row market-chart-indicator-filter-row-groups">
            <button
              type="button"
              className={`market-chart-btn ${indicatorGroupFilter === "ALL" ? "is-active" : ""}`}
              onClick={() => setIndicatorGroupFilter("ALL")}
            >
              全部分类
            </button>
            {indicatorGroupOptions.map((group) => (
              <button
                key={group}
                type="button"
                className={`market-chart-btn ${indicatorGroupFilter === group ? "is-active" : ""}`}
                onClick={() => setIndicatorGroupFilter(group)}
              >
                {group}
              </button>
            ))}
          </div>

          <div className="market-chart-indicator-list">
            {!talibReady && <div className="market-chart-indicator-empty">指标初始化中...</div>}
            {talibReady && filteredIndicatorGroups.length === 0 && (
              <div className="market-chart-indicator-empty">未找到匹配指标</div>
            )}
            {talibReady &&
              filteredIndicatorGroups.map(([group, items]) => (
                <div key={group} className="market-chart-indicator-group">
                  <div className="market-chart-indicator-group-header">
                    <span>{group}</span>
                    <span>{items.length}</span>
                  </div>
                  <div className="market-chart-indicator-grid">
                    {items.map((item) => {
                      const active =
                        item.pane === "main"
                          ? mainIndicators.includes(item.name)
                          : subIndicators.includes(item.name);
                      return (
                        <button
                          key={item.name}
                          type="button"
                          className={`market-chart-indicator-item ${active ? "is-active" : ""}`}
                          onClick={() => handleSelectIndicatorFromDialog(item.name, item.pane)}
                        >
                          <span className="market-chart-indicator-item-name">
                            {normalizeIndicatorName(item.name)}
                          </span>
                          <span className="market-chart-indicator-item-meta">
                            {item.pane === "main" ? "主图" : "副图"}
                          </span>
                        </button>
                      );
                    })}
                  </div>
                </div>
              ))}
          </div>
        </div>
      </Dialog>

      {editingIndicator && (
        <div className="market-chart-modal-mask" onClick={() => setEditingIndicator(null)}>
          <div className="market-chart-modal" onClick={(event) => event.stopPropagation()}>
            <div className="market-chart-modal-title">
              Indicator Settings - {editingIndicator.pane === "main" ? "Main" : "Sub"}{" "}
              {normalizeIndicatorName(editingIndicator.name)}
            </div>

            {editingIndicator.inputSlots.length > 0 && (
              <div className="market-chart-modal-form">
                <div className="market-chart-modal-section-title">Input Source</div>
                {editingIndicator.inputSlots.map((slot, index) => (
                  <label key={`${editingIndicator.key}-input-${slot.key}`} className="market-chart-modal-field">
                    <span>{slot.label}</span>
                    <select
                      value={editingIndicator.inputDrafts[index] ?? slot.defaultValue}
                      onChange={(event) => handleEditIndicatorInput(index, event.target.value)}
                    >
                      {slot.options.map((option) => (
                        <option key={`${slot.key}-${option.value}`} value={option.value}>
                          {option.label}
                        </option>
                      ))}
                    </select>
                  </label>
                ))}
              </div>
            )}

            {editingIndicator.paramDefinitions.length === 0 ? (
              <div className="market-chart-modal-empty">No editable parameters for this indicator.</div>
            ) : (
              <div className="market-chart-modal-form">
                <div className="market-chart-modal-section-title">Parameters</div>
                {editingIndicator.paramDefinitions.map((definition, index) => (
                  <label key={`${editingIndicator.key}-param-${definition.key}-${index}`} className="market-chart-modal-field">
                    <span>{definition.label}</span>
                    {definition.type === "enum" ? (
                      <select
                        value={editingIndicator.paramDrafts[index] ?? String(definition.defaultValue)}
                        onChange={(event) => handleEditIndicatorParam(index, event.target.value)}
                      >
                        {definition.enumOptions.map((option) => (
                          <option key={`${definition.key}-${option.value}`} value={String(option.value)}>
                            {option.description ? `${option.label} - ${option.description}` : option.label}
                          </option>
                        ))}
                      </select>
                    ) : (
                      <input
                        type="number"
                        value={editingIndicator.paramDrafts[index] ?? String(definition.defaultValue)}
                        step={definition.valueType === "double" ? "0.01" : "1"}
                        onChange={(event) => handleEditIndicatorParam(index, event.target.value)}
                      />
                    )}
                  </label>
                ))}
              </div>
            )}

            <div className="market-chart-modal-actions">
              <button type="button" className="market-chart-btn" onClick={() => setEditingIndicator(null)}>
                Cancel
              </button>
              <button
                type="button"
                className="market-chart-btn is-active"
                onClick={handleSaveIndicatorSettings}
                disabled={hasInvalidEditingParams}
              >
                Save
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default MarketChart;

