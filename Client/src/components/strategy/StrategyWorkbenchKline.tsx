import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { dispose, init, type Chart, type KLineData } from 'klinecharts';

import { HttpClient, getToken } from '../../network/index.ts';
import type { GeneratedIndicatorPayload } from '../indicator/IndicatorGeneratorSelector';
import {
  getTalibIndicatorEditorSchema,
  getTalibIndicatorMetaList,
  normalizeTalibInputSource,
  registerTalibIndicators,
} from '../../lib/registerTalibIndicators';

interface StrategyWorkbenchKlineProps {
  exchange: string;
  symbol: string;
  timeframeSec: number;
  selectedIndicators: GeneratedIndicatorPayload[];
  enableRealtime: boolean;
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
  enableRealtime,
  hoverValueId,
  hoverHasReference = false,
  onBarsUpdate,
}) => {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const chartRef = useRef<Chart | null>(null);
  const subPaneIdMapRef = useRef<Map<string, string>>(new Map());
  const mainIndicatorSetRef = useRef<Set<string>>(new Set());
  const blinkTimerRef = useRef<number | null>(null);
  const client = useMemo(() => new HttpClient({ tokenProvider: getToken }), []);
  const [bars, setBars] = useState<KLineData[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [errorMessage, setErrorMessage] = useState('');
  const [talibReady, setTalibReady] = useState(false);

  const timeframeKey = TIMEFRAME_TO_KEY[timeframeSec] || 'm5';
  const intervalMs = TIMEFRAME_TO_MS[timeframeSec] || 300_000;

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

  const loadHistory = useCallback(async (signal: AbortSignal) => {
    setIsLoading(true);
    setErrorMessage('');
    try {
      const endTime = Date.now();
      const count = 360;
      const startTime = endTime - intervalMs * (count - 1);
      const payload = {
        exchange: normalizeExchange(exchange),
        timeframe: timeframeKey,
        symbol: normalizeSymbol(symbol),
        count,
        startTime: formatDateTime(startTime),
        endTime: formatDateTime(endTime),
      };
      const data = await client.postProtocol<OhlcvDto[]>(
        '/api/marketdata/history',
        'marketdata.kline.history',
        payload,
        { signal },
      );
      const normalized = data
        .map(toKlineData)
        .filter((item): item is KLineData => item !== null)
        .sort((a, b) => a.timestamp - b.timestamp);
      if (normalized.length === 0) {
        setBars([]);
        setErrorMessage('暂无可用K线数据');
        return;
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
      setBars(deduped);
    } catch (error) {
      if ((error as { name?: string })?.name === 'AbortError') {
        return;
      }
      setBars([]);
      setErrorMessage(error instanceof Error ? error.message : 'K线数据加载失败');
    } finally {
      setIsLoading(false);
    }
  }, [client, exchange, intervalMs, symbol, timeframeKey]);

  useEffect(() => {
    const controller = new AbortController();
    loadHistory(controller.signal);
    return () => controller.abort();
  }, [loadHistory]);

  useEffect(() => {
    if (!enableRealtime) {
      return;
    }
    const timer = window.setInterval(() => {
      const controller = new AbortController();
      loadHistory(controller.signal);
      window.setTimeout(() => controller.abort(), 4500);
    }, 5000);
    return () => window.clearInterval(timer);
  }, [enableRealtime, loadHistory]);

  useEffect(() => {
    if (!containerRef.current || chartRef.current) {
      return;
    }
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
    if (!chartRef.current || bars.length === 0) {
      return;
    }
    chartRef.current.clearData();
    chartRef.current.applyNewData(bars);
  }, [bars]);

  useEffect(() => {
    onBarsUpdate?.(bars);
  }, [bars, onBarsUpdate]);

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
      if (chartRef.current) {
        dispose(chartRef.current);
        chartRef.current = null;
      }
      mainIndicatorSetRef.current.clear();
      subPaneIdMapRef.current.clear();
    };
  }, [clearBlinkTimer]);

  return (
    <div className="strategy-workbench-kline-shell">
      <div className="strategy-workbench-kline" ref={containerRef} />
      {isLoading && <div className="strategy-workbench-kline-mask">K线数据加载中...</div>}
      {!isLoading && errorMessage && <div className="strategy-workbench-kline-mask is-error">{errorMessage}</div>}
    </div>
  );
};

export default StrategyWorkbenchKline;
