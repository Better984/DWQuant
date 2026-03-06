import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import {
  DndContext,
  PointerSensor,
  pointerWithin,
  useDraggable,
  useDroppable,
  useSensor,
  useSensors,
  type DragCancelEvent,
  type DragEndEvent,
  type DragStartEvent,
} from '@dnd-kit/core';
import { CSS } from '@dnd-kit/utilities';
import type { KLineData } from 'klinecharts';
import * as echarts from 'echarts';
import type { ECharts, EChartsOption } from 'echarts';

import type { GeneratedIndicatorPayload } from '../indicator/IndicatorGeneratorSelector';
import { registerTalibIndicators } from '../../lib/registerTalibIndicators';
import type {
  ConditionContainer,
  ConditionItem,
  IndicatorOutputGroup,
  MethodOption,
  TimeframeOption,
  TradeOption,
} from './StrategyModule.types';
import {
  buildEmptyBacktestSummary,
  runLocalBacktestRealtime,
  type LocalBacktestEquityPoint,
  type LocalBacktestTrade,
  type LocalBacktestSummary,
} from './localBacktestEngine';
import StrategyWorkbenchKline, {
  type StrategyWorkbenchFocusRangeCoverage,
  type StrategyWorkbenchTradeFocusRange,
  type StrategyWorkbenchVisibleRange,
} from './StrategyWorkbenchKline';
import KlineOfflineCacheDialog from '../dialogs/KlineOfflineCacheDialog';
import './StrategyWorkbench.css';

type DropSlot = 'left' | 'method' | 'right' | 'extra';
type ArgValueMode = 'field' | 'number' | 'both';
type IndicatorLabelDisplayMode = 'code-only' | 'code-param' | 'code-input' | 'full';

type DragPayloadMeta = {
  previewClassName?: string;
  previewStyle?: React.CSSProperties;
  previewText?: string;
};

type ConditionValueDragSource = {
  containerId: string;
  groupId: string;
  conditionId: string;
  slot: 'left' | 'right' | 'extra';
};

type DragPayload =
  | ({ kind: 'output'; valueId: string; label: string } & DragPayloadMeta)
  | ({ kind: 'method'; method: string; label: string; category: string } & DragPayloadMeta)
  | ({ kind: 'category'; category: string; label: string } & DragPayloadMeta)
  | ({ kind: 'condition-value'; valueId: string; label: string; source: ConditionValueDragSource } & DragPayloadMeta)
  | ({ kind: 'number'; label: string } & DragPayloadMeta);

interface StrategyWorkbenchProps {
  selectedIndicators: GeneratedIndicatorPayload[];
  formatIndicatorName: (indicator: GeneratedIndicatorPayload) => string;
  onOpenIndicatorGenerator: () => void;
  onEditIndicator: (indicatorId: string) => void;
  onRemoveIndicator: (indicatorId: string) => void;
  logicContainers: ConditionContainer[];
  filterContainers: ConditionContainer[];
  maxGroupsPerContainer: number;
  onAddConditionGroup: (containerId: string) => void;
  onToggleGroupFlag: (containerId: string, groupId: string, key: 'enabled' | 'required') => void;
  onOpenConditionModal: (containerId: string, groupId: string, conditionId?: string) => void;
  onRemoveGroup: (containerId: string, groupId: string) => void;
  onToggleConditionFlag: (
    containerId: string,
    groupId: string,
    conditionId: string,
    key: 'enabled' | 'required',
  ) => void;
  onRemoveCondition: (containerId: string, groupId: string, conditionId: string) => void;
  renderToggle: (checked: boolean, onChange: () => void, label: string) => React.ReactNode;
  onClose: () => void;
  onOpenExport: () => void;
  exchangeOptions: TradeOption[];
  selectedExchange: string;
  onExchangeChange: (value: string) => void;
  symbolOptions: TradeOption[];
  selectedSymbol: string;
  onSymbolChange: (value: string) => void;
  timeframeOptions: TimeframeOption[];
  selectedTimeframeSec: number;
  onTimeframeChange: (value: number) => void;
  takeProfitPct: number;
  stopLossPct: number;
  leverage: number;
  orderQty: number;
  onTakeProfitPctChange: (value: number) => void;
  onStopLossPctChange: (value: number) => void;
  onLeverageChange: (value: number) => void;
  onOrderQtyChange: (value: number) => void;
  indicatorOutputGroups: IndicatorOutputGroup[];
  methodOptions: MethodOption[];
  onQuickAssignConditionMethod: (
    containerId: string,
    groupId: string,
    conditionId: string,
    method: string,
  ) => void;
  onQuickAssignConditionValue: (
    containerId: string,
    groupId: string,
    conditionId: string,
    slot: 'left' | 'right',
    valueId: string,
    source?: ConditionValueDragSource,
  ) => void;
  onQuickAssignConditionNumber: (containerId: string, groupId: string, conditionId: string) => void;
  onQuickUpdateConditionRightNumber: (
    containerId: string,
    groupId: string,
    conditionId: string,
    value: string,
  ) => void;
  onQuickAssignConditionExtraValue: (
    containerId: string,
    groupId: string,
    conditionId: string,
    valueId: string,
    source?: ConditionValueDragSource,
  ) => void;
  onQuickAssignConditionExtraNumber: (containerId: string, groupId: string, conditionId: string) => void;
  onQuickUpdateConditionExtraNumber: (
    containerId: string,
    groupId: string,
    conditionId: string,
    value: string,
  ) => void;
  onQuickUpdateConditionParamValue: (
    containerId: string,
    groupId: string,
    conditionId: string,
    paramIndex: number,
    value: string,
  ) => void;
  onQuickUpdateIndicatorInput: (indicatorId: string, fieldValueId: string) => void;
  onQuickEditIndicatorParams: (indicatorId: string) => void;
  onQuickCreateCondition: (containerId: string, groupId: string | null, method: string) => void;
  topbarExtraActions?: React.ReactNode;
  floatingOverlay?: React.ReactNode;
}

type DashboardMode = 'settings' | 'preview';
type BacktestRangeMode = 'latest_30d' | 'custom';
type PreviewTradeMode = 'normal' | 'full';
type WorkbenchLayoutMode = 'edit' | 'backtest';
type CalendarViewMode = 'month' | 'week' | 'day';
type LiveSummaryTab = 'overview' | 'stats' | 'professional' | 'calendar' | 'log';

type WorkbenchBacktestParams = {
  takeProfitPct: number;
  stopLossPct: number;
  leverage: number;
  orderQty: number;
  initialCapital: number;
  feeRate: number;
  fundingRate: number;
  slippageBps: number;
  autoReverse: boolean;
  useStrategyRuntime: boolean;
  executionMode: 'batch_open_close' | 'timeline';
  timeRangeMode: BacktestRangeMode;
  rangeStartMs: number | null;
  rangeEndMs: number | null;
};

type WorkbenchBacktestNumberField =
  | 'takeProfitPct'
  | 'stopLossPct'
  | 'leverage'
  | 'orderQty'
  | 'initialCapital'
  | 'feeRate'
  | 'fundingRate'
  | 'slippageBps';

type WorkbenchBacktestProgress = {
  processedBars: number;
  totalBars: number;
  progress: number;
  elapsedMs: number;
  done: boolean;
};

type PreviewTradeSyncItem = {
  key: string;
  startTime: number;
  endTime: number;
  midpoint: number;
};

type TradeStreakLocation = {
  count: number;
  startOrder: number;
  endOrder: number;
  endTimestamp: number;
};

type BacktestCurveInsight = {
  maxWinStreak: TradeStreakLocation | null;
  maxLossStreak: TradeStreakLocation | null;
  maxDrawdownPoint: LocalBacktestEquityPoint | null;
};

type CalendarBucketMetrics = {
  pnl: number;
  count: number;
  wins: number;
  representativeTradeIndex: number | null;
  representativeTimestamp: number;
};

type TimeAnalysisReferenceMode = 'entry' | 'exit';
type TimeAnalysisGranularity = 'day' | 'hour';

type TradeTimeAnalysisPoint = {
  tradeIndex: number;
  referenceTimestamp: number;
  holdingDurationMs: number;
  returnPct: number;
  pnl: number;
  side: 'Long' | 'Short';
  isWin: boolean;
};

type HoldingDurationBucket = {
  key: string;
  label: string;
  minMinutes: number;
  maxMinutes: number;
  count: number;
  wins: number;
  pnl: number;
  returnPctSum: number;
  avgReturnPct: number;
  winRate: number;
};

type PeriodBucketMetrics = {
  key: string;
  label: string;
  count: number;
  wins: number;
  losses: number;
  pnl: number;
  avgPnl: number;
  returnPctSum: number;
  avgReturnPct: number;
  winRate: number;
  lossRate: number;
};

type SideBreakdownRow = {
  key: 'Long' | 'Short';
  label: string;
  count: number;
  share: number;
  wins: number;
  losses: number;
  winRate: number;
  netPnl: number;
  grossBeforeCosts: number;
  totalFee: number;
  totalFunding: number;
  avgNetPnl: number;
  avgHoldingMs: number;
};

type ExitReasonBreakdownRow = {
  key: string;
  label: string;
  count: number;
  share: number;
  wins: number;
  losses: number;
  winRate: number;
  netPnl: number;
  avgNetPnl: number;
  totalFee: number;
  totalFunding: number;
  avgHoldingMs: number;
};

type MonthlyPerformanceCell = {
  key: string;
  label: string;
  year: number;
  monthIndex: number;
  count: number;
  wins: number;
  losses: number;
  winRate: number;
  netPnl: number;
  totalFee: number;
  totalFunding: number;
  grossBeforeCosts: number;
  startEquity: number;
  endEquity: number;
  returnPct: number;
};

type DrawdownEpisode = {
  key: string;
  startTimestamp: number;
  troughTimestamp: number;
  recoveryTimestamp: number;
  peakEquity: number;
  troughEquity: number;
  lossFromPeak: number;
  depth: number;
  durationMs: number;
  isRecovered: boolean;
};

type RollingQualityPoint = {
  key: string;
  timestamp: number;
  tradeIndex: number;
  windowStartIndex: number;
  windowCount: number;
  netPnl: number;
  winRate: number;
  avgNetPnl: number;
  profitFactor: number;
};

const CATEGORY_LABELS: Record<string, string> = {
  compare: '比较类',
  cross: '交叉类',
  range: '区间类',
  trend: '趋势类',
  change: '变化率类',
  stats: '统计类',
  channel: '通道类',
  bandwidth: '带宽类',
};

const CATEGORY_ORDER = ['compare', 'cross', 'range', 'trend', 'change', 'stats', 'channel', 'bandwidth'];

/** 多头相关容器 ID（顺序：筛选器 → 开多 → 平多） */
const LONG_CONTAINER_IDS = ['open-long-filter', 'open-long', 'close-long'] as const;
/** 空头相关容器 ID（顺序：筛选器 → 开空 → 平空） */
const SHORT_CONTAINER_IDS = ['open-short-filter', 'open-short', 'close-short'] as const;

const getDropZoneHint = (containerId: string): string => {
  if (containerId === 'open-long-filter' || containerId === 'open-short-filter') return '筛选器允许多周期判断完善策略';
  if (containerId === 'open-long') return '配置开多条件 信号 触发交易';
  if (containerId === 'open-short') return '配置开空条件 信号 触发交易';
  if (containerId === 'close-long') return '配置平多 信号 预设离场';
  if (containerId === 'close-short') return '配置平空 信号 预设离场';
  return '拖拽操作符到此快速新增条件';
};

// 指标固定色板：常见指标优先不重复，亮色浅色为主。
const INDICATOR_COLOR_PRESET: Record<string, string> = {
  SMA: '#7DD3FC',
  EMA: '#F9A8D4',
  MA: '#86EFAC',
  MACD: '#6EE7B7',
  RSI: '#BFDBFE',
  BBANDS: '#DDD6FE',
  KDJ: '#FDE68A',
  ATR: '#FECACA',
  OBV: '#A7F3D0',
  ADX: '#FDBA74',
  STOCH: '#C4B5FD',
  STOCHRSI: '#99F6E4',
  VWAP: '#93C5FD',
  MOM: '#FCD34D',
};

const INDICATOR_COLOR_POOL = [
  '#93C5FD',
  '#A7F3D0',
  '#FDE68A',
  '#FBCFE8',
  '#C4B5FD',
  '#BAE6FD',
  '#FECACA',
  '#BBF7D0',
  '#FDBA74',
  '#DDD6FE',
  '#99F6E4',
  '#F9A8D4',
];

const normalizeText = (value?: string) => (value || '').trim();
const upperText = (value?: string) => normalizeText(value).toUpperCase();
const DAY_MS = 86_400_000;
const MONTH_LABELS = Array.from({ length: 12 }, (_, index) => `${index + 1}月`);

const hashText = (value: string) => {
  let hash = 0;
  for (let i = 0; i < value.length; i += 1) {
    hash = (hash << 5) - hash + value.charCodeAt(i);
    hash |= 0;
  }
  return Math.abs(hash);
};

const rgba = (hex: string, alpha: number) => {
  const safeHex = hex.replace('#', '');
  if (safeHex.length !== 6) {
    return `rgba(148,163,184,${alpha})`;
  }
  const r = Number.parseInt(safeHex.slice(0, 2), 16);
  const g = Number.parseInt(safeHex.slice(2, 4), 16);
  const b = Number.parseInt(safeHex.slice(4, 6), 16);
  return `rgba(${r},${g},${b},${alpha})`;
};

const parseNumberOrNull = (raw: string) => {
  const value = Number(raw);
  return Number.isFinite(value) ? value : null;
};

const toDateTimeLocalValue = (timestamp: number | null | undefined): string => {
  if (!Number.isFinite(timestamp) || Number(timestamp) <= 0) {
    return '';
  }
  const date = new Date(Number(timestamp));
  const pad = (num: number) => String(num).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(
    date.getMinutes(),
  )}`;
};

const parseDateTimeLocalValue = (raw: string): number | null => {
  const text = normalizeText(raw);
  if (!text) {
    return null;
  }
  const timestamp = Date.parse(text);
  return Number.isFinite(timestamp) ? timestamp : null;
};

const createDefaultBacktestParams = (
  takeProfitPct: number,
  stopLossPct: number,
  leverage: number,
  orderQty: number,
): WorkbenchBacktestParams => ({
  takeProfitPct,
  stopLossPct,
  leverage,
  orderQty,
  initialCapital: 10000,
  feeRate: 0.0004,
  fundingRate: 0,
  slippageBps: 0,
  autoReverse: false,
  useStrategyRuntime: true,
  executionMode: 'batch_open_close',
  timeRangeMode: 'latest_30d',
  rangeStartMs: null,
  rangeEndMs: null,
});

const normalizeBacktestParams = (params: WorkbenchBacktestParams): WorkbenchBacktestParams => {
  const normalizeFinite = (value: number, fallback: number) => (Number.isFinite(value) ? value : fallback);
  return {
    takeProfitPct: normalizeFinite(params.takeProfitPct, 0),
    stopLossPct: normalizeFinite(params.stopLossPct, 0),
    leverage: Math.max(1, Math.trunc(normalizeFinite(params.leverage, 1))),
    orderQty: Math.max(0, normalizeFinite(params.orderQty, 0)),
    initialCapital: Math.max(0, normalizeFinite(params.initialCapital, 0)),
    feeRate: Math.max(0, normalizeFinite(params.feeRate, 0)),
    fundingRate: normalizeFinite(params.fundingRate, 0),
    slippageBps: Math.max(0, Math.trunc(normalizeFinite(params.slippageBps, 0))),
    autoReverse: Boolean(params.autoReverse),
    useStrategyRuntime: Boolean(params.useStrategyRuntime),
    executionMode: params.executionMode === 'timeline' ? 'timeline' : 'batch_open_close',
    timeRangeMode: params.timeRangeMode === 'custom' ? 'custom' : 'latest_30d',
    rangeStartMs: Number.isFinite(params.rangeStartMs) ? Number(params.rangeStartMs) : null,
    rangeEndMs: Number.isFinite(params.rangeEndMs) ? Number(params.rangeEndMs) : null,
  };
};

const isSameBacktestParams = (a: WorkbenchBacktestParams, b: WorkbenchBacktestParams) => {
  return a.takeProfitPct === b.takeProfitPct
    && a.stopLossPct === b.stopLossPct
    && a.leverage === b.leverage
    && a.orderQty === b.orderQty
    && a.initialCapital === b.initialCapital
    && a.feeRate === b.feeRate
    && a.fundingRate === b.fundingRate
    && a.slippageBps === b.slippageBps
    && a.autoReverse === b.autoReverse
    && a.useStrategyRuntime === b.useStrategyRuntime
    && a.executionMode === b.executionMode
    && a.timeRangeMode === b.timeRangeMode
    && a.rangeStartMs === b.rangeStartMs
    && a.rangeEndMs === b.rangeEndMs;
};

const validateBacktestParams = (params: WorkbenchBacktestParams): string | null => {
  if (!Number.isFinite(params.takeProfitPct) || params.takeProfitPct <= 0) {
    return '止盈比例必须大于 0';
  }
  if (!Number.isFinite(params.stopLossPct) || params.stopLossPct <= 0) {
    return '止损比例必须大于 0';
  }
  if (!Number.isFinite(params.leverage) || params.leverage < 1) {
    return '杠杆必须大于等于 1';
  }
  if (!Number.isFinite(params.orderQty) || params.orderQty <= 0) {
    return '单次开仓数量必须大于 0';
  }
  if (!Number.isFinite(params.initialCapital) || params.initialCapital < 0) {
    return '初始资金不能小于 0';
  }
  if (!Number.isFinite(params.feeRate) || params.feeRate < 0) {
    return '手续费率不能小于 0';
  }
  if (!Number.isFinite(params.fundingRate)) {
    return '资金费率必须是有效数字';
  }
  if (!Number.isFinite(params.slippageBps) || params.slippageBps < 0) {
    return '滑点 Bps 不能小于 0';
  }
  if (params.executionMode !== 'batch_open_close' && params.executionMode !== 'timeline') {
    return '执行模式不合法';
  }
  if (params.timeRangeMode === 'custom') {
    if (!Number.isFinite(params.rangeStartMs) || !Number.isFinite(params.rangeEndMs)) {
      return '自定义时间范围必须填写开始和结束时间';
    }
    if (Number(params.rangeEndMs) <= Number(params.rangeStartMs)) {
      return '结束时间必须晚于开始时间';
    }
  }
  return null;
};

const formatSignedNumber = (value: number, digits = 4) => {
  if (!Number.isFinite(value)) {
    if (value === Number.POSITIVE_INFINITY) {
      return '+∞';
    }
    if (value === Number.NEGATIVE_INFINITY) {
      return '-∞';
    }
    return '-';
  }
  if (value > 0) {
    return `+${value.toFixed(digits)}`;
  }
  return value.toFixed(digits);
};

const formatNumberValue = (value: number, digits = 4) => {
  if (!Number.isFinite(value)) {
    if (value === Number.POSITIVE_INFINITY) {
      return '∞';
    }
    if (value === Number.NEGATIVE_INFINITY) {
      return '-∞';
    }
    return '-';
  }
  return value.toFixed(digits);
};

const formatPercentValue = (value: number, digits = 2) => {
  if (!Number.isFinite(value)) {
    return '-';
  }
  return `${(value * 100).toFixed(digits)}%`;
};

const methodShortLabel = (label: string, fallback: string) => {
  const matched = normalizeText(label).match(/^(.+?)\s*\(/);
  return normalizeText(matched?.[1]) || normalizeText(label) || fallback;
};

const stripParenthetical = (value?: string) =>
  normalizeText((value || '').replace(/\s*[\(\（][^\)\）]*[\)\）]/g, '').replace(/\s{2,}/g, ' '));

const stripFieldPrefix = (value?: string) =>
  normalizeText((value || '').replace(/^K线字段\s*-\s*/i, ''));

const indicatorCode = (indicator: GeneratedIndicatorPayload) =>
  upperText(typeof indicator.config?.indicator === 'string' ? indicator.config.indicator : indicator.code);

const getIndicatorLabelMeta = (indicator: GeneratedIndicatorPayload) => {
  const config = (indicator.config || {}) as {
    indicator?: unknown;
    input?: unknown;
    params?: unknown[];
  };
  const code =
    normalizeText(typeof config.indicator === 'string' ? config.indicator : indicator.code) || 'Indicator';
  const input = normalizeText(typeof config.input === 'string' ? config.input : '');
  const params = Array.isArray(config.params)
    ? config.params.map((item) => normalizeText(String(item))).filter(Boolean)
    : [];
  return {
    id: indicator.id,
    code,
    codeKey: upperText(code),
    input,
    inputKey: upperText(input),
    paramsLabel: params.join(' '),
    paramsKey: params.join(','),
  };
};

const toDropId = (containerId: string, groupId: string, conditionId: string, slot: DropSlot) =>
  `cond-drop|${containerId}|${groupId}|${conditionId}|${slot}`;

const parseDropId = (rawId: string) => {
  const parts = rawId.split('|');
  if (parts.length !== 5 || parts[0] !== 'cond-drop') {
    return null;
  }
  const slot = parts[4];
  if (slot !== 'left' && slot !== 'method' && slot !== 'right' && slot !== 'extra') {
    return null;
  }
  return {
    containerId: parts[1],
    groupId: parts[2],
    conditionId: parts[3],
    slot,
  };
};

const toIndicatorInputDropId = (indicatorId: string) => `indicator-input-drop|${indicatorId}`;
const parseIndicatorInputDropId = (rawId: string) => {
  const parts = rawId.split('|');
  if (parts.length !== 2 || parts[0] !== 'indicator-input-drop') {
    return null;
  }
  return { indicatorId: parts[1] };
};

const toConditionCreateContainerDropId = (containerId: string) => `condition-create-drop|container|${containerId}`;
const toConditionCreateGroupDropId = (containerId: string, groupId: string) =>
  `condition-create-drop|group|${containerId}|${groupId}`;
const parseConditionCreateDropId = (rawId: string) => {
  const parts = rawId.split('|');
  if (parts.length < 3 || parts[0] !== 'condition-create-drop') {
    return null;
  }
  if (parts[1] === 'container' && parts.length === 3) {
    return { containerId: parts[2], groupId: null as string | null };
  }
  if (parts[1] === 'group' && parts.length === 4) {
    return { containerId: parts[2], groupId: parts[3] };
  }
  return null;
};

const formatDateTime = (timestamp: number) => {
  if (!Number.isFinite(timestamp) || timestamp <= 0) {
    return '-';
  }
  return new Date(timestamp).toLocaleString('zh-CN', { hour12: false });
};

const formatDuration = (durationMs: number) => {
  if (!Number.isFinite(durationMs) || durationMs <= 0) {
    return '0s';
  }
  const totalSeconds = Math.max(0, Math.floor(durationMs / 1000));
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return minutes > 0 ? `${minutes}m ${seconds}s` : `${seconds}s`;
};

const formatDateTimeShort = (timestamp: number) => {
  if (!Number.isFinite(timestamp) || timestamp <= 0) {
    return '-';
  }
  const date = new Date(timestamp);
  const month = `${date.getMonth() + 1}`.padStart(2, '0');
  const day = `${date.getDate()}`.padStart(2, '0');
  const hour = `${date.getHours()}`.padStart(2, '0');
  const minute = `${date.getMinutes()}`.padStart(2, '0');
  return `${month}/${day} ${hour}:${minute}`;
};

const sampleEquityPoints = (points: LocalBacktestEquityPoint[], maxPoints: number) => {
  if (points.length <= maxPoints) {
    return points;
  }
  const stride = Math.ceil(points.length / maxPoints);
  const sampled: LocalBacktestEquityPoint[] = [];
  for (let index = 0; index < points.length; index += stride) {
    sampled.push(points[index]);
  }
  const last = points[points.length - 1];
  if (sampled[sampled.length - 1]?.timestamp !== last.timestamp) {
    sampled.push(last);
  }
  return sampled;
};

const findNearestEquityPoint = (points: LocalBacktestEquityPoint[], timestamp: number): LocalBacktestEquityPoint | null => {
  if (points.length <= 0 || !Number.isFinite(timestamp)) {
    return null;
  }
  let left = 0;
  let right = points.length - 1;
  while (left < right) {
    const mid = Math.floor((left + right) / 2);
    if (points[mid].timestamp < timestamp) {
      left = mid + 1;
    } else {
      right = mid;
    }
  }
  const current = points[left];
  const previous = left > 0 ? points[left - 1] : null;
  if (!previous) {
    return current;
  }
  return Math.abs(current.timestamp - timestamp) < Math.abs(previous.timestamp - timestamp) ? current : previous;
};

type EChartClickPayload = {
  componentType?: string;
  seriesType?: string;
  dataIndex?: number;
  value?: unknown;
  data?: unknown;
};

type EChartTooltipParam = {
  dataIndex?: number;
  value?: unknown;
  data?: unknown;
};

const toEChartTooltipParams = (params: unknown): EChartTooltipParam[] => {
  if (Array.isArray(params)) {
    return params.filter((item): item is EChartTooltipParam => typeof item === 'object' && item !== null);
  }
  if (typeof params === 'object' && params !== null) {
    return [params as EChartTooltipParam];
  }
  return [];
};

const resolveTimestampFromEChartPayload = (payload: EChartClickPayload): number | null => {
  const pickTimestamp = (value: unknown): number | null => {
    if (Array.isArray(value) && value.length > 0) {
      const ts = Number(value[0]);
      return Number.isFinite(ts) && ts > 0 ? ts : null;
    }
    const ts = Number(value);
    return Number.isFinite(ts) && ts > 0 ? ts : null;
  };
  const fromValue = pickTimestamp(payload.value);
  if (fromValue !== null) {
    return fromValue;
  }
  if (payload.data && typeof payload.data === 'object' && !Array.isArray(payload.data)) {
    const maybeValue = (payload.data as { value?: unknown }).value;
    return pickTimestamp(maybeValue);
  }
  return null;
};

const CALENDAR_DAY_LABELS = ['一', '二', '三', '四', '五', '六', '日'];
const WEEKDAY_LABELS = ['周一', '周二', '周三', '周四', '周五', '周六', '周日'];

const HOLDING_DURATION_BUCKET_DEFS: Array<{ key: string; label: string; minMinutes: number; maxMinutes: number }> = [
  { key: '0-15m', label: '0-15m', minMinutes: 0, maxMinutes: 15 },
  { key: '15-30m', label: '15-30m', minMinutes: 15, maxMinutes: 30 },
  { key: '30-60m', label: '30-60m', minMinutes: 30, maxMinutes: 60 },
  { key: '1-2h', label: '1-2h', minMinutes: 60, maxMinutes: 120 },
  { key: '2-4h', label: '2-4h', minMinutes: 120, maxMinutes: 240 },
  { key: '4-8h', label: '4-8h', minMinutes: 240, maxMinutes: 480 },
  { key: '8-24h', label: '8-24h', minMinutes: 480, maxMinutes: 1440 },
  { key: '1-3d', label: '1-3d', minMinutes: 1440, maxMinutes: 4320 },
  { key: '3d+', label: '3d+', minMinutes: 4320, maxMinutes: Number.POSITIVE_INFINITY },
];

const toCalendarDateKey = (date: Date) => {
  const year = date.getFullYear();
  const month = `${date.getMonth() + 1}`.padStart(2, '0');
  const day = `${date.getDate()}`.padStart(2, '0');
  return `${year}-${month}-${day}`;
};

const toCalendarMonthKey = (date: Date) => {
  const year = date.getFullYear();
  const month = `${date.getMonth() + 1}`.padStart(2, '0');
  return `${year}-${month}`;
};

const startOfLocalDay = (timestamp: number) => {
  const date = new Date(timestamp);
  date.setHours(0, 0, 0, 0);
  return date;
};

const startOfLocalWeek = (date: Date) => {
  const start = new Date(date);
  const weekday = (start.getDay() + 6) % 7;
  start.setDate(start.getDate() - weekday);
  start.setHours(0, 0, 0, 0);
  return start;
};

const startOfLocalMonth = (date: Date) => {
  const start = new Date(date);
  start.setDate(1);
  start.setHours(0, 0, 0, 0);
  return start;
};

const addDays = (date: Date, days: number) => {
  const next = new Date(date);
  next.setDate(next.getDate() + days);
  return next;
};

const addMonths = (date: Date, months: number) => {
  const next = new Date(date);
  next.setMonth(next.getMonth() + months);
  return next;
};

const formatCalendarPeriodLabel = (mode: CalendarViewMode, anchorDate: Date) => {
  if (mode === 'month') {
    return `${anchorDate.getFullYear()}年 ${anchorDate.getMonth() + 1}月`;
  }
  if (mode === 'week') {
    const weekStart = startOfLocalWeek(anchorDate);
    const weekEnd = addDays(weekStart, 6);
    return `${weekStart.getMonth() + 1}/${weekStart.getDate()} - ${weekEnd.getMonth() + 1}/${weekEnd.getDate()}`;
  }
  return `${anchorDate.getFullYear()}/${anchorDate.getMonth() + 1}/${anchorDate.getDate()}`;
};

const buildEmptyCalendarMetrics = (): CalendarBucketMetrics => ({
  pnl: 0,
  count: 0,
  wins: 0,
  representativeTradeIndex: null,
  representativeTimestamp: 0,
});

const mergeCalendarMetrics = (a: CalendarBucketMetrics, b: CalendarBucketMetrics): CalendarBucketMetrics => ({
  pnl: a.pnl + b.pnl,
  count: a.count + b.count,
  wins: a.wins + b.wins,
  representativeTradeIndex:
    a.representativeTimestamp >= b.representativeTimestamp
      ? a.representativeTradeIndex
      : b.representativeTradeIndex,
  representativeTimestamp: Math.max(a.representativeTimestamp, b.representativeTimestamp),
});

const resolveTradeReferenceTimestamp = (trade: LocalBacktestTrade, mode: TimeAnalysisReferenceMode) => {
  const timestamp = mode === 'entry' ? Number(trade.entryTime) : Number(trade.exitTime);
  if (!Number.isFinite(timestamp) || timestamp <= 0) {
    return null;
  }
  return timestamp;
};

const resolveTradeHoldingDurationMs = (trade: LocalBacktestTrade, fallbackMs: number) => {
  const entry = Number(trade.entryTime);
  const exit = Number(trade.exitTime);
  if (!Number.isFinite(entry) || !Number.isFinite(exit) || entry <= 0 || exit <= 0) {
    return fallbackMs;
  }
  return Math.max(fallbackMs, exit - entry);
};

const resolveTradeReturnPct = (trade: LocalBacktestTrade) => {
  const notional = Math.abs(Number(trade.entryPrice) * Number(trade.qty));
  if (!Number.isFinite(notional) || notional <= 0) {
    return 0;
  }
  const pnl = Number.isFinite(trade.pnl) ? Number(trade.pnl) : 0;
  return (pnl / notional) * 100;
};

const resolveTradeFee = (trade: LocalBacktestTrade) => {
  return Number.isFinite(trade.fee) ? Number(trade.fee) : 0;
};

const resolveTradeFunding = (trade: LocalBacktestTrade) => {
  return Number.isFinite(trade.funding) ? Number(trade.funding) : 0;
};

const resolveTradeNetPnl = (trade: LocalBacktestTrade) => {
  const pnl = Number.isFinite(trade.pnl) ? Number(trade.pnl) : 0;
  return pnl - resolveTradeFunding(trade);
};

const formatExitReasonLabel = (reason?: string) => {
  switch (normalizeText(reason)) {
    case 'Signal':
      return '信号平仓';
    case 'Reverse':
      return '反向换仓';
    case 'TakeProfit':
      return '止盈平仓';
    case 'StopLoss':
      return '止损平仓';
    case 'Open':
      return '未平仓快照';
    default:
      return normalizeText(reason) || '未知原因';
  }
};

const toMonthlyBucketMeta = (timestamp: number) => {
  const date = new Date(timestamp);
  const year = date.getFullYear();
  const monthIndex = date.getMonth();
  const month = `${monthIndex + 1}`.padStart(2, '0');
  return {
    key: `${year}-${month}`,
    label: `${year}-${month}`,
    year,
    monthIndex,
  };
};

const buildDrawdownEpisodes = (points: LocalBacktestEquityPoint[]): DrawdownEpisode[] => {
  if (points.length < 2) {
    return [];
  }

  const episodes: DrawdownEpisode[] = [];
  let peakPoint = points[0];
  let active: DrawdownEpisode | null = null;

  for (let index = 1; index < points.length; index += 1) {
    const point = points[index];
    if (!Number.isFinite(point.equity) || !Number.isFinite(point.timestamp)) {
      continue;
    }

    if (point.equity >= peakPoint.equity) {
      if (active) {
        episodes.push({
          ...active,
          recoveryTimestamp: point.timestamp,
          durationMs: Math.max(0, point.timestamp - active.startTimestamp),
          isRecovered: true,
        });
        active = null;
      }
      peakPoint = point;
      continue;
    }

    if (!Number.isFinite(peakPoint.equity) || peakPoint.equity <= 0) {
      continue;
    }

    const lossFromPeak = peakPoint.equity - point.equity;
    const depth = lossFromPeak / peakPoint.equity;
    if (depth <= 0) {
      continue;
    }

    if (!active) {
      active = {
        key: `${peakPoint.timestamp}-${point.timestamp}`,
        startTimestamp: peakPoint.timestamp,
        troughTimestamp: point.timestamp,
        recoveryTimestamp: 0,
        peakEquity: peakPoint.equity,
        troughEquity: point.equity,
        lossFromPeak,
        depth,
        durationMs: Math.max(0, point.timestamp - peakPoint.timestamp),
        isRecovered: false,
      };
      continue;
    }

    if (point.equity <= active.troughEquity) {
      active = {
        ...active,
        troughTimestamp: point.timestamp,
        troughEquity: point.equity,
        lossFromPeak,
        depth,
        durationMs: Math.max(0, point.timestamp - active.startTimestamp),
      };
    }
  }

  if (active) {
    const lastPoint = points[points.length - 1];
    episodes.push({
      ...active,
      durationMs: Math.max(active.durationMs, Math.max(0, lastPoint.timestamp - active.startTimestamp)),
    });
  }

  return episodes.sort((a, b) => {
    if (b.depth !== a.depth) {
      return b.depth - a.depth;
    }
    if (b.lossFromPeak !== a.lossFromPeak) {
      return b.lossFromPeak - a.lossFromPeak;
    }
    return b.durationMs - a.durationMs;
  });
};

const buildPreviewTradeKey = (trade: LocalBacktestTrade, index: number) => {
  return [
    trade.side,
    trade.entryTime,
    trade.exitTime,
    trade.entryPrice,
    trade.exitPrice,
    trade.qty,
    trade.slippageBps,
    index,
  ].join('|');
};

const buildPreviewTradeFocusRange = (
  trade: LocalBacktestTrade,
  index: number,
  timeframeSec: number,
  latestBacktestTimestamp: number,
): StrategyWorkbenchTradeFocusRange | null => {
  const entryTime = Number(trade.entryTime);
  if (!Number.isFinite(entryTime) || entryTime <= 0) {
    return null;
  }
  const intervalMs = Math.max(1_000, Math.trunc(timeframeSec) * 1_000);
  const hasClosed = !trade.isOpen && Number.isFinite(trade.exitTime) && trade.exitTime > 0;
  let visualExitTime = hasClosed ? trade.exitTime : latestBacktestTimestamp + intervalMs * 10;
  if (!Number.isFinite(visualExitTime) || visualExitTime <= 0) {
    visualExitTime = entryTime + intervalMs * 10;
  }
  if (visualExitTime <= entryTime) {
    visualExitTime = entryTime + intervalMs * 10;
  }
  const startTime = Math.min(entryTime, visualExitTime);
  const endTime = Math.max(entryTime, visualExitTime);
  const tradeKey = buildPreviewTradeKey(trade, index);
  return {
    id: tradeKey,
    startTime,
    endTime,
    side: trade.side,
    entryPrice: trade.entryPrice,
    exitPrice: trade.exitPrice,
    stopLossPrice: trade.stopLossPrice,
    takeProfitPrice: trade.takeProfitPrice,
  };
};

const estimateTradeSpanBarsByTimeframe = (startTime: number, endTime: number, timeframeSec: number) => {
  const rangeMs = Math.max(1_000, Math.abs(endTime - startTime));
  const intervalMs = Math.max(1_000, Math.trunc(timeframeSec) * 1_000);
  return Math.max(2, Math.ceil(rangeMs / intervalMs) + 1);
};

const FOCUS_RANGE_TARGET_OCCUPANCY = 0.2;
const FOCUS_RANGE_MAX_OCCUPANCY = 2 / 3;

const extractClientPoint = (event: Event | null | undefined) => {
  if (!event) {
    return null;
  }
  if ('clientX' in event && 'clientY' in event) {
    const clientX = Number((event as MouseEvent).clientX);
    const clientY = Number((event as MouseEvent).clientY);
    if (Number.isFinite(clientX) && Number.isFinite(clientY)) {
      return { x: clientX, y: clientY };
    }
  }
  if ('touches' in event) {
    const touchEvent = event as TouchEvent;
    const touch = touchEvent.touches?.[0] || touchEvent.changedTouches?.[0];
    if (touch) {
      return { x: touch.clientX, y: touch.clientY };
    }
  }
  return null;
};

const DraggableToken: React.FC<{
  id: string;
  payload: DragPayload;
  className: string;
  style?: React.CSSProperties;
  children: React.ReactNode;
  onMouseEnter?: () => void;
  onMouseLeave?: () => void;
}> = ({ id, payload, className, style, children, onMouseEnter, onMouseLeave }) => {
  const previewText =
    typeof children === 'string' || typeof children === 'number'
      ? String(children)
      : payload.label;
  const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({
    id,
    data: {
      ...payload,
      previewClassName: className,
      previewStyle: style,
      previewText,
    },
  });
  const dragTransform = isDragging ? undefined : CSS.Translate.toString(transform);

  return (
    <button
      ref={setNodeRef}
      type="button"
      className={className}
      style={{
        ...style,
        transform: dragTransform,
        opacity: isDragging ? 0 : 1,
      }}
      onMouseEnter={onMouseEnter}
      onMouseLeave={onMouseLeave}
      {...listeners}
      {...attributes}
    >
      {children}
    </button>
  );
};

const DroppableSlot: React.FC<{
  id: string;
  className: string;
  disabled?: boolean;
  style?: React.CSSProperties;
  onClick?: () => void;
  children: React.ReactNode;
}> = ({ id, className, disabled = false, style, onClick, children }) => {
  const { setNodeRef, isOver } = useDroppable({ id, disabled });
  const clickable = Boolean(onClick) && !disabled;
  return (
    <div
      ref={setNodeRef}
      className={`${className} ${isOver ? 'is-over' : ''} ${disabled ? 'is-disabled' : ''} ${clickable ? 'is-clickable' : ''}`}
      style={style}
      onClick={clickable ? onClick : undefined}
      role={clickable ? 'button' : undefined}
      tabIndex={clickable ? 0 : undefined}
      onKeyDown={
        clickable
          ? (event) => {
              if (event.key === 'Enter' || event.key === ' ') {
                event.preventDefault();
                onClick?.();
              }
            }
          : undefined
      }
    >
      {children}
    </div>
  );
};

/** 可拖拽调整宽度的分隔条，用于左右面板之间 */
const ResizeHandle: React.FC<{
  onResize: (deltaX: number) => void;
  className?: string;
}> = ({ onResize, className }) => {
  const startX = useRef(0);
  const handlePointerDown = useCallback(
    (e: React.PointerEvent) => {
      e.preventDefault();
      e.stopPropagation();
      startX.current = e.clientX;
      const onMove = (e2: PointerEvent) => {
        const dx = e2.clientX - startX.current;
        onResize(dx);
        startX.current = e2.clientX;
      };
      const onUp = () => {
        document.removeEventListener('pointermove', onMove);
        document.removeEventListener('pointerup', onUp);
        (document.body as HTMLElement).style.cursor = '';
        (document.body as HTMLElement).style.userSelect = '';
      };
      document.addEventListener('pointermove', onMove);
      document.addEventListener('pointerup', onUp);
      (document.body as HTMLElement).style.cursor = 'col-resize';
      (document.body as HTMLElement).style.userSelect = 'none';
    },
    [onResize],
  );
  return <div className={className} onPointerDown={handlePointerDown} role="separator" aria-orientation="vertical" />;
};

/** 可拖拽调整高度的分隔条，用于上下区块之间 */
const ResizeHandleVertical: React.FC<{
  onResize: (deltaY: number) => void;
  className?: string;
}> = ({ onResize, className }) => {
  const startY = useRef(0);
  const handlePointerDown = useCallback(
    (e: React.PointerEvent) => {
      e.preventDefault();
      e.stopPropagation();
      startY.current = e.clientY;
      const onMove = (e2: PointerEvent) => {
        const dy = e2.clientY - startY.current;
        onResize(dy);
        startY.current = e2.clientY;
      };
      const onUp = () => {
        document.removeEventListener('pointermove', onMove);
        document.removeEventListener('pointerup', onUp);
        (document.body as HTMLElement).style.cursor = '';
        (document.body as HTMLElement).style.userSelect = '';
      };
      document.addEventListener('pointermove', onMove);
      document.addEventListener('pointerup', onUp);
      (document.body as HTMLElement).style.cursor = 'row-resize';
      (document.body as HTMLElement).style.userSelect = 'none';
    },
    [onResize],
  );
  return <div className={className} onPointerDown={handlePointerDown} role="separator" aria-orientation="horizontal" />;
};

const StrategyWorkbenchEChart: React.FC<{
  option: EChartsOption;
  className: string;
  onChartClick?: (payload: EChartClickPayload) => void;
}> = ({ option, className, onChartClick }) => {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const chartRef = useRef<ECharts | null>(null);

  useEffect(() => {
    const dom = containerRef.current;
    if (!dom) {
      return;
    }
    const chart = echarts.getInstanceByDom(dom) ?? echarts.init(dom);
    chartRef.current = chart;
    chart.setOption(option, true);

    const resizeObserver = new ResizeObserver(() => {
      chart.resize();
    });
    resizeObserver.observe(dom);

    return () => {
      resizeObserver.disconnect();
      chart.dispose();
      chartRef.current = null;
    };
  }, []);

  useEffect(() => {
    chartRef.current?.setOption(option, true);
  }, [option]);

  useEffect(() => {
    const chart = chartRef.current;
    if (!chart) {
      return;
    }
    const setChartCursor = (style: 'pointer' | 'default') => {
      const zr = (chart as { getZr?: () => { setCursorStyle?: (value: string) => void } | null }).getZr?.();
      zr?.setCursorStyle?.(style);
    };
    const handleClick = (payload: unknown) => {
      onChartClick?.(payload as EChartClickPayload);
    };
    chart.off('click', handleClick);
    if (onChartClick) {
      chart.on('click', handleClick);
      setChartCursor('pointer');
    } else {
      setChartCursor('default');
    }
    return () => {
      chart.off('click', handleClick);
      setChartCursor('default');
    };
  }, [onChartClick]);

  return <div ref={containerRef} className={className} />;
};

const StrategyWorkbench: React.FC<StrategyWorkbenchProps> = (props) => {
  const {
    selectedIndicators,
    formatIndicatorName,
    onOpenIndicatorGenerator,
    onEditIndicator,
    onRemoveIndicator,
    logicContainers,
    filterContainers,
    maxGroupsPerContainer,
    onAddConditionGroup,
    onToggleGroupFlag,
    onOpenConditionModal,
    onRemoveGroup,
    onToggleConditionFlag,
    onRemoveCondition,
    renderToggle,
    onClose,
    onOpenExport,
    exchangeOptions,
    selectedExchange,
    onExchangeChange,
    symbolOptions,
    selectedSymbol,
    onSymbolChange,
    timeframeOptions,
    selectedTimeframeSec,
    onTimeframeChange,
    takeProfitPct,
    stopLossPct,
    leverage,
    orderQty,
    onTakeProfitPctChange,
    onStopLossPctChange,
    onLeverageChange,
    onOrderQtyChange,
    indicatorOutputGroups,
    methodOptions,
    onQuickAssignConditionMethod,
    onQuickAssignConditionValue,
    onQuickAssignConditionNumber,
    onQuickUpdateConditionRightNumber,
    onQuickAssignConditionExtraValue,
    onQuickAssignConditionExtraNumber,
    onQuickUpdateConditionExtraNumber,
    onQuickUpdateConditionParamValue,
    onQuickUpdateIndicatorInput,
    onQuickEditIndicatorParams,
    onQuickCreateCondition,
    topbarExtraActions,
    floatingOverlay,
  } = props;

  const [ready, setReady] = useState(false);
  const [conditionSide, setConditionSide] = useState<'long' | 'short'>('long');
  const [expandedGroupId, setExpandedGroupId] = useState<string | null>(null);
  const [expandedConditionKey, setExpandedConditionKey] = useState<string | null>(null);
  const [klineBars, setKlineBars] = useState<KLineData[]>([]);
  const [talibReady, setTalibReady] = useState(false);
  const [activeDrag, setActiveDrag] = useState<DragPayload | null>(null);
  const [dragCursor, setDragCursor] = useState<{ x: number; y: number } | null>(null);
  const [focusRightNumberKey, setFocusRightNumberKey] = useState('');
  const [focusExtraNumberKey, setFocusExtraNumberKey] = useState('');
  const [activeHoverValueId, setActiveHoverValueId] = useState('');
  const [showOfflineCacheDialog, setShowOfflineCacheDialog] = useState(false);
  const [dashboardMode, setDashboardMode] = useState<DashboardMode>('preview');
  const [backtestFormParams, setBacktestFormParams] = useState<WorkbenchBacktestParams>(() =>
    createDefaultBacktestParams(takeProfitPct, stopLossPct, leverage, orderQty),
  );
  const [appliedBacktestParams, setAppliedBacktestParams] = useState<WorkbenchBacktestParams>(() =>
    normalizeBacktestParams(createDefaultBacktestParams(takeProfitPct, stopLossPct, leverage, orderQty)),
  );
  const [, setBacktestParamError] = useState('');
  const [localBacktestSummary, setLocalBacktestSummary] = useState<LocalBacktestSummary>(() =>
    buildEmptyBacktestSummary('waiting_data', '等待开始创建'),
  );
  const [backtestProgress, setBacktestProgress] = useState<WorkbenchBacktestProgress>({
    processedBars: 0,
    totalBars: 0,
    progress: 0,
    elapsedMs: 0,
    done: false,
  });
  const [selectedPreviewTradeKey, setSelectedPreviewTradeKey] = useState<string | null>(null);
  const [focusedPreviewTradeRange, setFocusedPreviewTradeRange] = useState<StrategyWorkbenchTradeFocusRange | null>(null);
  const [previewTradeMode, setPreviewTradeMode] = useState<PreviewTradeMode>('normal');
  const [workbenchLayoutMode, setWorkbenchLayoutMode] = useState<WorkbenchLayoutMode>('edit');
  const [previewScrollSyncEnabled, setPreviewScrollSyncEnabled] = useState(false);
  const [fullPreviewAnchorTradeKey, setFullPreviewAnchorTradeKey] = useState<string | null>(null);
  /** 左侧面板宽度占比 (20–80%)，用于 refactor 布局 */
  const [leftPanelWidth, setLeftPanelWidth] = useState(52.8);
  /** 右侧内部：右左面板宽度占比 (25–75%)，用于 refactor 布局 */
  const [rightLeftPanelWidth, setRightLeftPanelWidth] = useState(46.3);
  /** 仪表盘预览区：仓位列表宽度占比 (20–55%)，默认约 1/3 */
  const [liveListPanelWidth, setLiveListPanelWidth] = useState(33.333);
  const [calendarViewMode, setCalendarViewMode] = useState<CalendarViewMode>('month');
  const [liveSummaryTab, setLiveSummaryTab] = useState<LiveSummaryTab>('overview');
  const [chartTimeframeSec, setChartTimeframeSec] = useState(selectedTimeframeSec);
  const [calendarAnchorTimestamp, setCalendarAnchorTimestamp] = useState(0);
  const [timeAnalysisReferenceMode, setTimeAnalysisReferenceMode] = useState<TimeAnalysisReferenceMode>('entry');
  const [timeAnalysisGranularity, setTimeAnalysisGranularity] = useState<TimeAnalysisGranularity>('hour');
  /** 左侧：K线图高度占比 (30–85%)，用于 refactor 布局 */
  const [leftKlineHeight, setLeftKlineHeight] = useState(63.6);
  /** 右侧左栏：已选指标高度占比 (25–75%)，用于 refactor 布局 */
  const [rightPanelHeight, setRightPanelHeight] = useState(50);
  const [rightPanelHeightCustomized, setRightPanelHeightCustomized] = useState(false);
  const mainLayoutRef = useRef<HTMLDivElement>(null);
  const rightPanelRef = useRef<HTMLDivElement>(null);
  const leftPanelRef = useRef<HTMLDivElement>(null);
  const rightLeftRef = useRef<HTMLDivElement>(null);
  const livePreviewRef = useRef<HTMLDivElement>(null);
  const previewListBodyRef = useRef<HTMLDivElement | null>(null);
  const previewTradeItemRefs = useRef<Map<string, HTMLDivElement>>(new Map());
  const previewListScrollRafRef = useRef<number | null>(null);
  const previewViewportSyncRafRef = useRef<number | null>(null);
  const previewListSuppressTimerRef = useRef<number | null>(null);
  const previewListSyncLockedRef = useRef(false);
  const pendingViewportRef = useRef<StrategyWorkbenchVisibleRange | null>(null);
  const fullPreviewAnchorTradeKeyRef = useRef<string | null>(null);
  const previewAutoTimeframeSwitchKeyRef = useRef('');
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 6 } }));

  const handleMainResize = useCallback((deltaX: number) => {
    setLeftPanelWidth((prev) => {
      const el = mainLayoutRef.current;
      const w = el?.offsetWidth ?? 800;
      const newPct = prev + (deltaX / w) * 100;
      return Math.min(80, Math.max(20, newPct));
    });
  }, []);

  const handleRightResize = useCallback((deltaX: number) => {
    setRightLeftPanelWidth((prev) => {
      const el = rightPanelRef.current;
      const w = el?.offsetWidth ?? 400;
      const newPct = prev + (deltaX / w) * 100;
      return Math.min(75, Math.max(25, newPct));
    });
  }, []);

  const handleLivePreviewResize = useCallback((deltaX: number) => {
    setLiveListPanelWidth((prev) => {
      const el = livePreviewRef.current;
      const w = el?.offsetWidth ?? 640;
      const newPct = prev - (deltaX / w) * 100;
      return Math.min(55, Math.max(20, newPct));
    });
  }, []);

  const handleLeftVerticalResize = useCallback((deltaY: number) => {
    setLeftKlineHeight((prev) => {
      const el = leftPanelRef.current;
      const h = el?.offsetHeight ?? 600;
      const newPct = prev + (deltaY / h) * 100;
      return Math.min(85, Math.max(30, newPct));
    });
  }, []);

  const handleRightLeftVerticalResize = useCallback((deltaY: number) => {
    setRightPanelHeightCustomized((prev) => (prev ? prev : true));
    setRightPanelHeight((prev) => {
      const el = rightLeftRef.current;
      const h = el?.offsetHeight ?? 400;
      const newPct = prev + (deltaY / h) * 100;
      return Math.min(75, Math.max(25, newPct));
    });
  }, []);

  const availableDataRange = useMemo(() => {
    if (klineBars.length <= 0) {
      return { start: 0, end: 0 };
    }
    const start = Number(klineBars[0]?.timestamp ?? 0);
    const end = Number(klineBars[klineBars.length - 1]?.timestamp ?? 0);
    if (!Number.isFinite(start) || !Number.isFinite(end) || end <= 0) {
      return { start: 0, end: 0 };
    }
    return { start, end };
  }, [klineBars]);

  const exchangeLabel = useMemo(() => {
    return exchangeOptions.find((item) => item.value === selectedExchange)?.label || selectedExchange;
  }, [exchangeOptions, selectedExchange]);

  const symbolLabel = useMemo(() => {
    return symbolOptions.find((item) => item.value === selectedSymbol)?.label || selectedSymbol;
  }, [symbolOptions, selectedSymbol]);

  const timeframeLabel = useMemo(() => {
    return timeframeOptions.find((item) => item.value === selectedTimeframeSec)?.label || `${selectedTimeframeSec}s`;
  }, [timeframeOptions, selectedTimeframeSec]);

  const chartTimeframeOptions = useMemo(() => {
    const map = new Map<number, string>();
    timeframeOptions.forEach((item) => {
      const value = Number(item.value);
      if (!Number.isFinite(value) || value <= 0) {
        return;
      }
      map.set(value, item.label || `${value}s`);
    });
    if (!map.has(selectedTimeframeSec) && selectedTimeframeSec > 0) {
      map.set(selectedTimeframeSec, `${selectedTimeframeSec}s`);
    }
    return Array.from(map.entries())
      .map(([value, label]) => ({ value, label }))
      .sort((a, b) => a.value - b.value);
  }, [selectedTimeframeSec, timeframeOptions]);

  const chartTimeframeLabel = useMemo(() => {
    return chartTimeframeOptions.find((item) => item.value === chartTimeframeSec)?.label || `${chartTimeframeSec}s`;
  }, [chartTimeframeOptions, chartTimeframeSec]);

  const indicatorColorMap = useMemo(() => {
    const map = new Map<string, string>();
    selectedIndicators.forEach((indicator) => {
      const code = indicatorCode(indicator) || 'UNKNOWN';
      const color =
        INDICATOR_COLOR_PRESET[code] ||
        INDICATOR_COLOR_POOL[hashText(code) % INDICATOR_COLOR_POOL.length];
      map.set(indicator.id, color);
    });
    return map;
  }, [selectedIndicators]);

  const indicatorGroupMap = useMemo(() => {
    return new Map(indicatorOutputGroups.map((group) => [group.id, group]));
  }, [indicatorOutputGroups]);
  const fieldOutputGroup = indicatorGroupMap.get('kline-fields');

  const methodMap = useMemo(() => {
    return new Map(methodOptions.map((option) => [option.value, option]));
  }, [methodOptions]);

  const operatorGroups = useMemo(() => {
    const grouped = new Map<string, MethodOption[]>();
    methodOptions.forEach((method) => {
      const category = method.category || 'compare';
      const list = grouped.get(category) || [];
      list.push(method);
      grouped.set(category, list);
    });

    const orderedKeys = [
      ...CATEGORY_ORDER.filter((key) => grouped.has(key)),
      ...Array.from(grouped.keys()).filter((key) => !CATEGORY_ORDER.includes(key)),
    ];

    return orderedKeys.map((key) => ({
      key,
      label: CATEGORY_LABELS[key] || key,
      methods: grouped.get(key) || [],
    }));
  }, [methodOptions]);

  const firstMethodByCategory = useMemo(() => {
    const map = new Map<string, MethodOption>();
    operatorGroups.forEach((group) => {
      if (group.methods.length > 0) {
        map.set(group.key, group.methods[0]);
      }
    });
    return map;
  }, [operatorGroups]);

  const defaultCreateMethod = useMemo(() => {
    const compareMethod = firstMethodByCategory.get('compare')?.value;
    if (compareMethod) {
      return compareMethod;
    }
    for (const group of operatorGroups) {
      if (group.methods.length > 0) {
        return group.methods[0].value;
      }
    }
    return '';
  }, [firstMethodByCategory, operatorGroups]);

  const indicatorLabelSpecMap = useMemo(() => {
    const grouped = new Map<string, ReturnType<typeof getIndicatorLabelMeta>[]>();
    selectedIndicators.forEach((indicator) => {
      const meta = getIndicatorLabelMeta(indicator);
      const key = meta.codeKey || meta.code;
      const list = grouped.get(key) || [];
      list.push(meta);
      grouped.set(key, list);
    });

    const specMap = new Map<string, {
      code: string;
      paramsLabel: string;
      input: string;
      mode: IndicatorLabelDisplayMode;
    }>();

    grouped.forEach((items) => {
      const paramSet = new Set(items.map((item) => item.paramsKey || '__EMPTY__'));
      const inputSet = new Set(items.map((item) => item.inputKey || '__EMPTY__'));
      const mode: IndicatorLabelDisplayMode =
        items.length <= 1
          ? 'code-only'
          : paramSet.size > 1 && inputSet.size <= 1
            ? 'code-param'
            : paramSet.size <= 1 && inputSet.size > 1
              ? 'code-input'
              : paramSet.size <= 1 && inputSet.size <= 1
                ? 'code-only'
                : 'full';

      items.forEach((item) => {
        specMap.set(item.id, {
          code: item.code,
          paramsLabel: item.paramsLabel,
          input: item.input,
          mode,
        });
      });
    });

    return specMap;
  }, [selectedIndicators]);

  const valueLabelMap = useMemo(() => {
    const map = new Map<string, string>();
    indicatorOutputGroups.forEach((group) => {
      group.options.forEach((option) => {
        if (group.id === 'kline-fields') {
          map.set(option.id, option.fullLabel || option.label);
          return;
        }
        const plainLabel =
          stripParenthetical(option.label) || stripParenthetical(option.fullLabel) || option.label;
        map.set(option.id, plainLabel || option.fullLabel || option.label);
      });
    });
    return map;
  }, [indicatorOutputGroups]);

  const compactValueLabelMap = useMemo(() => {
    const map = new Map<string, string>();
    indicatorOutputGroups.forEach((group) => {
      group.options.forEach((option) => {
        if (group.id === 'kline-fields') {
          map.set(option.id, option.fullLabel || option.label);
          return;
        }
        const outputLabel =
          stripParenthetical(option.label) || stripParenthetical(option.fullLabel) || option.label;
        const spec = indicatorLabelSpecMap.get(group.id);
        if (!spec) {
          map.set(option.id, outputLabel || option.fullLabel || option.label);
          return;
        }
        const parts = [spec.code];
        if ((spec.mode === 'code-param' || spec.mode === 'full') && spec.paramsLabel) {
          parts.push(spec.paramsLabel);
        }
        if ((spec.mode === 'code-input' || spec.mode === 'full') && spec.input) {
          parts.push(spec.input);
        }
        const prefix = parts.join(' ').trim();
        map.set(option.id, prefix ? `${prefix} - ${outputLabel}` : outputLabel);
      });
    });
    return map;
  }, [indicatorLabelSpecMap, indicatorOutputGroups]);

  const outputBadgeStyleMap = useMemo(() => {
    const map = new Map<string, React.CSSProperties>();
    indicatorOutputGroups.forEach((group) => {
      if (group.id === 'kline-fields') {
        return;
      }
      const color = indicatorColorMap.get(group.id) || '#93C5FD';
      group.options.forEach((option, outputIndex) => {
        map.set(option.id, {
          background: rgba(color, 0.24 + outputIndex * 0.08),
          border: `1px solid ${rgba(color, 0.62)}`,
          color: '#0f172a',
        });
      });
    });
    return map;
  }, [indicatorOutputGroups, indicatorColorMap]);

  const conditionValueUsageCountMap = useMemo(() => {
    const map = new Map<string, number>();
    const pushValue = (valueId?: string) => {
      const normalized = normalizeText(valueId);
      if (!normalized) {
        return;
      }
      map.set(normalized, (map.get(normalized) || 0) + 1);
    };

    [...logicContainers, ...filterContainers].forEach((container) => {
      container.groups.forEach((group) => {
        group.conditions.forEach((condition) => {
          pushValue(condition.leftValueId);
          if (condition.rightValueType === 'field') {
            pushValue(condition.rightValueId);
          }
          if (condition.extraValueType === 'field') {
            pushValue(condition.extraValueId);
          }
        });
      });
    });

    return map;
  }, [logicContainers, filterContainers]);

  const activeHoverHasReference = useMemo(() => {
    if (!activeHoverValueId) {
      return false;
    }
    return (conditionValueUsageCountMap.get(activeHoverValueId) || 0) > 0;
  }, [activeHoverValueId, conditionValueUsageCountMap]);

  const isLinkedActive = (valueId?: string) => {
    const normalized = normalizeText(valueId);
    if (!normalized || !activeHoverHasReference || !activeHoverValueId) {
      return false;
    }
    return normalized === activeHoverValueId;
  };

  useEffect(() => {
    setBacktestFormParams((prev) => ({
      ...prev,
      takeProfitPct,
      stopLossPct,
      leverage,
      orderQty,
    }));
    setAppliedBacktestParams((prev) => {
      const next = normalizeBacktestParams({
        ...prev,
        takeProfitPct,
        stopLossPct,
        leverage,
        orderQty,
      });
      return isSameBacktestParams(prev, next) ? prev : next;
    });
  }, [takeProfitPct, stopLossPct, leverage, orderQty]);

  const updateBacktestNumberField = (field: WorkbenchBacktestNumberField, raw: string) => {
    const next = parseNumberOrNull(raw);
    if (next === null) {
      return;
    }
    setBacktestFormParams((prev) => ({
      ...prev,
      [field]: next,
    }));
  };

  const updateBacktestTimeRangeMode = (nextMode: BacktestRangeMode) => {
    setBacktestFormParams((prev) => {
      if (nextMode === 'custom') {
        const fallbackEnd = availableDataRange.end > 0 ? availableDataRange.end : Date.now();
        const fallbackStart = availableDataRange.start > 0
          ? availableDataRange.start
          : fallbackEnd - 30 * DAY_MS;
        return {
          ...prev,
          timeRangeMode: 'custom',
          rangeStartMs: Number.isFinite(prev.rangeStartMs) ? prev.rangeStartMs : fallbackStart,
          rangeEndMs: Number.isFinite(prev.rangeEndMs) ? prev.rangeEndMs : fallbackEnd,
        };
      }
      return {
        ...prev,
        timeRangeMode: 'latest_30d',
      };
    });
  };

  const updateBacktestRangeField = (field: 'rangeStartMs' | 'rangeEndMs', raw: string) => {
    const parsed = parseDateTimeLocalValue(raw);
    setBacktestFormParams((prev) => ({
      ...prev,
      [field]: parsed,
    }));
  };

  const applyBacktestParams = () => {
    const normalized = normalizeBacktestParams(backtestFormParams);
    const error = validateBacktestParams(normalized);
    if (error) {
      setBacktestParamError(error);
      return;
    }
    setBacktestParamError('');
    setDashboardMode('preview');
  };

  useEffect(() => {
    const normalized = normalizeBacktestParams(backtestFormParams);
    const error = validateBacktestParams(normalized);
    if (error) {
      setBacktestParamError(error);
      return;
    }
    setBacktestParamError('');
    setAppliedBacktestParams((prev) => (isSameBacktestParams(prev, normalized) ? prev : normalized));
    // 参数变更后实时同步到上层交易配置，确保“改参数即触发回测”。
    if (Math.abs(takeProfitPct - normalized.takeProfitPct) > 1e-10) {
      onTakeProfitPctChange(normalized.takeProfitPct);
    }
    if (Math.abs(stopLossPct - normalized.stopLossPct) > 1e-10) {
      onStopLossPctChange(normalized.stopLossPct);
    }
    if (leverage !== normalized.leverage) {
      onLeverageChange(normalized.leverage);
    }
    if (Math.abs(orderQty - normalized.orderQty) > 1e-10) {
      onOrderQtyChange(normalized.orderQty);
    }
  }, [
    backtestFormParams,
    takeProfitPct,
    stopLossPct,
    leverage,
    orderQty,
    onTakeProfitPctChange,
    onStopLossPctChange,
    onLeverageChange,
    onOrderQtyChange,
  ]);

  const backtestBars = useMemo(() => {
    if (klineBars.length <= 0) {
      return [];
    }

    const latestTimestamp = Number(klineBars[klineBars.length - 1]?.timestamp ?? 0);
    if (!Number.isFinite(latestTimestamp) || latestTimestamp <= 0) {
      return klineBars;
    }

    let startTimestamp = latestTimestamp - 30 * DAY_MS;
    let endTimestamp = latestTimestamp;
    if (appliedBacktestParams.timeRangeMode === 'custom') {
      startTimestamp = Number(appliedBacktestParams.rangeStartMs ?? startTimestamp);
      endTimestamp = Number(appliedBacktestParams.rangeEndMs ?? endTimestamp);
    }

    if (!Number.isFinite(startTimestamp) || !Number.isFinite(endTimestamp) || endTimestamp <= startTimestamp) {
      return [];
    }

    return klineBars.filter((bar) => {
      const timestamp = Number(bar.timestamp);
      return Number.isFinite(timestamp) && timestamp >= startTimestamp && timestamp <= endTimestamp;
    });
  }, [
    klineBars,
    appliedBacktestParams.timeRangeMode,
    appliedBacktestParams.rangeStartMs,
    appliedBacktestParams.rangeEndMs,
  ]);

  useEffect(() => {
    if (!ready) {
      setLocalBacktestSummary(buildEmptyBacktestSummary('waiting_data', '等待开始创建'));
      setBacktestProgress({
        processedBars: 0,
        totalBars: 0,
        progress: 0,
        elapsedMs: 0,
        done: false,
      });
      return;
    }
    if (!talibReady && selectedIndicators.length > 0) {
      setLocalBacktestSummary(buildEmptyBacktestSummary('waiting_data', '指标计算内核初始化中'));
      setBacktestProgress({
        processedBars: 0,
        totalBars: 0,
        progress: 0,
        elapsedMs: 0,
        done: false,
      });
      return;
    }
    let controller: ReturnType<typeof runLocalBacktestRealtime> | null = null;
    // 高频编辑场景下做轻量防抖，避免每次按键都触发完整重算。
    const debounceTimer = window.setTimeout(() => {
      controller = runLocalBacktestRealtime({
        bars: backtestBars,
        selectedIndicators,
        indicatorOutputGroups,
        logicContainers,
        filterContainers,
        methodOptions,
        takeProfitPct: appliedBacktestParams.takeProfitPct,
        stopLossPct: appliedBacktestParams.stopLossPct,
        leverage: appliedBacktestParams.leverage,
        orderQty: appliedBacktestParams.orderQty,
        initialCapital: appliedBacktestParams.initialCapital,
        feeRate: appliedBacktestParams.feeRate,
        fundingRate: appliedBacktestParams.fundingRate,
        slippageBps: appliedBacktestParams.slippageBps,
        autoReverse: appliedBacktestParams.autoReverse,
        executionMode: appliedBacktestParams.executionMode,
        useStrategyRuntime: appliedBacktestParams.useStrategyRuntime,
      }, {
        chunkSize: 320,
        tickMs: 16,
        onProgress: (progressInfo) => {
          setLocalBacktestSummary(progressInfo.summary);
          setBacktestProgress({
            processedBars: progressInfo.processedBars,
            totalBars: progressInfo.totalBars,
            progress: progressInfo.progress,
            elapsedMs: progressInfo.elapsedMs,
            done: progressInfo.done,
          });
        },
      });
    }, 80);
    return () => {
      window.clearTimeout(debounceTimer);
      controller?.cancel();
    };
  }, [
    ready,
    talibReady,
    backtestBars,
    selectedIndicators,
    indicatorOutputGroups,
    logicContainers,
    filterContainers,
    methodOptions,
    appliedBacktestParams,
  ]);

  useEffect(() => {
    let disposed = false;
    registerTalibIndicators()
      .then(() => {
        if (!disposed) {
          setTalibReady(true);
        }
      })
      .catch(() => {
        if (!disposed) {
          setTalibReady(false);
        }
      });
    return () => {
      disposed = true;
    };
  }, []);

  useEffect(() => {
    if (!activeDrag) {
      return;
    }

    const handlePointerMove = (event: PointerEvent) => {
      const point = extractClientPoint(event);
      if (point) {
        setDragCursor(point);
      }
    };
    const handleTouchMove = (event: TouchEvent) => {
      const point = extractClientPoint(event);
      if (point) {
        setDragCursor(point);
      }
    };

    window.addEventListener('pointermove', handlePointerMove, { passive: true });
    window.addEventListener('touchmove', handleTouchMove, { passive: true });
    return () => {
      window.removeEventListener('pointermove', handlePointerMove);
      window.removeEventListener('touchmove', handleTouchMove);
    };
  }, [activeDrag]);

  const resolveArgMode = (method: MethodOption | undefined, argIndex: number): ArgValueMode => {
    return method?.argValueTypes?.[argIndex] || 'both';
  };

  const buildValueBadgeStyle = (valueId?: string): React.CSSProperties | undefined => {
    const normalized = normalizeText(valueId);
    if (!normalized) {
      return undefined;
    }
    const outputStyle = outputBadgeStyleMap.get(normalized);
    if (outputStyle) {
      return outputStyle;
    }
    if (normalized.startsWith('field:')) {
      return {
        background: '#f1f5f9',
        border: '1px solid #cbd5e1',
        color: '#334155',
      };
    }
    const indicatorId = normalized.split(':')[0] || '';
    const color = indicatorColorMap.get(indicatorId);
    if (!color) {
      return undefined;
    }
    return {
      background: rgba(color, 0.24),
      border: `1px solid ${rgba(color, 0.62)}`,
      color: '#0f172a',
    };
  };

  const renderValueText = (valueId?: string) => {
    if (!valueId) {
      return '未配置';
    }
    const rawText = compactValueLabelMap.get(valueId) || valueLabelMap.get(valueId) || valueId;
    return stripFieldPrefix(rawText);
  };

  const onDragStart = (event: DragStartEvent) => {
    const payload = event.active.data.current as DragPayload | null | undefined;
    if (payload) {
      setActiveDrag(payload);
    }
    setActiveHoverValueId('');
    const point = extractClientPoint(event.activatorEvent);
    if (point) {
      setDragCursor(point);
    }
  };

  const onDragCancel = (_event: DragCancelEvent) => {
    setActiveDrag(null);
    setDragCursor(null);
    setActiveHoverValueId('');
  };

  const onDragEnd = (event: DragEndEvent) => {
    const payload = event.active.data.current as DragPayload | null | undefined;
    const dropTargetId = event.over ? String(event.over.id) : '';
    const conditionDrop = dropTargetId ? parseDropId(dropTargetId) : null;
    const indicatorInputDrop = dropTargetId ? parseIndicatorInputDropId(dropTargetId) : null;
    const conditionCreateDrop = dropTargetId ? parseConditionCreateDropId(dropTargetId) : null;
    setActiveDrag(null);
    setDragCursor(null);
    setActiveHoverValueId('');

    if (!payload) {
      return;
    }

    if (payload.kind === 'output' && indicatorInputDrop) {
      onQuickUpdateIndicatorInput(indicatorInputDrop.indicatorId, payload.valueId);
      return;
    }
    if (payload.kind === 'number' && indicatorInputDrop) {
      onQuickEditIndicatorParams(indicatorInputDrop.indicatorId);
      return;
    }

    if (
      conditionCreateDrop &&
      (payload.kind === 'method' || payload.kind === 'category')
    ) {
      const resolvedMethod =
        payload.kind === 'method'
          ? payload.method
          : firstMethodByCategory.get(payload.category)?.value;
      if (!resolvedMethod) {
        return;
      }
      onQuickCreateCondition(
        conditionCreateDrop.containerId,
        conditionCreateDrop.groupId,
        resolvedMethod,
      );
      return;
    }

    if (!conditionDrop) {
      return;
    }

    if (payload.kind === 'output' && (conditionDrop.slot === 'left' || conditionDrop.slot === 'right' || conditionDrop.slot === 'extra')) {
      if (conditionDrop.slot === 'extra') {
        onQuickAssignConditionExtraValue(
          conditionDrop.containerId,
          conditionDrop.groupId,
          conditionDrop.conditionId,
          payload.valueId,
        );
      } else {
        onQuickAssignConditionValue(
          conditionDrop.containerId,
          conditionDrop.groupId,
          conditionDrop.conditionId,
          conditionDrop.slot,
          payload.valueId,
        );
      }
      return;
    }
    if (payload.kind === 'condition-value' && (conditionDrop.slot === 'left' || conditionDrop.slot === 'right' || conditionDrop.slot === 'extra')) {
      const sameCondition =
        payload.source.containerId === conditionDrop.containerId
        && payload.source.groupId === conditionDrop.groupId
        && payload.source.conditionId === conditionDrop.conditionId;
      if (!sameCondition) {
        return;
      }
      if (payload.source.slot === conditionDrop.slot) {
        return;
      }
      if (conditionDrop.slot === 'extra') {
        onQuickAssignConditionExtraValue(
          conditionDrop.containerId,
          conditionDrop.groupId,
          conditionDrop.conditionId,
          payload.valueId,
          payload.source,
        );
      } else {
        onQuickAssignConditionValue(
          conditionDrop.containerId,
          conditionDrop.groupId,
          conditionDrop.conditionId,
          conditionDrop.slot,
          payload.valueId,
          payload.source,
        );
      }
      return;
    }
    if (payload.kind === 'number' && conditionDrop.slot === 'right') {
      onQuickAssignConditionNumber(
        conditionDrop.containerId,
        conditionDrop.groupId,
        conditionDrop.conditionId,
      );
      setFocusRightNumberKey(`${conditionDrop.containerId}|${conditionDrop.groupId}|${conditionDrop.conditionId}`);
      return;
    }
    if (payload.kind === 'number' && conditionDrop.slot === 'extra') {
      onQuickAssignConditionExtraNumber(
        conditionDrop.containerId,
        conditionDrop.groupId,
        conditionDrop.conditionId,
      );
      setFocusExtraNumberKey(`${conditionDrop.containerId}|${conditionDrop.groupId}|${conditionDrop.conditionId}|extra`);
      return;
    }

    if (conditionDrop.slot !== 'method') {
      return;
    }

    if (payload.kind === 'method') {
      onQuickAssignConditionMethod(
        conditionDrop.containerId,
        conditionDrop.groupId,
        conditionDrop.conditionId,
        payload.method,
      );
      return;
    }

    if (payload.kind === 'category') {
      const fallback = firstMethodByCategory.get(payload.category);
      if (!fallback) {
        return;
      }
      onQuickAssignConditionMethod(
        conditionDrop.containerId,
        conditionDrop.groupId,
        conditionDrop.conditionId,
        fallback.value,
      );
    }
  };

  const onClickCreateCondition = (containerId: string, groupId: string) => {
    if (!defaultCreateMethod) {
      return;
    }
    onQuickCreateCondition(containerId, groupId, defaultCreateMethod);
  };

  const renderCondition = (
    containerId: string,
    groupId: string,
    condition: ConditionItem,
    index: number,
  ) => {
    const methodMeta = methodMap.get(condition.method);
    const methodValue = methodMeta?.value || condition.method;
    const methodLabel = methodMeta?.label || condition.method;
    const methodCategory = methodMeta?.category || 'compare';
    const methodCategoryLabel = CATEGORY_LABELS[methodCategory] || methodCategory;
    const methodBriefLabel = methodShortLabel(methodLabel, methodValue);
    const argsCount = methodMeta?.argsCount ?? 2;
    const rightMode = resolveArgMode(methodMeta, 1);
    const extraMode = resolveArgMode(methodMeta, 2);
    const canShowRight = argsCount >= 2;
    const canShowExtra = argsCount >= 3;
    const canDropRight = argsCount >= 2 && rightMode !== 'number';
    const canUseRightNumber = argsCount >= 2 && rightMode !== 'field';
    const canDropExtra = canShowExtra && extraMode !== 'number';
    const canUseExtraNumber = canShowExtra && extraMode !== 'field';
    const numberFocusKey = `${containerId}|${groupId}|${condition.id}`;
    const extraNumberFocusKey = `${containerId}|${groupId}|${condition.id}|extra`;
    const conditionRowKey = `${containerId}|${groupId}|${condition.id}`;
    const isConditionExpanded = expandedConditionKey === conditionRowKey;
    const leftLinkedActive = isLinkedActive(condition.leftValueId);
    const rightLinkedActive = condition.rightValueType === 'field' && isLinkedActive(condition.rightValueId);
    const extraLinkedActive = condition.extraValueType === 'field' && isLinkedActive(condition.extraValueId);
    const argLabels = methodMeta?.argLabels || [];
    const leftArgLabel = argLabels[0] || '左值';
    const rightArgLabel = argLabels[1] || '右值';
    const extraArgLabel = argLabels[2] || '第三参数';
    const paramDefs = methodMeta?.params || [];
    const rawParamValues = condition.paramValues || [];
    const periodParamIndex = paramDefs.findIndex((param) =>
      normalizeText(param.key).toLowerCase().includes('period'),
    );
    const inlineParamIndex =
      (methodValue === 'Rising'
        || methodValue === 'Falling'
        || methodValue === 'AboveFor'
        || methodValue === 'BelowFor')
      && periodParamIndex >= 0
        ? periodParamIndex
        : -1;
    const visibleParamIndexes = paramDefs
      .map((_param, paramIndex) => paramIndex)
      .filter((paramIndex) => paramIndex !== inlineParamIndex);

    const rightCandidateCount = Array.from(valueLabelMap.keys()).filter(
      (valueId) => valueId !== condition.leftValueId,
    ).length;
    const extraCandidateCount = Array.from(valueLabelMap.keys()).filter(
      (valueId) => valueId !== condition.leftValueId && valueId !== condition.rightValueId,
    ).length;
    const rightNoTargetHint = canDropRight && rightCandidateCount === 0 ? '当前没有可用右值' : '';
    const extraNoTargetHint = canDropExtra && extraCandidateCount === 0 ? '当前没有可用第三参数' : '';
    const rightText =
      condition.rightValueType === 'number'
        ? `数值 ${condition.rightNumber || '未填写'}`
        : renderValueText(condition.rightValueId);
    const extraText =
      condition.extraValueType === 'number'
        ? `数值 ${condition.extraNumber || '未填写'}`
        : renderValueText(condition.extraValueId);

    const renderParamInputControl = (paramIndex: number, className: string) => {
      const param = paramDefs[paramIndex];
      if (!param) {
        return null;
      }
      return (
        <input
          className={className}
          type="number"
          value={rawParamValues[paramIndex] || ''}
          onChange={(event) =>
            onQuickUpdateConditionParamValue(
              containerId,
              groupId,
              condition.id,
              paramIndex,
              event.target.value,
            )
          }
          onMouseDown={(event) => event.stopPropagation()}
          onPointerDown={(event) => event.stopPropagation()}
          placeholder={param.placeholder || param.defaultValue || ''}
        />
      );
    };

    const renderLeftSlot = (showLabel = false, label = leftArgLabel) => (
      <DroppableSlot
        id={toDropId(containerId, groupId, condition.id, 'left')}
        className="condition-dnd-slot condition-dnd-slot--arg"
      >
        {showLabel ? <span className="condition-dnd-arg-label">{label}</span> : null}
        {condition.leftValueId ? (
          <DraggableToken
            id={`drag-condition-value|${containerId}|${groupId}|${condition.id}|left`}
            payload={{
              kind: 'condition-value',
              valueId: condition.leftValueId,
              label: renderValueText(condition.leftValueId),
              source: {
                containerId,
                groupId,
                conditionId: condition.id,
                slot: 'left',
              },
            }}
            className={`condition-dnd-value condition-dnd-value-badge ${leftLinkedActive ? 'is-linked-active' : ''}`}
            style={buildValueBadgeStyle(condition.leftValueId)}
            onMouseEnter={() => setActiveHoverValueId(condition.leftValueId)}
            onMouseLeave={() => setActiveHoverValueId('')}
          >
            {renderValueText(condition.leftValueId)}
          </DraggableToken>
        ) : (
          <span className="condition-dnd-value condition-dnd-value--placeholder">
            {renderValueText(condition.leftValueId)}
          </span>
        )}
      </DroppableSlot>
    );

    const renderMethodSlot = (text = methodBriefLabel) => (
      <DroppableSlot
        id={toDropId(containerId, groupId, condition.id, 'method')}
        className="condition-dnd-slot condition-dnd-slot--operator"
      >
        <span className="condition-dnd-value">{text}</span>
      </DroppableSlot>
    );

    const renderRightSlot = (showLabel = false, label = rightArgLabel) => {
      if (!canShowRight) {
        return null;
      }
      return (
        <DroppableSlot
          id={toDropId(containerId, groupId, condition.id, 'right')}
          className="condition-dnd-slot condition-dnd-slot--arg"
          disabled={!canDropRight}
        >
          {showLabel ? <span className="condition-dnd-arg-label">{label}</span> : null}
          {canUseRightNumber && condition.rightValueType === 'number' ? (
            <input
              className="condition-dnd-input"
              type="number"
              value={condition.rightNumber || ''}
              onChange={(event) =>
                onQuickUpdateConditionRightNumber(
                  containerId,
                  groupId,
                  condition.id,
                  event.target.value,
                )
              }
              onMouseDown={(event) => event.stopPropagation()}
              onPointerDown={(event) => event.stopPropagation()}
              autoFocus={focusRightNumberKey === numberFocusKey}
              onBlur={() => {
                if (focusRightNumberKey === numberFocusKey) {
                  setFocusRightNumberKey('');
                }
              }}
              placeholder="输入数值"
            />
          ) : condition.rightValueType === 'field' && condition.rightValueId ? (
            <DraggableToken
              id={`drag-condition-value|${containerId}|${groupId}|${condition.id}|right`}
              payload={{
                kind: 'condition-value',
                valueId: condition.rightValueId,
                label: renderValueText(condition.rightValueId),
                source: {
                  containerId,
                  groupId,
                  conditionId: condition.id,
                  slot: 'right',
                },
              }}
              className={`condition-dnd-value condition-dnd-value-badge ${rightLinkedActive ? 'is-linked-active' : ''}`}
              style={buildValueBadgeStyle(condition.rightValueId)}
              onMouseEnter={() => setActiveHoverValueId(condition.rightValueId || '')}
              onMouseLeave={() => setActiveHoverValueId('')}
            >
              {rightNoTargetHint || rightText}
            </DraggableToken>
          ) : (
            <span
              className={`condition-dnd-value ${condition.rightValueType === 'field' ? 'condition-dnd-value-badge' : 'condition-dnd-value--placeholder'} ${rightLinkedActive ? 'is-linked-active' : ''}`}
              style={condition.rightValueType === 'field' ? buildValueBadgeStyle(condition.rightValueId) : undefined}
            >
              {rightNoTargetHint || rightText}
            </span>
          )}
        </DroppableSlot>
      );
    };

    const renderExtraSlot = (showLabel = false, label = extraArgLabel) => {
      if (!canShowExtra) {
        return null;
      }
      return (
        <DroppableSlot
          id={toDropId(containerId, groupId, condition.id, 'extra')}
          className="condition-dnd-slot condition-dnd-slot--arg"
          disabled={!canDropExtra}
        >
          {showLabel ? <span className="condition-dnd-arg-label">{label}</span> : null}
          {canUseExtraNumber && condition.extraValueType === 'number' ? (
            <input
              className="condition-dnd-input"
              type="number"
              value={condition.extraNumber || ''}
              onChange={(event) =>
                onQuickUpdateConditionExtraNumber(
                  containerId,
                  groupId,
                  condition.id,
                  event.target.value,
                )
              }
              onMouseDown={(event) => event.stopPropagation()}
              onPointerDown={(event) => event.stopPropagation()}
              autoFocus={focusExtraNumberKey === extraNumberFocusKey}
              onBlur={() => {
                if (focusExtraNumberKey === extraNumberFocusKey) {
                  setFocusExtraNumberKey('');
                }
              }}
              placeholder="输入数值"
            />
          ) : condition.extraValueType === 'field' && condition.extraValueId ? (
            <DraggableToken
              id={`drag-condition-value|${containerId}|${groupId}|${condition.id}|extra`}
              payload={{
                kind: 'condition-value',
                valueId: condition.extraValueId,
                label: renderValueText(condition.extraValueId),
                source: {
                  containerId,
                  groupId,
                  conditionId: condition.id,
                  slot: 'extra',
                },
              }}
              className={`condition-dnd-value condition-dnd-value-badge ${extraLinkedActive ? 'is-linked-active' : ''}`}
              style={buildValueBadgeStyle(condition.extraValueId)}
              onMouseEnter={() => setActiveHoverValueId(condition.extraValueId || '')}
              onMouseLeave={() => setActiveHoverValueId('')}
            >
              {extraNoTargetHint || extraText}
            </DraggableToken>
          ) : (
            <span
              className={`condition-dnd-value ${condition.extraValueType === 'field' ? 'condition-dnd-value-badge' : 'condition-dnd-value--placeholder'} ${extraLinkedActive ? 'is-linked-active' : ''}`}
              style={condition.extraValueType === 'field' ? buildValueBadgeStyle(condition.extraValueId) : undefined}
            >
              {extraNoTargetHint || extraText}
            </span>
          )}
        </DroppableSlot>
      );
    };

    const renderInlinePeriodParam = () => {
      if (inlineParamIndex < 0) {
        return null;
      }
      return (
        <span className="condition-inline-param">
          {renderParamInputControl(inlineParamIndex, 'condition-inline-param-input')}
          <span className="condition-expression-text">个周期</span>
        </span>
      );
    };

    const renderExpressionFlow = () => {
      if (methodValue === 'Between' || methodValue === 'Outside') {
        return (
          <>
            {renderLeftSlot(false)}
            {renderMethodSlot('在')}
            <span className="condition-expression-text">上界</span>
            {renderExtraSlot(false, extraArgLabel)}
            <span className="condition-expression-text">和</span>
            <span className="condition-expression-text">下界</span>
            {renderRightSlot(false, rightArgLabel)}
            <span className="condition-expression-text">
              {methodValue === 'Between' ? '区间内' : '区间外'}
            </span>
          </>
        );
      }

      if (methodValue === 'Rising' || methodValue === 'Falling') {
        return (
          <>
            {renderLeftSlot(false)}
            {renderMethodSlot(methodBriefLabel)}
            {renderInlinePeriodParam()}
          </>
        );
      }

      if (methodValue === 'AboveFor' || methodValue === 'BelowFor') {
        return (
          <>
            {renderLeftSlot(false)}
            {renderMethodSlot(methodBriefLabel)}
            {renderRightSlot(false, rightArgLabel)}
            {renderInlinePeriodParam()}
          </>
        );
      }

      return (
        <>
          {renderLeftSlot(false)}
          {renderMethodSlot(methodBriefLabel)}
          {renderRightSlot(false, rightArgLabel)}
          {canShowExtra ? (
            <>
              <span className="condition-expression-text">与</span>
              {renderExtraSlot(false, extraArgLabel)}
            </>
          ) : null}
        </>
      );
    };

    return (
      <div className="condition-item-card condition-item-card--advanced" key={condition.id}>
        <div className="condition-item-top">
          <div className="condition-item-top-left">
            <div className="condition-item-index">条件 {index + 1}</div>
            <div className="condition-item-method-brief">
              {methodCategoryLabel} {methodBriefLabel}
            </div>
          </div>
        </div>

        <div className="condition-logic-zone">
          <div className="condition-logic-row">
            <div className="condition-item-dnd-row condition-item-dnd-row--method">
              {renderExpressionFlow()}
            </div>

            <div
              className={`condition-item-more-wrap ${isConditionExpanded ? 'is-expanded' : ''}`}
              onMouseEnter={() => setExpandedConditionKey(conditionRowKey)}
              onMouseLeave={() => setExpandedConditionKey(null)}
            >
              <div
                className={`condition-item-actions--expandable ${isConditionExpanded ? 'is-expanded' : ''}`}
                onClick={(event) => event.stopPropagation()}
              >
                {renderToggle(
                  condition.enabled,
                  () => onToggleConditionFlag(containerId, groupId, condition.id, 'enabled'),
                  '启用',
                )}
                {renderToggle(
                  condition.required,
                  () => onToggleConditionFlag(containerId, groupId, condition.id, 'required'),
                  '必选',
                )}
                <button
                  type="button"
                  className="condition-delete-button"
                  onClick={() => onOpenConditionModal(containerId, groupId, condition.id)}
                >
                  编辑
                </button>
                <button
                  type="button"
                  className="condition-delete-button"
                  onClick={() => onRemoveCondition(containerId, groupId, condition.id)}
                >
                  删除
                </button>
              </div>
              <button
                type="button"
                className="condition-more-button"
                onClick={(event) => {
                  event.stopPropagation();
                  setExpandedConditionKey((prev) => (prev === conditionRowKey ? null : conditionRowKey));
                }}
              >
                更多
              </button>
            </div>
          </div>

          {visibleParamIndexes.length > 0 ? (
            <div className="condition-param-row">
              {visibleParamIndexes.map((paramIndex) => {
                const param = paramDefs[paramIndex];
                return (
                  <label key={param.key} className="condition-param-chip">
                    <span className="condition-param-label">{param.label}</span>
                    {renderParamInputControl(paramIndex, 'condition-param-input')}
                  </label>
                );
              })}
            </div>
          ) : null}
        </div>
      </div>
    );
  };

  const renderContainer = (container: ConditionContainer) => (
    <DroppableSlot
      key={container.id}
      id={toConditionCreateContainerDropId(container.id)}
      className="condition-container-card condition-create-drop-zone"
    >
      <div className="condition-container-header">
        <div className="condition-container-header-left">
          <span className="condition-container-title">{container.title}</span>
          {container.groups.length > 0 && (
            <span className="condition-container-meta">
              条件组 {container.groups.length}/{maxGroupsPerContainer}
            </span>
          )}
        </div>
        <div className="condition-container-actions">
          <button
            type="button"
            className="condition-add-group"
            onClick={() => onAddConditionGroup(container.id)}
          >
            新增条件组
          </button>
        </div>
      </div>

      {container.groups.length === 0 ? (
        <div className="condition-container-drop-hint">{getDropZoneHint(container.id)}</div>
      ) : (
        <div className="condition-group-list">
          {container.groups.map((group, groupIndex) => (
            <div className="condition-group-card" key={group.id}>
              <div className="condition-group-header">
                <span className="condition-group-title">
                  {group.name || `条件组 ${groupIndex + 1}`}
                </span>
                <span className="condition-group-meta">条件数 {group.conditions.length}</span>
                <div
                  className={`condition-group-more-wrap ${expandedGroupId === group.id ? 'is-expanded' : ''}`}
                  onMouseEnter={() => setExpandedGroupId(group.id)}
                  onMouseLeave={() => setExpandedGroupId(null)}
                >
                  <div
                    className={`condition-group-actions condition-group-actions--expandable ${expandedGroupId === group.id ? 'is-expanded' : ''}`}
                  >
                    {renderToggle(
                      group.enabled,
                      () => onToggleGroupFlag(container.id, group.id, 'enabled'),
                      '启用',
                    )}
                    {renderToggle(
                      group.required,
                      () => onToggleGroupFlag(container.id, group.id, 'required'),
                      '必选',
                    )}
                    <button
                      type="button"
                      className="condition-delete-button"
                      onClick={() => onOpenConditionModal(container.id, group.id)}
                    >
                      新增
                    </button>
                    <button
                      type="button"
                      className="condition-delete-button"
                      onClick={() => onRemoveGroup(container.id, group.id)}
                    >
                      删除条件组
                    </button>
                  </div>
                  <button
                    type="button"
                    className="condition-more-button"
                    onClick={(e) => {
                      e.stopPropagation();
                      setExpandedGroupId((prev) => (prev === group.id ? null : group.id));
                    }}
                  >
                    更多
                  </button>
                </div>
              </div>

              <DroppableSlot
                id={toConditionCreateGroupDropId(container.id, group.id)}
                className="condition-create-drop-zone condition-create-drop-zone--group"
                onClick={() => onClickCreateCondition(container.id, group.id)}
              >
                <span className="condition-drop-zone-bold">点击</span> 或{' '}
                <span className="condition-drop-zone-bold">拖拽操作符</span>到此处创建条件
              </DroppableSlot>

              {group.conditions.length > 0 ? (
                <div className="condition-item-list">
                  {group.conditions.map((condition, index) =>
                    renderCondition(container.id, group.id, condition, index),
                  )}
                </div>
              ) : null}
            </div>
          ))}
        </div>
      )}
    </DroppableSlot>
  );

  const allContainers = useMemo(() => {
    const map = new Map<string, ConditionContainer>();
    [...logicContainers, ...filterContainers].forEach((c) => map.set(c.id, c));
    return map;
  }, [logicContainers, filterContainers]);

  const containers = useMemo(() => {
    const ids = conditionSide === 'long' ? LONG_CONTAINER_IDS : SHORT_CONTAINER_IDS;
    return ids.map((id) => allContainers.get(id)).filter(Boolean) as ConditionContainer[];
  }, [conditionSide, allContainers]);

  const previewTrades = useMemo(() => {
    return [...localBacktestSummary.trades].sort((a, b) => {
      if (a.entryTime !== b.entryTime) {
        return b.entryTime - a.entryTime;
      }
      return b.exitTime - a.exitTime;
    });
  }, [localBacktestSummary.trades]);

  const latestBacktestTimestamp = useMemo(() => {
    const last = backtestBars[backtestBars.length - 1];
    const timestamp = Number(last?.timestamp ?? 0);
    return Number.isFinite(timestamp) && timestamp > 0 ? timestamp : 0;
  }, [backtestBars]);

  const previewTradeRanges = useMemo(() => {
    return previewTrades
      .map((trade, index) => buildPreviewTradeFocusRange(trade, index, chartTimeframeSec, latestBacktestTimestamp))
      .filter((range): range is StrategyWorkbenchTradeFocusRange => range !== null);
  }, [chartTimeframeSec, latestBacktestTimestamp, previewTrades]);

  const previewTradeRangeMap = useMemo(() => {
    const map = new Map<string, StrategyWorkbenchTradeFocusRange>();
    previewTradeRanges.forEach((range) => {
      map.set(range.id, range);
    });
    return map;
  }, [previewTradeRanges]);

  const previewTradeSyncItems = useMemo<PreviewTradeSyncItem[]>(() => {
    return previewTradeRanges.map((range) => ({
      key: range.id,
      startTime: Math.min(range.startTime, range.endTime),
      endTime: Math.max(range.startTime, range.endTime),
      midpoint: Math.floor((range.startTime + range.endTime) / 2),
    }));
  }, [previewTradeRanges]);

  const fullPreviewAnchorRange = useMemo(() => {
    if (!fullPreviewAnchorTradeKey) {
      return null;
    }
    return previewTradeRangeMap.get(fullPreviewAnchorTradeKey) || null;
  }, [fullPreviewAnchorTradeKey, previewTradeRangeMap]);

  const activePreviewTradeKey = previewTradeMode === 'full' ? fullPreviewAnchorTradeKey : selectedPreviewTradeKey;

  const focusPreviewTrade = useCallback((trade: LocalBacktestTrade, index: number) => {
    const range = buildPreviewTradeFocusRange(trade, index, chartTimeframeSec, latestBacktestTimestamp);
    if (!range) {
      return;
    }
    const tradeKey = range.id;
    setSelectedPreviewTradeKey(tradeKey);
    setFocusedPreviewTradeRange({
      id: `${tradeKey}-${Date.now()}`,
      startTime: range.startTime,
      endTime: range.endTime,
      side: range.side,
      entryPrice: range.entryPrice,
      exitPrice: range.exitPrice,
      stopLossPrice: range.stopLossPrice,
      takeProfitPrice: range.takeProfitPrice,
    });
  }, [chartTimeframeSec, latestBacktestTimestamp]);

  const lockPreviewListSync = useCallback((durationMs = 90) => {
    previewListSyncLockedRef.current = true;
    if (previewListSuppressTimerRef.current !== null) {
      window.clearTimeout(previewListSuppressTimerRef.current);
      previewListSuppressTimerRef.current = null;
    }
    previewListSuppressTimerRef.current = window.setTimeout(() => {
      previewListSyncLockedRef.current = false;
      previewListSuppressTimerRef.current = null;
    }, durationMs);
  }, []);

  const resolveTopVisiblePreviewTradeKey = useCallback(() => {
    const listBody = previewListBodyRef.current;
    if (!listBody) {
      return null;
    }
    const rect = listBody.getBoundingClientRect();
    if (rect.width <= 0 || rect.height <= 0) {
      return null;
    }
    const sampleX = rect.left + rect.width * 0.5;
    const sampleOffsets = [0.18, 0.34, 0.52];
    for (const offsetRatio of sampleOffsets) {
      const sampleY = rect.top + rect.height * offsetRatio;
      const element = document.elementFromPoint(sampleX, sampleY) as HTMLElement | null;
      const item = element?.closest<HTMLElement>('[data-preview-trade-key]');
      const key = item?.dataset.previewTradeKey?.trim();
      if (key) {
        return key;
      }
    }
    return null;
  }, []);

  const scrollPreviewListToTrade = useCallback((tradeKey: string) => {
    const listBody = previewListBodyRef.current;
    const row = previewTradeItemRefs.current.get(tradeKey);
    if (!listBody || !row) {
      return;
    }
    const maxTop = Math.max(0, listBody.scrollHeight - listBody.clientHeight);
    const targetTop = Math.max(0, Math.min(maxTop, row.offsetTop - listBody.clientHeight * 0.3));
    if (Math.abs(listBody.scrollTop - targetTop) < 2) {
      return;
    }
    lockPreviewListSync();
    listBody.scrollTo({ top: targetTop, behavior: 'auto' });
  }, [lockPreviewListSync]);

  const resolvePreviewTradeKeyFromViewport = useCallback((viewport: StrategyWorkbenchVisibleRange) => {
    if (previewTradeSyncItems.length <= 0) {
      return null;
    }
    const viewportFrom = Math.min(viewport.fromTime, viewport.toTime);
    const viewportTo = Math.max(viewport.fromTime, viewport.toTime);
    const centerTime = Number.isFinite(viewport.centerTime)
      ? viewport.centerTime
      : Math.floor((viewportFrom + viewportTo) / 2);

    let bestKey: string | null = null;
    let bestScore = Number.POSITIVE_INFINITY;
    for (const item of previewTradeSyncItems) {
      const intersects = item.endTime >= viewportFrom && item.startTime <= viewportTo;
      const distance = Math.abs(item.midpoint - centerTime);
      const score = distance + (intersects ? 0 : 10_000_000_000_000);
      if (score < bestScore) {
        bestScore = score;
        bestKey = item.key;
      }
    }
    return bestKey;
  }, [previewTradeSyncItems]);

  const handlePreviewListScroll = useCallback(() => {
    if (previewTradeMode !== 'full' || !previewScrollSyncEnabled || previewListSyncLockedRef.current) {
      return;
    }
    if (previewListScrollRafRef.current !== null) {
      window.cancelAnimationFrame(previewListScrollRafRef.current);
      previewListScrollRafRef.current = null;
    }
    previewListScrollRafRef.current = window.requestAnimationFrame(() => {
      previewListScrollRafRef.current = null;
      const targetKey = resolveTopVisiblePreviewTradeKey();
      if (!targetKey || targetKey === fullPreviewAnchorTradeKeyRef.current) {
        return;
      }
      setFullPreviewAnchorTradeKey(targetKey);
      setSelectedPreviewTradeKey(targetKey);
    });
  }, [previewScrollSyncEnabled, previewTradeMode, resolveTopVisiblePreviewTradeKey]);

  const handlePreviewKlineVisibleRangeChange = useCallback((viewport: StrategyWorkbenchVisibleRange) => {
    if (previewTradeMode !== 'full' || !previewScrollSyncEnabled) {
      return;
    }
    pendingViewportRef.current = viewport;
    if (previewViewportSyncRafRef.current !== null) {
      return;
    }
    previewViewportSyncRafRef.current = window.requestAnimationFrame(() => {
      previewViewportSyncRafRef.current = null;
      const snapshot = pendingViewportRef.current;
      pendingViewportRef.current = null;
      if (!snapshot) {
        return;
      }
      const targetKey = resolvePreviewTradeKeyFromViewport(snapshot);
      if (!targetKey || targetKey === fullPreviewAnchorTradeKeyRef.current) {
        return;
      }
      setFullPreviewAnchorTradeKey(targetKey);
      setSelectedPreviewTradeKey(targetKey);
      scrollPreviewListToTrade(targetKey);
    });
  }, [previewScrollSyncEnabled, previewTradeMode, resolvePreviewTradeKeyFromViewport, scrollPreviewListToTrade]);

  const handleChartTimeframeChange = useCallback((value: number) => {
    if (!Number.isFinite(value) || value <= 0) {
      return;
    }
    previewAutoTimeframeSwitchKeyRef.current = '';
    setChartTimeframeSec((prev) => (prev === value ? prev : value));
  }, []);

  const handlePreviewFocusRangeCoverage = useCallback((coverage: StrategyWorkbenchFocusRangeCoverage) => {
    if (previewTradeMode !== 'normal') {
      return;
    }
    if (coverage.timeframeSec !== chartTimeframeSec) {
      return;
    }
    const requiredBars = Math.max(0, Math.floor(Number(coverage.requiredBars)));
    const maxVisibleBars = Math.max(0, Math.floor(Number(coverage.visibleBarMax)));
    if (requiredBars <= 0 || maxVisibleBars <= 0) {
      return;
    }
    const maxCompatibleBars = Math.max(1, Math.floor(maxVisibleBars * FOCUS_RANGE_MAX_OCCUPANCY));
    if (requiredBars <= maxCompatibleBars) {
      return;
    }
    const currentIndex = chartTimeframeOptions.findIndex((item) => item.value === chartTimeframeSec);
    if (currentIndex < 0 || currentIndex >= chartTimeframeOptions.length - 1) {
      return;
    }
    const coverageKey = `${coverage.rangeId}|${chartTimeframeSec}|${requiredBars}|${maxVisibleBars}`;
    if (previewAutoTimeframeSwitchKeyRef.current === coverageKey) {
      return;
    }
    const startTime = Math.min(coverage.startTime, coverage.endTime);
    const endTime = Math.max(coverage.startTime, coverage.endTime);
    if (!Number.isFinite(startTime) || !Number.isFinite(endTime) || startTime <= 0 || endTime <= 0 || endTime <= startTime) {
      return;
    }

    const preferredMaxBars = Math.max(1, Math.floor(maxVisibleBars * FOCUS_RANGE_TARGET_OCCUPANCY));
    let preferredTimeframeSec: number | null = null;
    let compatibleTimeframeSec: number | null = null;
    let nextTimeframeSec: number | null = null;
    for (let index = currentIndex + 1; index < chartTimeframeOptions.length; index += 1) {
      const candidate = chartTimeframeOptions[index];
      if (!candidate) {
        continue;
      }
      const estimatedBars = estimateTradeSpanBarsByTimeframe(startTime, endTime, candidate.value);
      if (preferredTimeframeSec === null && estimatedBars <= preferredMaxBars) {
        preferredTimeframeSec = candidate.value;
        nextTimeframeSec = preferredTimeframeSec;
        break;
      }
      if (compatibleTimeframeSec === null && estimatedBars <= maxCompatibleBars) {
        compatibleTimeframeSec = candidate.value;
      }
    }
    if (nextTimeframeSec === null) {
      nextTimeframeSec = preferredTimeframeSec ?? compatibleTimeframeSec;
    }
    if (nextTimeframeSec === null) {
      nextTimeframeSec = chartTimeframeOptions[chartTimeframeOptions.length - 1]?.value ?? null;
    }
    if (!nextTimeframeSec || nextTimeframeSec === chartTimeframeSec) {
      return;
    }
    previewAutoTimeframeSwitchKeyRef.current = coverageKey;
    setChartTimeframeSec(nextTimeframeSec);
  }, [chartTimeframeOptions, chartTimeframeSec, previewTradeMode]);

  const handlePreviewTradeActivate = useCallback((trade: LocalBacktestTrade, index: number) => {
    if (previewTradeMode !== 'full') {
      focusPreviewTrade(trade, index);
      return;
    }
    const range = buildPreviewTradeFocusRange(trade, index, chartTimeframeSec, latestBacktestTimestamp);
    if (!range) {
      return;
    }
    setFullPreviewAnchorTradeKey(range.id);
    setSelectedPreviewTradeKey(range.id);
  }, [chartTimeframeSec, focusPreviewTrade, latestBacktestTimestamp, previewTradeMode]);

  const activatePreviewTradeByIndex = useCallback((tradeIndex: number) => {
    const normalizedIndex = Math.floor(Number(tradeIndex));
    if (!Number.isFinite(normalizedIndex) || normalizedIndex < 0 || normalizedIndex >= previewTrades.length) {
      return;
    }
    const trade = previewTrades[normalizedIndex];
    if (!trade) {
      return;
    }
    handlePreviewTradeActivate(trade, normalizedIndex);
    const tradeKey = buildPreviewTradeKey(trade, normalizedIndex);
    window.requestAnimationFrame(() => {
      scrollPreviewListToTrade(tradeKey);
    });
  }, [handlePreviewTradeActivate, previewTrades, scrollPreviewListToTrade]);

  const activatePreviewTradeByTimestamp = useCallback((timestamp: number) => {
    const targetTimestamp = Number(timestamp);
    if (!Number.isFinite(targetTimestamp) || targetTimestamp <= 0 || previewTrades.length <= 0) {
      return;
    }
    let bestTradeIndex = -1;
    let bestScore = Number.POSITIVE_INFINITY;
    for (let index = 0; index < previewTrades.length; index += 1) {
      const trade = previewTrades[index];
      const range = buildPreviewTradeFocusRange(trade, index, chartTimeframeSec, latestBacktestTimestamp);
      if (!range) {
        continue;
      }
      const startTime = Math.min(range.startTime, range.endTime);
      const endTime = Math.max(range.startTime, range.endTime);
      const midpoint = Math.floor((startTime + endTime) / 2);
      const intersects = targetTimestamp >= startTime && targetTimestamp <= endTime;
      const score = Math.abs(midpoint - targetTimestamp) + (intersects ? 0 : 1_000_000_000_000);
      if (score < bestScore) {
        bestScore = score;
        bestTradeIndex = index;
      }
    }
    if (bestTradeIndex >= 0) {
      activatePreviewTradeByIndex(bestTradeIndex);
    }
  }, [activatePreviewTradeByIndex, chartTimeframeSec, latestBacktestTimestamp, previewTrades]);

  const handleEquityChartClick = useCallback((payload: EChartClickPayload) => {
    const targetTimestamp = resolveTimestampFromEChartPayload(payload);
    if (targetTimestamp === null) {
      return;
    }
    activatePreviewTradeByTimestamp(targetTimestamp);
  }, [activatePreviewTradeByTimestamp]);

  const handleHoldingDurationScatterClick = useCallback((payload: EChartClickPayload) => {
    const value = Array.isArray(payload.value)
      ? payload.value
      : (
        payload.data
        && typeof payload.data === 'object'
        && !Array.isArray(payload.data)
        && Array.isArray((payload.data as { value?: unknown }).value)
      )
        ? (payload.data as { value: unknown[] }).value
        : null;
    const valueTradeIndex = value ? Number(value[5]) : Number.NaN;
    if (Number.isFinite(valueTradeIndex) && valueTradeIndex >= 0) {
      activatePreviewTradeByIndex(valueTradeIndex);
      return;
    }
    const targetTimestamp = value ? Number(value[2]) : Number.NaN;
    if (Number.isFinite(targetTimestamp) && targetTimestamp > 0) {
      activatePreviewTradeByTimestamp(targetTimestamp);
    }
  }, [activatePreviewTradeByIndex, activatePreviewTradeByTimestamp]);

  const closedPreviewTrades = useMemo(() => {
    return previewTrades.filter((trade) => !trade.isOpen);
  }, [previewTrades]);

  const averageClosedPnl = useMemo(() => {
    if (closedPreviewTrades.length === 0) {
      return 0;
    }
    return closedPreviewTrades.reduce((sum, trade) => sum + trade.pnl, 0) / closedPreviewTrades.length;
  }, [closedPreviewTrades]);

  const progressPercent = Math.max(0, Math.min(100, Math.round(backtestProgress.progress * 100)));
  const isBacktestRunning =
    localBacktestSummary.status === 'running'
    && backtestProgress.totalBars > 0
    && !backtestProgress.done;
  const detectedTimestamp = useMemo(() => {
    if (backtestProgress.processedBars <= 0 || backtestBars.length === 0) {
      return 0;
    }
    const index = Math.min(backtestProgress.processedBars - 1, backtestBars.length - 1);
    return Number(backtestBars[index]?.timestamp ?? 0);
  }, [backtestBars, backtestProgress.processedBars]);
  const detectedTimeText = detectedTimestamp > 0 ? formatDateTime(detectedTimestamp) : '-';
  useEffect(() => {
    const fallback = detectedTimestamp > 0 ? detectedTimestamp : latestBacktestTimestamp;
    if (fallback <= 0) {
      return;
    }
    setCalendarAnchorTimestamp((prev) => (prev > 0 ? prev : fallback));
  }, [detectedTimestamp, latestBacktestTimestamp]);

  useEffect(() => {
    setCalendarAnchorTimestamp(0);
  }, [selectedExchange, selectedSymbol, selectedTimeframeSec]);

  useEffect(() => {
    setChartTimeframeSec(selectedTimeframeSec);
    previewAutoTimeframeSwitchKeyRef.current = '';
  }, [selectedExchange, selectedSymbol, selectedTimeframeSec]);

  const loadedDataDays = useMemo(() => {
    if (backtestBars.length < 2) {
      return 0;
    }
    const first = Number(backtestBars[0]?.timestamp ?? 0);
    const last = Number(backtestBars[backtestBars.length - 1]?.timestamp ?? 0);
    if (!Number.isFinite(first) || !Number.isFinite(last) || last <= first) {
      return 0;
    }
    return (last - first) / DAY_MS;
  }, [backtestBars]);
  const activeRangeText = useMemo(() => {
    if (backtestBars.length <= 0) {
      return '暂无样本';
    }
    const first = Number(backtestBars[0]?.timestamp ?? 0);
    const last = Number(backtestBars[backtestBars.length - 1]?.timestamp ?? 0);
    if (!Number.isFinite(first) || !Number.isFinite(last) || first <= 0 || last <= 0) {
      return '暂无样本';
    }
    return `${formatDateTime(first)} ~ ${formatDateTime(last)}`;
  }, [backtestBars]);
  const availableRangeText = useMemo(() => {
    if (availableDataRange.start <= 0 || availableDataRange.end <= 0) {
      return '暂无本地K线样本';
    }
    return `${formatDateTime(availableDataRange.start)} ~ ${formatDateTime(availableDataRange.end)}`;
  }, [availableDataRange.end, availableDataRange.start]);
  const headerRunText = isBacktestRunning
    ? `运行中 ${progressPercent}%`
    : localBacktestSummary.status === 'running'
      ? '已完成'
      : localBacktestSummary.status === 'waiting_data'
        ? '等待数据'
        : '未开始';
  const dashboardProgressTotalBars = Math.max(backtestProgress.totalBars || localBacktestSummary.bars || backtestBars.length, 0);
  const dashboardProgressProcessedBars = Math.max(
    0,
    Math.min(backtestProgress.processedBars, dashboardProgressTotalBars || backtestProgress.processedBars),
  );
  const dashboardProgressText = `回测进度 ${dashboardProgressProcessedBars}/${dashboardProgressTotalBars} · ${progressPercent}%`;
  const livePositionCount = previewTrades.length;
  const liveAveragePnl = averageClosedPnl;
  const backtestStats = localBacktestSummary.stats;
  const tradeSummary = localBacktestSummary.tradeSummary;
  const equitySummary = localBacktestSummary.equitySummary;
  const eventSummary = localBacktestSummary.eventSummary;
  const recentEvents = useMemo(
    () => [...(localBacktestSummary.events || [])].slice(-6).reverse(),
    [localBacktestSummary.events],
  );
  const topEventTypesText = useMemo(() => {
    const entries = Object.entries(eventSummary.typeCounts || {});
    if (entries.length <= 0) {
      return '-';
    }
    return entries
      .sort((a, b) => b[1] - a[1])
      .slice(0, 3)
      .map(([key, count]) => `${key}:${count}`)
      .join(' / ');
  }, [eventSummary.typeCounts]);
  const equityCurvePoints = useMemo(() => {
    return sampleEquityPoints(localBacktestSummary.equityPreview || [], 1200);
  }, [localBacktestSummary.equityPreview]);
  const closedPreviewTradesByExit = useMemo(() => {
    return [...closedPreviewTrades].sort((a, b) => {
      const byExit = Number(a.exitTime) - Number(b.exitTime);
      if (byExit !== 0) {
        return byExit;
      }
      return Number(a.entryTime) - Number(b.entryTime);
    });
  }, [closedPreviewTrades]);
  const curveInsight = useMemo<BacktestCurveInsight>(() => {
    let currentWins = 0;
    let currentLosses = 0;
    let maxWin: TradeStreakLocation | null = null;
    let maxLoss: TradeStreakLocation | null = null;
    closedPreviewTradesByExit.forEach((trade, index) => {
      const pnl = Number(trade.pnl);
      const order = index + 1;
      if (pnl > 0) {
        currentWins += 1;
        currentLosses = 0;
        if (!maxWin || currentWins > maxWin.count) {
          maxWin = {
            count: currentWins,
            startOrder: order - currentWins + 1,
            endOrder: order,
            endTimestamp: Number(trade.exitTime) || Number(trade.entryTime) || 0,
          };
        }
        return;
      }
      if (pnl < 0) {
        currentLosses += 1;
        currentWins = 0;
        if (!maxLoss || currentLosses > maxLoss.count) {
          maxLoss = {
            count: currentLosses,
            startOrder: order - currentLosses + 1,
            endOrder: order,
            endTimestamp: Number(trade.exitTime) || Number(trade.entryTime) || 0,
          };
        }
        return;
      }
      currentWins = 0;
      currentLosses = 0;
    });

    const equityPoints = localBacktestSummary.equityPreview || [];
    let maxDrawdownPoint: LocalBacktestEquityPoint | null = null;
    for (let index = 0; index < equityPoints.length; index += 1) {
      const point = equityPoints[index];
      if (!maxDrawdownPoint || point.drawdown > maxDrawdownPoint.drawdown) {
        maxDrawdownPoint = point;
      }
    }
    if (maxDrawdownPoint && maxDrawdownPoint.drawdown <= 0) {
      maxDrawdownPoint = null;
    }

    return {
      maxWinStreak: maxWin,
      maxLossStreak: maxLoss,
      maxDrawdownPoint,
    };
  }, [closedPreviewTradesByExit, localBacktestSummary.equityPreview]);
  const maxWinStreakMarkerPoint = useMemo(() => {
    if (!curveInsight.maxWinStreak) {
      return null;
    }
    return findNearestEquityPoint(localBacktestSummary.equityPreview || [], curveInsight.maxWinStreak.endTimestamp);
  }, [curveInsight.maxWinStreak, localBacktestSummary.equityPreview]);
  const maxLossStreakMarkerPoint = useMemo(() => {
    if (!curveInsight.maxLossStreak) {
      return null;
    }
    return findNearestEquityPoint(localBacktestSummary.equityPreview || [], curveInsight.maxLossStreak.endTimestamp);
  }, [curveInsight.maxLossStreak, localBacktestSummary.equityPreview]);
  const equityCurveOption = useMemo<EChartsOption>(() => {
    const lineData = equityCurvePoints.map((point) => [point.timestamp, point.equity]);
    const series: any[] = [
      {
        name: '资金',
        type: 'line',
        smooth: false,
        showSymbol: false,
        symbol: 'circle',
        lineStyle: {
          width: 2,
          color: '#2563eb',
        },
        areaStyle: {
          color: 'rgba(37, 99, 235, 0.1)',
        },
        data: lineData,
      },
    ];
    if (curveInsight.maxDrawdownPoint) {
      series.push({
        name: '最大回撤',
        type: 'scatter',
        symbolSize: 10,
        itemStyle: { color: '#dc2626' },
        label: {
          show: true,
          position: 'top',
          color: '#991b1b',
          fontSize: 10,
          formatter: `最大回撤 ${formatPercentValue(curveInsight.maxDrawdownPoint.drawdown)}`,
        },
        data: [[curveInsight.maxDrawdownPoint.timestamp, curveInsight.maxDrawdownPoint.equity]],
      });
    }
    if (curveInsight.maxWinStreak && maxWinStreakMarkerPoint) {
      series.push({
        name: '最大连胜',
        type: 'scatter',
        symbolSize: 9,
        itemStyle: { color: '#16a34a' },
        label: {
          show: true,
          position: 'top',
          color: '#166534',
          fontSize: 10,
          formatter: `连胜 ${curveInsight.maxWinStreak.count}`,
        },
        data: [[maxWinStreakMarkerPoint.timestamp, maxWinStreakMarkerPoint.equity]],
      });
    }
    if (curveInsight.maxLossStreak && maxLossStreakMarkerPoint) {
      series.push({
        name: '最大连败',
        type: 'scatter',
        symbolSize: 9,
        itemStyle: { color: '#ea580c' },
        label: {
          show: true,
          position: 'top',
          color: '#c2410c',
          fontSize: 10,
          formatter: `连败 ${curveInsight.maxLossStreak.count}`,
        },
        data: [[maxLossStreakMarkerPoint.timestamp, maxLossStreakMarkerPoint.equity]],
      });
    }
    return {
      animation: false,
      grid: {
        left: 48,
        right: 14,
        top: 18,
        bottom: 30,
      },
      tooltip: {
        trigger: 'axis',
        axisPointer: { type: 'line' },
      },
      xAxis: {
        type: 'time',
        axisLine: { lineStyle: { color: '#dbe2ef' } },
        axisLabel: {
          color: '#64748b',
          formatter: (value: number) => formatDateTimeShort(Number(value)),
        },
        splitLine: { show: false },
      },
      yAxis: {
        type: 'value',
        scale: true,
        axisLine: { show: false },
        axisLabel: {
          color: '#64748b',
          formatter: (value: number) => formatNumberValue(Number(value), 2),
        },
        splitLine: {
          lineStyle: { color: '#edf2fb' },
        },
      },
      series,
      graphic: lineData.length > 0
        ? undefined
        : [
            {
              type: 'text',
              left: 'center',
              top: 'middle',
              style: {
                text: '暂无资金曲线数据',
                fill: '#94a3b8',
                fontSize: 12,
              },
            },
          ],
    };
  }, [
    curveInsight.maxDrawdownPoint,
    curveInsight.maxLossStreak,
    curveInsight.maxWinStreak,
    equityCurvePoints,
    maxLossStreakMarkerPoint,
    maxWinStreakMarkerPoint,
  ]);
  const winLossPieOption = useMemo<EChartsOption>(() => {
    const closedCount = Math.max(0, tradeSummary.winCount + tradeSummary.lossCount);
    const hasClosed = closedCount > 0;
    const pieData = hasClosed
      ? [
          { value: Math.max(0, tradeSummary.winCount), name: '盈利平仓', itemStyle: { color: '#16a34a' } },
          { value: Math.max(0, tradeSummary.lossCount), name: '亏损平仓', itemStyle: { color: '#dc2626' } },
        ]
      : [{ value: 1, name: '暂无平仓', itemStyle: { color: '#cbd5e1' } }];
    return {
      animation: false,
      tooltip: {
        trigger: 'item',
      },
      legend: {
        bottom: 0,
        left: 'center',
        textStyle: {
          color: '#64748b',
          fontSize: 11,
        },
      },
      series: [
        {
          type: 'pie',
          radius: ['48%', '76%'],
          center: ['50%', '40%'],
          avoidLabelOverlap: true,
          label: {
            show: true,
            formatter: hasClosed ? '{b}\n{d}%' : '{b}',
            color: '#334155',
            fontSize: 11,
          },
          labelLine: {
            length: 10,
            length2: 8,
          },
          data: pieData,
        },
      ],
    };
  }, [tradeSummary.lossCount, tradeSummary.winCount]);
  const maxWinStreakText = curveInsight.maxWinStreak
    ? `最大连胜 ${curveInsight.maxWinStreak.count} 次（第 ${curveInsight.maxWinStreak.startOrder}-${curveInsight.maxWinStreak.endOrder} 笔，${formatDateTime(curveInsight.maxWinStreak.endTimestamp)}）`
    : '最大连胜 暂无';
  const maxLossStreakText = curveInsight.maxLossStreak
    ? `最大连败 ${curveInsight.maxLossStreak.count} 次（第 ${curveInsight.maxLossStreak.startOrder}-${curveInsight.maxLossStreak.endOrder} 笔，${formatDateTime(curveInsight.maxLossStreak.endTimestamp)}）`
    : '最大连败 暂无';
  const maxDrawdownPositionText = curveInsight.maxDrawdownPoint
    ? `最大回撤位置 ${formatDateTime(curveInsight.maxDrawdownPoint.timestamp)}（${formatPercentValue(curveInsight.maxDrawdownPoint.drawdown)}）`
    : '最大回撤位置 暂无';
  const timeAnalysisReferenceModeLabel = timeAnalysisReferenceMode === 'entry' ? '开仓时间' : '平仓时间';
  const tradeTimeAnalysisPoints = useMemo<TradeTimeAnalysisPoint[]>(() => {
    const fallbackHoldingMs = Math.max(60_000, selectedTimeframeSec * 1000);
    return previewTrades
      .map((trade, tradeIndex) => {
        const referenceTimestamp = resolveTradeReferenceTimestamp(trade, timeAnalysisReferenceMode);
        if (!referenceTimestamp) {
          return null;
        }
        const pnl = Number.isFinite(trade.pnl) ? Number(trade.pnl) : 0;
        const returnPct = resolveTradeReturnPct(trade);
        return {
          tradeIndex,
          referenceTimestamp,
          holdingDurationMs: resolveTradeHoldingDurationMs(trade, fallbackHoldingMs),
          returnPct: Number.isFinite(returnPct) ? returnPct : 0,
          pnl,
          side: trade.side,
          isWin: pnl > 0,
        } satisfies TradeTimeAnalysisPoint;
      })
      .filter((item): item is TradeTimeAnalysisPoint => item !== null);
  }, [previewTrades, selectedTimeframeSec, timeAnalysisReferenceMode]);
  const holdingDurationBuckets = useMemo<HoldingDurationBucket[]>(() => {
    const buckets = HOLDING_DURATION_BUCKET_DEFS.map((definition) => ({
      ...definition,
      count: 0,
      wins: 0,
      pnl: 0,
      returnPctSum: 0,
      avgReturnPct: 0,
      winRate: 0,
    }));
    tradeTimeAnalysisPoints.forEach((point) => {
      const holdingMinutes = point.holdingDurationMs / 60_000;
      const bucket = buckets.find((item) => holdingMinutes >= item.minMinutes && holdingMinutes < item.maxMinutes);
      if (!bucket) {
        return;
      }
      bucket.count += 1;
      bucket.wins += point.isWin ? 1 : 0;
      bucket.pnl += point.pnl;
      bucket.returnPctSum += point.returnPct;
    });
    return buckets.map((bucket) => ({
      ...bucket,
      avgReturnPct: bucket.count > 0 ? bucket.returnPctSum / bucket.count : 0,
      winRate: bucket.count > 0 ? bucket.wins / bucket.count : 0,
    }));
  }, [tradeTimeAnalysisPoints]);
  const holdingDurationSpeedInsight = useMemo<{
    fastestProfitBucket: HoldingDurationBucket | null;
    fastestLossBucket: HoldingDurationBucket | null;
  }>(() => {
    const activeBuckets = holdingDurationBuckets.filter((bucket) => bucket.count > 0);
    const resolveBucketMidMinutes = (bucket: HoldingDurationBucket) => {
      if (!Number.isFinite(bucket.maxMinutes)) {
        return Math.max(bucket.minMinutes * 1.2, bucket.minMinutes + 60);
      }
      return (bucket.minMinutes + bucket.maxMinutes) / 2;
    };
    let fastestProfitBucket: HoldingDurationBucket | null = null;
    let fastestProfitScore = Number.NEGATIVE_INFINITY;
    let fastestLossBucket: HoldingDurationBucket | null = null;
    let fastestLossScore = Number.POSITIVE_INFINITY;
    activeBuckets.forEach((bucket) => {
      const speedScore = bucket.avgReturnPct / Math.max(resolveBucketMidMinutes(bucket), 1);
      if (bucket.avgReturnPct > 0 && speedScore > fastestProfitScore) {
        fastestProfitBucket = bucket;
        fastestProfitScore = speedScore;
      }
      if (bucket.avgReturnPct < 0 && speedScore < fastestLossScore) {
        fastestLossBucket = bucket;
        fastestLossScore = speedScore;
      }
    });
    return { fastestProfitBucket, fastestLossBucket };
  }, [holdingDurationBuckets]);
  const hourPeriodBuckets = useMemo<PeriodBucketMetrics[]>(() => {
    const buckets = Array.from({ length: 24 }, (_, hour) => ({
      key: `hour-${hour}`,
      label: `${`${hour}`.padStart(2, '0')}:00`,
      count: 0,
      wins: 0,
      losses: 0,
      pnl: 0,
      avgPnl: 0,
      returnPctSum: 0,
      avgReturnPct: 0,
      winRate: 0,
      lossRate: 0,
    }));
    tradeTimeAnalysisPoints.forEach((point) => {
      const date = new Date(point.referenceTimestamp);
      const bucket = buckets[date.getHours()];
      if (!bucket) {
        return;
      }
      bucket.count += 1;
      bucket.wins += point.isWin ? 1 : 0;
      bucket.losses += point.pnl < 0 ? 1 : 0;
      bucket.pnl += point.pnl;
      bucket.returnPctSum += point.returnPct;
    });
    return buckets.map((bucket) => ({
      ...bucket,
      avgPnl: bucket.count > 0 ? bucket.pnl / bucket.count : 0,
      avgReturnPct: bucket.count > 0 ? bucket.returnPctSum / bucket.count : 0,
      winRate: bucket.count > 0 ? bucket.wins / bucket.count : 0,
      lossRate: bucket.count > 0 ? bucket.losses / bucket.count : 0,
    }));
  }, [tradeTimeAnalysisPoints]);
  const dayPeriodBuckets = useMemo<PeriodBucketMetrics[]>(() => {
    const buckets = Array.from({ length: 7 }, (_, weekdayIndex) => ({
      key: `day-${weekdayIndex}`,
      label: WEEKDAY_LABELS[weekdayIndex] || `Day ${weekdayIndex + 1}`,
      count: 0,
      wins: 0,
      losses: 0,
      pnl: 0,
      avgPnl: 0,
      returnPctSum: 0,
      avgReturnPct: 0,
      winRate: 0,
      lossRate: 0,
    }));
    tradeTimeAnalysisPoints.forEach((point) => {
      const date = new Date(point.referenceTimestamp);
      const weekdayIndex = (date.getDay() + 6) % 7;
      const bucket = buckets[weekdayIndex];
      if (!bucket) {
        return;
      }
      bucket.count += 1;
      bucket.wins += point.isWin ? 1 : 0;
      bucket.losses += point.pnl < 0 ? 1 : 0;
      bucket.pnl += point.pnl;
      bucket.returnPctSum += point.returnPct;
    });
    return buckets.map((bucket) => ({
      ...bucket,
      avgPnl: bucket.count > 0 ? bucket.pnl / bucket.count : 0,
      avgReturnPct: bucket.count > 0 ? bucket.returnPctSum / bucket.count : 0,
      winRate: bucket.count > 0 ? bucket.wins / bucket.count : 0,
      lossRate: bucket.count > 0 ? bucket.losses / bucket.count : 0,
    }));
  }, [tradeTimeAnalysisPoints]);
  const selectedPeriodBuckets = useMemo(
    () => (timeAnalysisGranularity === 'hour' ? hourPeriodBuckets : dayPeriodBuckets),
    [dayPeriodBuckets, hourPeriodBuckets, timeAnalysisGranularity],
  );
  const selectedPeriodInsightText = useMemo(() => {
    const activeBuckets = selectedPeriodBuckets.filter((bucket) => bucket.count > 0);
    if (activeBuckets.length <= 0) {
      return `当前按${timeAnalysisReferenceModeLabel}统计暂无时段样本`;
    }
    const bestWinBucket = activeBuckets.reduce<PeriodBucketMetrics | null>((best, bucket) => {
      if (!best) {
        return bucket;
      }
      if (bucket.winRate > best.winRate) {
        return bucket;
      }
      if (bucket.winRate === best.winRate && bucket.count > best.count) {
        return bucket;
      }
      return best;
    }, null);
    const worstReturnBucket = activeBuckets.reduce<PeriodBucketMetrics | null>((worst, bucket) => {
      if (!worst || bucket.avgReturnPct < worst.avgReturnPct) {
        return bucket;
      }
      return worst;
    }, null);
    const granularityLabel = timeAnalysisGranularity === 'hour' ? '小时级' : '日级';
    if (!bestWinBucket || !worstReturnBucket) {
      return `${granularityLabel}暂无足够样本用于总结`;
    }
    return `${granularityLabel}高胜率时段：${bestWinBucket.label}（${formatPercentValue(bestWinBucket.winRate, 1)}，${bestWinBucket.count} 笔） · 明显亏损时段：${worstReturnBucket.label}（均值 ${formatSignedNumber(worstReturnBucket.avgReturnPct, 2)}%）`;
  }, [selectedPeriodBuckets, timeAnalysisGranularity, timeAnalysisReferenceModeLabel]);
  const holdingDurationScatterOption = useMemo<EChartsOption>(() => {
    const points = tradeTimeAnalysisPoints.map((point) => ({
      value: [
        Number((point.holdingDurationMs / 60_000).toFixed(4)),
        Number(point.returnPct.toFixed(4)),
        point.referenceTimestamp,
        Number(point.pnl.toFixed(6)),
        point.side,
        point.tradeIndex,
      ],
      itemStyle: {
        color: point.returnPct >= 0 ? '#16a34a' : '#dc2626',
        opacity: 0.75,
      },
    }));
    const formatDurationMinutes = (value: number) => {
      if (!Number.isFinite(value) || value <= 0) {
        return '0m';
      }
      if (value >= 1_440) {
        return `${(value / 1_440).toFixed(value >= 10_080 ? 0 : 1)}d`;
      }
      if (value >= 60) {
        return `${(value / 60).toFixed(value >= 600 ? 0 : 1)}h`;
      }
      return `${Math.round(value)}m`;
    };
    return {
      animation: false,
      grid: {
        left: 54,
        right: 16,
        top: 24,
        bottom: 36,
      },
      tooltip: {
        trigger: 'item',
        formatter: (params: any) => {
          const values = Array.isArray(params.value) ? params.value : [];
          const holdingMinutes = Number(values[0]);
          const returnPct = Number(values[1]);
          const timestamp = Number(values[2]);
          const pnl = Number(values[3]);
          const side = `${values[4] ?? '-'}`;
          return [
            `${side} · ${timeAnalysisReferenceModeLabel} ${formatDateTime(timestamp)}`,
            `持仓时长: ${formatDuration(Math.max(0, holdingMinutes) * 60_000)}`,
            `收益率: ${formatSignedNumber(returnPct, 2)}%`,
            `盈亏: ${formatSignedNumber(pnl)}`,
          ].join('<br/>');
        },
      },
      xAxis: {
        type: 'value',
        name: '持仓时长',
        nameTextStyle: {
          color: '#64748b',
          fontSize: 11,
          padding: [8, 0, 0, 0],
        },
        axisLine: { lineStyle: { color: '#dbe2ef' } },
        axisLabel: {
          color: '#64748b',
          formatter: (value: number) => formatDurationMinutes(Number(value)),
        },
        splitLine: {
          lineStyle: { color: '#edf2fb' },
        },
      },
      yAxis: {
        type: 'value',
        name: '收益率(%)',
        nameTextStyle: {
          color: '#64748b',
          fontSize: 11,
        },
        axisLine: { show: false },
        axisLabel: {
          color: '#64748b',
          formatter: (value: number) => `${Number(value).toFixed(2)}%`,
        },
        splitLine: {
          lineStyle: { color: '#edf2fb' },
        },
      },
      series: [
        {
          type: 'scatter',
          data: points,
          symbolSize: (value: unknown) => {
            if (!Array.isArray(value)) {
              return 8;
            }
            const absReturnPct = Math.abs(Number(value[1]) || 0);
            return Math.max(7, Math.min(16, 7 + absReturnPct * 0.5));
          },
          markLine: {
            silent: true,
            symbol: 'none',
            lineStyle: {
              color: 'rgba(71, 85, 105, 0.35)',
              type: 'dashed',
            },
            data: [{ yAxis: 0 }],
          },
        },
      ],
      graphic: points.length > 0
        ? undefined
        : [
            {
              type: 'text',
              left: 'center',
              top: 'middle',
              style: {
                text: `暂无${timeAnalysisReferenceModeLabel}统计点`,
                fill: '#94a3b8',
                fontSize: 12,
              },
            },
          ],
    };
  }, [timeAnalysisReferenceModeLabel, tradeTimeAnalysisPoints]);
  const periodDistributionOption = useMemo<EChartsOption>(() => {
    const labels = selectedPeriodBuckets.map((bucket) => bucket.label);
    return {
      animation: false,
      legend: {
        top: 0,
        right: 0,
        textStyle: {
          color: '#64748b',
          fontSize: 11,
        },
      },
      grid: {
        left: 54,
        right: 46,
        top: 30,
        bottom: 36,
      },
      tooltip: {
        trigger: 'axis',
        axisPointer: { type: 'shadow' },
        formatter: (params: any) => {
          const rows = Array.isArray(params) ? params : [params];
          const dataIndex = Number(rows?.[0]?.dataIndex ?? -1);
          const bucket = dataIndex >= 0 ? selectedPeriodBuckets[dataIndex] : null;
          if (!bucket) {
            return '';
          }
          return [
            `${bucket.label}（${timeAnalysisReferenceModeLabel}）`,
            `样本笔数: ${bucket.count}（胜 ${bucket.wins} / 负 ${bucket.losses}）`,
            `胜率: ${formatPercentValue(bucket.winRate, 1)}`,
            `平均收益率: ${formatSignedNumber(bucket.avgReturnPct, 2)}%`,
            `总盈亏: ${formatSignedNumber(bucket.pnl)}`,
          ].join('<br/>');
        },
      },
      xAxis: {
        type: 'category',
        data: labels,
        axisLabel: {
          color: '#64748b',
          interval: 0,
        },
        axisLine: {
          lineStyle: { color: '#dbe2ef' },
        },
      },
      yAxis: [
        {
          type: 'value',
          name: '平均收益率(%)',
          axisLine: { show: false },
          axisLabel: {
            color: '#64748b',
            formatter: (value: number) => `${Number(value).toFixed(1)}%`,
          },
          splitLine: {
            lineStyle: { color: '#edf2fb' },
          },
        },
        {
          type: 'value',
          name: '胜率(%)',
          min: 0,
          max: 100,
          axisLine: { show: false },
          axisLabel: {
            color: '#64748b',
            formatter: (value: number) => `${Number(value).toFixed(0)}%`,
          },
          splitLine: { show: false },
        },
      ],
      series: [
        {
          name: '平均收益率',
          type: 'bar',
          data: selectedPeriodBuckets.map((bucket) => bucket.avgReturnPct),
          itemStyle: {
            color: (params: any) => (Number(params?.value) >= 0 ? '#16a34a' : '#dc2626'),
            opacity: 0.86,
          },
          barMaxWidth: 24,
        },
        {
          name: '胜率',
          type: 'line',
          yAxisIndex: 1,
          smooth: true,
          showSymbol: false,
          lineStyle: {
            width: 2,
            color: '#2563eb',
          },
          areaStyle: {
            color: 'rgba(37, 99, 235, 0.12)',
          },
          data: selectedPeriodBuckets.map((bucket) => Number((bucket.winRate * 100).toFixed(3))),
        },
      ],
      graphic: selectedPeriodBuckets.some((bucket) => bucket.count > 0)
        ? undefined
        : [
            {
              type: 'text',
              left: 'center',
              top: 'middle',
              style: {
                text: `暂无${timeAnalysisReferenceModeLabel}时段分布`,
                fill: '#94a3b8',
                fontSize: 12,
              },
            },
          ],
    };
  }, [selectedPeriodBuckets, timeAnalysisReferenceModeLabel]);
  const fastestProfitBucketText = holdingDurationSpeedInsight.fastestProfitBucket
    ? `${holdingDurationSpeedInsight.fastestProfitBucket.label}（均值 ${formatSignedNumber(holdingDurationSpeedInsight.fastestProfitBucket.avgReturnPct, 2)}%，${holdingDurationSpeedInsight.fastestProfitBucket.count} 笔）`
    : '暂无明显快速盈利持仓段';
  const fastestLossBucketText = holdingDurationSpeedInsight.fastestLossBucket
    ? `${holdingDurationSpeedInsight.fastestLossBucket.label}（均值 ${formatSignedNumber(holdingDurationSpeedInsight.fastestLossBucket.avgReturnPct, 2)}%，${holdingDurationSpeedInsight.fastestLossBucket.count} 笔）`
    : '暂无明显快速亏损持仓段';
  const calendarBucketMaps = useMemo(() => {
    const dayMap = new Map<string, CalendarBucketMetrics>();
    const weekMap = new Map<string, CalendarBucketMetrics>();
    const monthMap = new Map<string, CalendarBucketMetrics>();
    const hourMap = new Map<string, CalendarBucketMetrics>();
    const mergeToMap = (
      map: Map<string, CalendarBucketMetrics>,
      key: string,
      pnl: number,
      win: number,
      tradeIndex: number,
      timestamp: number,
    ) => {
      const prev = map.get(key) || buildEmptyCalendarMetrics();
      map.set(key, {
        pnl: prev.pnl + pnl,
        count: prev.count + 1,
        wins: prev.wins + win,
        representativeTradeIndex:
          timestamp >= prev.representativeTimestamp
            ? tradeIndex
            : prev.representativeTradeIndex,
        representativeTimestamp: Math.max(prev.representativeTimestamp, timestamp),
      });
    };
    previewTrades.forEach((trade, tradeIndex) => {
      const entryTimestamp = Number(trade.entryTime);
      if (!Number.isFinite(entryTimestamp) || entryTimestamp <= 0) {
        return;
      }
      const entryDate = new Date(entryTimestamp);
      const dayKey = toCalendarDateKey(entryDate);
      const monthKey = toCalendarMonthKey(entryDate);
      const weekKey = toCalendarDateKey(startOfLocalWeek(entryDate));
      const hourKey = `${dayKey}-${`${entryDate.getHours()}`.padStart(2, '0')}`;
      const pnl = Number.isFinite(trade.pnl) ? trade.pnl : 0;
      const win = pnl > 0 ? 1 : 0;
      mergeToMap(dayMap, dayKey, pnl, win, tradeIndex, entryTimestamp);
      mergeToMap(weekMap, weekKey, pnl, win, tradeIndex, entryTimestamp);
      mergeToMap(monthMap, monthKey, pnl, win, tradeIndex, entryTimestamp);
      mergeToMap(hourMap, hourKey, pnl, win, tradeIndex, entryTimestamp);
    });
    return { dayMap, weekMap, monthMap, hourMap };
  }, [previewTrades]);
  const calendarAnchorDate = useMemo(() => {
    const fallbackTimestamp =
      calendarAnchorTimestamp > 0
        ? calendarAnchorTimestamp
        : detectedTimestamp > 0
          ? detectedTimestamp
          : latestBacktestTimestamp > 0
            ? latestBacktestTimestamp
            : Date.now();
    return new Date(fallbackTimestamp);
  }, [calendarAnchorTimestamp, detectedTimestamp, latestBacktestTimestamp]);
  const handleCalendarShift = useCallback((direction: -1 | 1) => {
    setCalendarAnchorTimestamp((prev) => {
      const baseTimestamp =
        prev > 0
          ? prev
          : detectedTimestamp > 0
            ? detectedTimestamp
            : latestBacktestTimestamp > 0
              ? latestBacktestTimestamp
              : Date.now();
      const baseDate = new Date(baseTimestamp);
      const shiftedDate =
        calendarViewMode === 'month'
          ? addMonths(baseDate, direction)
          : calendarViewMode === 'week'
            ? addDays(baseDate, direction * 7)
            : addDays(baseDate, direction);
      return shiftedDate.getTime();
    });
  }, [calendarViewMode, detectedTimestamp, latestBacktestTimestamp]);
  const handleCalendarBackToLatest = useCallback(() => {
    const fallback = detectedTimestamp > 0 ? detectedTimestamp : latestBacktestTimestamp;
    if (fallback > 0) {
      setCalendarAnchorTimestamp(fallback);
    }
  }, [detectedTimestamp, latestBacktestTimestamp]);
  const calendarViewData = useMemo(() => {
    type CalendarCell = {
      id: string;
      label: string;
      subLabel?: string;
      inCurrentPeriod: boolean;
      metrics: CalendarBucketMetrics;
    };
    const anchor = calendarAnchorDate;
    if (calendarViewMode === 'day') {
      const dayStart = startOfLocalDay(anchor.getTime());
      const dayKey = toCalendarDateKey(dayStart);
      const cells: CalendarCell[] = Array.from({ length: 24 }, (_, hour) => {
        const hourText = `${hour}`.padStart(2, '0');
        const key = `${dayKey}-${hourText}`;
        return {
          id: key,
          label: `${hourText}:00`,
          subLabel: hour === 0 ? `${dayStart.getMonth() + 1}/${dayStart.getDate()}` : undefined,
          inCurrentPeriod: true,
          metrics: calendarBucketMaps.hourMap.get(key) || buildEmptyCalendarMetrics(),
        };
      });
      const periodMetrics = cells.reduce(
        (acc, cell) => mergeCalendarMetrics(acc, cell.metrics),
        buildEmptyCalendarMetrics(),
      );
      const maxAbsPnl = cells.reduce((acc, cell) => Math.max(acc, Math.abs(cell.metrics.pnl)), 0);
      return {
        cells,
        periodLabel: formatCalendarPeriodLabel('day', anchor),
        periodMetrics,
        maxAbsPnl,
      };
    }
    if (calendarViewMode === 'week') {
      const weekStart = startOfLocalWeek(anchor);
      const cells: CalendarCell[] = Array.from({ length: 7 }, (_, index) => {
        const cellDate = addDays(weekStart, index);
        const key = toCalendarDateKey(cellDate);
        return {
          id: key,
          label: `周${CALENDAR_DAY_LABELS[index]}`,
          subLabel: `${cellDate.getMonth() + 1}/${cellDate.getDate()}`,
          inCurrentPeriod: true,
          metrics: calendarBucketMaps.dayMap.get(key) || buildEmptyCalendarMetrics(),
        };
      });
      const periodMetrics = cells.reduce(
        (acc, cell) => mergeCalendarMetrics(acc, cell.metrics),
        buildEmptyCalendarMetrics(),
      );
      const maxAbsPnl = cells.reduce((acc, cell) => Math.max(acc, Math.abs(cell.metrics.pnl)), 0);
      return {
        cells,
        periodLabel: formatCalendarPeriodLabel('week', anchor),
        periodMetrics,
        maxAbsPnl,
      };
    }
    const monthStart = startOfLocalMonth(anchor);
    const monthKey = toCalendarMonthKey(monthStart);
    const monthWeekStart = startOfLocalWeek(monthStart);
    const cells: CalendarCell[] = Array.from({ length: 42 }, (_, index) => {
      const cellDate = addDays(monthWeekStart, index);
      const key = toCalendarDateKey(cellDate);
      return {
        id: key,
        label: `${cellDate.getDate()}`,
        inCurrentPeriod: cellDate.getMonth() === monthStart.getMonth() && cellDate.getFullYear() === monthStart.getFullYear(),
        metrics: calendarBucketMaps.dayMap.get(key) || buildEmptyCalendarMetrics(),
      };
    });
    const periodMetrics = calendarBucketMaps.monthMap.get(monthKey) || buildEmptyCalendarMetrics();
    const maxAbsPnl = cells.reduce(
      (acc, cell) => (cell.inCurrentPeriod ? Math.max(acc, Math.abs(cell.metrics.pnl)) : acc),
      0,
    );
    return {
      cells,
      periodLabel: formatCalendarPeriodLabel('month', anchor),
      periodMetrics,
      maxAbsPnl,
    };
  }, [calendarAnchorDate, calendarBucketMaps.dayMap, calendarBucketMaps.hourMap, calendarBucketMaps.monthMap, calendarViewMode]);
  const getCalendarCellBackground = useCallback((pnl: number, maxAbsPnl: number) => {
    if (!Number.isFinite(pnl) || Math.abs(pnl) <= 0 || maxAbsPnl <= 0) {
      return undefined;
    }
    const intensity = Math.min(1, Math.abs(pnl) / maxAbsPnl);
    const alpha = 0.06 + intensity * 0.28;
    if (pnl > 0) {
      return `rgba(22, 163, 74, ${alpha.toFixed(3)})`;
    }
    return `rgba(220, 38, 38, ${alpha.toFixed(3)})`;
  }, []);
  const visibleDiagnostics = useMemo(
    () => (Array.isArray(localBacktestSummary.diagnostics) ? localBacktestSummary.diagnostics : []).slice(0, 8),
    [localBacktestSummary.diagnostics],
  );
  const professionalFallbackHoldingMs = Math.max(60_000, selectedTimeframeSec * 1000);
  const closedCostSummary = useMemo(() => {
    return closedPreviewTradesByExit.reduce(
      (acc, trade) => {
        const totalFee = resolveTradeFee(trade);
        const totalFunding = resolveTradeFunding(trade);
        const netPnl = resolveTradeNetPnl(trade);
        acc.netPnl += netPnl;
        acc.grossBeforeCosts += netPnl + totalFee + totalFunding;
        acc.totalFee += totalFee;
        acc.totalFunding += totalFunding;
        return acc;
      },
      {
        netPnl: 0,
        grossBeforeCosts: 0,
        totalFee: 0,
        totalFunding: 0,
      },
    );
  }, [closedPreviewTradesByExit]);
  const sideBreakdownRows = useMemo<SideBreakdownRow[]>(() => {
    const totalCount = closedPreviewTradesByExit.length;
    const rows = new Map<'Long' | 'Short', Omit<SideBreakdownRow, 'share' | 'winRate' | 'avgNetPnl' | 'avgHoldingMs'>>([
      [
        'Long',
        {
          key: 'Long',
          label: '多头',
          count: 0,
          wins: 0,
          losses: 0,
          netPnl: 0,
          grossBeforeCosts: 0,
          totalFee: 0,
          totalFunding: 0,
        },
      ],
      [
        'Short',
        {
          key: 'Short',
          label: '空头',
          count: 0,
          wins: 0,
          losses: 0,
          netPnl: 0,
          grossBeforeCosts: 0,
          totalFee: 0,
          totalFunding: 0,
        },
      ],
    ]);
    const totalHoldingMs = new Map<'Long' | 'Short', number>([
      ['Long', 0],
      ['Short', 0],
    ]);

    closedPreviewTradesByExit.forEach((trade) => {
      const side = trade.side === 'Short' ? 'Short' : 'Long';
      const row = rows.get(side);
      if (!row) {
        return;
      }
      const totalFee = resolveTradeFee(trade);
      const totalFunding = resolveTradeFunding(trade);
      const netPnl = resolveTradeNetPnl(trade);
      row.count += 1;
      row.wins += netPnl > 0 ? 1 : 0;
      row.losses += netPnl < 0 ? 1 : 0;
      row.netPnl += netPnl;
      row.grossBeforeCosts += netPnl + totalFee + totalFunding;
      row.totalFee += totalFee;
      row.totalFunding += totalFunding;
      totalHoldingMs.set(side, (totalHoldingMs.get(side) || 0) + resolveTradeHoldingDurationMs(trade, professionalFallbackHoldingMs));
    });

    return (['Long', 'Short'] as const).map((side) => {
      const row = rows.get(side)!;
      return {
        ...row,
        share: totalCount > 0 ? row.count / totalCount : 0,
        winRate: row.count > 0 ? row.wins / row.count : 0,
        avgNetPnl: row.count > 0 ? row.netPnl / row.count : 0,
        avgHoldingMs: row.count > 0 ? (totalHoldingMs.get(side) || 0) / row.count : 0,
      };
    });
  }, [closedPreviewTradesByExit, professionalFallbackHoldingMs]);
  const exitReasonRows = useMemo<ExitReasonBreakdownRow[]>(() => {
    const totalCount = closedPreviewTradesByExit.length;
    const rows = new Map<string, ExitReasonBreakdownRow & { totalHoldingMs: number }>();

    closedPreviewTradesByExit.forEach((trade) => {
      const key = normalizeText(trade.exitReason) || 'Unknown';
      const current = rows.get(key) || {
        key,
        label: formatExitReasonLabel(key),
        count: 0,
        share: 0,
        wins: 0,
        losses: 0,
        winRate: 0,
        netPnl: 0,
        avgNetPnl: 0,
        totalFee: 0,
        totalFunding: 0,
        avgHoldingMs: 0,
        totalHoldingMs: 0,
      };
      const totalFee = resolveTradeFee(trade);
      const totalFunding = resolveTradeFunding(trade);
      const netPnl = resolveTradeNetPnl(trade);
      current.count += 1;
      current.wins += netPnl > 0 ? 1 : 0;
      current.losses += netPnl < 0 ? 1 : 0;
      current.netPnl += netPnl;
      current.totalFee += totalFee;
      current.totalFunding += totalFunding;
      current.totalHoldingMs += resolveTradeHoldingDurationMs(trade, professionalFallbackHoldingMs);
      rows.set(key, current);
    });

    return Array.from(rows.values())
      .map((row) => ({
        key: row.key,
        label: row.label,
        count: row.count,
        share: totalCount > 0 ? row.count / totalCount : 0,
        wins: row.wins,
        losses: row.losses,
        winRate: row.count > 0 ? row.wins / row.count : 0,
        netPnl: row.netPnl,
        avgNetPnl: row.count > 0 ? row.netPnl / row.count : 0,
        totalFee: row.totalFee,
        totalFunding: row.totalFunding,
        avgHoldingMs: row.count > 0 ? row.totalHoldingMs / row.count : 0,
      }))
      .sort((a, b) => {
        if (b.count !== a.count) {
          return b.count - a.count;
        }
        return Math.abs(b.netPnl) - Math.abs(a.netPnl);
      });
  }, [closedPreviewTradesByExit, professionalFallbackHoldingMs]);
  const monthlyPerformance = useMemo(() => {
    const bucketMap = new Map<string, MonthlyPerformanceCell>();
    let runningEquity = Math.max(0, appliedBacktestParams.initialCapital);

    closedPreviewTradesByExit.forEach((trade) => {
      const exitTimestamp = Number(trade.exitTime) || Number(trade.entryTime);
      if (!Number.isFinite(exitTimestamp) || exitTimestamp <= 0) {
        return;
      }
      const meta = toMonthlyBucketMeta(exitTimestamp);
      let cell = bucketMap.get(meta.key);
      if (!cell) {
        cell = {
          key: meta.key,
          label: meta.label,
          year: meta.year,
          monthIndex: meta.monthIndex,
          count: 0,
          wins: 0,
          losses: 0,
          winRate: 0,
          netPnl: 0,
          totalFee: 0,
          totalFunding: 0,
          grossBeforeCosts: 0,
          startEquity: runningEquity,
          endEquity: runningEquity,
          returnPct: 0,
        };
        bucketMap.set(meta.key, cell);
      }

      const totalFee = resolveTradeFee(trade);
      const totalFunding = resolveTradeFunding(trade);
      const netPnl = resolveTradeNetPnl(trade);
      cell.count += 1;
      cell.wins += netPnl > 0 ? 1 : 0;
      cell.losses += netPnl < 0 ? 1 : 0;
      cell.netPnl += netPnl;
      cell.totalFee += totalFee;
      cell.totalFunding += totalFunding;
      cell.grossBeforeCosts += netPnl + totalFee + totalFunding;
      runningEquity += netPnl;
      cell.endEquity = runningEquity;
    });

    const activeCells = Array.from(bucketMap.values())
      .map((cell) => ({
        ...cell,
        winRate: cell.count > 0 ? cell.wins / cell.count : 0,
        returnPct: cell.startEquity > 0 ? cell.netPnl / cell.startEquity : 0,
      }))
      .sort((a, b) => {
        if (a.year !== b.year) {
          return a.year - b.year;
        }
        return a.monthIndex - b.monthIndex;
      });

    const years = Array.from(new Set(activeCells.map((cell) => cell.year))).sort((a, b) => b - a);
    const activeMap = new Map(activeCells.map((cell) => [cell.key, cell]));
    const cells: MonthlyPerformanceCell[] = [];
    years.forEach((year) => {
      for (let monthIndex = 0; monthIndex < 12; monthIndex += 1) {
        const key = `${year}-${`${monthIndex + 1}`.padStart(2, '0')}`;
        cells.push(
          activeMap.get(key) || {
            key,
            label: key,
            year,
            monthIndex,
            count: 0,
            wins: 0,
            losses: 0,
            winRate: 0,
            netPnl: 0,
            totalFee: 0,
            totalFunding: 0,
            grossBeforeCosts: 0,
            startEquity: 0,
            endEquity: 0,
            returnPct: 0,
          },
        );
      }
    });

    const bestCell = activeCells.reduce<MonthlyPerformanceCell | null>((best, cell) => {
      if (!best || cell.returnPct > best.returnPct) {
        return cell;
      }
      return best;
    }, null);
    const worstCell = activeCells.reduce<MonthlyPerformanceCell | null>((worst, cell) => {
      if (!worst || cell.returnPct < worst.returnPct) {
        return cell;
      }
      return worst;
    }, null);

    return {
      cells,
      activeCells,
      years,
      maxAbsReturnPct: activeCells.reduce((acc, cell) => Math.max(acc, Math.abs(cell.returnPct * 100)), 0),
      bestCell,
      worstCell,
    };
  }, [appliedBacktestParams.initialCapital, closedPreviewTradesByExit]);
  const rollingWindowSize = useMemo(() => {
    const count = closedPreviewTradesByExit.length;
    if (count <= 0) {
      return 0;
    }
    return Math.min(count, Math.min(24, Math.max(6, Math.round(count / 4) || 0)));
  }, [closedPreviewTradesByExit.length]);
  const rollingQualityPoints = useMemo<RollingQualityPoint[]>(() => {
    if (rollingWindowSize <= 0 || closedPreviewTradesByExit.length <= 0) {
      return [];
    }

    const prefixNetPnl = new Array<number>(closedPreviewTradesByExit.length + 1).fill(0);
    const prefixWins = new Array<number>(closedPreviewTradesByExit.length + 1).fill(0);
    const prefixGrossProfit = new Array<number>(closedPreviewTradesByExit.length + 1).fill(0);
    const prefixGrossLoss = new Array<number>(closedPreviewTradesByExit.length + 1).fill(0);

    closedPreviewTradesByExit.forEach((trade, index) => {
      const netPnl = resolveTradeNetPnl(trade);
      prefixNetPnl[index + 1] = prefixNetPnl[index] + netPnl;
      prefixWins[index + 1] = prefixWins[index] + (netPnl > 0 ? 1 : 0);
      prefixGrossProfit[index + 1] = prefixGrossProfit[index] + (netPnl > 0 ? netPnl : 0);
      prefixGrossLoss[index + 1] = prefixGrossLoss[index] + (netPnl < 0 ? Math.abs(netPnl) : 0);
    });

    const points: RollingQualityPoint[] = [];
    for (let index = 0; index < closedPreviewTradesByExit.length; index += 1) {
      const windowStartIndex = Math.max(0, index - rollingWindowSize + 1);
      const windowCount = index - windowStartIndex + 1;
      const netPnl = prefixNetPnl[index + 1] - prefixNetPnl[windowStartIndex];
      const winCount = prefixWins[index + 1] - prefixWins[windowStartIndex];
      const grossProfit = prefixGrossProfit[index + 1] - prefixGrossProfit[windowStartIndex];
      const grossLoss = prefixGrossLoss[index + 1] - prefixGrossLoss[windowStartIndex];
      points.push({
        key: `${closedPreviewTradesByExit[index].exitTime}-${index}`,
        timestamp: Number(closedPreviewTradesByExit[index].exitTime) || Number(closedPreviewTradesByExit[index].entryTime) || 0,
        tradeIndex: index,
        windowStartIndex,
        windowCount,
        netPnl,
        winRate: windowCount > 0 ? winCount / windowCount : 0,
        avgNetPnl: windowCount > 0 ? netPnl / windowCount : 0,
        profitFactor: grossLoss > 0 ? grossProfit / grossLoss : (grossProfit > 0 ? Number.POSITIVE_INFINITY : 0),
      });
    }
    return points;
  }, [closedPreviewTradesByExit, rollingWindowSize]);
  const drawdownEpisodes = useMemo<DrawdownEpisode[]>(() => {
    return buildDrawdownEpisodes(localBacktestSummary.equityPreview || []).slice(0, 6);
  }, [localBacktestSummary.equityPreview]);
  const professionalCoverageNote = useMemo(() => {
    const previewPointCount = localBacktestSummary.equityPreview?.length || 0;
    if (equitySummary.pointCount > previewPointCount) {
      return `回撤区间基于权益预览 ${previewPointCount}/${equitySummary.pointCount} 点，超长样本下主要用于观察结构，不建议视作完整逐点复盘。`;
    }
    return `回撤区间基于当前本地权益序列 ${previewPointCount} 点；成本、方向、平仓原因和月度收益均基于 ${closedPreviewTradesByExit.length} 笔已平仓交易。`;
  }, [closedPreviewTradesByExit.length, equitySummary.pointCount, localBacktestSummary.equityPreview]);
  const professionalMetricCards = useMemo(() => {
    const longRow = sideBreakdownRows.find((row) => row.key === 'Long') || null;
    const shortRow = sideBreakdownRows.find((row) => row.key === 'Short') || null;
    const deepestDrawdown = drawdownEpisodes[0] || null;
    return [
      {
        key: 'net-pnl',
        label: '已平仓净收益',
        value: formatSignedNumber(closedCostSummary.netPnl),
        helper: `${closedPreviewTradesByExit.length} 笔已平仓`,
        tone: closedCostSummary.netPnl >= 0 ? 'positive' : 'negative',
      },
      {
        key: 'gross-edge',
        label: '成本前边际',
        value: formatSignedNumber(closedCostSummary.grossBeforeCosts),
        helper: '扣除手续费与资金费前',
        tone: closedCostSummary.grossBeforeCosts >= 0 ? 'positive' : 'negative',
      },
      {
        key: 'fee-drag',
        label: '手续费拖累',
        value: formatSignedNumber(-closedCostSummary.totalFee),
        helper: `累计手续费 ${formatNumberValue(closedCostSummary.totalFee, 2)}`,
        tone: closedCostSummary.totalFee > 0 ? 'negative' : 'neutral',
      },
      {
        key: 'funding-impact',
        label: '资金费影响',
        value: formatSignedNumber(-closedCostSummary.totalFunding),
        helper: `累计资金费 ${formatSignedNumber(closedCostSummary.totalFunding, 2)}`,
        tone: -closedCostSummary.totalFunding >= 0 ? 'positive' : 'negative',
      },
      {
        key: 'long-net',
        label: '多头净贡献',
        value: formatSignedNumber(longRow?.netPnl || 0),
        helper: longRow ? `${longRow.count} 笔 / 胜率 ${formatPercentValue(longRow.winRate, 1)}` : '暂无多头平仓',
        tone: (longRow?.netPnl || 0) >= 0 ? 'positive' : 'negative',
      },
      {
        key: 'short-net',
        label: '空头净贡献',
        value: formatSignedNumber(shortRow?.netPnl || 0),
        helper: shortRow ? `${shortRow.count} 笔 / 胜率 ${formatPercentValue(shortRow.winRate, 1)}` : '暂无空头平仓',
        tone: (shortRow?.netPnl || 0) >= 0 ? 'positive' : 'negative',
      },
      {
        key: 'best-month',
        label: '最佳月份',
        value: monthlyPerformance.bestCell
          ? `${monthlyPerformance.bestCell.label} ${formatPercentValue(monthlyPerformance.bestCell.returnPct, 1)}`
          : '-',
        helper: monthlyPerformance.bestCell
          ? `净盈亏 ${formatSignedNumber(monthlyPerformance.bestCell.netPnl, 2)}`
          : '暂无月度样本',
        tone: monthlyPerformance.bestCell && monthlyPerformance.bestCell.returnPct >= 0 ? 'positive' : 'neutral',
      },
      {
        key: 'deepest-drawdown',
        label: '最深回撤区间',
        value: deepestDrawdown ? formatPercentValue(deepestDrawdown.depth, 1) : '-',
        helper: deepestDrawdown
          ? `${formatDateTimeShort(deepestDrawdown.startTimestamp)} -> ${formatDateTimeShort(deepestDrawdown.troughTimestamp)}`
          : '暂无显著回撤区间',
        tone: deepestDrawdown ? 'negative' : 'neutral',
      },
    ] as const;
  }, [closedCostSummary, closedPreviewTradesByExit.length, drawdownEpisodes, monthlyPerformance.bestCell, sideBreakdownRows]);
  const costBreakdownOption = useMemo<EChartsOption>(() => {
    const rows = [
      {
        label: '成本前边际',
        value: closedCostSummary.grossBeforeCosts,
        color: '#2563eb',
        helper: '手续费和资金费尚未扣除',
      },
      {
        label: '手续费影响',
        value: -closedCostSummary.totalFee,
        color: '#dc2626',
        helper: `累计手续费 ${formatNumberValue(closedCostSummary.totalFee, 2)}`,
      },
      {
        label: '资金费影响',
        value: -closedCostSummary.totalFunding,
        color: -closedCostSummary.totalFunding >= 0 ? '#16a34a' : '#dc2626',
        helper: `累计资金费 ${formatSignedNumber(closedCostSummary.totalFunding, 2)}`,
      },
      {
        label: '净收益',
        value: closedCostSummary.netPnl,
        color: closedCostSummary.netPnl >= 0 ? '#16a34a' : '#dc2626',
        helper: '已平仓净口径',
      },
    ];
    const hasValue = rows.some((row) => Math.abs(row.value) > 0);
    return {
      animation: false,
      grid: {
        left: 88,
        right: 16,
        top: 18,
        bottom: 24,
      },
      tooltip: {
        trigger: 'axis',
        axisPointer: { type: 'shadow' },
        formatter: (params: unknown) => {
          const rowsData = toEChartTooltipParams(params);
          const dataIndex = Number(rowsData?.[0]?.dataIndex ?? -1);
          const row = dataIndex >= 0 ? rows[dataIndex] : null;
          if (!row) {
            return '';
          }
          return [
            row.label,
            `金额: ${formatSignedNumber(row.value, 2)}`,
            row.helper,
          ].join('<br/>');
        },
      },
      xAxis: {
        type: 'value',
        axisLine: { show: false },
        axisLabel: {
          color: '#64748b',
          formatter: (value: number) => formatNumberValue(Number(value), 2),
        },
        splitLine: {
          lineStyle: { color: '#edf2fb' },
        },
      },
      yAxis: {
        type: 'category',
        data: rows.map((row) => row.label),
        axisLine: { show: false },
        axisTick: { show: false },
        axisLabel: {
          color: '#475569',
          fontSize: 11,
        },
      },
      series: [
        {
          type: 'bar',
          data: rows.map((row) => ({
            value: Number(row.value.toFixed(6)),
            itemStyle: {
              color: row.color,
              borderRadius: 6,
            },
          })),
          barMaxWidth: 22,
          markLine: {
            silent: true,
            symbol: 'none',
            lineStyle: {
              color: 'rgba(71, 85, 105, 0.35)',
              type: 'dashed',
            },
            data: [{ xAxis: 0 }],
          },
        },
      ],
      graphic: hasValue
        ? undefined
        : [
            {
              type: 'text',
              left: 'center',
              top: 'middle',
              style: {
                text: '暂无已平仓成本归因样本',
                fill: '#94a3b8',
                fontSize: 12,
              },
            },
          ],
    };
  }, [
    closedCostSummary.grossBeforeCosts,
    closedCostSummary.netPnl,
    closedCostSummary.totalFee,
    closedCostSummary.totalFunding,
  ]);
  const rollingQualityOption = useMemo<EChartsOption>(() => {
    const hasData = rollingQualityPoints.length > 0;
    const labels = rollingQualityPoints.map((point) => formatDateTimeShort(point.timestamp));
    return {
      animation: false,
      legend: {
        top: 0,
        right: 0,
        textStyle: {
          color: '#64748b',
          fontSize: 11,
        },
      },
      grid: {
        left: 52,
        right: 48,
        top: 30,
        bottom: 36,
      },
      tooltip: {
        trigger: 'axis',
        axisPointer: { type: 'shadow' },
        formatter: (params: unknown) => {
          const rows = toEChartTooltipParams(params);
          const dataIndex = Number(rows?.[0]?.dataIndex ?? -1);
          const point = dataIndex >= 0 ? rollingQualityPoints[dataIndex] : null;
          if (!point) {
            return '';
          }
          return [
            `${formatDateTime(point.timestamp)} · 近 ${point.windowCount} 笔`,
            `滚动净盈亏: ${formatSignedNumber(point.netPnl, 2)}`,
            `滚动胜率: ${formatPercentValue(point.winRate, 1)}`,
            `平均单笔净盈亏: ${formatSignedNumber(point.avgNetPnl, 2)}`,
            `滚动 ProfitFactor: ${formatNumberValue(point.profitFactor, 2)}`,
          ].join('<br/>');
        },
      },
      xAxis: {
        type: 'category',
        data: labels,
        axisLabel: {
          color: '#64748b',
          hideOverlap: true,
        },
        axisLine: {
          lineStyle: { color: '#dbe2ef' },
        },
      },
      yAxis: [
        {
          type: 'value',
          name: '滚动净盈亏',
          axisLine: { show: false },
          axisLabel: {
            color: '#64748b',
            formatter: (value: number) => formatNumberValue(Number(value), 1),
          },
          splitLine: {
            lineStyle: { color: '#edf2fb' },
          },
        },
        {
          type: 'value',
          name: '胜率(%)',
          min: 0,
          max: 100,
          axisLine: { show: false },
          axisLabel: {
            color: '#64748b',
            formatter: (value: number) => `${Number(value).toFixed(0)}%`,
          },
          splitLine: { show: false },
        },
      ],
      series: [
        {
          name: '滚动净盈亏',
          type: 'bar',
          data: rollingQualityPoints.map((point) => point.netPnl),
          itemStyle: {
            color: (params: { value?: unknown }) => (Number(params?.value) >= 0 ? '#16a34a' : '#dc2626'),
            opacity: 0.82,
          },
          barMaxWidth: 16,
        },
        {
          name: '滚动胜率',
          type: 'line',
          yAxisIndex: 1,
          smooth: true,
          showSymbol: false,
          lineStyle: {
            width: 2,
            color: '#2563eb',
          },
          areaStyle: {
            color: 'rgba(37, 99, 235, 0.12)',
          },
          data: rollingQualityPoints.map((point) => Number((point.winRate * 100).toFixed(3))),
        },
      ],
      graphic: hasData
        ? undefined
        : [
            {
              type: 'text',
              left: 'center',
              top: 'middle',
              style: {
                text: '暂无滚动质量样本',
                fill: '#94a3b8',
                fontSize: 12,
              },
            },
          ],
    };
  }, [rollingQualityPoints]);
  const monthlyPerformanceOption = useMemo<EChartsOption>(() => {
    const safeYears = monthlyPerformance.years.length > 0 ? monthlyPerformance.years.map((year) => `${year}`) : ['-'];
    const yearIndexMap = new Map(safeYears.map((year, index) => [year, index]));
    const maxAbsReturnPct = Math.max(1, monthlyPerformance.maxAbsReturnPct);
    const hasData = monthlyPerformance.activeCells.length > 0;
    const heatmapData = hasData
      ? monthlyPerformance.cells.map((cell) => ({
          value: [
            cell.monthIndex,
            yearIndexMap.get(`${cell.year}`) ?? 0,
            Number((cell.returnPct * 100).toFixed(3)),
          ],
          label: cell.label,
          count: cell.count,
          winRate: cell.winRate,
          pnl: cell.netPnl,
          totalFee: cell.totalFee,
          totalFunding: cell.totalFunding,
        }))
      : [];
    return {
      animation: false,
      grid: {
        left: 48,
        right: 18,
        top: 18,
        bottom: 56,
      },
      tooltip: {
        position: 'top',
        formatter: (params: unknown) => {
          const [entry] = toEChartTooltipParams(params);
          const data = entry?.data as {
            label?: string;
            count?: number;
            winRate?: number;
            pnl?: number;
            totalFee?: number;
            totalFunding?: number;
            value?: unknown[];
          } | undefined;
          if (!data) {
            return '';
          }
          if (!data.count) {
            return `${data.label || '月份'}<br/>暂无已平仓`;
          }
          const returnPct = Array.isArray(data.value) ? Number(data.value[2]) : 0;
          return [
            data.label || '月份',
            `净收益率: ${returnPct.toFixed(2)}%`,
            `净盈亏: ${formatSignedNumber(Number(data.pnl) || 0, 2)}`,
            `样本: ${data.count} 笔 · 胜率 ${formatPercentValue(Number(data.winRate) || 0, 1)}`,
            `手续费: ${formatNumberValue(Number(data.totalFee) || 0, 2)} · 资金费 ${formatSignedNumber(Number(data.totalFunding) || 0, 2)}`,
          ].join('<br/>');
        },
      },
      xAxis: {
        type: 'category',
        data: MONTH_LABELS,
        axisLine: { show: false },
        axisTick: { show: false },
        axisLabel: {
          color: '#64748b',
          fontSize: 11,
        },
      },
      yAxis: {
        type: 'category',
        data: safeYears,
        axisLine: { show: false },
        axisTick: { show: false },
        axisLabel: {
          color: '#64748b',
          fontSize: 11,
        },
      },
      visualMap: {
        min: -maxAbsReturnPct,
        max: maxAbsReturnPct,
        calculable: false,
        orient: 'horizontal',
        left: 'center',
        bottom: 6,
        textStyle: {
          color: '#64748b',
          fontSize: 11,
        },
        inRange: {
          color: ['#7f1d1d', '#fca5a5', '#f8fafc', '#86efac', '#166534'],
        },
      },
      series: [
        {
          type: 'heatmap',
          data: heatmapData,
          label: {
            show: true,
            fontSize: 10,
            color: '#0f172a',
            formatter: (params: { data?: unknown }) => {
              const data = params?.data as { count?: number; value?: unknown[] } | undefined;
              if (!data || !data.count || !Array.isArray(data.value)) {
                return '';
              }
              return `${Number(data.value[2]).toFixed(1)}%`;
            },
          },
          itemStyle: {
            borderWidth: 1,
            borderColor: 'rgba(255,255,255,0.66)',
          },
          emphasis: {
            itemStyle: {
              shadowBlur: 10,
              shadowColor: 'rgba(15, 23, 42, 0.18)',
            },
          },
        },
      ],
      graphic: hasData
        ? undefined
        : [
            {
              type: 'text',
              left: 'center',
              top: 'middle',
              style: {
                text: '暂无月度收益热力图样本',
                fill: '#94a3b8',
                fontSize: 12,
              },
            },
          ],
    };
  }, [monthlyPerformance]);

  useEffect(() => {
    fullPreviewAnchorTradeKeyRef.current = fullPreviewAnchorTradeKey;
  }, [fullPreviewAnchorTradeKey]);

  useEffect(() => {
    if (previewTrades.length <= 0 || previewTradeRanges.length <= 0) {
      setSelectedPreviewTradeKey(null);
      setFocusedPreviewTradeRange(null);
      setFullPreviewAnchorTradeKey(null);
      return;
    }
    if (selectedPreviewTradeKey && !previewTradeRangeMap.has(selectedPreviewTradeKey)) {
      setSelectedPreviewTradeKey(null);
      setFocusedPreviewTradeRange(null);
    }
    if (fullPreviewAnchorTradeKey && !previewTradeRangeMap.has(fullPreviewAnchorTradeKey)) {
      setFullPreviewAnchorTradeKey(null);
    }
  }, [fullPreviewAnchorTradeKey, previewTradeRangeMap, previewTradeRanges.length, previewTrades.length, selectedPreviewTradeKey]);

  useEffect(() => {
    if (previewTradeMode !== 'full') {
      setFullPreviewAnchorTradeKey(null);
      return;
    }
    setFocusedPreviewTradeRange(null);
    if (previewTradeRanges.length <= 0) {
      return;
    }
    const fallbackKey = previewTradeRanges[0]?.id ?? null;
    const nextKey =
      (fullPreviewAnchorTradeKey && previewTradeRangeMap.has(fullPreviewAnchorTradeKey) ? fullPreviewAnchorTradeKey : null)
      || (selectedPreviewTradeKey && previewTradeRangeMap.has(selectedPreviewTradeKey) ? selectedPreviewTradeKey : null)
      || fallbackKey;
    if (!nextKey) {
      return;
    }
    if (nextKey !== fullPreviewAnchorTradeKey) {
      setFullPreviewAnchorTradeKey(nextKey);
    }
    if (nextKey !== selectedPreviewTradeKey) {
      setSelectedPreviewTradeKey(nextKey);
    }
    window.requestAnimationFrame(() => {
      scrollPreviewListToTrade(nextKey);
    });
  }, [
    fullPreviewAnchorTradeKey,
    previewTradeMode,
    previewTradeRangeMap,
    previewTradeRanges,
    scrollPreviewListToTrade,
    selectedPreviewTradeKey,
  ]);

  useEffect(() => {
    setSelectedPreviewTradeKey(null);
    setFocusedPreviewTradeRange(null);
    setFullPreviewAnchorTradeKey(null);
  }, [selectedExchange, selectedSymbol, selectedTimeframeSec]);

  useEffect(() => {
    if (previewScrollSyncEnabled) {
      return;
    }
    pendingViewportRef.current = null;
    if (previewListScrollRafRef.current !== null) {
      window.cancelAnimationFrame(previewListScrollRafRef.current);
      previewListScrollRafRef.current = null;
    }
    if (previewViewportSyncRafRef.current !== null) {
      window.cancelAnimationFrame(previewViewportSyncRafRef.current);
      previewViewportSyncRafRef.current = null;
    }
  }, [previewScrollSyncEnabled]);

  useEffect(() => {
    return () => {
      if (previewListScrollRafRef.current !== null) {
        window.cancelAnimationFrame(previewListScrollRafRef.current);
        previewListScrollRafRef.current = null;
      }
      if (previewViewportSyncRafRef.current !== null) {
        window.cancelAnimationFrame(previewViewportSyncRafRef.current);
        previewViewportSyncRafRef.current = null;
      }
      if (previewListSuppressTimerRef.current !== null) {
        window.clearTimeout(previewListSuppressTimerRef.current);
        previewListSuppressTimerRef.current = null;
      }
      previewListSyncLockedRef.current = false;
      pendingViewportRef.current = null;
      previewTradeItemRefs.current.clear();
    };
  }, []);

  const liveListPanel = (
    <div
      className={[
        'strategy-workbench-live-list',
        workbenchLayoutMode === 'backtest' ? 'strategy-workbench-live-list--backtest' : '',
      ].join(' ').trim()}
    >
      <div className="strategy-workbench-live-list-header">
        <span>仓位列表</span>
        <div className="strategy-workbench-live-list-header-right">
          <label
            className={`strategy-workbench-live-sync-toggle ${previewScrollSyncEnabled ? 'is-enabled' : ''}`}
            title="控制滚动联动：仓位列表滚动跟随K线，K线拖动回推仓位列表"
          >
            <input
              type="checkbox"
              checked={previewScrollSyncEnabled}
              onChange={(event) => {
                setPreviewScrollSyncEnabled(event.target.checked);
              }}
            />
            <span className="strategy-workbench-live-sync-toggle-icon" aria-hidden="true" />
            <span>{previewScrollSyncEnabled ? '联动开' : '联动关'}</span>
          </label>
          <span>{previewTrades.length} 条</span>
          <label className="strategy-workbench-live-mode-toggle">
            <input
              type="checkbox"
              checked={previewTradeMode === 'full'}
              onChange={(event) => {
                setPreviewTradeMode(event.target.checked ? 'full' : 'normal');
              }}
            />
            <span>全量预览</span>
          </label>
        </div>
      </div>
      <div
        className="strategy-workbench-live-list-body"
        ref={previewListBodyRef}
        onScroll={handlePreviewListScroll}
      >
        {previewTrades.length === 0 ? (
          <div className="strategy-workbench-live-empty">
            暂无仓位记录，回测执行后会实时出现在右侧列表。
          </div>
        ) : (
          previewTrades.map((trade, index) => {
            const tradeKey = buildPreviewTradeKey(trade, index);
            const isSelected = activePreviewTradeKey === tradeKey;
            return (
              <div
                key={tradeKey}
                className={`strategy-workbench-live-item ${trade.isOpen ? 'is-open' : ''} ${isSelected ? 'is-selected' : ''}`}
                role="button"
                tabIndex={0}
                aria-pressed={isSelected}
                data-preview-trade-key={tradeKey}
                ref={(node) => {
                  if (node) {
                    previewTradeItemRefs.current.set(tradeKey, node);
                  } else {
                    previewTradeItemRefs.current.delete(tradeKey);
                  }
                }}
                onClick={() => handlePreviewTradeActivate(trade, index)}
                onKeyDown={(event) => {
                  if (event.key === 'Enter' || event.key === ' ') {
                    event.preventDefault();
                    handlePreviewTradeActivate(trade, index);
                  }
                }}
              >
                <div className="strategy-workbench-live-item-head">
                  <span className={`strategy-workbench-live-item-side ${trade.side === 'Long' ? 'is-long' : 'is-short'}`}>
                    {trade.side}
                  </span>
                  <span className="strategy-workbench-live-item-status">
                    {trade.isOpen ? '持仓中' : '已平仓'}
                  </span>
                  <span className={`strategy-workbench-live-item-pnl ${trade.pnl >= 0 ? 'is-positive' : 'is-negative'}`}>
                    {formatSignedNumber(trade.pnl)}
                  </span>
                </div>
                <div className="strategy-workbench-live-item-row">
                  <span>开仓: {formatDateTime(trade.entryTime)}</span>
                  <span>平仓: {trade.isOpen ? '持仓中' : formatDateTime(trade.exitTime)}</span>
                </div>
                <div className="strategy-workbench-live-item-row">
                  <span>开/平价: {trade.entryPrice.toFixed(4)} / {trade.exitPrice.toFixed(4)}</span>
                  <span>数量: {trade.qty}</span>
                </div>
                <div className="strategy-workbench-live-item-row">
                  <span>手续费: {trade.fee.toFixed(4)}</span>
                  <span>资金费: {formatSignedNumber(trade.funding)}</span>
                </div>
                <div className="strategy-workbench-live-item-row">
                  <span>离场: {trade.exitReason || '-'}</span>
                  <span>滑点: {trade.slippageBps} Bps</span>
                </div>
              </div>
            );
          })
        )}
      </div>
    </div>
  );

  if (typeof document === 'undefined') {
    return null;
  }

  return createPortal(
    <DndContext
      sensors={sensors}
      collisionDetection={pointerWithin}
      onDragStart={onDragStart}
      onDragCancel={onDragCancel}
      onDragEnd={onDragEnd}
      autoScroll={true}
    >
      <div className="strategy-workbench strategy-workbench--refactor">
        <div className="strategy-workbench-topbar">
          <div className="strategy-workbench-topbar-left">
            <button type="button" className="strategy-workbench-topbar-button" onClick={onClose}>
              返回
            </button>
            <div className="strategy-workbench-layout-toggle" role="tablist" aria-label="工作台布局模式">
              <button
                type="button"
                className={`strategy-workbench-layout-toggle-button ${workbenchLayoutMode === 'edit' ? 'is-active' : ''}`}
                onClick={() => setWorkbenchLayoutMode('edit')}
              >
                编辑模式
              </button>
              <button
                type="button"
                className={`strategy-workbench-layout-toggle-button ${workbenchLayoutMode === 'backtest' ? 'is-active' : ''}`}
                onClick={() => setWorkbenchLayoutMode('backtest')}
              >
                回测模式
              </button>
            </div>
          </div>
          <div className="strategy-workbench-title-wrap">
            <div className="strategy-workbench-title">
              策略指标工作台
              <span className={`strategy-workbench-run-state ${isBacktestRunning ? 'is-running' : ''}`}>
                {headerRunText}
              </span>
            </div>
            <div className="strategy-workbench-subtitle">
              {exchangeLabel} · {symbolLabel} · {timeframeLabel}
              {' · '}
              检测至 {detectedTimeText}
              {' · '}
              样本约 {loadedDataDays.toFixed(1)} 天（{backtestBars.length} 根）
            </div>
          </div>
          <div className="strategy-workbench-topbar-actions">
            {topbarExtraActions}
            <button
              type="button"
              className="strategy-workbench-topbar-button"
              onClick={() => setShowOfflineCacheDialog(true)}
            >
              本地缓存
            </button>
            <button
              type="button"
              className="strategy-workbench-topbar-button is-primary"
              onClick={onOpenExport}
            >
              导出
            </button>
          </div>
        </div>

        {!ready ? (
          <div className="strategy-workbench-selection-overlay">
            <div className="strategy-workbench-selection-panel">
              <div className="strategy-workbench-selection-title">选择交易标的与周期</div>
              <div className="strategy-workbench-selection-hint">
                默认已选择 币安 + 5分钟。确认后进入全屏策略工作台。
              </div>

              <div className="strategy-workbench-selection-group">
                <div className="strategy-workbench-selection-group-title">交易所</div>
                <div className="strategy-workbench-choice-grid">
                  {exchangeOptions.map((option) => (
                    <button
                      type="button"
                      key={option.value}
                      className={`strategy-workbench-choice-card ${selectedExchange === option.value ? 'is-active' : ''}`}
                      onClick={() => onExchangeChange(option.value)}
                    >
                      {option.icon ? (
                        <img className="strategy-workbench-choice-icon" src={option.icon} alt={option.label} />
                      ) : null}
                      <div className="strategy-workbench-choice-label">{option.label}</div>
                    </button>
                  ))}
                </div>
              </div>

              <div className="strategy-workbench-selection-group">
                <div className="strategy-workbench-selection-group-title">币种</div>
                <div className="strategy-workbench-choice-grid">
                  {symbolOptions.map((option) => (
                    <button
                      type="button"
                      key={option.value}
                      className={`strategy-workbench-choice-card ${selectedSymbol === option.value ? 'is-active' : ''}`}
                      onClick={() => onSymbolChange(option.value)}
                    >
                      {option.icon ? (
                        <img className="strategy-workbench-choice-icon" src={option.icon} alt={option.label} />
                      ) : null}
                      <div className="strategy-workbench-choice-label">{option.label}</div>
                    </button>
                  ))}
                </div>
              </div>

              <div className="strategy-workbench-selection-group">
                <div className="strategy-workbench-selection-group-title">周期</div>
                <div className="strategy-workbench-choice-grid">
                  {timeframeOptions.map((option) => (
                    <button
                      type="button"
                      key={option.value}
                      className={`strategy-workbench-choice-card ${selectedTimeframeSec === option.value ? 'is-active' : ''}`}
                      onClick={() => onTimeframeChange(option.value)}
                    >
                      <div className="strategy-workbench-choice-label">{option.label}</div>
                    </button>
                  ))}
                </div>
              </div>

              <button type="button" className="strategy-workbench-activate" onClick={() => setReady(true)}>
                开始创建
              </button>
            </div>
          </div>
        ) : (
          <div
            className={`strategy-workbench-main strategy-workbench-main--resizable ${workbenchLayoutMode === 'backtest' ? 'is-backtest-mode' : 'is-edit-mode'}`}
            ref={mainLayoutRef}
            style={
              {
                '--wb-left-width': `${leftPanelWidth}%`,
                '--wb-right-left-width': `${rightLeftPanelWidth}%`,
                '--wb-left-kline-height': `${leftKlineHeight}%`,
                '--wb-right-panel-height': rightPanelHeightCustomized ? `${rightPanelHeight}%` : undefined,
                '--wb-right-panel-auto-max-height': '66.666vh',
              } as React.CSSProperties
            }
          >
            <div className="strategy-workbench-left strategy-workbench-left--resizable" ref={leftPanelRef}>
              <div className="strategy-workbench-card strategy-workbench-card--kline">
                <div className="strategy-workbench-card-header strategy-workbench-card-header--kline">
                  <div className="strategy-workbench-kline-header-main">
                    <span className="strategy-workbench-card-title">K线图</span>
                    <span className="strategy-workbench-card-meta">
                      当前周期：{chartTimeframeLabel} · 已联动指标：{selectedIndicators.length}
                    </span>
                  </div>
                  <label className="strategy-workbench-kline-timeframe-switch">
                    <span className="strategy-workbench-kline-timeframe-switch-label">图表周期</span>
                    <select
                      value={chartTimeframeSec}
                      onChange={(event) => handleChartTimeframeChange(Number(event.target.value))}
                    >
                      {chartTimeframeOptions.map((option) => (
                        <option key={option.value} value={option.value}>
                          {option.label}
                        </option>
                      ))}
                    </select>
                  </label>
                </div>
                <div className="strategy-workbench-kline-wrap">
                  <StrategyWorkbenchKline
                    exchange={selectedExchange}
                    symbol={selectedSymbol}
                    timeframeSec={chartTimeframeSec}
                    selectedIndicators={selectedIndicators}
                    previewMode={previewTradeMode}
                    focusRange={previewTradeMode === 'normal' ? focusedPreviewTradeRange : null}
                    fullPreviewRanges={previewTradeMode === 'full' ? previewTradeRanges : []}
                    selectedRangeId={activePreviewTradeKey}
                    syncTargetRange={previewTradeMode === 'full' ? fullPreviewAnchorRange : null}
                    onFocusRangeCoverage={handlePreviewFocusRangeCoverage}
                    onVisibleRangeChange={
                      previewTradeMode === 'full' && previewScrollSyncEnabled
                        ? handlePreviewKlineVisibleRangeChange
                        : undefined
                    }
                    hoverValueId={activeHoverValueId}
                    hoverHasReference={activeHoverHasReference}
                    onBarsUpdate={setKlineBars}
                  />
                </div>
              </div>

              <ResizeHandleVertical
                className="strategy-workbench-resize-handle strategy-workbench-resize-handle--vertical strategy-workbench-resize-handle--left"
                onResize={handleLeftVerticalResize}
              />

              <div className="strategy-workbench-card strategy-workbench-card--dashboard">
                <div className="strategy-workbench-card-header">
                  <div className="strategy-workbench-dashboard-title-group">
                    <span className="strategy-workbench-card-title">仪表盘</span>
                    <span className={`strategy-workbench-dashboard-progress-chip ${isBacktestRunning ? 'is-running' : ''}`}>
                      {dashboardProgressText}
                    </span>
                  </div>
                  <div className="strategy-workbench-dashboard-header-actions">
                    <span className="strategy-workbench-card-meta">
                      {headerRunText} · 检测至 {detectedTimeText} · 耗时 {formatDuration(backtestProgress.elapsedMs)}
                    </span>
                    <button
                      type="button"
                      className="strategy-workbench-dashboard-mode-button"
                      onClick={() =>
                        setDashboardMode((prev) => (prev === 'preview' ? 'settings' : 'preview'))
                      }
                    >
                      {dashboardMode === 'preview' ? '参数设置' : '返回预览'}
                    </button>
                  </div>
                </div>

                {dashboardMode === 'settings' ? (
                  <>
                    <div className="strategy-workbench-risk-form">
                      <label className="strategy-workbench-risk-field">
                        <span>止盈比例(%)</span>
                        <input
                          type="number"
                          step={0.1}
                          min={0}
                          value={backtestFormParams.takeProfitPct}
                          onChange={(event) => updateBacktestNumberField('takeProfitPct', event.target.value)}
                        />
                      </label>
                      <label className="strategy-workbench-risk-field">
                        <span>止损比例(%)</span>
                        <input
                          type="number"
                          step={0.1}
                          min={0}
                          value={backtestFormParams.stopLossPct}
                          onChange={(event) => updateBacktestNumberField('stopLossPct', event.target.value)}
                        />
                      </label>
                      <label className="strategy-workbench-risk-field">
                        <span>杠杆倍数</span>
                        <input
                          type="number"
                          step={1}
                          min={1}
                          value={backtestFormParams.leverage}
                          onChange={(event) => updateBacktestNumberField('leverage', event.target.value)}
                        />
                      </label>
                      <label className="strategy-workbench-risk-field">
                        <span>单次开仓数量</span>
                        <input
                          type="number"
                          step={0.001}
                          min={0}
                          value={backtestFormParams.orderQty}
                          onChange={(event) => updateBacktestNumberField('orderQty', event.target.value)}
                        />
                      </label>
                      <label className="strategy-workbench-risk-field">
                        <span>初始资金</span>
                        <input
                          type="number"
                          step={100}
                          min={0}
                          value={backtestFormParams.initialCapital}
                          onChange={(event) => updateBacktestNumberField('initialCapital', event.target.value)}
                        />
                      </label>
                      <label className="strategy-workbench-risk-field">
                        <span>手续费率</span>
                        <input
                          type="number"
                          step={0.0001}
                          min={0}
                          value={backtestFormParams.feeRate}
                          onChange={(event) => updateBacktestNumberField('feeRate', event.target.value)}
                        />
                      </label>
                      <label className="strategy-workbench-risk-field">
                        <span>资金费率</span>
                        <input
                          type="number"
                          step={0.0001}
                          value={backtestFormParams.fundingRate}
                          onChange={(event) => updateBacktestNumberField('fundingRate', event.target.value)}
                        />
                      </label>
                      <label className="strategy-workbench-risk-field">
                        <span>滑点(Bps)</span>
                        <input
                          type="number"
                          step={1}
                          min={0}
                          value={backtestFormParams.slippageBps}
                          onChange={(event) => updateBacktestNumberField('slippageBps', event.target.value)}
                        />
                      </label>
                      <label className="strategy-workbench-risk-field">
                        <span>执行模式</span>
                        <select
                          value={backtestFormParams.executionMode}
                          onChange={(event) =>
                            setBacktestFormParams((prev) => ({
                              ...prev,
                              executionMode:
                                event.target.value === 'timeline' ? 'timeline' : 'batch_open_close',
                            }))
                          }
                        >
                          <option value="batch_open_close">batch_open_close</option>
                          <option value="timeline">timeline</option>
                        </select>
                      </label>
                      <label className="strategy-workbench-risk-field">
                        <span>回测范围</span>
                        <select
                          value={backtestFormParams.timeRangeMode}
                          onChange={(event) =>
                            updateBacktestTimeRangeMode(
                              event.target.value === 'custom' ? 'custom' : 'latest_30d',
                            )
                          }
                        >
                          <option value="latest_30d">最近30天（默认）</option>
                          <option value="custom">自定义起止时间</option>
                        </select>
                      </label>
                      <label className="strategy-workbench-risk-field">
                        <span>开始时间</span>
                        <input
                          type="datetime-local"
                          disabled={backtestFormParams.timeRangeMode !== 'custom'}
                          value={toDateTimeLocalValue(backtestFormParams.rangeStartMs)}
                          onChange={(event) => updateBacktestRangeField('rangeStartMs', event.target.value)}
                        />
                      </label>
                      <label className="strategy-workbench-risk-field">
                        <span>结束时间</span>
                        <input
                          type="datetime-local"
                          disabled={backtestFormParams.timeRangeMode !== 'custom'}
                          value={toDateTimeLocalValue(backtestFormParams.rangeEndMs)}
                          onChange={(event) => updateBacktestRangeField('rangeEndMs', event.target.value)}
                        />
                      </label>
                      <label className="strategy-workbench-risk-field strategy-workbench-risk-field--checkbox">
                        <span>自动反向</span>
                        <input
                          type="checkbox"
                          checked={backtestFormParams.autoReverse}
                          onChange={(event) =>
                            setBacktestFormParams((prev) => ({ ...prev, autoReverse: event.target.checked }))
                          }
                        />
                      </label>
                      <label className="strategy-workbench-risk-field strategy-workbench-risk-field--checkbox">
                        <span>运行时间门禁</span>
                        <input
                          type="checkbox"
                          checked={backtestFormParams.useStrategyRuntime}
                          onChange={(event) =>
                            setBacktestFormParams((prev) => ({
                              ...prev,
                              useStrategyRuntime: event.target.checked,
                            }))
                          }
                        />
                      </label>
                    </div>

                    <div className="strategy-workbench-risk-field-hint">
                      参数口径已对齐后端回测：初始资金、手续费率、资金费率、滑点、自动反向、运行时间门禁、执行模式。当前可用样本范围：{availableRangeText}。
                    </div>
                    <div className="strategy-workbench-risk-field-hint">
                      当前生效范围：{activeRangeText}（约 {loadedDataDays.toFixed(1)} 天，{backtestBars.length} 根）。
                    </div>

                    <div className="strategy-workbench-dashboard-actions">
                      <button
                        type="button"
                        className="strategy-workbench-dashboard-confirm"
                        onClick={applyBacktestParams}
                      >
                        确认并预览
                      </button>
                    </div>
                  </>
                ) : (
                  <>
                    <div
                      className={[
                        'strategy-workbench-live-preview',
                        'strategy-workbench-live-preview--resizable',
                        workbenchLayoutMode === 'backtest' ? 'strategy-workbench-live-preview--backtest' : '',
                      ].join(' ').trim()}
                      ref={livePreviewRef}
                      style={
                        {
                          '--wb-live-list-width': `${liveListPanelWidth}%`,
                        } as React.CSSProperties
                      }
                    >
                      <div className="strategy-workbench-live-summary">
                        <div className="strategy-workbench-live-summary-tabs" role="tablist" aria-label="回测摘要分组">
                          <button
                            type="button"
                            className={`strategy-workbench-live-summary-tab ${liveSummaryTab === 'overview' ? 'is-active' : ''}`}
                            onClick={() => setLiveSummaryTab('overview')}
                          >
                            概论
                          </button>
                          <button
                            type="button"
                            className={`strategy-workbench-live-summary-tab ${liveSummaryTab === 'stats' ? 'is-active' : ''}`}
                            onClick={() => setLiveSummaryTab('stats')}
                          >
                            统计
                          </button>
                          <button
                            type="button"
                            className={`strategy-workbench-live-summary-tab ${liveSummaryTab === 'professional' ? 'is-active' : ''}`}
                            onClick={() => setLiveSummaryTab('professional')}
                          >
                            专业
                          </button>
                          <button
                            type="button"
                            className={`strategy-workbench-live-summary-tab ${liveSummaryTab === 'calendar' ? 'is-active' : ''}`}
                            onClick={() => setLiveSummaryTab('calendar')}
                          >
                            日历
                          </button>
                          <button
                            type="button"
                            className={`strategy-workbench-live-summary-tab ${liveSummaryTab === 'log' ? 'is-active' : ''}`}
                            onClick={() => setLiveSummaryTab('log')}
                          >
                            Log
                          </button>
                        </div>
                        <div className="strategy-workbench-live-summary-panel">
                          {liveSummaryTab === 'overview' ? (
                        <div className="strategy-workbench-live-visual-grid">
                          <div className="strategy-workbench-live-chart-card strategy-workbench-live-chart-card--equity">
                            <div className="strategy-workbench-live-chart-title strategy-workbench-live-chart-title--with-metrics">
                              <span>收益资金曲线</span>
                              <span className="strategy-workbench-live-chart-title-metrics">
                                <span>
                                  平均盈亏
                                  <strong className={liveAveragePnl >= 0 ? 'is-positive' : 'is-negative'}>
                                    {formatSignedNumber(liveAveragePnl)}
                                  </strong>
                                </span>
                                <span>
                                  累计收益
                                  <strong className={localBacktestSummary.totalProfit >= 0 ? 'is-positive' : 'is-negative'}>
                                    {formatSignedNumber(localBacktestSummary.totalProfit)}
                                  </strong>
                                </span>
                              </span>
                            </div>
                            <StrategyWorkbenchEChart
                              className="strategy-workbench-live-chart-canvas strategy-workbench-live-chart-canvas--equity"
                              option={equityCurveOption}
                              onChartClick={handleEquityChartClick}
                            />
                            <div className="strategy-workbench-live-chart-insights">
                              <span>{maxWinStreakText}</span>
                              <span>{maxLossStreakText}</span>
                              <span>{maxDrawdownPositionText}</span>
                            </div>
                          </div>
                          <div className="strategy-workbench-live-chart-card strategy-workbench-live-chart-card--winrate">
                            <div className="strategy-workbench-live-chart-title">胜负分布</div>
                            <StrategyWorkbenchEChart
                              className="strategy-workbench-live-chart-canvas strategy-workbench-live-chart-canvas--winrate"
                              option={winLossPieOption}
                            />
                            <div className="strategy-workbench-live-winrate-total">
                              开仓总数量
                              <strong>{livePositionCount}</strong>
                            </div>
                          </div>
                        </div>
                          ) : null}
                          {liveSummaryTab === 'calendar' ? (
                        <div className="strategy-workbench-live-calendar">
                          <div className="strategy-workbench-live-calendar-header">
                            <div className="strategy-workbench-live-calendar-title">盈亏日历分析</div>
                            <div className="strategy-workbench-live-calendar-header-right">
                              <div className="strategy-workbench-live-calendar-mode-toggle" role="tablist" aria-label="日历视图切换">
                                <button
                                  type="button"
                                  className={`strategy-workbench-live-calendar-mode-button ${calendarViewMode === 'month' ? 'is-active' : ''}`}
                                  onClick={() => setCalendarViewMode('month')}
                                >
                                  月
                                </button>
                                <button
                                  type="button"
                                  className={`strategy-workbench-live-calendar-mode-button ${calendarViewMode === 'week' ? 'is-active' : ''}`}
                                  onClick={() => setCalendarViewMode('week')}
                                >
                                  周
                                </button>
                                <button
                                  type="button"
                                  className={`strategy-workbench-live-calendar-mode-button ${calendarViewMode === 'day' ? 'is-active' : ''}`}
                                  onClick={() => setCalendarViewMode('day')}
                                >
                                  日
                                </button>
                              </div>
                              <div className="strategy-workbench-live-calendar-nav">
                                <button
                                  type="button"
                                  className="strategy-workbench-live-calendar-nav-button"
                                  onClick={() => handleCalendarShift(-1)}
                                  aria-label="上一周期"
                                >
                                  {'<'}
                                </button>
                                <span className="strategy-workbench-live-calendar-period-label">{calendarViewData.periodLabel}</span>
                                <button
                                  type="button"
                                  className="strategy-workbench-live-calendar-nav-button"
                                  onClick={() => handleCalendarShift(1)}
                                  aria-label="下一周期"
                                >
                                  {'>'}
                                </button>
                                <button
                                  type="button"
                                  className="strategy-workbench-live-calendar-nav-button"
                                  onClick={handleCalendarBackToLatest}
                                >
                                  最新
                                </button>
                              </div>
                            </div>
                          </div>
                          <div className="strategy-workbench-live-calendar-summary">
                            <span>
                              总盈亏
                              <strong className={calendarViewData.periodMetrics.pnl >= 0 ? 'is-positive' : 'is-negative'}>
                                {formatSignedNumber(calendarViewData.periodMetrics.pnl)}
                              </strong>
                            </span>
                            <span>
                              开仓单数
                              <strong>{calendarViewData.periodMetrics.count}</strong>
                            </span>
                            <span>
                              胜率
                              <strong>
                                {formatPercentValue(
                                  calendarViewData.periodMetrics.count > 0
                                    ? calendarViewData.periodMetrics.wins / calendarViewData.periodMetrics.count
                                    : 0,
                                )}
                              </strong>
                            </span>
                          </div>
                          <div className={`strategy-workbench-live-calendar-grid is-${calendarViewMode}`}>
                            {calendarViewData.cells.map((cell) => {
                              const cellWinRate = cell.metrics.count > 0 ? cell.metrics.wins / cell.metrics.count : 0;
                              const tone = getCalendarCellBackground(cell.metrics.pnl, calendarViewData.maxAbsPnl);
                              const linkedTradeIndex = Number.isFinite(cell.metrics.representativeTradeIndex)
                                ? Number(cell.metrics.representativeTradeIndex)
                                : Number.NaN;
                              const clickable = Number.isFinite(linkedTradeIndex) && linkedTradeIndex >= 0;
                              return (
                                <div
                                  key={cell.id}
                                  className={[
                                    'strategy-workbench-live-calendar-cell',
                                    clickable ? 'is-clickable' : '',
                                    cell.inCurrentPeriod ? '' : 'is-outside',
                                    cell.metrics.pnl > 0 ? 'is-positive' : '',
                                    cell.metrics.pnl < 0 ? 'is-negative' : '',
                                  ].join(' ').trim()}
                                  style={tone ? { backgroundColor: tone } : undefined}
                                  role={clickable ? 'button' : undefined}
                                  tabIndex={clickable ? 0 : undefined}
                                  onClick={clickable ? () => activatePreviewTradeByIndex(linkedTradeIndex) : undefined}
                                  onKeyDown={
                                    clickable
                                      ? (event) => {
                                          if (event.key === 'Enter' || event.key === ' ') {
                                            event.preventDefault();
                                            activatePreviewTradeByIndex(linkedTradeIndex);
                                          }
                                        }
                                      : undefined
                                  }
                                >
                                  <div className="strategy-workbench-live-calendar-cell-head">
                                    <span>{cell.label}</span>
                                    {cell.subLabel ? (
                                      <span className="strategy-workbench-live-calendar-cell-sub">{cell.subLabel}</span>
                                    ) : null}
                                  </div>
                                  <div className="strategy-workbench-live-calendar-cell-pnl">{formatSignedNumber(cell.metrics.pnl)}</div>
                                  <div className="strategy-workbench-live-calendar-cell-meta">开仓 {cell.metrics.count}</div>
                                  <div className="strategy-workbench-live-calendar-cell-meta">胜率 {formatPercentValue(cellWinRate, 1)}</div>
                                </div>
                              );
                            })}
                          </div>
                        </div>
                          ) : null}
                          {liveSummaryTab === 'stats' ? (
                            <>
                        <div className="strategy-workbench-live-time-analysis">
                          <div className="strategy-workbench-live-time-analysis-header">
                            <div className="strategy-workbench-live-time-analysis-title">时段盈利分析</div>
                            <div className="strategy-workbench-live-time-analysis-controls">
                              <div
                                className="strategy-workbench-live-time-analysis-toggle"
                                role="tablist"
                                aria-label="时段统计口径切换"
                              >
                                <button
                                  type="button"
                                  className={`strategy-workbench-live-time-analysis-toggle-button ${timeAnalysisReferenceMode === 'entry' ? 'is-active' : ''}`}
                                  onClick={() => setTimeAnalysisReferenceMode('entry')}
                                >
                                  开仓时间
                                </button>
                                <button
                                  type="button"
                                  className={`strategy-workbench-live-time-analysis-toggle-button ${timeAnalysisReferenceMode === 'exit' ? 'is-active' : ''}`}
                                  onClick={() => setTimeAnalysisReferenceMode('exit')}
                                >
                                  平仓时间
                                </button>
                              </div>
                              <div
                                className="strategy-workbench-live-time-analysis-toggle"
                                role="tablist"
                                aria-label="时段粒度切换"
                              >
                                <button
                                  type="button"
                                  className={`strategy-workbench-live-time-analysis-toggle-button ${timeAnalysisGranularity === 'hour' ? 'is-active' : ''}`}
                                  onClick={() => setTimeAnalysisGranularity('hour')}
                                >
                                  小时级
                                </button>
                                <button
                                  type="button"
                                  className={`strategy-workbench-live-time-analysis-toggle-button ${timeAnalysisGranularity === 'day' ? 'is-active' : ''}`}
                                  onClick={() => setTimeAnalysisGranularity('day')}
                                >
                                  日级
                                </button>
                              </div>
                            </div>
                          </div>
                          <div className="strategy-workbench-live-time-analysis-insights">
                            <span>统计口径：{timeAnalysisReferenceModeLabel}</span>
                            <span>盈利最快持仓段：{fastestProfitBucketText}</span>
                            <span>亏损最快持仓段：{fastestLossBucketText}</span>
                            <span>{selectedPeriodInsightText}</span>
                          </div>
                          <div className="strategy-workbench-live-time-analysis-chart-grid">
                            <div className="strategy-workbench-live-time-analysis-card">
                              <div className="strategy-workbench-live-time-analysis-card-title">持仓周期点阵图</div>
                              <StrategyWorkbenchEChart
                                className="strategy-workbench-live-chart-canvas strategy-workbench-live-chart-canvas--duration-scatter"
                                option={holdingDurationScatterOption}
                                onChartClick={handleHoldingDurationScatterClick}
                              />
                            </div>
                            <div className="strategy-workbench-live-time-analysis-card">
                              <div className="strategy-workbench-live-time-analysis-card-title">
                                {timeAnalysisGranularity === 'hour' ? '小时级平均收益与胜率' : '日级平均收益与胜率'}
                              </div>
                              <StrategyWorkbenchEChart
                                className="strategy-workbench-live-chart-canvas strategy-workbench-live-chart-canvas--period-distribution"
                                option={periodDistributionOption}
                              />
                            </div>
                          </div>
                          <div className="strategy-workbench-live-time-analysis-table-grid">
                            <div className="strategy-workbench-live-time-analysis-card">
                              <div className="strategy-workbench-live-time-analysis-card-title">日级分布（样本笔数/胜亏占比）</div>
                              <div className="strategy-workbench-live-time-analysis-table-scroll">
                                <table className="strategy-workbench-live-time-analysis-table">
                                  <thead>
                                    <tr>
                                      <th>时段</th>
                                      <th>笔数</th>
                                      <th>胜率</th>
                                      <th>亏损占比</th>
                                      <th>均值(%)</th>
                                      <th>总盈亏</th>
                                    </tr>
                                  </thead>
                                  <tbody>
                                    {dayPeriodBuckets.map((bucket) => (
                                      <tr key={bucket.key}>
                                        <td>{bucket.label}</td>
                                        <td>{bucket.count}</td>
                                        <td>{formatPercentValue(bucket.winRate, 1)}</td>
                                        <td>{formatPercentValue(bucket.lossRate, 1)}</td>
                                        <td className={bucket.avgReturnPct >= 0 ? 'is-positive' : 'is-negative'}>
                                          {formatSignedNumber(bucket.avgReturnPct, 2)}%
                                        </td>
                                        <td className={bucket.pnl >= 0 ? 'is-positive' : 'is-negative'}>
                                          {formatSignedNumber(bucket.pnl)}
                                        </td>
                                      </tr>
                                    ))}
                                  </tbody>
                                </table>
                              </div>
                            </div>
                            <div className="strategy-workbench-live-time-analysis-card">
                              <div className="strategy-workbench-live-time-analysis-card-title">小时级分布（样本笔数/胜亏占比）</div>
                              <div className="strategy-workbench-live-time-analysis-table-scroll">
                                <table className="strategy-workbench-live-time-analysis-table">
                                  <thead>
                                    <tr>
                                      <th>时段</th>
                                      <th>笔数</th>
                                      <th>胜率</th>
                                      <th>亏损占比</th>
                                      <th>均值(%)</th>
                                      <th>总盈亏</th>
                                    </tr>
                                  </thead>
                                  <tbody>
                                    {hourPeriodBuckets.map((bucket) => (
                                      <tr key={bucket.key}>
                                        <td>{bucket.label}</td>
                                        <td>{bucket.count}</td>
                                        <td>{formatPercentValue(bucket.winRate, 1)}</td>
                                        <td>{formatPercentValue(bucket.lossRate, 1)}</td>
                                        <td className={bucket.avgReturnPct >= 0 ? 'is-positive' : 'is-negative'}>
                                          {formatSignedNumber(bucket.avgReturnPct, 2)}%
                                        </td>
                                        <td className={bucket.pnl >= 0 ? 'is-positive' : 'is-negative'}>
                                          {formatSignedNumber(bucket.pnl)}
                                        </td>
                                      </tr>
                                    ))}
                                  </tbody>
                                </table>
                              </div>
                            </div>
                          </div>
                        </div>
                        <div className="strategy-workbench-live-advanced-grid">
                          <div className="strategy-workbench-live-advanced-item">
                            <span>盈亏比</span>
                            <strong>{formatNumberValue(backtestStats.profitFactor)}</strong>
                          </div>
                          <div className="strategy-workbench-live-advanced-item">
                            <span>平均盈利/亏损</span>
                            <strong>{formatSignedNumber(backtestStats.avgWin)} / {formatSignedNumber(backtestStats.avgLoss)}</strong>
                          </div>
                          <div className="strategy-workbench-live-advanced-item">
                            <span>夏普/Sortino</span>
                            <strong>{formatNumberValue(backtestStats.sharpeRatio)} / {formatNumberValue(backtestStats.sortinoRatio)}</strong>
                          </div>
                          <div className="strategy-workbench-live-advanced-item">
                            <span>年化/Calmar</span>
                            <strong>{formatPercentValue(backtestStats.annualizedReturn)} / {formatNumberValue(backtestStats.calmarRatio)}</strong>
                          </div>
                          <div className="strategy-workbench-live-advanced-item">
                            <span>最大连胜/连败</span>
                            <strong>{backtestStats.maxConsecutiveWins} / {backtestStats.maxConsecutiveLosses}</strong>
                          </div>
                          <div className="strategy-workbench-live-advanced-item">
                            <span>平均持仓</span>
                            <strong>{formatDuration(backtestStats.avgHoldingMs)}</strong>
                          </div>
                          <div className="strategy-workbench-live-advanced-item">
                            <span>回撤持续</span>
                            <strong>{formatDuration(backtestStats.maxDrawdownDurationMs)}</strong>
                          </div>
                          <div className="strategy-workbench-live-advanced-item">
                            <span>浮动盈亏</span>
                            <strong>{formatSignedNumber(localBacktestSummary.unrealizedPnl)}</strong>
                          </div>
                        </div>
                            </>
                          ) : null}
                          {liveSummaryTab === 'professional' ? (
                            <div className="strategy-workbench-live-professional">
                              <div
                                className={[
                                  'strategy-workbench-live-professional-note',
                                  equitySummary.pointCount > (localBacktestSummary.equityPreview?.length || 0) ? 'is-warning' : '',
                                ].join(' ').trim()}
                              >
                                {professionalCoverageNote}
                              </div>
                              <div className="strategy-workbench-live-professional-metrics">
                                {professionalMetricCards.map((card) => (
                                  <div className="strategy-workbench-live-professional-metric" key={card.key}>
                                    <span className="strategy-workbench-live-professional-metric-label">{card.label}</span>
                                    <strong
                                      className={[
                                        'strategy-workbench-live-professional-metric-value',
                                        card.tone !== 'neutral' ? `is-${card.tone}` : '',
                                      ].join(' ').trim()}
                                    >
                                      {card.value}
                                    </strong>
                                    <span className="strategy-workbench-live-professional-metric-helper">{card.helper}</span>
                                  </div>
                                ))}
                              </div>
                              <div className="strategy-workbench-live-professional-chart-grid">
                                <div className="strategy-workbench-live-time-analysis-card">
                                  <div className="strategy-workbench-live-time-analysis-card-title">已平仓成本归因</div>
                                  <StrategyWorkbenchEChart
                                    className="strategy-workbench-live-chart-canvas strategy-workbench-live-chart-canvas--professional"
                                    option={costBreakdownOption}
                                  />
                                </div>
                                <div className="strategy-workbench-live-time-analysis-card">
                                  <div className="strategy-workbench-live-time-analysis-card-title">
                                    {rollingWindowSize > 0 ? `近 ${rollingWindowSize} 笔滚动质量` : '滚动质量'}
                                  </div>
                                  <StrategyWorkbenchEChart
                                    className="strategy-workbench-live-chart-canvas strategy-workbench-live-chart-canvas--professional"
                                    option={rollingQualityOption}
                                  />
                                </div>
                              </div>
                              <div className="strategy-workbench-live-time-analysis-card">
                                <div className="strategy-workbench-live-time-analysis-card-title">月度收益热力图</div>
                                <StrategyWorkbenchEChart
                                  className="strategy-workbench-live-chart-canvas strategy-workbench-live-chart-canvas--professional"
                                  option={monthlyPerformanceOption}
                                />
                              </div>
                              <div className="strategy-workbench-live-professional-table-grid">
                                <div className="strategy-workbench-live-time-analysis-card">
                                  <div className="strategy-workbench-live-time-analysis-card-title">Long / Short 分解</div>
                                  <div className="strategy-workbench-live-time-analysis-table-scroll">
                                    <table className="strategy-workbench-live-time-analysis-table">
                                      <thead>
                                        <tr>
                                          <th>方向</th>
                                          <th>平仓</th>
                                          <th>占比</th>
                                          <th>胜率</th>
                                          <th>净盈亏</th>
                                          <th>成本前</th>
                                          <th>手续费</th>
                                          <th>资金费</th>
                                          <th>平均持仓</th>
                                        </tr>
                                      </thead>
                                      <tbody>
                                        {sideBreakdownRows.map((row) => (
                                          <tr key={row.key}>
                                            <td>{row.label}</td>
                                            <td>{row.count}</td>
                                            <td>{formatPercentValue(row.share, 1)}</td>
                                            <td>{formatPercentValue(row.winRate, 1)}</td>
                                            <td className={row.netPnl >= 0 ? 'is-positive' : 'is-negative'}>
                                              {formatSignedNumber(row.netPnl, 2)}
                                            </td>
                                            <td className={row.grossBeforeCosts >= 0 ? 'is-positive' : 'is-negative'}>
                                              {formatSignedNumber(row.grossBeforeCosts, 2)}
                                            </td>
                                            <td>{formatNumberValue(row.totalFee, 2)}</td>
                                            <td className={row.totalFunding <= 0 ? 'is-positive' : 'is-negative'}>
                                              {formatSignedNumber(row.totalFunding, 2)}
                                            </td>
                                            <td>{formatDuration(row.avgHoldingMs)}</td>
                                          </tr>
                                        ))}
                                      </tbody>
                                    </table>
                                  </div>
                                </div>
                                <div className="strategy-workbench-live-time-analysis-card">
                                  <div className="strategy-workbench-live-time-analysis-card-title">平仓原因分解</div>
                                  <div className="strategy-workbench-live-time-analysis-table-scroll">
                                    <table className="strategy-workbench-live-time-analysis-table">
                                      <thead>
                                        <tr>
                                          <th>原因</th>
                                          <th>平仓</th>
                                          <th>占比</th>
                                          <th>胜率</th>
                                          <th>净盈亏</th>
                                          <th>平均净盈亏</th>
                                          <th>手续费</th>
                                          <th>资金费</th>
                                        </tr>
                                      </thead>
                                      <tbody>
                                        {exitReasonRows.length <= 0 ? (
                                          <tr>
                                            <td colSpan={8}>暂无已平仓原因样本</td>
                                          </tr>
                                        ) : (
                                          exitReasonRows.map((row) => (
                                            <tr key={row.key}>
                                              <td>{row.label}</td>
                                              <td>{row.count}</td>
                                              <td>{formatPercentValue(row.share, 1)}</td>
                                              <td>{formatPercentValue(row.winRate, 1)}</td>
                                              <td className={row.netPnl >= 0 ? 'is-positive' : 'is-negative'}>
                                                {formatSignedNumber(row.netPnl, 2)}
                                              </td>
                                              <td className={row.avgNetPnl >= 0 ? 'is-positive' : 'is-negative'}>
                                                {formatSignedNumber(row.avgNetPnl, 2)}
                                              </td>
                                              <td>{formatNumberValue(row.totalFee, 2)}</td>
                                              <td className={row.totalFunding <= 0 ? 'is-positive' : 'is-negative'}>
                                                {formatSignedNumber(row.totalFunding, 2)}
                                              </td>
                                            </tr>
                                          ))
                                        )}
                                      </tbody>
                                    </table>
                                  </div>
                                </div>
                                <div className="strategy-workbench-live-time-analysis-card strategy-workbench-live-professional-table-card--full">
                                  <div className="strategy-workbench-live-time-analysis-card-title">Top 回撤区间</div>
                                  <div className="strategy-workbench-live-time-analysis-table-scroll">
                                    <table className="strategy-workbench-live-time-analysis-table">
                                      <thead>
                                        <tr>
                                          <th>开始峰值</th>
                                          <th>谷底</th>
                                          <th>恢复</th>
                                          <th>深度</th>
                                          <th>回撤额</th>
                                          <th>持续</th>
                                          <th>状态</th>
                                        </tr>
                                      </thead>
                                      <tbody>
                                        {drawdownEpisodes.length <= 0 ? (
                                          <tr>
                                            <td colSpan={7}>暂无显著回撤区间</td>
                                          </tr>
                                        ) : (
                                          drawdownEpisodes.map((episode) => (
                                            <tr key={episode.key}>
                                              <td>{formatDateTimeShort(episode.startTimestamp)}</td>
                                              <td>{formatDateTimeShort(episode.troughTimestamp)}</td>
                                              <td>{episode.isRecovered ? formatDateTimeShort(episode.recoveryTimestamp) : '未恢复'}</td>
                                              <td className="is-negative">{formatPercentValue(episode.depth, 1)}</td>
                                              <td className="is-negative">{formatSignedNumber(-episode.lossFromPeak, 2)}</td>
                                              <td>{formatDuration(episode.durationMs)}</td>
                                              <td>{episode.isRecovered ? '已恢复' : '进行中'}</td>
                                            </tr>
                                          ))
                                        )}
                                      </tbody>
                                    </table>
                                  </div>
                                </div>
                              </div>
                            </div>
                          ) : null}
                          {liveSummaryTab === 'overview' ? (
                        <div className="strategy-workbench-live-summary-extra">
                          <span>样本覆盖 {loadedDataDays.toFixed(1)} 天</span>
                          <span>交易汇总: 胜/负 {tradeSummary.winCount}/{tradeSummary.lossCount}，手续费 {formatNumberValue(tradeSummary.totalFee)}</span>
                          <span>资金曲线: 点数 {equitySummary.pointCount}，最高权益 {formatNumberValue(equitySummary.maxEquity)}</span>
                          <span>资金波动: 最大区间盈利 {formatSignedNumber(equitySummary.maxPeriodProfit)}，最大区间亏损 {formatSignedNumber(equitySummary.maxPeriodLoss)}</span>
                          <span>事件: 总数 {eventSummary.totalCount}，类型 {topEventTypesText}</span>
                          <span>资金费累计 {formatSignedNumber(tradeSummary.totalFunding)}</span>
                        </div>
                          ) : null}
                          {liveSummaryTab === 'log' ? (
                            <div className="strategy-workbench-live-log-grid">
                        <div className="strategy-workbench-live-events">
                          <div className="strategy-workbench-live-events-title">最近事件（6条）</div>
                          {recentEvents.length <= 0 ? (
                            <div className="strategy-workbench-live-events-empty">暂无事件记录</div>
                          ) : (
                            recentEvents.map((event, index) => (
                              <div className="strategy-workbench-live-events-line" key={`${event.timestamp}-${index}-${event.type}`}>
                                <span className="strategy-workbench-live-events-time">{formatDateTime(event.timestamp)}</span>
                                <span className="strategy-workbench-live-events-type">{event.type}</span>
                                <span className="strategy-workbench-live-events-message">{event.message}</span>
                              </div>
                            ))
                          )}
                        </div>
                        <div className="strategy-workbench-live-diagnostics">
                          <div className="strategy-workbench-live-diagnostics-title">回测阶段诊断（8条）</div>
                          {visibleDiagnostics.length <= 0 ? (
                            <div className="strategy-workbench-live-diagnostics-empty">暂无诊断日志</div>
                          ) : (
                            visibleDiagnostics.map((line, index) => (
                              <div className="strategy-workbench-live-diagnostics-line" key={`${index}-${line}`}>
                                {line}
                              </div>
                            ))
                          )}
                        </div>
                            </div>
                          ) : null}
                        </div>
                      </div>

                      {workbenchLayoutMode !== 'backtest' ? (
                        <>
                          <ResizeHandle
                            className="strategy-workbench-resize-handle strategy-workbench-resize-handle--live-preview"
                            onResize={handleLivePreviewResize}
                          />
                          {liveListPanel}
                        </>
                      ) : null}
                    </div>
                  </>
                )}
              </div>
            </div>

            {workbenchLayoutMode === 'backtest' ? (
              <div className="strategy-workbench-backtest-side">
                {liveListPanel}
              </div>
            ) : null}

            <ResizeHandle
              className="strategy-workbench-resize-handle strategy-workbench-resize-handle--main"
              onResize={handleMainResize}
            />

            <div className="strategy-workbench-right" ref={rightPanelRef}>
              <div
                className={`strategy-workbench-right-left strategy-workbench-right-left--resizable ${rightPanelHeightCustomized ? '' : 'is-auto-height'}`}
                ref={rightLeftRef}
              >
                <div className="strategy-workbench-card strategy-workbench-card--panel">
                  <div className="strategy-workbench-card-header">
                    <span className="strategy-workbench-card-title">已选指标</span>
                    <button
                      type="button"
                      className="strategy-indicator-add"
                      onClick={onOpenIndicatorGenerator}
                    >
                      新建指标
                    </button>
                  </div>

                  {selectedIndicators.length === 0 ? (
                    <div className="strategy-indicator-empty">暂无指标，点击“新建指标”开始配置。</div>
                  ) : (
                    <div className="strategy-indicator-list strategy-indicator-list--workbench">
                      {selectedIndicators.map((indicator) => {
                        const group = indicatorGroupMap.get(indicator.id);
                        const options = group?.options || [];
                        const config = indicator.config as { input?: unknown };
                        const inputSource =
                          typeof config.input === 'string' && config.input.trim()
                            ? config.input.trim()
                            : '-';
                        const color = indicatorColorMap.get(indicator.id) || '#93C5FD';
                        return (
                          <DroppableSlot
                            key={indicator.id}
                            id={toIndicatorInputDropId(indicator.id)}
                            className="strategy-indicator-item strategy-indicator-item--colorful"
                            style={{
                              background: rgba(color, 0.14),
                              borderColor: rgba(color, 0.45),
                            }}
                          >
                            <div className="strategy-indicator-layout">
                              <div className="strategy-indicator-head-row">
                                <div className="strategy-indicator-title-inline">
                                  <div className="strategy-indicator-name">{formatIndicatorName(indicator)}</div>
                                  <span className="strategy-indicator-input-value">{inputSource}</span>
                                </div>
                                <div className="strategy-indicator-actions strategy-indicator-actions--compact">
                                  <button
                                    type="button"
                                    className="strategy-indicator-action"
                                    onClick={() => onEditIndicator(indicator.id)}
                                  >
                                    编辑
                                  </button>
                                  <button
                                    type="button"
                                    className="strategy-indicator-action strategy-indicator-remove"
                                    onClick={() => onRemoveIndicator(indicator.id)}
                                  >
                                    删除
                                  </button>
                                </div>
                              </div>
                              <div className="strategy-indicator-output-row">
                                <span className="strategy-indicator-row-label">输出</span>
                                <div className="strategy-indicator-output-list">
                                  {options.length === 0 ? (
                                    <span className="strategy-indicator-empty-output">-</span>
                                  ) : (
                                    options.map((option, outputIndex) => (
                                      <DraggableToken
                                        key={option.id}
                                        id={`drag-output|${option.id}`}
                                        payload={{
                                          kind: 'output',
                                          valueId: option.id,
                                          label:
                                            stripParenthetical(option.label) ||
                                            stripParenthetical(option.fullLabel) ||
                                            option.label,
                                        }}
                                        className={`strategy-indicator-output-chip ${isLinkedActive(option.id) ? 'is-linked-active' : ''}`}
                                        style={outputBadgeStyleMap.get(option.id) || {
                                          background: rgba(color, 0.24 + outputIndex * 0.08),
                                          borderColor: rgba(color, 0.62),
                                          color: '#0f172a',
                                        }}
                                        onMouseEnter={() => setActiveHoverValueId(option.id)}
                                        onMouseLeave={() => setActiveHoverValueId('')}
                                      >
                                        {stripParenthetical(option.label) || option.label}
                                      </DraggableToken>
                                    ))
                                  )}
                                </div>
                              </div>
                            </div>
                          </DroppableSlot>
                        );
                      })}
                    </div>
                  )}

                  {fieldOutputGroup && fieldOutputGroup.options.length > 0 ? (
                    <div className="strategy-indicator-field-zone">
                      <div className="strategy-indicator-field-title">K线字段（可拖拽）</div>
                      <div className="strategy-indicator-output-list">
                        <DraggableToken
                          id="drag-number|constant"
                          payload={{
                            kind: 'number',
                            label: '数值',
                          }}
                          className="strategy-indicator-output-chip strategy-indicator-output-chip--number"
                        >
                          数值
                        </DraggableToken>
                        {fieldOutputGroup.options.map((option) => (
                          <DraggableToken
                            key={option.id}
                            id={`drag-output|${option.id}`}
                            payload={{
                              kind: 'output',
                              valueId: option.id,
                              label: option.fullLabel || option.label,
                            }}
                            className={`strategy-indicator-output-chip strategy-indicator-output-chip--field ${isLinkedActive(option.id) ? 'is-linked-active' : ''}`}
                            onMouseEnter={() => setActiveHoverValueId(option.id)}
                            onMouseLeave={() => setActiveHoverValueId('')}
                          >
                            {option.label}
                          </DraggableToken>
                        ))}
                      </div>
                    </div>
                  ) : null}
                </div>

                <ResizeHandleVertical
                  className="strategy-workbench-resize-handle strategy-workbench-resize-handle--vertical strategy-workbench-resize-handle--right-left"
                  onResize={handleRightLeftVerticalResize}
                />

                <div className="strategy-workbench-card strategy-workbench-card--operator">
                  <div className="strategy-workbench-card-header">
                    <span className="strategy-workbench-card-title">快捷操作符</span>
                  </div>

                  <div className="strategy-workbench-operator-list">
                    {operatorGroups.map((group) => (
                      <div className="strategy-workbench-operator-group" key={group.key}>
                        <div className="strategy-workbench-operator-group-title-wrap">
                          <DraggableToken
                            id={`drag-category|${group.key}`}
                            payload={{
                              kind: 'category',
                              category: group.key,
                              label: group.label,
                            }}
                            className="strategy-workbench-operator-group-title strategy-workbench-operator-group-title--drag"
                          >
                            {group.label}
                          </DraggableToken>
                        </div>
                        <div className="strategy-workbench-operator-chips">
                          {group.methods.map((method) => (
                            <DraggableToken
                              key={method.value}
                              id={`drag-method|${method.value}`}
                              payload={{
                                kind: 'method',
                                method: method.value,
                                label: method.label,
                                category: method.category || group.key,
                              }}
                              className="strategy-workbench-operator-chip strategy-workbench-operator-chip--drag"
                            >
                              {methodShortLabel(method.label, method.value)}
                            </DraggableToken>
                          ))}
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              </div>

              <ResizeHandle
                className="strategy-workbench-resize-handle strategy-workbench-resize-handle--right"
                onResize={handleRightResize}
              />

              <div className="strategy-workbench-right-right">
                <div className="strategy-workbench-card">
                  <div className="strategy-workbench-card-header">
                    <span className="strategy-workbench-card-title">策略条件编辑器</span>
                  </div>
                  <div className="strategy-condition-side-tabs">
                    <button
                      type="button"
                      className={`strategy-condition-side-tab strategy-condition-side-tab--long ${conditionSide === 'long' ? 'is-active' : ''}`}
                      onClick={() => setConditionSide('long')}
                    >
                      多头操作
                    </button>
                    <button
                      type="button"
                      className={`strategy-condition-side-tab strategy-condition-side-tab--short ${conditionSide === 'short' ? 'is-active' : ''}`}
                      onClick={() => setConditionSide('short')}
                    >
                      空头操作
                    </button>
                  </div>
                  <div className="strategy-condition-section">
                    <div className="strategy-condition-grid">
                      {containers.map((container) => renderContainer(container))}
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>
        )}

        <KlineOfflineCacheDialog
          open={showOfflineCacheDialog}
          onClose={() => setShowOfflineCacheDialog(false)}
        />
        {ready && floatingOverlay}
      </div>

      {activeDrag && dragCursor ? (
        <div
          className="strategy-workbench-pointer-ghost"
          style={{
            left: `${dragCursor.x}px`,
            top: `${dragCursor.y}px`,
          }}
        >
          <div
            className={`${activeDrag.previewClassName || ''} strategy-workbench-pointer-ghost-item`}
            style={activeDrag.previewStyle}
          >
            {activeDrag.previewText || activeDrag.label}
          </div>
        </div>
      ) : null}
    </DndContext>,
    document.body,
  );
};

export default StrategyWorkbench;

