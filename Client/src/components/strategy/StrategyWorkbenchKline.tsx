import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { ActionType, LoadDataType, dispose, init, type Chart, type KLineData } from 'klinecharts';

import { HttpClient, getToken } from '../../network/index.ts';
import type { GeneratedIndicatorPayload } from '../indicator/IndicatorGeneratorSelector';
import {
  loadLocalKlineBars,
  loadLocalKlineBarsBefore,
  loadLocalKlineBarsWithCloudFallback,
} from '../../lib/klineOfflinePackageManager';
import {
  getTalibIndicatorEditorSchema,
  getTalibIndicatorMetaList,
  normalizeTalibInputSource,
  registerTalibIndicators,
} from '../../lib/registerTalibIndicators';
import { registerCustomOverlays } from '../market/customOverlays';

export type StrategyWorkbenchTradeFocusRange = {
  id: string;
  startTime: number;
  endTime: number;
  side: string;
  entryPrice?: number | null;
  exitPrice?: number | null;
  stopLossPrice?: number | null;
  takeProfitPrice?: number | null;
};

export type StrategyWorkbenchVisibleRange = {
  fromIndex: number;
  toIndex: number;
  fromTime: number;
  toTime: number;
  centerTime: number;
};

type StrategyWorkbenchPreviewMode = 'normal' | 'full';

interface StrategyWorkbenchKlineProps {
  exchange: string;
  symbol: string;
  timeframeSec: number;
  selectedIndicators: GeneratedIndicatorPayload[];
  previewMode?: StrategyWorkbenchPreviewMode;
  focusRange?: StrategyWorkbenchTradeFocusRange | null;
  fullPreviewRanges?: StrategyWorkbenchTradeFocusRange[];
  syncTargetRange?: StrategyWorkbenchTradeFocusRange | null;
  onVisibleRangeChange?: (range: StrategyWorkbenchVisibleRange) => void;
  hoverValueId?: string;
  hoverHasReference?: boolean;
  onBarsUpdate?: (bars: KLineData[]) => void;
}

type OhlcvDto = {
  timestamp?: number | null;
  open?: number | null;
  high?: number | null;
  low?: number | null;
  close?: number | null;
  volume?: number | null;
};

const TIMEFRAME_TO_KEY: Record<number, string> = {
  60: 'm1',
  180: 'm3',
  300: 'm5',
  900: 'm15',
  1800: 'm30',
  3600: 'h1',
  7200: 'h2',
  14400: 'h4',
  21600: 'h6',
  28800: 'h8',
  43200: 'h12',
  86400: 'd1',
  259200: 'd3',
  604800: 'w1',
  2592000: '1mo',
};

const TIMEFRAME_TO_LOCAL_KEY: Record<number, string> = {
  60: '1m',
  180: '3m',
  300: '5m',
  900: '15m',
  1800: '30m',
  3600: '1h',
  7200: '2h',
  14400: '4h',
  21600: '6h',
  28800: '8h',
  43200: '12h',
  86400: '1d',
  259200: '3d',
  604800: '1w',
  2592000: '1mo',
};

const TIMEFRAME_TO_MS: Record<number, number> = {
  60: 60_000,
  180: 180_000,
  300: 300_000,
  900: 900_000,
  1800: 1_800_000,
  3600: 3_600_000,
  7200: 7_200_000,
  14400: 14_400_000,
  21600: 21_600_000,
  28800: 28_800_000,
  43200: 43_200_000,
  86400: 86_400_000,
  259200: 259_200_000,
  604800: 604_800_000,
  2592000: 2_592_000_000,
};

const DEFAULT_INITIAL_LOOKBACK_DAYS = 30;
const MAX_INITIAL_LOAD_BAR_COUNT = 50_000;
const LOAD_MORE_BAR_COUNT = 1200;
const FULL_PREVIEW_OVERLAY_MIN_COUNT = 48;
const FULL_PREVIEW_OVERLAY_MAX_COUNT = 220;

// 按周期换算“最近30天”需要的K线条数，作为工作台默认样本窗口。
const resolveInitialLoadBarCount = (intervalMs: number) => {
  if (!Number.isFinite(intervalMs) || intervalMs <= 0) {
    return 720;
  }
  const estimated = Math.ceil((DEFAULT_INITIAL_LOOKBACK_DAYS * 86_400_000) / intervalMs);
  return Math.max(1, Math.min(MAX_INITIAL_LOAD_BAR_COUNT, estimated));
};

const normalizeExchangeForLocal = (value: string) => (value || '').trim().toLowerCase() || 'binance';

const normalizeSymbolForLocal = (value: string) => {
  const normalized = (value || 'BTC/USDT').replaceAll('_', '/').replaceAll('-', '/').toUpperCase();
  if (normalized.includes('/')) {
    const parts = normalized.split('/').filter(Boolean);
    if (parts.length === 2) {
      return `${parts[0]}/${parts[1]}`;
    }
  }
  if (normalized.endsWith('USDT') && normalized.length > 4) {
    return `${normalized.slice(0, -4)}/USDT`;
  }
  return normalized;
};

const normalizeExchange = (value: string) => {
  const lower = (value || '').trim().toLowerCase();
  if (lower === 'binance') {
    return 'Binance';
  }
  if (lower === 'bitget') {
    return 'Bitget';
  }
  if (lower === 'okx') {
    return 'Okx';
  }
  if (!lower) {
    return 'Binance';
  }
  return `${lower.slice(0, 1).toUpperCase()}${lower.slice(1)}`;
};

const normalizeSymbol = (value: string) => {
  return (value || 'BTC/USDT').replace('/', '_').replace('-', '_').toUpperCase();
};

const formatDateTime = (ms: number) => {
  const date = new Date(ms);
  const pad = (num: number) => String(num).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(
    date.getMinutes(),
  )}:${pad(date.getSeconds())}`;
};

const toFinitePrice = (value?: number | null) => {
  const num = Number(value);
  return Number.isFinite(num) ? num : null;
};

const getDefaultRiskTakeProfitPrice = (entryPrice: number, side: 'long' | 'short') => (
  side === 'short' ? entryPrice * 0.99 : entryPrice * 1.01
);

const getDefaultRiskStopLossPrice = (entryPrice: number, side: 'long' | 'short') => (
  side === 'short' ? entryPrice * 1.01 : entryPrice * 0.99
);

const clampNumber = (value: number, min: number, max: number) => Math.max(min, Math.min(max, value));

const findNearestDataIndexByTimestamp = (dataList: KLineData[], timestamp: number): number | null => {
  if (!Array.isArray(dataList) || dataList.length === 0 || !Number.isFinite(timestamp)) {
    return null;
  }
  let left = 0;
  let right = dataList.length - 1;
  while (left <= right) {
    const mid = Math.floor((left + right) / 2);
    const midTs = Number(dataList[mid]?.timestamp ?? 0);
    if (!Number.isFinite(midTs)) {
      return null;
    }
    if (midTs === timestamp) {
      return mid;
    }
    if (midTs < timestamp) {
      left = mid + 1;
    } else {
      right = mid - 1;
    }
  }
  const leftIndex = clampNumber(left, 0, dataList.length - 1);
  const rightIndex = clampNumber(right, 0, dataList.length - 1);
  const leftDiff = Math.abs(Number(dataList[leftIndex]?.timestamp ?? 0) - timestamp);
  const rightDiff = Math.abs(Number(dataList[rightIndex]?.timestamp ?? 0) - timestamp);
  return leftDiff <= rightDiff ? leftIndex : rightIndex;
};

type VisibleRangeLike = {
  from?: number;
  to?: number;
};

const normalizeTradeSide = (value?: string): 'long' | 'short' => {
  return value?.trim().toLowerCase() === 'short' ? 'short' : 'long';
};

const buildVisibleRangeSnapshotFromChart = (
  chart: Chart,
  rangeLike?: VisibleRangeLike | null,
): StrategyWorkbenchVisibleRange | null => {
  const dataList = chart.getDataList();
  if (!Array.isArray(dataList) || dataList.length <= 0) {
    return null;
  }
  const fallbackRange = chart.getVisibleRange();
  const fromRaw = Number(rangeLike?.from ?? fallbackRange.from ?? 0);
  const toRaw = Number(rangeLike?.to ?? fallbackRange.to ?? 0);
  if (!Number.isFinite(fromRaw) || !Number.isFinite(toRaw)) {
    return null;
  }
  const rawFromIndex = clampNumber(Math.floor(Math.min(fromRaw, toRaw)), 0, dataList.length - 1);
  const rawToIndex = clampNumber(Math.ceil(Math.max(fromRaw, toRaw)), 0, dataList.length - 1);
  const fromIndex = Math.min(rawFromIndex, rawToIndex);
  const toIndex = Math.max(rawFromIndex, rawToIndex);
  const centerIndex = clampNumber(Math.round((fromIndex + toIndex) / 2), 0, dataList.length - 1);
  const fromTime = Number(dataList[fromIndex]?.timestamp ?? 0);
  const toTime = Number(dataList[toIndex]?.timestamp ?? 0);
  const centerTime = Number(dataList[centerIndex]?.timestamp ?? 0);
  if (!Number.isFinite(fromTime) || !Number.isFinite(toTime) || !Number.isFinite(centerTime)) {
    return null;
  }
  return {
    fromIndex,
    toIndex,
    fromTime,
    toTime,
    centerTime,
  };
};

const toKlineData = (item: OhlcvDto): KLineData | null => {
  const timestamp = Number(item.timestamp);
  const open = Number(item.open);
  const high = Number(item.high);
  const low = Number(item.low);
  const close = Number(item.close);
  const volume = Number(item.volume ?? 0);
  if (
    !Number.isFinite(timestamp) ||
    !Number.isFinite(open) ||
    !Number.isFinite(high) ||
    !Number.isFinite(low) ||
    !Number.isFinite(close)
  ) {
    return null;
  }
  return { timestamp, open, high, low, close, volume: Number.isFinite(volume) ? volume : 0 };
};

const normalizeHistoryBars = (source: OhlcvDto[]): KLineData[] => {
  const normalized = source
    .map(toKlineData)
    .filter((item): item is KLineData => item !== null)
    .sort((a, b) => a.timestamp - b.timestamp);
  if (normalized.length <= 1) {
    return normalized;
  }
  const deduped: KLineData[] = [];
  const timestamps = new Set<number>();
  normalized.forEach((item) => {
    if (timestamps.has(item.timestamp)) {
      return;
    }
    timestamps.add(item.timestamp);
    deduped.push(item);
  });
  return deduped;
};

type IndicatorInputMap = Record<string, string>;
type IndicatorPaneType = 'main' | 'sub';

type ResolvedChartIndicator = {
  chartName: string;
  backendCode: string;
  pane: IndicatorPaneType;
  params?: number[];
  inputMap?: IndicatorInputMap;
};

const CANDLE_PANE_ID = 'candle_pane';
const BLINK_STEP_MS_REFERENCED = 108;
const BLINK_STEP_MS_BRIEF = 160;
const BLINK_PHASES_REFERENCED = [true, false, true, false, true, false];
const BLINK_PHASES_BRIEF = [true, false];
const HIGHLIGHT_COLOR_ON = 'rgba(245, 158, 11, 0.98)';
const HIGHLIGHT_COLOR_OFF = 'rgba(251, 191, 36, 0.78)';

type FigureType = 'line' | 'bar' | 'circle';
type FigureAddress = { type: FigureType; index: number };
type IndicatorHoverEntry = {
  chartName: string;
  backendCode: string;
  outputs: Array<{ key: string; hint?: string }>;
};
type IndicatorVisualTarget = {
  paneId: string;
  chartName: string;
  figure: FigureAddress;
};
type LineStyleLike = {
  color?: string;
  size?: number;
  smooth?: boolean;
  style?: unknown;
  dashedValue?: number[];
};
type BarStyleLike = {
  upColor?: string;
  downColor?: string;
  noChangeColor?: string;
  borderSize?: number;
  style?: unknown;
  borderStyle?: unknown;
  borderDashedValue?: number[];
};

type HistorySourceMode = 'none' | 'local' | 'backend';
type CircleStyleLike = {
  upColor?: string;
  downColor?: string;
  noChangeColor?: string;
  borderSize?: number;
  style?: unknown;
  borderStyle?: unknown;
  borderDashedValue?: number[];
};

const BASE_LINE_STYLE: LineStyleLike = {
  style: 'solid',
  smooth: false,
  size: 1,
  dashedValue: [2, 2],
  color: '#FF9600',
};

const BASE_BAR_STYLE: BarStyleLike = {
  style: 'fill',
  borderStyle: 'solid',
  borderSize: 1,
  borderDashedValue: [2, 2],
  upColor: 'rgba(34,197,94,0.55)',
  downColor: 'rgba(239,68,68,0.55)',
  noChangeColor: '#9ca3af',
};

const BASE_CIRCLE_STYLE: CircleStyleLike = {
  style: 'fill',
  borderStyle: 'solid',
  borderSize: 1,
  borderDashedValue: [2, 2],
  upColor: 'rgba(34,197,94,0.55)',
  downColor: 'rgba(239,68,68,0.55)',
  noChangeColor: '#9ca3af',
};

const normalizeKeyText = (value: string) => value.trim().toUpperCase();
const normalizeKey = (value?: string) => (value || '').trim().toUpperCase();

const resolveTalibMeta = (
  rawCode: string,
  byCode: Map<string, ReturnType<typeof getTalibIndicatorMetaList>[number]>,
  byChartName: Map<string, ReturnType<typeof getTalibIndicatorMetaList>[number]>,
) => {
  const normalized = normalizeKey(rawCode);
  return byCode.get(normalized)
    || byChartName.get(normalized)
    || byChartName.get(`TA_${normalized}`)
    || null;
};

const parseIndicatorOutputValueId = (valueId?: string) => {
  const normalized = (valueId || '').trim();
  if (!normalized || normalized.toLowerCase().startsWith('field:')) {
    return null;
  }
  const splitIndex = normalized.indexOf(':');
  if (splitIndex <= 0 || splitIndex >= normalized.length - 1) {
    return null;
  }
  return {
    indicatorId: normalized.slice(0, splitIndex),
    outputKey: normalized.slice(splitIndex + 1),
  };
};

const guessFigureType = (backendCode: string, output: { key: string; hint?: string }): FigureType => {
  const code = normalizeKey(backendCode);
  const hint = `${output.hint || ''} ${output.key || ''}`.toLowerCase();
  if (code === 'SAR' || code === 'SAREXT') {
    return 'circle';
  }
  if (hint.includes('histogram') || hint.includes('bar') || hint.includes('hist')) {
    return 'bar';
  }
  if (hint.includes('dot') || hint.includes('point') || hint.includes('circle')) {
    return 'circle';
  }
  return 'line';
};

const resolveFigureAddress = (
  backendCode: string,
  outputs: Array<{ key: string; hint?: string }>,
  outputKey: string,
): FigureAddress => {
  const target = normalizeKey(outputKey);
  let lineIndex = 0;
  let barIndex = 0;
  let circleIndex = 0;
  const list = outputs.length > 0 ? outputs : [{ key: outputKey, hint: outputKey }];

  for (const output of list) {
    const type = guessFigureType(backendCode, output);
    if (type === 'line') {
      const index = lineIndex;
      lineIndex += 1;
      if (normalizeKey(output.key) === target) {
        return { type, index };
      }
      continue;
    }
    if (type === 'bar') {
      const index = barIndex;
      barIndex += 1;
      if (normalizeKey(output.key) === target) {
        return { type, index };
      }
      continue;
    }
    const index = circleIndex;
    circleIndex += 1;
    if (normalizeKey(output.key) === target) {
      return { type, index };
    }
  }

  const fallbackType = guessFigureType(backendCode, list[0]);
  return { type: fallbackType, index: 0 };
};

const buildStyleList = <T extends object>(
  source: T[] | undefined,
  minCount: number,
  fallback: T,
) => {
  const seed = (Array.isArray(source) && source.length > 0 ? source : [fallback]).map(
    (item) => ({ ...(item as object) } as T),
  );
  const targetCount = Math.max(1, minCount);
  const result = seed.slice(0, targetCount);
  while (result.length < targetCount) {
    result.push({ ...(seed[result.length % seed.length] as object) } as T);
  }
  return result;
};

const parseIndicatorInputMap = (
  rawInput: string,
  slotKeys: string[],
): IndicatorInputMap | undefined => {
  if (slotKeys.length === 0) {
    return undefined;
  }
  const normalizedSlots = new Map(slotKeys.map((slot) => [normalizeKeyText(slot), slot]));
  const map: IndicatorInputMap = {};
  const text = (rawInput || '').trim();

  if (!text) {
    return undefined;
  }

  if (text.includes('=')) {
    text.split(';').forEach((segment) => {
      const [rawKey, rawValue] = segment.split('=');
      if (!rawKey || !rawValue) {
        return;
      }
      const matchedSlot = normalizedSlots.get(normalizeKeyText(rawKey));
      if (!matchedSlot) {
        return;
      }
      map[matchedSlot] = normalizeTalibInputSource(rawValue);
    });
    return Object.keys(map).length > 0 ? map : undefined;
  }

  // 单输入槽兼容：如 "Close" 直接映射到首个输入槽。
  map[slotKeys[0]] = normalizeTalibInputSource(text);
  return map;
};

const buildIndicatorPayload = (entry: ResolvedChartIndicator) => {
  return {
    name: entry.chartName,
    visible: true,
    calcParams: entry.params && entry.params.length > 0 ? entry.params : undefined,
    extendData: entry.inputMap && Object.keys(entry.inputMap).length > 0
      ? { taInputMap: entry.inputMap }
      : undefined,
  };
};

const toResolvedChartIndicatorList = (
  selectedIndicators: GeneratedIndicatorPayload[],
): ResolvedChartIndicator[] => {
  const metaList = getTalibIndicatorMetaList();
  const byCode = new Map(metaList.map((item) => [normalizeKeyText(item.code), item]));
  const byChartName = new Map(metaList.map((item) => [normalizeKeyText(item.name), item]));
  const deduped = new Map<string, ResolvedChartIndicator>();

  selectedIndicators.forEach((item) => {
    const config = (item.config || {}) as {
      indicator?: string;
      params?: unknown[];
      input?: string;
    };
    const rawCode = String(config.indicator || item.code || '').trim();
    if (!rawCode) {
      return;
    }
    const matched = resolveTalibMeta(rawCode, byCode, byChartName);
    if (!matched) {
      return;
    }

    const params = Array.isArray(config.params)
      ? config.params.map((value) => Number(value)).filter((value) => Number.isFinite(value))
      : undefined;
    const editorSchema = getTalibIndicatorEditorSchema(matched.name);
    const inputMap = parseIndicatorInputMap(
      typeof config.input === 'string' ? config.input : '',
      (editorSchema?.inputSlots || []).map((slot) => slot.key),
    );

    deduped.set(matched.name, {
      chartName: matched.name,
      backendCode: matched.talibCode || matched.code || rawCode,
      pane: matched.pane === 'main' ? 'main' : 'sub',
      params,
      inputMap,
    });
  });

  return Array.from(deduped.values());
};

const StrategyWorkbenchKline: React.FC<StrategyWorkbenchKlineProps> = ({
  exchange,
  symbol,
  timeframeSec,
  selectedIndicators,
  previewMode = 'normal',
  focusRange,
  fullPreviewRanges = [],
  syncTargetRange,
  onVisibleRangeChange,
  hoverValueId,
  hoverHasReference = false,
  onBarsUpdate,
}) => {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const chartRef = useRef<Chart | null>(null);
  const subPaneIdMapRef = useRef<Map<string, string>>(new Map());
  const mainIndicatorSetRef = useRef<Set<string>>(new Set());
  const focusOverlayGroupIdRef = useRef<string | null>(null);
  const fullPreviewOverlayGroupIdRef = useRef<string | null>(null);
  const fullPreviewOverlaySignatureRef = useRef('');
  const focusSyncTargetSignatureRef = useRef('');
  const latestVisibleRangeRef = useRef<StrategyWorkbenchVisibleRange | null>(null);
  const visibleRangeActionRafRef = useRef<number | null>(null);
  const fullPreviewOverlayRafRef = useRef<number | null>(null);
  const blinkTimerRef = useRef<number | null>(null);
  const cloudMissMemoRef = useRef<Map<string, boolean>>(new Map());
  const historySourceModeRef = useRef<HistorySourceMode>('none');
  const historySeriesKeyRef = useRef('');
  const client = useMemo(() => new HttpClient({ tokenProvider: getToken }), []);
  const [bars, setBars] = useState<KLineData[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [errorMessage, setErrorMessage] = useState('');
  const [talibReady, setTalibReady] = useState(false);
  const [chartReady, setChartReady] = useState(false);

  const clearFocusOverlay = useCallback(() => {
    const chart = chartRef.current;
    if (!chart) {
      return;
    }
    if (focusOverlayGroupIdRef.current) {
      chart.removeOverlay({ groupId: focusOverlayGroupIdRef.current });
      focusOverlayGroupIdRef.current = null;
    }
  }, []);

  const clearFullPreviewOverlay = useCallback(() => {
    const chart = chartRef.current;
    if (!chart) {
      return;
    }
    if (fullPreviewOverlayGroupIdRef.current) {
      chart.removeOverlay({ groupId: fullPreviewOverlayGroupIdRef.current });
      fullPreviewOverlayGroupIdRef.current = null;
    }
    fullPreviewOverlaySignatureRef.current = '';
  }, []);

  const timeframeKey = TIMEFRAME_TO_KEY[timeframeSec] || 'm5';
  const localTimeframeKey = TIMEFRAME_TO_LOCAL_KEY[timeframeSec] || '5m';
  const intervalMs = TIMEFRAME_TO_MS[timeframeSec] || 300_000;
  const initialLoadBarCount = useMemo(() => resolveInitialLoadBarCount(intervalMs), [intervalMs]);
  const seriesKey = useMemo(() => {
    const normalizedExchange = normalizeExchangeForLocal(exchange);
    const normalizedSymbol = normalizeSymbolForLocal(symbol);
    return `${normalizedExchange}|${normalizedSymbol}|${localTimeframeKey}`;
  }, [exchange, localTimeframeKey, symbol]);

  useEffect(() => {
    focusSyncTargetSignatureRef.current = '';
    fullPreviewOverlaySignatureRef.current = '';
    latestVisibleRangeRef.current = null;
  }, [seriesKey]);

  const hoverIndicatorMap = useMemo(() => {
    const map = new Map<string, IndicatorHoverEntry>();
    const metaList = getTalibIndicatorMetaList();
    const byCode = new Map(metaList.map((item) => [normalizeKeyText(item.code), item]));
    const byChartName = new Map(metaList.map((item) => [normalizeKeyText(item.name), item]));
    selectedIndicators.forEach((item) => {
      const config = (item.config || {}) as { indicator?: string };
      const rawCode = String(config.indicator || item.code || '').trim();
      if (!rawCode) {
        return;
      }
      const matched = resolveTalibMeta(rawCode, byCode, byChartName);
      if (!matched) {
        return;
      }
      map.set(item.id, {
        chartName: matched.name,
        backendCode: matched.talibCode || matched.code || rawCode,
        outputs: Array.isArray(item.outputs) ? item.outputs : [],
      });
    });
    return map;
  }, [selectedIndicators]);

  const requestRemoteHistoryBars = useCallback(async (
    count: number,
    startTimeMs: number,
    endTimeMs: number,
    signal?: AbortSignal,
  ): Promise<KLineData[]> => {
    const payload = {
      exchange: normalizeExchange(exchange),
      timeframe: timeframeKey,
      symbol: normalizeSymbol(symbol),
      count,
      startTime: formatDateTime(startTimeMs),
      endTime: formatDateTime(endTimeMs),
    };
    const data = await client.postProtocol<OhlcvDto[]>(
      '/api/marketdata/history',
      'marketdata.kline.history',
      payload,
      { signal },
    );
    return normalizeHistoryBars(Array.isArray(data) ? data : []);
  }, [client, exchange, symbol, timeframeKey]);

  const loadHistory = useCallback(async (signal: AbortSignal) => {
    setIsLoading(true);
    setErrorMessage('');
    historySourceModeRef.current = 'none';
    historySeriesKeyRef.current = seriesKey;

    try {
      const normalizedExchange = normalizeExchangeForLocal(exchange);
      const normalizedSymbol = normalizeSymbolForLocal(symbol);
      try {
        let localBars: KLineData[] = [];
        if (cloudMissMemoRef.current.get(seriesKey)) {
          localBars = await loadLocalKlineBars(
            normalizedExchange,
            normalizedSymbol,
            localTimeframeKey,
            initialLoadBarCount,
          );
        } else {
          const localWithCloud = await loadLocalKlineBarsWithCloudFallback(
            normalizedExchange,
            normalizedSymbol,
            localTimeframeKey,
            initialLoadBarCount,
            signal,
          );
          if (localWithCloud.source === 'none') {
            cloudMissMemoRef.current.set(seriesKey, true);
          } else {
            cloudMissMemoRef.current.delete(seriesKey);
          }
          localBars = localWithCloud.bars;
        }

        if (localBars.length > 0) {
          historySourceModeRef.current = 'local';
          setBars(localBars);
          return;
        }
      } catch (localError) {
        console.warn('本地离线K线缓存读取失败，回退到接口拉取', localError);
      }

      const endTime = Date.now();
      const startTime = endTime - intervalMs * (initialLoadBarCount - 1);
      const remoteBars = await requestRemoteHistoryBars(
        initialLoadBarCount,
        startTime,
        endTime,
        signal,
      );
      if (remoteBars.length <= 0) {
        setBars([]);
        setErrorMessage('暂无可用K线数据');
        return;
      }

      historySourceModeRef.current = 'backend';
      setBars(remoteBars);
    } catch (error) {
      if ((error as { name?: string })?.name === 'AbortError') {
        return;
      }
      setBars([]);
      historySourceModeRef.current = 'none';
      setErrorMessage(error instanceof Error ? error.message : 'K线数据加载失败');
    } finally {
      setIsLoading(false);
    }
  }, [exchange, initialLoadBarCount, intervalMs, localTimeframeKey, requestRemoteHistoryBars, seriesKey, symbol]);

  useEffect(() => {
    historySeriesKeyRef.current = seriesKey;
    const controller = new AbortController();
    loadHistory(controller.signal);
    return () => controller.abort();
  }, [loadHistory, seriesKey]);

  useEffect(() => {
    const chart = chartRef.current;
    if (!chart || !chartReady) {
      return;
    }

    chart.setLoadDataCallback((params) => {
      if (params.type !== LoadDataType.Forward) {
        params.callback([], false);
        return;
      }

      const normalizedExchange = normalizeExchangeForLocal(exchange);
      const normalizedSymbol = normalizeSymbolForLocal(symbol);
      const referenceTimestamp = Number(params.data?.timestamp ?? Number.NaN);
      if (!Number.isFinite(referenceTimestamp)) {
        params.callback([], false);
        return;
      }

      void (async () => {
        try {
          if (historySeriesKeyRef.current !== seriesKey) {
            params.callback([], false);
            return;
          }

          if (historySourceModeRef.current === 'local') {
            const localResult = await loadLocalKlineBarsBefore(
              normalizedExchange,
              normalizedSymbol,
              localTimeframeKey,
              referenceTimestamp,
              LOAD_MORE_BAR_COUNT,
            );
            if (historySeriesKeyRef.current !== seriesKey) {
              params.callback([], false);
              return;
            }
            params.callback(localResult.bars, localResult.hasMore);
            return;
          }

          if (historySourceModeRef.current === 'backend') {
            const endTime = referenceTimestamp - intervalMs;
            if (!Number.isFinite(endTime) || endTime <= 0) {
              params.callback([], false);
              return;
            }
            const startTime = endTime - intervalMs * (LOAD_MORE_BAR_COUNT - 1);
            const remoteBars = await requestRemoteHistoryBars(
              LOAD_MORE_BAR_COUNT,
              startTime,
              endTime,
            );
            if (historySeriesKeyRef.current !== seriesKey) {
              params.callback([], false);
              return;
            }
            params.callback(remoteBars, remoteBars.length >= LOAD_MORE_BAR_COUNT);
            return;
          }

          params.callback([], false);
        } catch (error) {
          console.warn('左滑加载历史K线失败', error);
          params.callback([], false);
        }
      })();
    });
  }, [chartReady, exchange, intervalMs, localTimeframeKey, requestRemoteHistoryBars, seriesKey, symbol]);

  useEffect(() => {
    if (!containerRef.current || chartRef.current) {
      return;
    }
    registerCustomOverlays();
    chartRef.current = init(containerRef.current);
    if (!chartRef.current) {
      return;
    }
    chartRef.current.setStyles({
      grid: {
        horizontal: { color: 'rgba(148, 163, 184, 0.15)' },
        vertical: { color: 'rgba(148, 163, 184, 0.15)' },
      },
      candle: {
        bar: {
          upColor: '#16a34a',
          downColor: '#dc2626',
          noChangeColor: '#64748b',
        },
      },
    });
    setChartReady(true);
  }, []);

  useEffect(() => {
    let disposed = false;
    const bootstrapTalib = async () => {
      try {
        await registerTalibIndicators();
        if (!disposed) {
          setTalibReady(true);
        }
      } catch (error) {
        if (!disposed) {
          setErrorMessage(error instanceof Error ? error.message : '指标注册失败');
        }
      }
    };
    bootstrapTalib();
    return () => {
      disposed = true;
    };
  }, []);

  useEffect(() => {
    if (!chartRef.current || !chartReady) {
      return;
    }
    chartRef.current.clearData();
    if (bars.length > 0) {
      chartRef.current.applyNewData(bars);
    }
  }, [bars, chartReady]);

  useEffect(() => {
    onBarsUpdate?.(bars);
  }, [bars, onBarsUpdate]);

  const refreshFullPreviewOverlays = useCallback(() => {
    const chart = chartRef.current;
    if (!chart || !chartReady) {
      return;
    }
    if (previewMode !== 'full') {
      clearFullPreviewOverlay();
      return;
    }
    if (!Array.isArray(fullPreviewRanges) || fullPreviewRanges.length <= 0) {
      clearFullPreviewOverlay();
      return;
    }

    const snapshot =
      latestVisibleRangeRef.current
      ?? buildVisibleRangeSnapshotFromChart(chart, chart.getVisibleRange());
    if (!snapshot) {
      clearFullPreviewOverlay();
      return;
    }

    const viewportFrom = Math.min(snapshot.fromTime, snapshot.toTime);
    const viewportTo = Math.max(snapshot.fromTime, snapshot.toTime);
    const centerTime = snapshot.centerTime;
    const paddingMs = Math.max(intervalMs * 2, Math.round((viewportTo - viewportFrom) * 0.06));
    const maxOverlayCount = clampNumber(
      Math.round((snapshot.toIndex - snapshot.fromIndex + 1) * 1.6),
      FULL_PREVIEW_OVERLAY_MIN_COUNT,
      FULL_PREVIEW_OVERLAY_MAX_COUNT,
    );

    let visibleRanges = fullPreviewRanges
      .map((range) => {
        const startTime = Math.min(range.startTime, range.endTime);
        const endTime = Math.max(range.startTime, range.endTime);
        if (!Number.isFinite(startTime) || !Number.isFinite(endTime) || startTime <= 0 || endTime <= 0) {
          return null;
        }
        if (endTime < viewportFrom - paddingMs || startTime > viewportTo + paddingMs) {
          return null;
        }
        return {
          ...range,
          startTime,
          endTime,
          midpoint: Math.floor((startTime + endTime) / 2),
        };
      })
      .filter((item): item is StrategyWorkbenchTradeFocusRange & { midpoint: number } => item !== null);

    if (visibleRanges.length <= 0) {
      clearFullPreviewOverlay();
      return;
    }

    if (visibleRanges.length > maxOverlayCount) {
      visibleRanges = [...visibleRanges]
        .sort((a, b) => Math.abs(a.midpoint - centerTime) - Math.abs(b.midpoint - centerTime))
        .slice(0, maxOverlayCount);
    }

    const signature = `${snapshot.fromIndex}:${snapshot.toIndex}|${visibleRanges.map((item) => item.id).join(',')}`;
    if (signature === fullPreviewOverlaySignatureRef.current) {
      return;
    }
    clearFullPreviewOverlay();
    fullPreviewOverlaySignatureRef.current = signature;

    const groupId = `workbench-full-preview-${Date.now()}`;
    fullPreviewOverlayGroupIdRef.current = groupId;
    const getBars = () => chart.getDataList();
    const currentBars = getBars();
    const payloads = visibleRanges
      .map((range) => {
        const side = normalizeTradeSide(range.side);
        const overlayName = side === 'short' ? 'riskRewardShort' : 'riskRewardLong';
        const entryPrice = toFinitePrice(range.entryPrice) ?? toFinitePrice(range.exitPrice);
        if (entryPrice === null) {
          return null;
        }
        const exitPrice = toFinitePrice(range.exitPrice);
        const takeProfitPrice =
          toFinitePrice(range.takeProfitPrice) ?? getDefaultRiskTakeProfitPrice(entryPrice, side);
        const stopLossPrice =
          toFinitePrice(range.stopLossPrice) ?? getDefaultRiskStopLossPrice(entryPrice, side);
        return {
          name: overlayName,
          groupId,
          lock: true,
          onRightClick: () => true,
          points: [
            { timestamp: range.startTime, value: entryPrice },
            { timestamp: range.endTime, value: entryPrice },
            { timestamp: range.endTime, value: takeProfitPrice },
            { timestamp: range.endTime, value: stopLossPrice },
          ],
          extendData: {
            direction: side,
            getBars,
            bars: currentBars,
            positionEntryTime: range.startTime,
            positionExitTime: range.endTime,
            positionExitPrice: exitPrice,
            positionTakeProfitPrice: takeProfitPrice,
            positionStopLossPrice: stopLossPrice,
          },
        };
      })
      .filter(Boolean);

    if (payloads.length <= 0) {
      clearFullPreviewOverlay();
      return;
    }

    chart.createOverlay(payloads as never);
  }, [chartReady, clearFullPreviewOverlay, fullPreviewRanges, intervalMs, previewMode]);

  const scheduleFullPreviewOverlayRefresh = useCallback(() => {
    if (fullPreviewOverlayRafRef.current !== null) {
      return;
    }
    fullPreviewOverlayRafRef.current = window.requestAnimationFrame(() => {
      fullPreviewOverlayRafRef.current = null;
      refreshFullPreviewOverlays();
    });
  }, [refreshFullPreviewOverlays]);

  useEffect(() => {
    const chart = chartRef.current;
    if (!chart || !chartReady) {
      return;
    }

    const handleVisibleRangeChange = (payload?: unknown) => {
      if (visibleRangeActionRafRef.current !== null) {
        window.cancelAnimationFrame(visibleRangeActionRafRef.current);
      }
      visibleRangeActionRafRef.current = window.requestAnimationFrame(() => {
        visibleRangeActionRafRef.current = null;
        const snapshot = buildVisibleRangeSnapshotFromChart(chart, payload as VisibleRangeLike);
        if (!snapshot) {
          return;
        }
        latestVisibleRangeRef.current = snapshot;
        onVisibleRangeChange?.(snapshot);
        if (previewMode === 'full') {
          scheduleFullPreviewOverlayRefresh();
        }
      });
    };

    chart.subscribeAction(ActionType.OnVisibleRangeChange, handleVisibleRangeChange);
    handleVisibleRangeChange(chart.getVisibleRange());
    return () => {
      chart.unsubscribeAction(ActionType.OnVisibleRangeChange, handleVisibleRangeChange);
      if (visibleRangeActionRafRef.current !== null) {
        window.cancelAnimationFrame(visibleRangeActionRafRef.current);
        visibleRangeActionRafRef.current = null;
      }
    };
  }, [chartReady, onVisibleRangeChange, previewMode, scheduleFullPreviewOverlayRefresh]);

  useEffect(() => {
    const chart = chartRef.current;
    if (!chart || !chartReady) {
      return;
    }
    if (previewMode !== 'normal' || !focusRange) {
      clearFocusOverlay();
      return;
    }
    if (bars.length <= 0) {
      return;
    }

    const startTime = Math.min(focusRange.startTime, focusRange.endTime);
    const endTime = Math.max(focusRange.startTime, focusRange.endTime);
    if (!Number.isFinite(startTime) || !Number.isFinite(endTime) || startTime <= 0 || endTime <= 0) {
      return;
    }

    const side = focusRange.side?.trim().toLowerCase() === 'short' ? 'short' : 'long';
    const overlayName = side === 'short' ? 'riskRewardShort' : 'riskRewardLong';
    const entryPrice = toFinitePrice(focusRange.entryPrice) ?? toFinitePrice(focusRange.exitPrice);
    if (entryPrice === null) {
      return;
    }

    const exitPrice = toFinitePrice(focusRange.exitPrice);
    const takeProfitPrice = toFinitePrice(focusRange.takeProfitPrice) ?? getDefaultRiskTakeProfitPrice(entryPrice, side);
    const stopLossPrice = toFinitePrice(focusRange.stopLossPrice) ?? getDefaultRiskStopLossPrice(entryPrice, side);

    clearFocusOverlay();
    const groupId = `workbench-focus-${focusRange.id}-${Date.now()}`;
    focusOverlayGroupIdRef.current = groupId;

    chart.createOverlay({
      name: overlayName,
      groupId,
      lock: true,
      onRightClick: () => true,
      points: [
        { timestamp: startTime, value: entryPrice },
        { timestamp: endTime, value: entryPrice },
        { timestamp: endTime, value: takeProfitPrice },
        { timestamp: endTime, value: stopLossPrice },
      ],
      extendData: {
        direction: side,
        getBars: () => chart.getDataList(),
        bars: chart.getDataList(),
        positionEntryTime: startTime,
        positionExitTime: endTime,
        positionExitPrice: exitPrice,
        positionTakeProfitPrice: takeProfitPrice,
        positionStopLossPrice: stopLossPrice,
      },
    } as never);

    const dataList = chart.getDataList();
    const leftIndex = findNearestDataIndexByTimestamp(dataList, startTime);
    const rightIndex = findNearestDataIndexByTimestamp(dataList, endTime);
    const midpoint = Math.floor((startTime + endTime) / 2);

    if (leftIndex !== null && rightIndex !== null) {
      const fromIndex = Math.min(leftIndex, rightIndex);
      const toIndex = Math.max(leftIndex, rightIndex);
      const width = toIndex - fromIndex + 1;
      if (width > 0) {
        const visibleRange = chart.getVisibleRange();
        const currentVisibleBars = Math.max(2, Math.round((visibleRange.to ?? 0) - (visibleRange.from ?? 0) + 1));
        const desiredVisibleBars = clampNumber(Math.round(width * 1.8), 16, 420);
        const scale = currentVisibleBars / desiredVisibleBars;
        if (Number.isFinite(scale) && scale > 0 && Math.abs(scale - 1) > 0.08) {
          chart.zoomAtTimestamp(scale, midpoint, 0);
        }
      }
    }

    chart.scrollToTimestamp(midpoint, 0);
  }, [bars, chartReady, clearFocusOverlay, focusRange, previewMode]);

  useEffect(() => {
    if (previewMode !== 'full') {
      focusSyncTargetSignatureRef.current = '';
      clearFullPreviewOverlay();
      return;
    }
    scheduleFullPreviewOverlayRefresh();
  }, [clearFullPreviewOverlay, previewMode, scheduleFullPreviewOverlayRefresh]);

  useEffect(() => {
    scheduleFullPreviewOverlayRefresh();
  }, [bars, fullPreviewRanges, scheduleFullPreviewOverlayRefresh]);

  useEffect(() => {
    const chart = chartRef.current;
    if (!chart || !chartReady || previewMode !== 'full' || !syncTargetRange) {
      return;
    }
    const startTime = Math.min(syncTargetRange.startTime, syncTargetRange.endTime);
    const endTime = Math.max(syncTargetRange.startTime, syncTargetRange.endTime);
    if (!Number.isFinite(startTime) || !Number.isFinite(endTime) || startTime <= 0 || endTime <= 0) {
      return;
    }
    const signature = `${syncTargetRange.id}|${startTime}|${endTime}`;
    if (signature === focusSyncTargetSignatureRef.current) {
      return;
    }
    focusSyncTargetSignatureRef.current = signature;
    chart.scrollToTimestamp(Math.floor((startTime + endTime) / 2), 0);
    scheduleFullPreviewOverlayRefresh();
  }, [chartReady, previewMode, scheduleFullPreviewOverlayRefresh, syncTargetRange]);

  useEffect(() => {
    const chart = chartRef.current;
    if (!chart || !talibReady) {
      return;
    }

    const nextIndicators = toResolvedChartIndicatorList(selectedIndicators);
    const nextMain = nextIndicators.filter((item) => item.pane === 'main');
    const nextSub = nextIndicators.filter((item) => item.pane === 'sub');

    const nextMainNames = new Set(nextMain.map((item) => item.chartName));
    for (const prevName of Array.from(mainIndicatorSetRef.current.values())) {
      if (!nextMainNames.has(prevName)) {
        chart.removeIndicator(CANDLE_PANE_ID, prevName);
      }
    }

    nextMain.forEach((item) => {
      const payload = buildIndicatorPayload(item);
      const exists = Boolean(chart.getIndicatorByPaneId(CANDLE_PANE_ID, item.chartName));
      if (!exists) {
        chart.createIndicator(payload, true, { id: CANDLE_PANE_ID });
      } else {
        chart.overrideIndicator(payload, CANDLE_PANE_ID);
      }
    });
    mainIndicatorSetRef.current = nextMainNames;

    const paneMap = subPaneIdMapRef.current;
    for (const [indicatorName, paneId] of Array.from(paneMap.entries())) {
      if (!nextSub.some((item) => item.chartName === indicatorName)) {
        chart.removeIndicator(paneId);
        paneMap.delete(indicatorName);
      }
    }

    nextSub.forEach((item) => {
      const payload = buildIndicatorPayload(item);
      const paneId = paneMap.get(item.chartName);
      if (!paneId) {
        const createdPaneId = chart.createIndicator(payload, false, { height: 96 });
        if (typeof createdPaneId === 'string' && createdPaneId) {
          paneMap.set(item.chartName, createdPaneId);
        }
        return;
      }

      const exists = Boolean(chart.getIndicatorByPaneId(paneId, item.chartName));
      if (!exists) {
        paneMap.delete(item.chartName);
        const recreatedPaneId = chart.createIndicator(payload, false, { height: 96 });
        if (typeof recreatedPaneId === 'string' && recreatedPaneId) {
          paneMap.set(item.chartName, recreatedPaneId);
        }
        return;
      }

      chart.overrideIndicator(payload, paneId);
    });
  }, [selectedIndicators, talibReady]);

  const clearBlinkTimer = useCallback(() => {
    if (blinkTimerRef.current !== null) {
      window.clearTimeout(blinkTimerRef.current);
      blinkTimerRef.current = null;
    }
  }, []);

  const resolveHoverTarget = useCallback((valueId?: string): IndicatorVisualTarget | null => {
    const parsed = parseIndicatorOutputValueId(valueId);
    if (!parsed) {
      return null;
    }
    const indicator = hoverIndicatorMap.get(parsed.indicatorId);
    if (!indicator) {
      return null;
    }

    const figure = resolveFigureAddress(indicator.backendCode, indicator.outputs, parsed.outputKey);
    if (mainIndicatorSetRef.current.has(indicator.chartName)) {
      return {
        paneId: CANDLE_PANE_ID,
        chartName: indicator.chartName,
        figure,
      };
    }
    const subPaneId = subPaneIdMapRef.current.get(indicator.chartName);
    if (!subPaneId) {
      return null;
    }
    return {
      paneId: subPaneId,
      chartName: indicator.chartName,
      figure,
    };
  }, [hoverIndicatorMap]);

  const applyIndicatorPulse = useCallback((target: IndicatorVisualTarget, phaseOn: boolean) => {
    const chart = chartRef.current;
    if (!chart) {
      return;
    }
    const exists = chart.getIndicatorByPaneId(target.paneId, target.chartName);
    if (!exists) {
      return;
    }

    const indicatorStyles = chart.getStyles().indicator;
    const lines = buildStyleList(
      (indicatorStyles.lines || []) as LineStyleLike[],
      target.figure.type === 'line' ? Math.max(5, target.figure.index + 1) : 5,
      BASE_LINE_STYLE,
    );
    const bars = buildStyleList(
      (indicatorStyles.bars || []) as BarStyleLike[],
      target.figure.type === 'bar' ? Math.max(1, target.figure.index + 1) : 1,
      BASE_BAR_STYLE,
    );
    const circles = buildStyleList(
      (indicatorStyles.circles || []) as CircleStyleLike[],
      target.figure.type === 'circle' ? Math.max(1, target.figure.index + 1) : 1,
      BASE_CIRCLE_STYLE,
    );

    if (target.figure.type === 'line') {
      lines[target.figure.index] = {
        ...lines[target.figure.index],
        color: phaseOn ? HIGHLIGHT_COLOR_ON : HIGHLIGHT_COLOR_OFF,
        size: phaseOn ? 2.8 : 1.35,
        smooth: true,
      };
    } else if (target.figure.type === 'bar') {
      bars[target.figure.index] = {
        ...bars[target.figure.index],
        upColor: phaseOn ? HIGHLIGHT_COLOR_ON : HIGHLIGHT_COLOR_OFF,
        downColor: phaseOn ? HIGHLIGHT_COLOR_ON : HIGHLIGHT_COLOR_OFF,
        noChangeColor: phaseOn ? HIGHLIGHT_COLOR_ON : HIGHLIGHT_COLOR_OFF,
        borderSize: phaseOn ? 2 : 1,
      };
    } else {
      circles[target.figure.index] = {
        ...circles[target.figure.index],
        upColor: phaseOn ? HIGHLIGHT_COLOR_ON : HIGHLIGHT_COLOR_OFF,
        downColor: phaseOn ? HIGHLIGHT_COLOR_ON : HIGHLIGHT_COLOR_OFF,
        noChangeColor: phaseOn ? HIGHLIGHT_COLOR_ON : HIGHLIGHT_COLOR_OFF,
        borderSize: phaseOn ? 2 : 1,
      };
    }

    chart.overrideIndicator(
      {
        name: target.chartName,
        styles: {
          lines: lines as unknown as never[],
          bars: bars as unknown as never[],
          circles: circles as unknown as never[],
        },
      },
      target.paneId,
    );
  }, []);

  useEffect(() => {
    if (!talibReady) {
      return;
    }
    clearBlinkTimer();
    const target = resolveHoverTarget(hoverValueId);
    if (!target) {
      return;
    }

    // 有引用时和右侧抖动节奏接近；无引用仅做短闪，避免无意义持续动画。
    const phases = hoverHasReference ? BLINK_PHASES_REFERENCED : BLINK_PHASES_BRIEF;
    const stepMs = hoverHasReference ? BLINK_STEP_MS_REFERENCED : BLINK_STEP_MS_BRIEF;
    let step = 0;
    const runStep = () => {
      const phaseOn = phases[step];
      applyIndicatorPulse(target, phaseOn);
      step += 1;
      if (step >= phases.length) {
        applyIndicatorPulse(target, false);
        return;
      }
      blinkTimerRef.current = window.setTimeout(runStep, stepMs);
    };

    runStep();
    return () => {
      clearBlinkTimer();
      applyIndicatorPulse(target, false);
    };
  }, [applyIndicatorPulse, clearBlinkTimer, hoverHasReference, hoverValueId, resolveHoverTarget, talibReady]);

  useEffect(() => {
    if (!containerRef.current || !chartRef.current) {
      return;
    }
    // 监听容器尺寸，避免全屏切换或窗口缩放后图表尺寸异常。
    const observer = new ResizeObserver(() => {
      chartRef.current?.resize();
    });
    observer.observe(containerRef.current);
    return () => observer.disconnect();
  }, []);

  useEffect(() => {
    return () => {
      clearBlinkTimer();
      clearFocusOverlay();
      clearFullPreviewOverlay();
      if (visibleRangeActionRafRef.current !== null) {
        window.cancelAnimationFrame(visibleRangeActionRafRef.current);
        visibleRangeActionRafRef.current = null;
      }
      if (fullPreviewOverlayRafRef.current !== null) {
        window.cancelAnimationFrame(fullPreviewOverlayRafRef.current);
        fullPreviewOverlayRafRef.current = null;
      }
      cloudMissMemoRef.current.clear();
      if (chartRef.current) {
        dispose(chartRef.current);
        chartRef.current = null;
      }
      latestVisibleRangeRef.current = null;
      focusSyncTargetSignatureRef.current = '';
      fullPreviewOverlaySignatureRef.current = '';
      mainIndicatorSetRef.current.clear();
      subPaneIdMapRef.current.clear();
    };
  }, [clearBlinkTimer, clearFocusOverlay, clearFullPreviewOverlay]);

  return (
    <div className="strategy-workbench-kline-shell">
      <div className="strategy-workbench-kline" ref={containerRef} />
      {isLoading && <div className="strategy-workbench-kline-mask">K线数据加载中...</div>}
      {!isLoading && errorMessage && <div className="strategy-workbench-kline-mask is-error">{errorMessage}</div>}
    </div>
  );
};

export default StrategyWorkbenchKline;
