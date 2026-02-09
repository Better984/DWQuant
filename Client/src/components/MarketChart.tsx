import React, { useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import HorizontalLineIcon from "../assets/KLineCharts/01_horizontal_line.svg?react";
import HorizontalRayIcon from "../assets/KLineCharts/02_horizontal_ray.svg?react";
import TrendLineIcon from "../assets/KLineCharts/03_trend_line.svg?react";
import VerticalLineIcon from "../assets/KLineCharts/04_vertical_line.svg?react";
import VerticalRayIcon from "../assets/KLineCharts/05_vertical_ray.svg?react";
import VerticalSegmentIcon from "../assets/KLineCharts/06_vertical_segment.svg?react";
import ExtendedLineIcon from "../assets/KLineCharts/07_extended_line.svg?react";
import RayIcon from "../assets/KLineCharts/08_ray.svg?react";
import SegmentIcon from "../assets/KLineCharts/09_segment.svg?react";
import PriceLineIcon from "../assets/KLineCharts/10_price_line.svg?react";
import RectangleIcon from "../assets/KLineCharts/01_rectangle.svg?react";
import CircleCenterRightIcon from "../assets/KLineCharts/02_circle_center_right.svg?react";
import TriangleIcon from "../assets/KLineCharts/03_triangle.svg?react";
import ParallelogramIcon from "../assets/KLineCharts/04_parallelogram.svg?react";
import ParallelLinesIcon from "../assets/KLineCharts/05_parallel_lines.svg?react";
import PriceChannelIcon from "../assets/KLineCharts/06_price_channel.svg?react";
// 斐波那契工具与江恩箱图标（11~17）
import FibonacciLineIcon from "../assets/KLineCharts/11_fibonacci_retracement_line.svg?react";
import FibonacciSegmentIcon from "../assets/KLineCharts/12_fibonacci_retracement_segment.svg?react";
import FibonacciCircleIcon from "../assets/KLineCharts/13_fibonacci_circle.svg?react";
import FibonacciSpiralIcon from "../assets/KLineCharts/14_fibonacci_spiral.svg?react";
import FibonacciSpeedFanIcon from "../assets/KLineCharts/15_fibonacci_speed_resistance_fan.svg?react";
import FibonacciExtensionIcon from "../assets/KLineCharts/16_fibonacci_extension.svg?react";
import GannBoxIcon from "../assets/KLineCharts/17_gann_box.svg?react";
// 波浪形态工具图标（18~23）
import WaveXabcdIcon from "../assets/KLineCharts/18_wave_xabcd.svg?react";
import WaveAbcdIcon from "../assets/KLineCharts/19_wave_abcd.svg?react";
import WaveThreeIcon from "../assets/KLineCharts/20_wave_three.svg?react";
import WaveFiveIcon from "../assets/KLineCharts/21_wave_five.svg?react";
import WaveEightIcon from "../assets/KLineCharts/22_wave_eight.svg?react";
import WaveAnyIcon from "../assets/KLineCharts/23_wave_any.svg?react";
import MagnetWeakOnIcon from "../assets/KLineCharts/magnet-weak-on.svg?react";
import MagnetWeakOffIcon from "../assets/KLineCharts/magnet-weak-off.svg?react";
import MagnetStrongOnIcon from "../assets/KLineCharts/magnet-strong-on.svg?react";
import MagnetStrongOffIcon from "../assets/KLineCharts/magnet-strong-off.svg?react";
import DrawingLockOffIcon from "../assets/KLineCharts/drawing-lock-off.svg?react";
import DrawingLockOnIcon from "../assets/KLineCharts/drawing-lock-on.svg?react";
import DrawingShowAllIcon from "../assets/KLineCharts/drawing-show-all.svg?react";
import DrawingHideAllIcon from "../assets/KLineCharts/drawing-hide-all.svg?react";
import DrawingClearAllIcon from "../assets/KLineCharts/drawing-clear-all.svg?react";
import {
  DomPosition,
  dispose,
  getSupportedIndicators,
  init,
  LoadDataType,
  registerYAxis,
  type Chart,
  type Coordinate,
  type KLineData,
} from "klinecharts";
import { HttpClient, subscribeMarket } from "../network";
import { registerCustomOverlays } from "./customOverlays";
import "./MarketChart.css";

// 注册自定义绘图工具
registerCustomOverlays();

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

type IndicatorOption = {
  label: string;
  /** klinecharts 内部指标名称 */
  value: string;
  /** 是否叠加在同一 pane 上 */
  stack: boolean;
  /** 使用的 pane 类型，用于区分主图 / 副图 */
  pane?: "main" | "sub";
  /** 指标中文描述，用于弹窗展示 */
  description?: string;
};

type IconAsset = React.ComponentType<React.SVGProps<SVGSVGElement>> | string;

const DEFAULT_SYMBOL = "Binance:BTC/USDT";
const DEFAULT_INTERVAL = "1";
const DEFAULT_COUNT = 500;
const CANDLE_PANE_ID = "candle_pane";
const DEBUG_KLINE = import.meta.env.DEV;

const PERIOD_TABS = [
  { label: "1m", value: "1" },
  { label: "5m", value: "5" },
  { label: "15m", value: "15" },
  { label: "1H", value: "60" },
  { label: "2H", value: "120", disabled: true },
  { label: "4H", value: "240" },
  { label: "D", value: "D" },
  { label: "W", value: "W" },
  { label: "M", value: "M", disabled: true },
  { label: "Y", value: "Y", disabled: true },
];

const INDICATOR_OPTIONS: IndicatorOption[] = [
  // 主图指标
  { label: "MA", value: "MA", stack: true, pane: "main", description: "移动平均线" },
  { label: "EMA", value: "EMA", stack: true, pane: "main", description: "指数平滑移动平均线" },
  { label: "SMA", value: "SMA", stack: true, pane: "main", description: "平滑移动平均线" },
  { label: "BOLL", value: "BOLL", stack: true, pane: "main", description: "布林线" },
  { label: "SAR", value: "SAR", stack: true, pane: "main", description: "停损点指向指标" },
  { label: "BBI", value: "BBI", stack: true, pane: "main", description: "多空指数" },
  // 副图指标
  { label: "MA", value: "MA", stack: false, pane: "sub", description: "移动平均线" },
  { label: "EMA", value: "EMA", stack: false, pane: "sub", description: "指数平滑移动平均线" },
  { label: "VOL", value: "VOL", stack: false, pane: "sub", description: "成交量" },
  { label: "MACD", value: "MACD", stack: false, pane: "sub", description: "指数平滑异同平均线" },
  { label: "BOLL", value: "BOLL", stack: false, pane: "sub", description: "布林线" },
  { label: "KDJ", value: "KDJ", stack: false, pane: "sub", description: "随机指标" },
  { label: "RSI", value: "RSI", stack: false, pane: "sub", description: "相对强弱指标" },
  { label: "BIAS", value: "BIAS", stack: false, pane: "sub", description: "乖离率" },
  { label: "BRAR", value: "BRAR", stack: false, pane: "sub", description: "情绪指标" },
  { label: "CCI", value: "CCI", stack: false, pane: "sub", description: "顺势指标" },
  { label: "DMI", value: "DMI", stack: false, pane: "sub", description: "动向指标" },
  { label: "CR", value: "CR", stack: false, pane: "sub", description: "能量指标" },
  { label: "PSY", value: "PSY", stack: false, pane: "sub", description: "心理线" },
  { label: "DMA", value: "DMA", stack: false, pane: "sub", description: "平行线差指标" },
  { label: "TRIX", value: "TRIX", stack: false, pane: "sub", description: "三重指数平滑平均线" },
  { label: "OBV", value: "OBV", stack: false, pane: "sub", description: "能量潮指标" },
  { label: "VR", value: "VR", stack: false, pane: "sub", description: "成交量变异率" },
  { label: "WR", value: "WR", stack: false, pane: "sub", description: "威廉指标" },
  { label: "MTM", value: "MTM", stack: false, pane: "sub", description: "动量指标" },
  { label: "EMV", value: "EMV", stack: false, pane: "sub", description: "简易波动指标" },
  { label: "SAR", value: "SAR", stack: false, pane: "sub", description: "停损点指向指标" },
  { label: "SMA", value: "SMA", stack: false, pane: "sub", description: "平滑移动平均线" },
  { label: "ROC", value: "ROC", stack: false, pane: "sub", description: "变动率指标" },
  { label: "PVT", value: "PVT", stack: false, pane: "sub", description: "价量趋势指标" },
  { label: "BBI", value: "BBI", stack: false, pane: "sub", description: "多空指数" },
  { label: "AO", value: "AO", stack: false, pane: "sub", description: "动量震荡指标" },
];

// 工具栏分组配置 - 完全匹配 KlineCharts Pro
const DRAWING_TOOL_GROUPS = [
  // 1. 线段工具
  {
    id: "singleLine",
    label: "线段工具",
    defaultIcon: "horizontalStraightLine",
    tools: [
      { id: "horizontalStraightLine", label: "水平直线", overlay: "horizontalStraightLine" },
      { id: "horizontalRayLine", label: "水平射线", overlay: "horizontalRayLine" },
      { id: "horizontalSegment", label: "水平线段", overlay: "horizontalSegment" },
      { id: "verticalStraightLine", label: "垂直直线", overlay: "verticalStraightLine" },
      { id: "verticalRayLine", label: "垂直射线", overlay: "verticalRayLine" },
      { id: "verticalSegment", label: "垂直线段", overlay: "verticalSegment" },
      { id: "straightLine", label: "直线", overlay: "straightLine" },
      { id: "rayLine", label: "射线", overlay: "rayLine" },
      { id: "segment", label: "线段", overlay: "segment" },
      { id: "priceLine", label: "价格线", overlay: "priceLine" },
    ],
  },
  // 2. 通道工具
  {
    id: "moreLine",
    label: "通道工具",
    defaultIcon: "priceChannelLine",
    tools: [
      { id: "priceChannelLine", label: "价格通道线", overlay: "priceChannelLine" },
      { id: "parallelStraightLine", label: "平行直线", overlay: "parallelStraightLine" },
    ],
  },
  // 3. 绘图（形状）
  {
    id: "polygon",
    label: "绘图",
    defaultIcon: "circle",
    tools: [
      { id: "circle", label: "圆", overlay: "circle" },
      { id: "rect", label: "矩形", overlay: "rect" },
      { id: "parallelogram", label: "平行四边形", overlay: "parallelogram" },
      { id: "triangle", label: "三角形", overlay: "triangle" },
    ],
  },
  // 4. 绘图（斐波那契）
  {
    id: "fibonacci",
    label: "绘图",
    defaultIcon: "fibonacciLine",
    tools: [
      { id: "fibonacciLine", label: "斐波那契回调直线", overlay: "fibonacciLine" },
      { id: "fibonacciSegment", label: "斐波那契回调线段", overlay: "fibonacciSegment" },
      { id: "fibonacciCircle", label: "斐波那契圆环", overlay: "fibonacciCircle" },
      { id: "fibonacciSpiral", label: "斐波那契螺旋", overlay: "fibonacciSpiral" },
      { id: "fibonacciSpeedResistanceFan", label: "斐波那契速度阻力扇", overlay: "fibonacciSpeedResistanceFan" },
      { id: "fibonacciExtension", label: "斐波那契趋势扩展", overlay: "fibonacciExtension" },
      { id: "gannBox", label: "江恩箱", overlay: "gannBox" },
    ],
  },
  // 5. 形态（波浪）
  {
    id: "wave",
    label: "形态",
    defaultIcon: "xabcd",
    tools: [
      { id: "xabcd", label: "XABCD形态", overlay: "xabcd" },
      { id: "abcd", label: "ABCD形态", overlay: "abcd" },
      { id: "threeWaves", label: "三浪", overlay: "threeWaves" },
      { id: "fiveWaves", label: "五浪", overlay: "fiveWaves" },
      { id: "eightWaves", label: "八浪", overlay: "eightWaves" },
      { id: "anyWaves", label: "任意浪", overlay: "anyWaves" },
    ],
  },
];

const TIMEZONE_OPTIONS = [
  { label: "本地（自动）", value: "local" },
  { label: "世界统一时间", value: "UTC" },
  { label: "(UTC-10) 檀香山", value: "Pacific/Honolulu" },
  { label: "(UTC-8) 朱诺", value: "America/Juneau" },
  { label: "(UTC-7) 洛杉矶", value: "America/Los_Angeles" },
  { label: "(UTC-5) 芝加哥", value: "America/Chicago" },
  { label: "(UTC-4) 多伦多", value: "America/Toronto" },
  { label: "(UTC-3) 圣保罗", value: "America/Sao_Paulo" },
  { label: "(UTC+1) 伦敦", value: "Europe/London" },
  { label: "(UTC+2) 柏林", value: "Europe/Berlin" },
  { label: "(UTC+3) 巴林", value: "Asia/Bahrain" },
  { label: "(UTC+4) 迪拜", value: "Asia/Dubai" },
  { label: "(UTC+5) 阿什哈巴德", value: "Asia/Ashgabat" },
  { label: "(UTC+6) 阿拉木图", value: "Asia/Almaty" },
  { label: "(UTC+7) 曼谷", value: "Asia/Bangkok" },
  { label: "(UTC+8) 上海", value: "Asia/Shanghai" },
  { label: "(UTC+9) 东京", value: "Asia/Tokyo" },
  { label: "(UTC+10) 悉尼", value: "Australia/Sydney" },
  { label: "(UTC+12) 诺福克岛", value: "Pacific/Norfolk" },
];

const SYMBOL_OPTIONS = [
  { label: "BTC/USDT", value: "Binance:BTC/USDT", badge: "B", color: "#f59e0b" },
  { label: "ETH/USDT", value: "Binance:ETH/USDT", badge: "E", color: "#627eea" },
  { label: "BNB/USDT", value: "Binance:BNB/USDT", badge: "B", color: "#f3ba2f" },
  { label: "SOL/USDT", value: "Binance:SOL/USDT", badge: "S", color: "#00ffa3" },
  { label: "XRP/USDT", value: "Binance:XRP/USDT", badge: "X", color: "#23292f" },
  { label: "DOGE/USDT", value: "Binance:DOGE/USDT", badge: "D", color: "#c3a634" },
];

const RESOLUTION_TO_TIMEFRAME: Record<string, string> = {
  "1": "m1",
  "3": "m3",
  "5": "m5",
  "15": "m15",
  "30": "m30",
  "60": "h1",
  "240": "h4",
  "D": "d1",
  "1D": "d1",
  "W": "w1",
  "1W": "w1",
};

const RESOLUTION_TO_MS: Record<string, number> = {
  "1": 60_000,
  "3": 180_000,
  "5": 300_000,
  "15": 900_000,
  "30": 1_800_000,
  "60": 3_600_000,
  "240": 14_400_000,
  "D": 86_400_000,
  "1D": 86_400_000,
  "W": 604_800_000,
  "1W": 604_800_000,
};

const FOCUS_AUTO_INTERVAL_ORDER = ["1", "3", "5", "15", "30", "60", "240", "D", "W"] as const;

// 注册一个倒置坐标轴，后续通过 setPaneOptions 切换
registerYAxis({
  name: "reversed",
  reverse: true,
});

// 线段类工具图标统一使用本地 KLineCharts SVG 资源
const LINE_TOOL_ICON_MAP: Record<string, IconAsset> = {
  horizontalStraightLine: HorizontalLineIcon,
  horizontalRayLine: HorizontalRayIcon,
  horizontalSegment: TrendLineIcon,
  verticalStraightLine: VerticalLineIcon,
  verticalRayLine: VerticalRayIcon,
  verticalSegment: VerticalSegmentIcon,
  straightLine: ExtendedLineIcon,
  rayLine: RayIcon,
  segment: SegmentIcon,
  priceLine: PriceLineIcon,
};

// 通道与形状工具图标统一使用本地 KLineCharts SVG 资源
const CHANNEL_AND_SHAPE_TOOL_ICON_MAP: Record<string, IconAsset> = {
  priceChannelLine: PriceChannelIcon,
  parallelStraightLine: ParallelLinesIcon,
  circle: CircleCenterRightIcon,
  rect: RectangleIcon,
  parallelogram: ParallelogramIcon,
  triangle: TriangleIcon,
};

// 斐波那契与江恩箱工具图标映射
const FIB_TOOL_ICON_MAP: Record<string, IconAsset> = {
  fibonacciLine: FibonacciLineIcon,
  fibonacciSegment: FibonacciSegmentIcon,
  fibonacciCircle: FibonacciCircleIcon,
  fibonacciSpiral: FibonacciSpiralIcon,
  fibonacciSpeedResistanceFan: FibonacciSpeedFanIcon,
  fibonacciExtension: FibonacciExtensionIcon,
  gannBox: GannBoxIcon,
};

// 波浪形态工具图标映射
const WAVE_TOOL_ICON_MAP: Record<string, IconAsset> = {
  xabcd: WaveXabcdIcon,
  abcd: WaveAbcdIcon,
  threeWaves: WaveThreeIcon,
  fiveWaves: WaveFiveIcon,
  eightWaves: WaveEightIcon,
  anyWaves: WaveAnyIcon,
};
const TIMEFRAME_TO_RESOLUTION: Record<string, string> = {
  m1: "1",
  m3: "3",
  m5: "5",
  m15: "15",
  m30: "30",
  h1: "60",
  h4: "240",
  d: "D",
  d1: "D",
  w: "W",
  w1: "W",
  "1m": "1",
  "3m": "3",
  "5m": "5",
  "15m": "15",
  "30m": "30",
  "1h": "60",
  "4h": "240",
  "1d": "D",
  "1w": "W",
};

const toResolution = (timeframe?: string) => {
  if (!timeframe) {
    return undefined;
  }
  const normalized = timeframe.trim().toLowerCase();
  if (!normalized) {
    return undefined;
  }
  if (TIMEFRAME_TO_RESOLUTION[normalized]) {
    return TIMEFRAME_TO_RESOLUTION[normalized];
  }
  if (/^\d+$/.test(normalized)) {
    return normalized;
  }
  return undefined;
};

const logKline = (...args: unknown[]) => {
  if (DEBUG_KLINE) {
    console.debug("[Klinecharts]", ...args);
  }
};

const logKlineTrace = (stage: string, payload?: unknown) => {
  if (DEBUG_KLINE) {
    console.info("[KlinechartsTrace]", stage, payload ?? {});
  }
};

const buildTraceId = (prefix: string) => `${prefix}-${Date.now()}-${Math.floor(Math.random() * 10_000)}`;

type FocusZoneKind = "takeProfitPlan" | "stopLossPlan" | "executedProfit" | "executedLoss";

type FocusZone = {
  kind: FocusZoneKind;
  upper: number;
  lower: number;
  color: string;
};

const FOCUS_ZONE_COLORS: Record<FocusZoneKind, string> = {
  takeProfitPlan: "rgba(34, 197, 94, 0.16)",
  stopLossPlan: "rgba(239, 68, 68, 0.20)",
  executedProfit: "rgba(21, 128, 61, 0.32)",
  executedLoss: "rgba(220, 38, 38, 0.32)",
};

const toFinitePrice = (value: number | undefined) => {
  if (typeof value !== "number" || !Number.isFinite(value) || value <= 0) {
    return null;
  }
  return value;
};

const clamp = (value: number, min: number, max: number) => {
  return Math.max(min, Math.min(max, value));
};

const normalizeFocusResolution = (value?: string) => {
  if (!value) {
    return undefined;
  }
  if (value === "1D") {
    return "D";
  }
  if (value === "1W") {
    return "W";
  }
  return value;
};

const estimateFocusMaxVisibleBars = (chart: Chart) => {
  const paneSize =
    chart.getSize(CANDLE_PANE_ID, DomPosition.Main) ??
    chart.getSize(undefined, DomPosition.Main) ??
    chart.getSize();
  const paneWidth = paneSize?.width ?? 0;
  const barSpace = chart.getBarSpace();
  const safeBarSpace = Number.isFinite(barSpace) && barSpace > 0 ? barSpace : 6;
  const estimatedBySpace = paneWidth > 0 ? Math.floor(paneWidth / safeBarSpace) : 0;
  const visibleRange = chart.getVisibleRange();
  const estimatedByRange =
    Number.isFinite(visibleRange.realFrom) && Number.isFinite(visibleRange.realTo)
      ? Math.floor(Math.abs(visibleRange.realTo - visibleRange.realFrom)) + 1
      : 0;
  return Math.max(60, Math.max(estimatedBySpace, estimatedByRange));
};

const pickFocusAutoInterval = (
  rawStart: number,
  rawEnd: number,
  baseInterval: string,
  maxVisibleBars: number
) => {
  const normalizedBase = normalizeFocusResolution(baseInterval) ?? DEFAULT_INTERVAL;
  const baseMs = RESOLUTION_TO_MS[normalizedBase] ?? RESOLUTION_TO_MS[DEFAULT_INTERVAL];
  const candidateIntervals = FOCUS_AUTO_INTERVAL_ORDER.filter((interval) => {
    const ms = RESOLUTION_TO_MS[interval];
    return Number.isFinite(ms) && ms >= baseMs;
  });
  const candidates = candidateIntervals.length > 0
    ? candidateIntervals
    : [FOCUS_AUTO_INTERVAL_ORDER[FOCUS_AUTO_INTERVAL_ORDER.length - 1]];
  const safeMaxVisibleBars = Math.max(30, Math.floor(maxVisibleBars));
  const rangeMs = Math.max(0, rawEnd - rawStart);
  for (const interval of candidates) {
    const intervalMs = RESOLUTION_TO_MS[interval];
    const bars = Math.max(1, Math.ceil(rangeMs / intervalMs) + 1);
    if (bars <= safeMaxVisibleBars) {
      return interval;
    }
  }
  return candidates[candidates.length - 1];
};

const resolveFocusDirection = (
  side: string | undefined,
  entryPrice: number,
  exitPrice: number | null,
  stopLossPrice: number | null,
  takeProfitPrice: number | null
) => {
  const normalized = side?.trim().toLowerCase();
  if (normalized === "long") {
    return true;
  }
  if (normalized === "short") {
    return false;
  }
  if (takeProfitPrice !== null && stopLossPrice !== null) {
    if (takeProfitPrice > entryPrice && stopLossPrice < entryPrice) {
      return true;
    }
    if (takeProfitPrice < entryPrice && stopLossPrice > entryPrice) {
      return false;
    }
  }
  if (takeProfitPrice !== null && takeProfitPrice !== entryPrice) {
    return takeProfitPrice > entryPrice;
  }
  if (stopLossPrice !== null && stopLossPrice !== entryPrice) {
    return stopLossPrice < entryPrice;
  }
  if (exitPrice !== null && exitPrice !== entryPrice) {
    return exitPrice >= entryPrice;
  }
  return true;
};

const getIndicatorPaneOptions = (option?: IndicatorOption) => {
  if (option?.pane === "main") {
    return { id: CANDLE_PANE_ID };
  }
  return undefined;
};

const MarketChart: React.FC<MarketChartProps> = ({
  symbol = DEFAULT_SYMBOL,
  interval = DEFAULT_INTERVAL,
  height = 420,
  theme = "light",
  focusRange = null,
}) => {
  const shellRef = useRef<HTMLDivElement>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  const chartRef = useRef<Chart | null>(null);
  const lastBarRef = useRef<KLineData | null>(null);
  const indicatorPaneRef = useRef(new Map<string, string>());
  const overlayIdsRef = useRef<string[]>([]);
  const focusOverlayIdsRef = useRef<string[]>([]);
  const focusModeRef = useRef(false);
  const dataVersionRef = useRef(0);
  const httpRef = useRef(new HttpClient());
  const unsubscribeRef = useRef<(() => void) | null>(null);
  const indicatorPanelRef = useRef<HTMLDivElement>(null);
  const timezonePanelRef = useRef<HTMLDivElement>(null);
  const settingsPanelRef = useRef<HTMLDivElement>(null);
  const symbolPanelRef = useRef<HTMLDivElement>(null);
  const [activeSymbol, setActiveSymbol] = useState(symbol);
  const [activeInterval, setActiveInterval] = useState(interval);
  const [activeIndicators, setActiveIndicators] = useState<string[]>([]);
  const [activeDrawing, setActiveDrawing] = useState<string>("cursor");
  const [reloadKey, setReloadKey] = useState(0);
  const [latestBar, setLatestBar] = useState<KLineData | null>(null);
  const [activeTimezone, setActiveTimezone] = useState("Asia/Shanghai");
  const [showIndicators, setShowIndicators] = useState(false);
  const [showTimezone, setShowTimezone] = useState(false);
  const [showSettings, setShowSettings] = useState(false);
  const [showSymbolSelector, setShowSymbolSelector] = useState(false);
  const [isFullscreen, setIsFullscreen] = useState(false);
  const [expandedToolGroup, setExpandedToolGroup] = useState<string | null>(null);
  const [toolListDirection, setToolListDirection] = useState<"down" | "up">("down");
  const [selectedToolPerGroup, setSelectedToolPerGroup] = useState<Record<string, string>>(() => {
    // 每个分组默认选中第一个工具
    const defaults: Record<string, string> = {};
    DRAWING_TOOL_GROUPS.forEach((group) => {
      if (group.tools.length > 0) {
        defaults[group.id] = group.defaultIcon || group.tools[0].id;
      }
    });
    return defaults;
  });
  const drawingBarRef = useRef<HTMLDivElement>(null);
  const expandedListRef = useRef<HTMLUListElement>(null);
  const [isDrawingLocked, setIsDrawingLocked] = useState(false);
  const [isDrawingVisible, setIsDrawingVisible] = useState(true);
  const [magnetMode, setMagnetMode] = useState<"weak_magnet" | "strong_magnet">("weak_magnet");
  const [isMagnetEnabled, setIsMagnetEnabled] = useState(false);
  // 图表显示设置
  const [showLatestPrice, setShowLatestPrice] = useState(true);
  const [showHighPrice, setShowHighPrice] = useState(true);
  const [showLowPrice, setShowLowPrice] = useState(true);
  const [showIndicatorLastValue, setShowIndicatorLastValue] = useState(false);
  const [invertYAxis, setInvertYAxis] = useState(false);
  const [showGrid, setShowGrid] = useState(true);
  const [candleType, setCandleType] = useState<
    "candle_solid" | "candle_stroke" | "candle_up_stroke" | "candle_down_stroke" | "ohlc" | "area"
  >("candle_solid");
  const [priceAxisType, setPriceAxisType] = useState<"linear" | "percentage" | "log">("linear");

  useEffect(() => {
    setActiveSymbol(symbol ?? DEFAULT_SYMBOL);
  }, [symbol]);

  useEffect(() => {
    setActiveInterval(interval ?? DEFAULT_INTERVAL);
  }, [interval]);

  const supportedIndicators = useMemo(() => new Set(getSupportedIndicators()), []);
  const indicatorOptions = useMemo(
    () => INDICATOR_OPTIONS.filter((option) => supportedIndicators.has(option.value)),
    [supportedIndicators]
  );
  // 显示所有绘图工具组（不过滤，保持完整的工具栏）
  const filteredToolGroups = DRAWING_TOOL_GROUPS;
  const displaySymbol = useMemo(() => {
    if (!activeSymbol) {
      return "";
    }
    const parts = activeSymbol.split(":");
    return parts.length > 1 ? parts[1] : parts[0];
  }, [activeSymbol]);

  const clearFocusOverlays = () => {
    const chart = chartRef.current;
    if (!chart) {
      focusOverlayIdsRef.current = [];
      return;
    }
    if (focusOverlayIdsRef.current.length > 0) {
      logKlineTrace("focus:overlay:clear", {
        count: focusOverlayIdsRef.current.length,
      });
    }
    for (const id of focusOverlayIdsRef.current) {
      chart.removeOverlay(id);
    }
    focusOverlayIdsRef.current = [];
  };

  const bumpDataVersion = (reason: string) => {
    dataVersionRef.current += 1;
    const version = dataVersionRef.current;
    logKlineTrace("data:version:bump", { reason, version });
    return version;
  };

  const isDataVersionCurrent = (version: number) => dataVersionRef.current === version;

  const exitFocusMode = (reason: string, reloadHistory = false) => {
    const wasFocused = focusModeRef.current;
    focusModeRef.current = false;
    clearFocusOverlays();
    if (reloadHistory && wasFocused) {
      bumpDataVersion(`focus-exit:${reason}`);
      logKlineTrace("focus:restore-history", { reason });
      setReloadKey((prev) => prev + 1);
    }
  };

  useEffect(() => {
    if (!showIndicators && !showTimezone && !showSettings && !showSymbolSelector && !expandedToolGroup) {
      return undefined;
    }

    const handleClick = (event: MouseEvent) => {
      const target = event.target as Node;
      if (showIndicators && indicatorPanelRef.current && indicatorPanelRef.current.contains(target)) {
        return;
      }
      if (showTimezone && timezonePanelRef.current && timezonePanelRef.current.contains(target)) {
        return;
      }
      if (showSettings && settingsPanelRef.current && settingsPanelRef.current.contains(target)) {
        return;
      }
      if (showSymbolSelector && symbolPanelRef.current && symbolPanelRef.current.contains(target)) {
        return;
      }
      if (expandedToolGroup && drawingBarRef.current && drawingBarRef.current.contains(target)) {
        return;
      }
      setShowIndicators(false);
      setShowTimezone(false);
      setShowSettings(false);
      setShowSymbolSelector(false);
      setExpandedToolGroup(null);
    };

    document.addEventListener("mousedown", handleClick);
    return () => document.removeEventListener("mousedown", handleClick);
  }, [showIndicators, showTimezone, showSettings, showSymbolSelector, expandedToolGroup]);

  useLayoutEffect(() => {
    if (!expandedToolGroup || !expandedListRef.current) {
      return;
    }
    const listRect = expandedListRef.current.getBoundingClientRect();
    const bottomOverflow = listRect.bottom > window.innerHeight - 8;
    const topOverflow = listRect.top < 8;

    if (bottomOverflow && toolListDirection !== "up") {
      setToolListDirection("up");
      return;
    }
    if (topOverflow && toolListDirection !== "down") {
      setToolListDirection("down");
    }
  }, [expandedToolGroup, toolListDirection]);

  useEffect(() => {
    if (!containerRef.current) {
      return undefined;
    }

    return () => {
      unsubscribeRef.current?.();
      unsubscribeRef.current = null;
      if (chartRef.current) {
        dispose(chartRef.current);
        chartRef.current = null;
      }
    };
  }, []);

  useEffect(() => {
    if (!containerRef.current || chartRef.current) {
      return;
    }

    const chart = init(containerRef.current, { locale: "zh" });
    if (!chart) {
      return;
    }
    chartRef.current = chart;
    indicatorPaneRef.current.clear();
    overlayIdsRef.current = [];

    const resizeObserver = new ResizeObserver(() => chart.resize());
    resizeObserver.observe(containerRef.current);

    const defaults = ["BOLL", "MA", "VOL", "MACD"];
    const nextIndicators: string[] = [];
    for (const indicator of defaults) {
      if (!supportedIndicators.has(indicator)) {
        continue;
      }
      const option = INDICATOR_OPTIONS.find((item) => item.value === indicator);
      const paneId = chart.createIndicator(
        indicator,
        option?.stack ?? false,
        getIndicatorPaneOptions(option)
      );
      if (paneId) {
        indicatorPaneRef.current.set(indicator, paneId);
        nextIndicators.push(indicator);
      }
    }
    if (nextIndicators.length > 0) {
      setActiveIndicators(nextIndicators);
    }

    return () => {
      resizeObserver.disconnect();
    };
  }, [supportedIndicators]);

  useEffect(() => {
    const chart = chartRef.current;
    if (!chart) {
      return;
    }
    chart.setStyles(theme === "dark" ? "dark" : "light");
  }, [theme]);

  useEffect(() => {
    const chart = chartRef.current;
    if (!chart) {
      return;
    }
    const timezone = activeTimezone === "local"
      ? Intl.DateTimeFormat().resolvedOptions().timeZone
      : activeTimezone;
    chart.setTimezone(timezone);
  }, [activeTimezone]);

  // 根据设置面板状态同步样式（最新价、高低价、指标最新值、网格、蜡烛图类型）
  useEffect(() => {
    const chart = chartRef.current;
    if (!chart) {
      return;
    }
    chart.setStyles({
      grid: {
        show: showGrid,
      },
      candle: {
        type: candleType,
        priceMark: {
          high: { show: showHighPrice },
          low: { show: showLowPrice },
          last: { show: showLatestPrice },
        },
      },
      indicator: {
        lastValueMark: {
          show: showIndicatorLastValue,
          text: { show: showIndicatorLastValue },
        },
      },
    });
  }, [showGrid, candleType, showHighPrice, showLowPrice, showLatestPrice, showIndicatorLastValue]);

  // 坐标倒置与价格轴类型（目前仅倒置生效，轴类型预留）
  useEffect(() => {
    const chart = chartRef.current;
    if (!chart) {
      return;
    }
    const paneIds = new Set<string>();
    paneIds.add(CANDLE_PANE_ID);
    indicatorPaneRef.current.forEach((paneId) => {
      paneIds.add(paneId);
    });
    const axisName = invertYAxis ? "reversed" : "default";
    paneIds.forEach((id) => {
      const opts = { id, axisOptions: { name: axisName } } as Parameters<Chart["setPaneOptions"]>[0];
      chart.setPaneOptions(opts);
    });
  }, [invertYAxis, priceAxisType]);

  useEffect(() => {
    const chart = chartRef.current;
    if (!chart) {
      return;
    }

    const parsed = parseSymbolName(activeSymbol);
    if (!parsed) {
      chart.clearData();
      setLatestBar(null);
      return;
    }

    exitFocusMode("history-load:start");

    const timeframe = RESOLUTION_TO_TIMEFRAME[activeInterval] ?? "m1";
    const intervalMs = RESOLUTION_TO_MS[activeInterval] ?? 60_000;
    const baseTraceId = buildTraceId(`symbol-${parsed.symbol}-interval-${activeInterval}`);
    const historyDataVersion = bumpDataVersion(`history:${parsed.symbol}:${activeInterval}:${reloadKey}`);

    chart.clearData();
    lastBarRef.current = null;
    setLatestBar(null);

    const controller = new AbortController();
    let disposed = false;

    const loadHistory = async () => {
      try {
        const traceId = `${baseTraceId}-init`;
        logKline("loadHistory:init", { traceId, symbol: parsed.symbol, timeframe, count: DEFAULT_COUNT });
        logKlineTrace("history:init:start", {
          traceId,
          symbol: parsed.symbol,
          interval: activeInterval,
          timeframe,
          count: DEFAULT_COUNT,
        });
        const bars = await fetchHistory({
          http: httpRef.current,
          exchange: parsed.exchange,
          symbol: parsed.symbol,
          timeframe,
          intervalMs,
          count: DEFAULT_COUNT,
          signal: controller.signal,
          traceId,
          traceSource: "chart.init",
        });
        if (disposed || !isDataVersionCurrent(historyDataVersion)) {
          logKlineTrace("history:init:skip:stale", {
            traceId,
            disposed,
            historyDataVersion,
            currentDataVersion: dataVersionRef.current,
          });
          return;
        }
        logKline("loadHistory:done", {
          traceId,
          count: bars.length,
          first: bars[0]?.timestamp,
          last: bars[bars.length - 1]?.timestamp,
        });
        logKlineTrace("history:init:draw:start", { traceId, bars: bars.length });
        chart.applyNewData(bars);
        logKlineTrace("history:init:draw:done", { traceId, bars: bars.length });
        if (bars.length > 0) {
          lastBarRef.current = bars[bars.length - 1];
          setLatestBar(bars[bars.length - 1]);
        }
      } catch (error) {
        if (!disposed) {
          logKlineTrace("history:init:error", { traceId: `${baseTraceId}-init`, error });
          console.error("[Klinecharts] Failed to load history", error);
        }
      }
    };

    loadHistory();

    chart.setLoadDataCallback(async (params) => {
      if (disposed) {
        return;
      }
      if (!isDataVersionCurrent(historyDataVersion) || focusModeRef.current) {
        logKlineTrace("history:loadMore:skip:stale-or-focus", {
          type: params.type,
          historyDataVersion,
          currentDataVersion: dataVersionRef.current,
          focusMode: focusModeRef.current,
        });
        params.callback([], false);
        return;
      }
      if (!params.data) {
        logKline("loadMore:skip:no-anchor", { type: params.type });
        params.callback([], false);
        return;
      }
      if (params.type === LoadDataType.Forward) {
        const endTime = params.data.timestamp - intervalMs;
        const traceId = `${baseTraceId}-load-more-${endTime}`;
        if (!Number.isFinite(endTime) || endTime <= 0) {
          logKline("loadMore:skip:invalid-end", { traceId, endTime });
          logKlineTrace("history:loadMore:skip:invalid-end", { traceId, endTime, anchor: params.data.timestamp });
          params.callback([], false);
          return;
        }
        try {
          logKline("loadMore:backward", {
            traceId,
            symbol: parsed.symbol,
            timeframe,
            endTime,
            count: DEFAULT_COUNT,
          });
          logKlineTrace("history:loadMore:start", {
            traceId,
            symbol: parsed.symbol,
            interval: activeInterval,
            timeframe,
            endTime,
            count: DEFAULT_COUNT,
          });
          const bars = await fetchHistory({
            http: httpRef.current,
            exchange: parsed.exchange,
            symbol: parsed.symbol,
            timeframe,
            intervalMs,
            count: DEFAULT_COUNT,
            endTime,
            signal: controller.signal,
            traceId,
            traceSource: "chart.loadMore.forward",
          });
          if (!isDataVersionCurrent(historyDataVersion) || disposed) {
            logKlineTrace("history:loadMore:skip:stale-after-fetch", {
              traceId,
              disposed,
              historyDataVersion,
              currentDataVersion: dataVersionRef.current,
            });
            params.callback([], false);
            return;
          }
          const more = bars.length === DEFAULT_COUNT;
          logKline("loadMore:done", { traceId, count: bars.length, more });
          logKlineTrace("history:loadMore:done", {
            traceId,
            count: bars.length,
            more,
            first: bars[0]?.timestamp,
            last: bars[bars.length - 1]?.timestamp,
          });
          params.callback(bars, more);
        } catch (error) {
          if (!disposed) {
            logKlineTrace("history:loadMore:error", { traceId, error });
            console.error("[Klinecharts] Failed to load more history", error);
          }
          params.callback([], false);
        }
        return;
      }
      if (params.type === LoadDataType.Backward) {
        logKline("loadMore:forward:skip", { reason: "live-ticks", last: params.data.timestamp });
        params.callback([], false);
      }
    });

    unsubscribeRef.current?.();
    unsubscribeRef.current = subscribeMarket([parsed.symbol], (ticks) => {
      if (disposed) {
        return;
      }
      if (!isDataVersionCurrent(historyDataVersion)) {
        return;
      }
      if (focusModeRef.current) {
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
        setLatestBar(nextBar);
        chart.updateData(nextBar);
      }
    });

    return () => {
      disposed = true;
      controller.abort();
      unsubscribeRef.current?.();
      unsubscribeRef.current = null;
    };
  }, [activeSymbol, activeInterval, reloadKey]);

  useEffect(() => {
    const traceId = focusRange?.id?.trim() || buildTraceId("focus-range");
    const chart = chartRef.current;
    if (!chart) {
      logKlineTrace("focus:skip:no-chart", { traceId });
      return;
    }
    if (!focusRange) {
      logKlineTrace("focus:clear", { traceId, reason: "focusRange-empty" });
      exitFocusMode("focusRange-empty", true);
      return;
    }

    logKlineTrace("focus:receive", {
      traceId,
      focusId: focusRange.id,
      chartSymbol: focusRange.chartSymbol,
      chartInterval: focusRange.chartInterval,
      startTime: focusRange.startTime,
      endTime: focusRange.endTime,
      side: focusRange.side,
      exitReason: focusRange.exitReason,
      entryPrice: focusRange.entryPrice,
      exitPrice: focusRange.exitPrice,
      stopLossPrice: focusRange.stopLossPrice,
      takeProfitPrice: focusRange.takeProfitPrice,
      activeSymbol,
      activeInterval,
    });

    const targetSymbol = focusRange.chartSymbol?.trim();
    if (targetSymbol && targetSymbol !== activeSymbol) {
      logKlineTrace("focus:skip:symbol-mismatch", { traceId, targetSymbol, activeSymbol });
      exitFocusMode("symbol-mismatch", true);
      return;
    }

    const rawStart = Math.min(focusRange.startTime, focusRange.endTime);
    const rawEnd = Math.max(focusRange.startTime, focusRange.endTime);
    if (!Number.isFinite(rawStart) || !Number.isFinite(rawEnd) || rawStart <= 0 || rawEnd <= 0) {
      logKlineTrace("focus:skip:invalid-range", {
        traceId,
        rawStart,
        rawEnd,
      });
      exitFocusMode("invalid-range", true);
      return;
    }

    const baseInterval = normalizeFocusResolution(toResolution(focusRange.chartInterval) ?? activeInterval) ?? DEFAULT_INTERVAL;
    const activeIntervalNormalized = normalizeFocusResolution(activeInterval) ?? activeInterval;
    const maxVisibleBars = estimateFocusMaxVisibleBars(chart);
    const desiredInterval = pickFocusAutoInterval(rawStart, rawEnd, baseInterval, maxVisibleBars);
    if (desiredInterval !== activeIntervalNormalized) {
      logKlineTrace("focus:auto-interval:switch", {
        traceId,
        baseInterval,
        activeInterval,
        activeIntervalNormalized,
        desiredInterval,
        maxVisibleBars,
        rangeMs: rawEnd - rawStart,
      });
      exitFocusMode("auto-interval-switch");
      setActiveInterval(desiredInterval);
      return;
    }

    const parsed = parseSymbolName(activeSymbol);
    if (!parsed) {
      logKlineTrace("focus:skip:invalid-symbol", { traceId, activeSymbol });
      exitFocusMode("invalid-symbol", true);
      return;
    }
    clearFocusOverlays();

    const timeframe = RESOLUTION_TO_TIMEFRAME[activeInterval] ?? "m1";
    const intervalMs = RESOLUTION_TO_MS[activeInterval] ?? 60_000;

    const barsInRange = Math.max(1, Math.ceil((rawEnd - rawStart) / intervalMs) + 1);
    const paddingBars = Math.min(180, Math.max(8, Math.ceil(barsInRange * 0.4)));
    const startTime = rawStart - paddingBars * intervalMs;
    const endTime = rawEnd + paddingBars * intervalMs;
    const count = Math.min(1600, barsInRange + paddingBars * 2 + 20);
    const focusDataVersion = bumpDataVersion(`focus:${traceId}`);

    const controller = new AbortController();
    let disposed = false;
    let alignRafId: number | null = null;
    let lastFocusBackwardAnchor = -1;
    let lastFocusBackwardAt = 0;
    const focusBackwardCooldownMs = 450;
    let focusBackwardLoading = false;
    chart.setLoadDataCallback(async (params) => {
      if (disposed) {
        return;
      }
      if (!isDataVersionCurrent(focusDataVersion) || !focusModeRef.current) {
        logKlineTrace("focus:loadMore:skip:stale-or-not-focused", {
          traceId,
          type: params.type,
          focusDataVersion,
          currentDataVersion: dataVersionRef.current,
          focusMode: focusModeRef.current,
        });
        params.callback([], false);
        return;
      }
      const anchorTimestamp = params.data?.timestamp;
      if (!Number.isFinite(anchorTimestamp) || (anchorTimestamp ?? 0) <= 0) {
        logKlineTrace("focus:loadMore:skip:invalid-anchor", {
          traceId,
          type: params.type,
          anchorTimestamp,
        });
        params.callback([], false);
        return;
      }

      const forwardFetchCount = DEFAULT_COUNT;
      const backwardFetchCount = Math.min(120, DEFAULT_COUNT);
      const anchor = anchorTimestamp as number;
      const dataList = chart.getDataList();
      const currentMinTimestamp = dataList.length > 0 ? dataList[0]?.timestamp ?? 0 : 0;
      const currentMaxTimestamp = dataList.length > 0 ? dataList[dataList.length - 1]?.timestamp ?? 0 : 0;
      const measureTimestampX = (timestamp: number, value: number) => {
        const pixel = chart.convertToPixel(
          { timestamp, value },
          { paneId: CANDLE_PANE_ID, absolute: false }
        ) as Partial<Coordinate> | Array<Partial<Coordinate>>;
        const coordinate = Array.isArray(pixel) ? pixel[0] : pixel;
        const x = coordinate?.x;
        return typeof x === "number" && Number.isFinite(x) ? x : null;
      };

      try {
        if (params.type === LoadDataType.Forward) {
          const endTime = anchor - intervalMs;
          if (!Number.isFinite(endTime) || endTime <= 0) {
            logKlineTrace("focus:loadMore:skip:invalid-end", {
              traceId,
              anchor,
              endTime,
            });
            params.callback([], false);
            return;
          }
          const loadTraceId = `${traceId}-loadMore-forward-${endTime}`;
          logKlineTrace("focus:loadMore:forward:start", {
            traceId: loadTraceId,
            symbol: parsed.symbol,
            timeframe,
            endTime,
            count: forwardFetchCount,
          });
          const bars = await fetchHistory({
            http: httpRef.current,
            exchange: parsed.exchange,
            symbol: parsed.symbol,
            timeframe,
            intervalMs,
            count: forwardFetchCount,
            endTime,
            signal: controller.signal,
            traceId: loadTraceId,
            traceSource: "focus.loadMore.forward",
          });
          if (disposed || !isDataVersionCurrent(focusDataVersion)) {
            logKlineTrace("focus:loadMore:forward:skip:stale-after-fetch", {
              traceId: loadTraceId,
              disposed,
              focusDataVersion,
              currentDataVersion: dataVersionRef.current,
            });
            params.callback([], false);
            return;
          }
          const olderBars = bars.filter((bar) => bar.timestamp < currentMinTimestamp);
          const more = bars.length === forwardFetchCount && olderBars.length > 0;
          logKlineTrace("focus:loadMore:forward:done", {
            traceId: loadTraceId,
            bars: bars.length,
            acceptedBars: olderBars.length,
            more,
            first: bars[0]?.timestamp,
            last: bars[bars.length - 1]?.timestamp,
            currentMinTimestamp,
          });
          params.callback(olderBars, more);
          return;
        }

        if (params.type === LoadDataType.Backward) {
          const edgeToleranceMs = Math.max(1, Math.floor(intervalMs * 0.05));
          if (currentMaxTimestamp > 0 && anchor < currentMaxTimestamp - edgeToleranceMs) {
            logKlineTrace("focus:loadMore:backward:skip:already-has-right-buffer", {
              traceId,
              anchor,
              currentMaxTimestamp,
              edgeToleranceMs,
            });
            params.callback([], false);
            return;
          }
          if (focusBackwardLoading) {
            logKlineTrace("focus:loadMore:backward:skip:inflight", {
              traceId,
              anchor,
            });
            params.callback([], false);
            return;
          }
          const now = Date.now();
          if (lastFocusBackwardAnchor === anchor && now - lastFocusBackwardAt < focusBackwardCooldownMs) {
            logKlineTrace("focus:loadMore:backward:skip:cooldown", {
              traceId,
              anchor,
              cooldownMs: focusBackwardCooldownMs,
              elapsedMs: now - lastFocusBackwardAt,
            });
            params.callback([], false);
            return;
          }
          lastFocusBackwardAnchor = anchor;
          lastFocusBackwardAt = now;

          focusBackwardLoading = true;
          try {
            const startTime = anchor + intervalMs;
            const endTime = startTime + intervalMs * (backwardFetchCount - 1);
            if (!Number.isFinite(startTime) || !Number.isFinite(endTime) || startTime <= 0 || endTime <= startTime) {
              logKlineTrace("focus:loadMore:skip:invalid-forward-range", {
                traceId,
                anchor,
                startTime,
                endTime,
              });
              params.callback([], false);
              return;
            }
            const loadTraceId = `${traceId}-loadMore-backward-${startTime}`;
            logKlineTrace("focus:loadMore:backward:start", {
              traceId: loadTraceId,
              symbol: parsed.symbol,
              timeframe,
              startTime,
              endTime,
              count: backwardFetchCount,
            });
            const bars = await fetchHistory({
              http: httpRef.current,
              exchange: parsed.exchange,
              symbol: parsed.symbol,
              timeframe,
              intervalMs,
              count: backwardFetchCount,
              startTime,
              endTime,
              signal: controller.signal,
              traceId: loadTraceId,
              traceSource: "focus.loadMore.backward",
            });
            if (disposed || !isDataVersionCurrent(focusDataVersion)) {
              logKlineTrace("focus:loadMore:backward:skip:stale-after-fetch", {
                traceId: loadTraceId,
                disposed,
                focusDataVersion,
                currentDataVersion: dataVersionRef.current,
              });
              params.callback([], false);
              return;
            }
            const newerBars = bars.filter((bar) => bar.timestamp > currentMaxTimestamp);
            if (newerBars.length === 0) {
              logKlineTrace("focus:loadMore:backward:done:no-new-bars", {
                traceId: loadTraceId,
                bars: bars.length,
                currentMaxTimestamp,
              });
              params.callback([], false);
              return;
            }

            // 记录补数据前当前最右锚点的像素位置，补完后回滚位移，避免图表跳到最新处
            const anchorMeasureValueRaw = dataList[dataList.length - 1]?.close;
            const anchorMeasureValue =
              typeof anchorMeasureValueRaw === "number" && Number.isFinite(anchorMeasureValueRaw)
                ? anchorMeasureValueRaw
                : newerBars[0].close;
            const anchorBeforeX =
              Number.isFinite(anchorMeasureValue) && currentMaxTimestamp > 0
                ? measureTimestampX(currentMaxTimestamp, anchorMeasureValue)
                : null;

            // 右侧加载仅按用户触发单次补数据，禁止链式追拉
            const more = false;
            logKlineTrace("focus:loadMore:backward:done", {
              traceId: loadTraceId,
              bars: bars.length,
              acceptedBars: newerBars.length,
              more,
              first: bars[0]?.timestamp,
              last: bars[bars.length - 1]?.timestamp,
              currentMaxTimestamp,
            });
            params.callback(newerBars, more);

            if (anchorBeforeX !== null) {
              window.requestAnimationFrame(() => {
                if (disposed || !isDataVersionCurrent(focusDataVersion)) {
                  return;
                }
                const anchorAfterX = measureTimestampX(currentMaxTimestamp, anchorMeasureValue);
                if (anchorAfterX === null) {
                  return;
                }
                const deltaX = anchorBeforeX - anchorAfterX;
                if (Math.abs(deltaX) <= 0.5) {
                  return;
                }
                chart.scrollByDistance(deltaX, 0);
                logKlineTrace("focus:loadMore:backward:stabilized", {
                  traceId: loadTraceId,
                  currentMaxTimestamp,
                  anchorBeforeX,
                  anchorAfterX,
                  deltaX,
                });
              });
            }
            return;
          } finally {
            focusBackwardLoading = false;
          }
        }
      } catch (error) {
        if (!disposed) {
          logKlineTrace("focus:loadMore:error", {
            traceId,
            type: params.type,
            error,
          });
          console.error("[Klinecharts] Failed to load focused loadMore data", error);
        }
      }
      params.callback([], false);
    });

    const loadFocusedRange = async () => {
      try {
        logKline("focusRange:load", {
          traceId,
          symbol: parsed.symbol,
          timeframe,
          startTime,
          endTime,
          count,
        });
        logKlineTrace("focus:request:prepared", {
          traceId,
          symbol: parsed.symbol,
          interval: activeInterval,
          timeframe,
          rawStart,
          rawEnd,
          startTime,
          endTime,
          barsInRange,
          paddingBars,
          count,
        });
        const bars = await fetchHistory({
          http: httpRef.current,
          exchange: parsed.exchange,
          symbol: parsed.symbol,
          timeframe,
          intervalMs,
          count,
          startTime,
          endTime,
          signal: controller.signal,
          traceId,
          traceSource: "focus.range",
        });
        if (disposed || !isDataVersionCurrent(focusDataVersion)) {
          logKlineTrace("focus:cancelled", {
            traceId,
            reason: disposed ? "effect-disposed" : "stale-data-version",
            focusDataVersion,
            currentDataVersion: dataVersionRef.current,
          });
          return;
        }
        if (bars.length === 0) {
          logKlineTrace("focus:skip:empty-bars", { traceId });
          exitFocusMode("empty-bars", true);
          return;
        }

        logKlineTrace("focus:draw:start", { traceId, bars: bars.length });
        focusModeRef.current = true;
        chart.applyNewData(bars);
        logKlineTrace("focus:draw:done", {
          traceId,
          bars: bars.length,
          first: bars[0]?.timestamp,
          last: bars[bars.length - 1]?.timestamp,
        });
        lastBarRef.current = bars[bars.length - 1];
        setLatestBar(bars[bars.length - 1]);

        const high = Math.max(...bars.map((bar) => bar.high));
        const low = Math.min(...bars.map((bar) => bar.low));
        const fallbackValue = Number.isFinite((high + low) / 2) ? (high + low) / 2 : bars[bars.length - 1].close;
        const entryPrice = toFinitePrice(focusRange.entryPrice);
        const exitPrice = toFinitePrice(focusRange.exitPrice);
        const stopLossPrice = toFinitePrice(focusRange.stopLossPrice);
        const takeProfitPrice = toFinitePrice(focusRange.takeProfitPrice);
        const focusZones: FocusZone[] = [];
        if (entryPrice !== null) {
          const isLong = resolveFocusDirection(
            focusRange.side,
            entryPrice,
            exitPrice,
            stopLossPrice,
            takeProfitPrice
          );
          const toProgress = (price: number) => (isLong ? price - entryPrice : entryPrice - price);
          const fromProgress = (progress: number) => (isLong ? entryPrice + progress : entryPrice - progress);
          const progressEpsilon = 1e-9;
          const occupiedRanges: Array<{ start: number; end: number }> = [];
          const appendProgressZone = (
            kind: FocusZoneKind,
            startProgress: number,
            endProgress: number,
            options?: { subtractOccupied?: boolean }
          ) => {
            const subtractOccupied = options?.subtractOccupied ?? true;
            const minProgress = Math.min(startProgress, endProgress);
            const maxProgress = Math.max(startProgress, endProgress);
            if (
              !Number.isFinite(minProgress) ||
              !Number.isFinite(maxProgress) ||
              Math.abs(maxProgress - minProgress) <= progressEpsilon
            ) {
              return;
            }
            let segments = [{ start: minProgress, end: maxProgress }];
            if (subtractOccupied && occupiedRanges.length > 0) {
              for (const occupied of occupiedRanges) {
                const nextSegments: Array<{ start: number; end: number }> = [];
                for (const segment of segments) {
                  const overlapStart = Math.max(segment.start, occupied.start);
                  const overlapEnd = Math.min(segment.end, occupied.end);
                  if (overlapEnd - overlapStart <= progressEpsilon) {
                    nextSegments.push(segment);
                    continue;
                  }
                  if (overlapStart - segment.start > progressEpsilon) {
                    nextSegments.push({ start: segment.start, end: overlapStart });
                  }
                  if (segment.end - overlapEnd > progressEpsilon) {
                    nextSegments.push({ start: overlapEnd, end: segment.end });
                  }
                }
                segments = nextSegments;
                if (segments.length === 0) {
                  break;
                }
              }
            }

            for (const segment of segments) {
              if (segment.end - segment.start <= progressEpsilon) {
                continue;
              }
              let upper = Math.max(fromProgress(segment.start), fromProgress(segment.end));
              let lower = Math.min(fromProgress(segment.start), fromProgress(segment.end));
              if (!Number.isFinite(upper) || !Number.isFinite(lower)) {
                continue;
              }
              if (Math.abs(upper - lower) <= Number.EPSILON) {
                const delta = Math.max(Math.abs(upper) * 0.0005, 0.0001);
                upper += delta;
                lower -= delta;
              }
              focusZones.push({
                kind,
                upper,
                lower,
                color: FOCUS_ZONE_COLORS[kind],
              });
              occupiedRanges.push(segment);
            }
          };
          const normalizedExitReason = focusRange.exitReason?.trim().toLowerCase() ?? "";
          const isTakeProfitClosed = normalizedExitReason === "takeprofit";
          const isStopLossClosed = normalizedExitReason === "stoploss";

          if (takeProfitPrice !== null && takeProfitPrice !== entryPrice) {
            const takeProfitProgress = toProgress(takeProfitPrice);
            if (takeProfitProgress > progressEpsilon) {
              const executedProfitProgress = isTakeProfitClosed
                ? takeProfitProgress
                : exitPrice === null
                  ? 0
                  : clamp(toProgress(exitPrice), 0, takeProfitProgress);
              appendProgressZone("executedProfit", 0, executedProfitProgress, { subtractOccupied: false });
              if (!isTakeProfitClosed) {
                appendProgressZone("takeProfitPlan", executedProfitProgress, takeProfitProgress);
              }
            }
          }

          if (stopLossPrice !== null && stopLossPrice !== entryPrice) {
            const stopLossProgress = -toProgress(stopLossPrice);
            if (stopLossProgress > progressEpsilon) {
              const executedLossProgress = isStopLossClosed
                ? stopLossProgress
                : exitPrice === null
                  ? 0
                  : clamp(-toProgress(exitPrice), 0, stopLossProgress);
              appendProgressZone("executedLoss", -executedLossProgress, 0, { subtractOccupied: false });
              if (!isStopLossClosed) {
                appendProgressZone("stopLossPlan", -stopLossProgress, -executedLossProgress);
              }
            }
          }

          if (focusZones.length === 0 && exitPrice !== null && exitPrice !== entryPrice) {
            const exitProgress = toProgress(exitPrice);
            if (exitProgress > 0) {
              appendProgressZone("executedProfit", 0, exitProgress, { subtractOccupied: false });
            } else {
              appendProgressZone("executedLoss", exitProgress, 0, { subtractOccupied: false });
            }
          }
        }

        clearFocusOverlays();
        logKlineTrace("focus:overlay:create:start", {
          traceId,
          rawStart,
          rawEnd,
          side: focusRange.side,
          exitReason: focusRange.exitReason,
          entryPrice,
          exitPrice,
          stopLossPrice,
          takeProfitPrice,
          high,
          low,
          zones: focusZones.map((zone) => ({
            kind: zone.kind,
            upper: zone.upper,
            lower: zone.lower,
          })),
        });
        const createdOverlayIds: string[] = [];
        const appendOverlayIds = (value: string | string[] | null | undefined) => {
          if (!value) {
            return;
          }
          if (Array.isArray(value)) {
            createdOverlayIds.push(...value.filter((id): id is string => Boolean(id)));
            return;
          }
          createdOverlayIds.push(value);
        };
        for (const zone of focusZones) {
          const created = chart.createOverlay({
            name: "rect",
            lock: true,
            styles: {
              polygon: {
                style: "fill",
                color: zone.color,
                borderSize: 0,
                borderColor: "rgba(0, 0, 0, 0)",
              },
            },
            points: [
              { timestamp: rawStart, value: zone.upper },
              { timestamp: rawEnd, value: zone.lower },
            ],
          } as any);
          appendOverlayIds(created as string | string[] | null | undefined);
        }
        if (entryPrice !== null && exitPrice !== null) {
          const link = chart.createOverlay({
            name: "segment",
            lock: true,
            styles: {
              line: {
                style: "dashed",
                size: 1,
                color: "rgba(30, 41, 59, 0.82)",
                dashedValue: [6, 4],
                smooth: false,
              },
            },
            points: [
              { timestamp: rawStart, value: entryPrice },
              { timestamp: rawEnd, value: exitPrice },
            ],
          } as any);
          appendOverlayIds(link as string | string[] | null | undefined);
        }
        focusOverlayIdsRef.current = createdOverlayIds;
        logKlineTrace("focus:overlay:create:done", {
          traceId,
          zoneCount: focusZones.length,
          overlayIds: focusOverlayIdsRef.current,
        });

        const midTimestamp = Math.floor((rawStart + rawEnd) / 2);
        logKlineTrace("focus:scroll:start", { traceId, midTimestamp });
        chart.scrollToTimestamp(midTimestamp);
        logKlineTrace("focus:scroll:done", { traceId, midTimestamp });

        const alignFocusedRangeToCenter = () => {
          if (disposed || !isDataVersionCurrent(focusDataVersion)) {
            return;
          }
          const paneSize =
            chart.getSize(CANDLE_PANE_ID, DomPosition.Main) ??
            chart.getSize(undefined, DomPosition.Main) ??
            chart.getSize();
          const paneWidth = paneSize?.width ?? 0;
          if (!Number.isFinite(paneWidth) || paneWidth <= 0) {
            logKlineTrace("focus:center:skip:no-pane-size", { traceId, paneSize });
            return;
          }

          const toXAxis = (timestamp: number) => {
            const pixel = chart.convertToPixel(
              { timestamp, value: fallbackValue },
              { paneId: CANDLE_PANE_ID, absolute: false }
            ) as Partial<Coordinate> | Array<Partial<Coordinate>>;
            const coordinate = Array.isArray(pixel) ? pixel[0] : pixel;
            if (!coordinate) {
              return null;
            }
            const x = coordinate.x;
            return typeof x === "number" && Number.isFinite(x) ? x : null;
          };

          const measureCenterOffset = () => {
            const startX = toXAxis(rawStart);
            const endX = toXAxis(rawEnd);
            if (startX === null || endX === null) {
              return null;
            }
            const currentCenterX = (startX + endX) / 2;
            const targetCenterX = paneWidth / 2;
            return {
              startX,
              endX,
              currentCenterX,
              targetCenterX,
              deltaX: targetCenterX - currentCenterX,
            };
          };

          const before = measureCenterOffset();
          if (!before) {
            logKlineTrace("focus:center:skip:measure-failed", {
              traceId,
              rawStart,
              rawEnd,
            });
            return;
          }

          logKlineTrace("focus:center:measure", { traceId, ...before });
          if (Math.abs(before.deltaX) <= 1) {
            logKlineTrace("focus:center:already-centered", { traceId, deltaX: before.deltaX });
            return;
          }

          chart.scrollByDistance(before.deltaX, 0);
          const after = measureCenterOffset();
          logKlineTrace("focus:center:after-scroll", {
            traceId,
            beforeDeltaX: before.deltaX,
            afterDeltaX: after?.deltaX ?? null,
          });

          if (after && Math.abs(after.deltaX) > Math.abs(before.deltaX)) {
            // 若方向判断与组件内部实现相反，反向纠偏
            chart.scrollByDistance(-2 * before.deltaX, 0);
            const corrected = measureCenterOffset();
            logKlineTrace("focus:center:after-reverse", {
              traceId,
              correctedDeltaX: corrected?.deltaX ?? null,
            });
          }
        };

        alignRafId = window.requestAnimationFrame(() => {
          alignFocusedRangeToCenter();
        });
      } catch (err) {
        if (!disposed) {
          logKlineTrace("focus:error", { traceId, err });
          console.error("[Klinecharts] Failed to load focused range", err);
          exitFocusMode("focus-error", true);
        }
      }
    };

    loadFocusedRange();
    return () => {
      disposed = true;
      controller.abort();
      if (alignRafId !== null) {
        window.cancelAnimationFrame(alignRafId);
        alignRafId = null;
      }
      logKlineTrace("focus:cleanup", { traceId });
    };
  }, [focusRange, activeSymbol, activeInterval]);

  useEffect(() => {
    const handleFullscreenChange = () => {
      setIsFullscreen(Boolean(document.fullscreenElement));
    };
    document.addEventListener("fullscreenchange", handleFullscreenChange);
    return () => document.removeEventListener("fullscreenchange", handleFullscreenChange);
  }, []);

  const handleToggleIndicator = (option: IndicatorOption) => {
    const chart = chartRef.current;
    if (!chart) {
      return;
    }
    const paneId = indicatorPaneRef.current.get(option.value);
    if (paneId) {
      chart.removeIndicator(paneId, option.value);
      indicatorPaneRef.current.delete(option.value);
      setActiveIndicators((prev) => prev.filter((name) => name !== option.value));
      return;
    }
    const createdPaneId = chart.createIndicator(
      option.value,
      option.stack,
      getIndicatorPaneOptions(option)
    );
    if (createdPaneId) {
      indicatorPaneRef.current.set(option.value, createdPaneId);
      setActiveIndicators((prev) => [...prev, option.value]);
    }
  };

  const handleStartDrawing = (overlay: string, id: string) => {
    setActiveDrawing(id);
    if (!overlay) {
      return;
    }
    const chart = chartRef.current;
    if (!chart) {
      return;
    }
    const mode = isMagnetEnabled ? magnetMode : "normal";
    // klinecharts 9.x 需要传入对象格式
    const created = chart.createOverlay({
      name: overlay,
      lock: isDrawingLocked,
      visible: isDrawingVisible,
      mode,
    });
    if (created) {
      overlayIdsRef.current = [...overlayIdsRef.current, created as string];
    }
  };

  const handleClearDrawings = () => {
    const chart = chartRef.current;
    if (!chart) {
      return;
    }
    for (const id of overlayIdsRef.current) {
      chart.removeOverlay(id);
    }
    overlayIdsRef.current = [];
    setActiveDrawing("cursor");
  };

  const handleToggleDrawingsVisible = () => {
    const chart = chartRef.current;
    if (!chart) {
      return;
    }
    const nextVisible = !isDrawingVisible;
    const anyChart = chart as unknown as { overrideOverlay?: (options: { id: string; visible: boolean }) => void };
    if (anyChart.overrideOverlay) {
      for (const id of overlayIdsRef.current) {
        anyChart.overrideOverlay({ id, visible: nextVisible });
      }
    }
    setIsDrawingVisible(nextVisible);
  };

  const handleScreenshot = () => {
    const chart = chartRef.current;
    if (!chart) {
      return;
    }
    const url = chart.getConvertPictureUrl(true);
    if (!url) {
      return;
    }
    const link = document.createElement("a");
    link.href = url;
    link.download = `${displaySymbol || "chart"}.png`;
    link.click();
  };

  const handleFullscreen = async () => {
    if (!shellRef.current) {
      return;
    }
    if (!document.fullscreenElement) {
      await shellRef.current.requestFullscreen();
    } else {
      await document.exitFullscreen();
    }
  };

  const formattedTime = latestBar ? formatDisplayTime(latestBar.timestamp) : "--";
  const openText = latestBar ? formatNumber(latestBar.open) : "--";
  const highText = latestBar ? formatNumber(latestBar.high) : "--";
  const lowText = latestBar ? formatNumber(latestBar.low) : "--";
  const closeText = latestBar ? formatNumber(latestBar.close) : "--";
  const volumeText = latestBar ? formatNumber(latestBar.volume ?? 0) : "--";
  const showBoll = activeIndicators.includes("BOLL");

  const currentSymbolOption = SYMBOL_OPTIONS.find((opt) => opt.value === activeSymbol) || SYMBOL_OPTIONS[0];

  return (
    <div ref={shellRef} className="market-chart-shell">
      <div className="market-chart-header">
        <div className="market-chart-header-left">
          <div className="market-chart-symbol-selector">
            <button
              type="button"
              className={`market-chart-symbol-button ${showSymbolSelector ? "is-active" : ""}`}
              onClick={() => {
                setShowSymbolSelector((prev) => !prev);
                setShowIndicators(false);
                setShowTimezone(false);
                setShowSettings(false);
              }}
            >
              <span
                className="market-chart-symbol-badge"
                style={{ background: currentSymbolOption.color }}
              >
                {currentSymbolOption.badge}
              </span>
              <span className="market-chart-symbol-text">{displaySymbol || activeSymbol}</span>
              <svg className="market-chart-symbol-arrow" viewBox="0 0 12 12" width="12" height="12">
                <path d="M3 4.5L6 7.5L9 4.5" stroke="currentColor" strokeWidth="1.5" fill="none" />
              </svg>
            </button>
            {showSymbolSelector && (
              <div ref={symbolPanelRef} className="market-chart-symbol-popover">
                <div className="market-chart-popover-title">选择交易对</div>
                <div className="market-chart-symbol-list">
                  {SYMBOL_OPTIONS.map((opt) => (
                    <button
                      key={opt.value}
                      type="button"
                      className={`market-chart-symbol-item ${activeSymbol === opt.value ? "is-active" : ""}`}
                      onClick={() => {
                        setActiveSymbol(opt.value);
                        setShowSymbolSelector(false);
                      }}
                    >
                      <span className="market-chart-symbol-badge" style={{ background: opt.color }}>
                        {opt.badge}
                      </span>
                      <span>{opt.label}</span>
                    </button>
                  ))}
                </div>
              </div>
            )}
          </div>
          <div className="market-chart-periods">
            {PERIOD_TABS.map((tab) => (
              <button
                key={tab.label}
                type="button"
                className={`market-chart-period ${
                  activeInterval === tab.value ? "is-active" : ""
                } ${tab.disabled ? "is-disabled" : ""}`}
                onClick={() => {
                  if (tab.disabled) {
                    return;
                  }
                  setActiveInterval(tab.value);
                }}
                disabled={tab.disabled}
              >
                {tab.label}
              </button>
            ))}
          </div>
        </div>
        <div className="market-chart-header-right">
          <div className="market-chart-action-group">
            <button
              type="button"
              className={`market-chart-action ${showIndicators ? "is-active" : ""}`}
              onClick={() => {
                setShowIndicators((prev) => !prev);
                setShowTimezone(false);
                setShowSettings(false);
              }}
            >
              <span className="market-chart-action-icon">指标</span>
            </button>
            {showIndicators && (
              <div className="klinecharts-pro-modal">
                <div ref={indicatorPanelRef} className="klinecharts-pro-modal-inner">
                  <div className="klinecharts-pro-modal-title">指标</div>
                  <div className="klinecharts-pro-modal-content">
                    <div className="klinecharts-pro-modal-section">
                      <div className="klinecharts-pro-modal-section-title">主图指标</div>
                      <div className="klinecharts-pro-modal-list">
                        {indicatorOptions
                          .filter((option) => option.pane === "main")
                          .map((option) => {
                            const isActive = activeIndicators.includes(option.value);
                            return (
                              <button
                                key={`main-${option.value}`}
                                type="button"
                                className={`klinecharts-pro-modal-item ${isActive ? "is-active" : ""}`}
                                onClick={() => handleToggleIndicator(option)}
                              >
                                <div className="klinecharts-pro-modal-item-text">
                                  <span className="name">{option.label}</span>
                                  {option.description && <span className="desc">{option.description}</span>}
                                </div>
                                <span className={`switch ${isActive ? "on" : "off"}`}>
                                  <span className="handle" />
                                </span>
                              </button>
                            );
                          })}
                      </div>
                    </div>
                    <div className="klinecharts-pro-modal-section">
                      <div className="klinecharts-pro-modal-section-title">副图指标</div>
                      <div className="klinecharts-pro-modal-list">
                        {indicatorOptions
                          .filter((option) => option.pane !== "main")
                          .map((option) => {
                            const isActive = activeIndicators.includes(option.value);
                            return (
                              <button
                                key={`sub-${option.value}`}
                                type="button"
                                className={`klinecharts-pro-modal-item ${isActive ? "is-active" : ""}`}
                                onClick={() => handleToggleIndicator(option)}
                              >
                                <div className="klinecharts-pro-modal-item-text">
                                  <span className="name">{option.label}</span>
                                  {option.description && <span className="desc">{option.description}</span>}
                                </div>
                                <span className={`switch ${isActive ? "on" : "off"}`}>
                                  <span className="handle" />
                                </span>
                              </button>
                            );
                          })}
                      </div>
                    </div>
                  </div>
                </div>
              </div>
            )}
          </div>
          <div className="market-chart-action-group">
            <button
              type="button"
              className={`market-chart-action ${showTimezone ? "is-active" : ""}`}
              onClick={() => {
                setShowTimezone((prev) => !prev);
                setShowIndicators(false);
                setShowSettings(false);
              }}
            >
              <span className="market-chart-action-icon">时区</span>
            </button>
            {showTimezone && (
              <div ref={timezonePanelRef} className="market-chart-popover">
                <div className="market-chart-popover-title">时区</div>
                <div className="market-chart-popover-list">
                  {TIMEZONE_OPTIONS.map((option) => (
                    <button
                      key={option.value}
                      type="button"
                      className={`market-chart-popover-item ${
                        activeTimezone === option.value ? "is-active" : ""
                      }`}
                      onClick={() => setActiveTimezone(option.value)}
                    >
                      {option.label}
                    </button>
                  ))}
                </div>
              </div>
            )}
          </div>
          <div className="market-chart-action-group">
            <button
              type="button"
              className={`market-chart-action ${showSettings ? "is-active" : ""}`}
              onClick={() => {
                setShowSettings((prev) => !prev);
                setShowIndicators(false);
                setShowTimezone(false);
              }}
            >
              <span className="market-chart-action-icon">设置</span>
            </button>
            {showSettings && (
              <div ref={settingsPanelRef} className="market-chart-popover market-chart-settings-popover">
                <div className="market-chart-popover-title">设置</div>
                <div className="market-chart-settings-groups">
                  <div className="market-chart-settings-group">
                    <div className="market-chart-settings-title">显示</div>
                    <button
                      type="button"
                      className={`market-chart-settings-item ${showLatestPrice ? "is-active" : ""}`}
                      onClick={() => setShowLatestPrice((prev) => !prev)}
                    >
                      <span className="label">最新价显示</span>
                      <span className={`toggle ${showLatestPrice ? "on" : "off"}`}>
                        <span className="handle" />
                      </span>
                    </button>
                    <button
                      type="button"
                      className={`market-chart-settings-item ${showHighPrice ? "is-active" : ""}`}
                      onClick={() => setShowHighPrice((prev) => !prev)}
                    >
                      <span className="label">最高价显示</span>
                      <span className={`toggle ${showHighPrice ? "on" : "off"}`}>
                        <span className="handle" />
                      </span>
                    </button>
                    <button
                      type="button"
                      className={`market-chart-settings-item ${showLowPrice ? "is-active" : ""}`}
                      onClick={() => setShowLowPrice((prev) => !prev)}
                    >
                      <span className="label">最低价显示</span>
                      <span className={`toggle ${showLowPrice ? "on" : "off"}`}>
                        <span className="handle" />
                      </span>
                    </button>
                    <button
                      type="button"
                      className={`market-chart-settings-item ${showIndicatorLastValue ? "is-active" : ""}`}
                      onClick={() => setShowIndicatorLastValue((prev) => !prev)}
                    >
                      <span className="label">指标最新值显示</span>
                      <span className={`toggle ${showIndicatorLastValue ? "on" : "off"}`}>
                        <span className="handle" />
                      </span>
                    </button>
                    <button
                      type="button"
                      className={`market-chart-settings-item ${invertYAxis ? "is-active" : ""}`}
                      onClick={() => setInvertYAxis((prev) => !prev)}
                    >
                      <span className="label">倒置坐标</span>
                      <span className={`toggle ${invertYAxis ? "on" : "off"}`}>
                        <span className="handle" />
                      </span>
                    </button>
                    <button
                      type="button"
                      className={`market-chart-settings-item ${showGrid ? "is-active" : ""}`}
                      onClick={() => setShowGrid((prev) => !prev)}
                    >
                      <span className="label">网格线显示</span>
                      <span className={`toggle ${showGrid ? "on" : "off"}`}>
                        <span className="handle" />
                      </span>
                    </button>
                  </div>
                  <div className="market-chart-settings-group">
                    <div className="market-chart-settings-title">蜡烛图类型</div>
                    <div className="market-chart-settings-radio-group">
                      <button
                        type="button"
                        className={`market-chart-settings-radio ${candleType === "candle_solid" ? "is-active" : ""}`}
                        onClick={() => setCandleType("candle_solid")}
                      >
                        全实心
                      </button>
                      <button
                        type="button"
                        className={`market-chart-settings-radio ${candleType === "candle_stroke" ? "is-active" : ""}`}
                        onClick={() => setCandleType("candle_stroke")}
                      >
                        全空心
                      </button>
                      <button
                        type="button"
                        className={`market-chart-settings-radio ${
                          candleType === "candle_up_stroke" ? "is-active" : ""
                        }`}
                        onClick={() => setCandleType("candle_up_stroke")}
                      >
                        涨空心
                      </button>
                      <button
                        type="button"
                        className={`market-chart-settings-radio ${
                          candleType === "candle_down_stroke" ? "is-active" : ""
                        }`}
                        onClick={() => setCandleType("candle_down_stroke")}
                      >
                        跌空心
                      </button>
                      <button
                        type="button"
                        className={`market-chart-settings-radio ${candleType === "ohlc" ? "is-active" : ""}`}
                        onClick={() => setCandleType("ohlc")}
                      >
                        OHLC
                      </button>
                      <button
                        type="button"
                        className={`market-chart-settings-radio ${candleType === "area" ? "is-active" : ""}`}
                        onClick={() => setCandleType("area")}
                      >
                        面积图
                      </button>
                    </div>
                  </div>
                  <div className="market-chart-settings-group">
                    <div className="market-chart-settings-title">价格轴类型</div>
                    <div className="market-chart-settings-radio-group">
                      <button
                        type="button"
                        className={`market-chart-settings-radio ${priceAxisType === "linear" ? "is-active" : ""}`}
                        onClick={() => setPriceAxisType("linear")}
                      >
                        线性轴
                      </button>
                      <button
                        type="button"
                        className={`market-chart-settings-radio market-chart-settings-radio--disabled ${
                          priceAxisType === "percentage" ? "is-active" : ""
                        }`}
                        disabled
                      >
                        百分比轴（暂未实现）
                      </button>
                      <button
                        type="button"
                        className={`market-chart-settings-radio market-chart-settings-radio--disabled ${
                          priceAxisType === "log" ? "is-active" : ""
                        }`}
                        disabled
                      >
                        对数轴（暂未实现）
                      </button>
                    </div>
                  </div>
                  <div className="market-chart-settings-group">
                    <div className="market-chart-settings-title">其它</div>
                    <div className="market-chart-settings-radio-group">
                      <button
                        type="button"
                        className="market-chart-settings-radio"
                        onClick={handleClearDrawings}
                      >
                        清除绘图
                      </button>
                      <button
                        type="button"
                        className="market-chart-settings-radio"
                        onClick={() => setReloadKey((prev) => prev + 1)}
                      >
                        重载数据
                      </button>
                    </div>
                  </div>
                </div>
              </div>
            )}
          </div>
          <button type="button" className="market-chart-action" onClick={handleScreenshot}>
            <span className="market-chart-action-icon">截图</span>
          </button>
          <button
            type="button"
            className={`market-chart-action ${isFullscreen ? "is-active" : ""}`}
            onClick={handleFullscreen}
          >
            <span className="market-chart-action-icon">全屏</span>
          </button>
        </div>
      </div>
      <div className="market-chart-body" style={{ height }}>
        <div ref={drawingBarRef} className="klinecharts-pro-drawing-bar">
          {filteredToolGroups.map((group) => {
            const selectedToolId = selectedToolPerGroup[group.id] || group.defaultIcon || group.tools[0]?.id;
            const selectedTool = group.tools.find((t) => t.id === selectedToolId) || group.tools[0];
            const isExpanded = expandedToolGroup === group.id;

            return (
              <div key={group.id} className="item" tabIndex={0}>
                <span
                  className="icon-overlay"
                  onClick={() => {
                    if (selectedTool) {
                      handleStartDrawing(selectedTool.overlay, selectedTool.id);
                    }
                  }}
                  title={selectedTool?.label || group.label}
                >
                  <ToolIcon id={selectedTool?.id || group.defaultIcon} />
                </span>
                <div
                  className="icon-arrow"
                  onClick={() => {
                    if (isExpanded) {
                      setExpandedToolGroup(null);
                      return;
                    }
                    setToolListDirection("down");
                    setExpandedToolGroup(group.id);
                  }}
                >
                  <svg viewBox="0 0 4 6" className={isExpanded ? "rotate" : ""}>
                    <path
                      d="M1.07298,0.159458C0.827521,-0.0531526,0.429553,-0.0531526,0.184094,0.159458C-0.0613648,0.372068,-0.0613648,0.716778,0.184094,0.929388L2.61275,3.03303L0.260362,5.07061C0.0149035,5.28322,0.0149035,5.62793,0.260362,5.84054C0.505822,6.05315,0.903789,6.05315,1.14925,5.84054L3.81591,3.53075C4.01812,3.3556,4.05374,3.0908,3.92279,2.88406C3.93219,2.73496,3.87113,2.58315,3.73964,2.46925L1.07298,0.159458Z"
                      fill="currentColor"
                    />
                  </svg>
                </div>
                {isExpanded && (
                  <ul
                    ref={expandedListRef}
                    className={`list ${toolListDirection === "up" ? "is-up" : ""}`}
                  >
                    {group.tools.map((tool) => {
                      const isToolActive = activeDrawing === tool.id;
                      return (
                        <li
                          key={tool.id}
                          onClick={() => {
                            setSelectedToolPerGroup((prev) => ({ ...prev, [group.id]: tool.id }));
                            handleStartDrawing(tool.overlay, tool.id);
                            setExpandedToolGroup(null);
                          }}
                        >
                          <span className={`icon-overlay ${isToolActive ? "selected" : ""}`}>
                            <ToolIcon id={tool.id} />
                          </span>
                          <span style={{ paddingLeft: 8 }}>{tool.label}</span>
                        </li>
                      );
                    })}
                  </ul>
                )}
              </div>
            );
          })}
          <span className="split-line" />
          <div className="item" tabIndex={0}>
            <span
              className={`icon-overlay ${isMagnetEnabled ? "selected" : ""}`}
              onClick={() => setIsMagnetEnabled((prev) => !prev)}
              title={isMagnetEnabled ? "磁吸关闭" : "磁吸开启"}
            >
              {magnetMode === "weak_magnet"
                ? isMagnetEnabled
                  ? renderIconAsset(MagnetWeakOnIcon)
                  : renderIconAsset(MagnetWeakOffIcon)
                : isMagnetEnabled
                  ? renderIconAsset(MagnetStrongOnIcon)
                  : renderIconAsset(MagnetStrongOffIcon)}
            </span>
            <div
              className="icon-arrow"
              onClick={() => {
                if (expandedToolGroup === "magnet") {
                  setExpandedToolGroup(null);
                  return;
                }
                setToolListDirection("down");
                setExpandedToolGroup("magnet");
              }}
            >
              <svg viewBox="0 0 4 6" className={expandedToolGroup === "magnet" ? "rotate" : ""}>
                <path
                  d="M1.07298,0.159458C0.827521,-0.0531526,0.429553,-0.0531526,0.184094,0.159458C-0.0613648,0.372068,-0.0613648,0.716778,0.184094,0.929388L2.61275,3.03303L0.260362,5.07061C0.0149035,5.28322,0.0149035,5.62793,0.260362,5.84054C0.505822,6.05315,0.903789,6.05315,1.14925,5.84054L3.81591,3.53075C4.01812,3.3556,4.05374,3.0908,3.92279,2.88406C3.93219,2.73496,3.87113,2.58315,3.73964,2.46925L1.07298,0.159458Z"
                  fill="currentColor"
                />
              </svg>
            </div>
            {expandedToolGroup === "magnet" && (
              <ul
                ref={expandedListRef}
                className={`list ${toolListDirection === "up" ? "is-up" : ""}`}
              >
                <li
                  onClick={() => {
                    setMagnetMode("weak_magnet");
                    setExpandedToolGroup(null);
                  }}
                >
                  <span className="icon-overlay">
                    {renderIconAsset(MagnetWeakOffIcon)}
                  </span>
                  <span style={{ paddingLeft: 8 }}>弱磁模式</span>
                </li>
                <li
                  onClick={() => {
                    setMagnetMode("strong_magnet");
                    setExpandedToolGroup(null);
                  }}
                >
                  <span className="icon-overlay">
                    {renderIconAsset(MagnetStrongOffIcon)}
                  </span>
                  <span style={{ paddingLeft: 8 }}>强磁模式</span>
                </li>
              </ul>
            )}
          </div>
          <div className="item" tabIndex={0}>
            <span
              className={`icon-overlay ${isDrawingLocked ? "selected" : ""}`}
              onClick={() => setIsDrawingLocked((prev) => !prev)}
              title={isDrawingLocked ? "解锁" : "锁定"}
            >
              <ToolIcon id={isDrawingLocked ? "lock" : "unlock"} />
            </span>
          </div>
          <div className="item" tabIndex={0}>
            <span
              className={`icon-overlay ${!isDrawingVisible ? "selected" : ""}`}
              onClick={handleToggleDrawingsVisible}
              title={isDrawingVisible ? "隐藏所有画图" : "显示所有画图"}
            >
              {isDrawingVisible ? (
                renderIconAsset(DrawingHideAllIcon)
              ) : (
                renderIconAsset(DrawingShowAllIcon)
              )}
            </span>
          </div>
          <div className="item" tabIndex={0}>
            <span
              className="icon-overlay"
              onClick={handleClearDrawings}
              title="删除所有绘图"
            >
              {renderIconAsset(DrawingClearAllIcon)}
            </span>
          </div>
        </div>
        <div className="market-chart-stage">
          <div className="market-chart-info">
            <span>时间: {formattedTime}</span>
            <span>开: {openText}</span>
            <span>高: {highText}</span>
            <span>低: {lowText}</span>
            <span>收: {closeText}</span>
            <span>成交量: {volumeText}</span>
          </div>
          {showBoll && (
            <div className="market-chart-indicators">
              <span className="market-chart-indicator-item">
                <span className="market-chart-indicator-dot boll-up" /> BOLL(20,2)
              </span>
              <span className="market-chart-indicator-item">
                <span className="market-chart-indicator-dot boll-mid" /> MID
              </span>
              <span className="market-chart-indicator-item">
                <span className="market-chart-indicator-dot boll-low" /> DN
              </span>
            </div>
          )}
          <div
            className={`market-chart-wrapper ${
              theme === "dark" ? "market-chart-wrapper--dark" : "market-chart-wrapper--light"
            }`}
          >
            <div ref={containerRef} className="market-chart-container" />
            <div className="market-chart-currency">USD</div>
          </div>
        </div>
      </div>
    </div>
  );
};

export default MarketChart;

type OHLCV = {
  timestamp?: number | null;
  open?: number | null;
  high?: number | null;
  low?: number | null;
  close?: number | null;
  volume?: number | null;
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

function toSymbolEnum(symbol: string): string {
  return symbol.replace("/", "_").replace("-", "_").toUpperCase();
}

async function fetchHistory(options: {
  http: HttpClient;
  exchange: string;
  symbol: string;
  timeframe: string;
  intervalMs: number;
  count: number;
  endTime?: number;
  startTime?: number;
  signal: AbortSignal;
  traceId?: string;
  traceSource?: string;
}): Promise<KLineData[]> {
  const traceId = options.traceId || buildTraceId("history");
  const traceSource = options.traceSource || "unknown";
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
  const requestStartedAt = Date.now();
  logKlineTrace("protocol:send", {
    traceId,
    source: traceSource,
    url: "/api/marketdata/history",
    type: "marketdata.kline.history",
    payload,
  });

  const data = await options.http.postProtocol<OHLCV[]>(
    "/api/marketdata/history",
    "marketdata.kline.history",
    payload,
    { signal: options.signal }
  );
  const elapsedMs = Date.now() - requestStartedAt;
  logKlineTrace("protocol:recv", {
    traceId,
    source: traceSource,
    type: "marketdata.kline.history",
    rawCount: data.length,
    elapsedMs,
    firstRawTs: data[0]?.timestamp ?? null,
    lastRawTs: data[data.length - 1]?.timestamp ?? null,
  });

  const bars = data
    .map((item) => toKLine(item))
    .filter((bar): bar is KLineData => bar !== null)
    .sort((a, b) => a.timestamp - b.timestamp);
  logKlineTrace("history:normalized", {
    traceId,
    source: traceSource,
    barsCount: bars.length,
    firstBarTs: bars[0]?.timestamp ?? null,
    lastBarTs: bars[bars.length - 1]?.timestamp ?? null,
  });
  return bars;
}

function toKLine(item: OHLCV): KLineData | null {
  if (!item.timestamp) {
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

function formatDateTime(ms: number): string {
  const date = new Date(ms);
  const pad = (value: number) => String(value).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(
    date.getMinutes()
  )}:${pad(date.getSeconds())}`;
}

function formatDisplayTime(ms: number): string {
  const date = new Date(ms);
  const pad = (value: number) => String(value).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(
    date.getMinutes()
  )}:${pad(date.getSeconds())}`;
}

function formatNumber(value: number): string {
  if (!Number.isFinite(value)) {
    return "--";
  }
  if (Math.abs(value) >= 1_000_000) {
    return `${(value / 1_000_000).toFixed(2)}M`;
  }
  if (Math.abs(value) >= 1_000) {
    return `${(value / 1_000).toFixed(2)}K`;
  }
  return value.toFixed(2);
}

function renderIconAsset(icon: IconAsset | undefined) {
  if (!icon) {
    return null;
  }
  if (typeof icon === "string") {
    return (
      <img
        src={icon}
        alt=""
        aria-hidden="true"
        draggable={false}
        style={{ width: "100%", height: "100%", objectFit: "contain" }}
      />
    );
  }
  const IconComponent = icon;
  return <IconComponent aria-hidden="true" />;
}

function ToolIcon({ id }: { id: string }) {
  const lineIcon = renderIconAsset(LINE_TOOL_ICON_MAP[id]);
  if (lineIcon) {
    return lineIcon;
  }

  const channelAndShapeIcon = renderIconAsset(CHANNEL_AND_SHAPE_TOOL_ICON_MAP[id]);
  if (channelAndShapeIcon) {
    return channelAndShapeIcon;
  }

  const fibIcon = renderIconAsset(FIB_TOOL_ICON_MAP[id]);
  if (fibIcon) {
    return fibIcon;
  }

  const waveIcon = renderIconAsset(WAVE_TOOL_ICON_MAP[id]);
  if (waveIcon) {
    return waveIcon;
  }

  switch (id) {
    // 功能图标
    case "lock":
      return renderIconAsset(DrawingLockOnIcon);
    case "unlock":
      return renderIconAsset(DrawingLockOffIcon);
    case "visible":
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <ellipse cx="12" cy="12" rx="9" ry="5" stroke="currentColor" strokeWidth="2" fill="none" />
          <circle cx="12" cy="12" r="3" stroke="currentColor" strokeWidth="2" fill="none" />
        </svg>
      );
    case "invisible":
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <ellipse cx="12" cy="12" rx="9" ry="5" stroke="currentColor" strokeWidth="2" fill="none" />
          <line x1="4" y1="20" x2="20" y2="4" stroke="currentColor" strokeWidth="2" />
        </svg>
      );
    case "trash":
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <rect x="7" y="9" width="10" height="11" rx="1" stroke="currentColor" strokeWidth="1.5" fill="none" />
          <line x1="5" y1="6" x2="19" y2="6" stroke="currentColor" strokeWidth="1.5" />
          <line x1="10" y1="6" x2="10" y2="4" stroke="currentColor" strokeWidth="1.5" />
          <line x1="14" y1="6" x2="14" y2="4" stroke="currentColor" strokeWidth="1.5" />
          <line x1="10" y1="12" x2="10" y2="17" stroke="currentColor" strokeWidth="1" />
          <line x1="14" y1="12" x2="14" y2="17" stroke="currentColor" strokeWidth="1" />
        </svg>
      );
    default:
      return (
        <svg viewBox="0 0 24 24" aria-hidden="true">
          <circle cx="12" cy="12" r="6" stroke="currentColor" strokeWidth="2" fill="none" />
        </svg>
      );
  }
}
