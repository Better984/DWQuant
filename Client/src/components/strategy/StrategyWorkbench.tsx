import React, { useEffect, useMemo, useState } from 'react';
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
  type LocalBacktestSummary,
} from './localBacktestEngine';
import StrategyWorkbenchKline from './StrategyWorkbenchKline';
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
}

type DashboardMode = 'settings' | 'preview';
type BacktestRangeMode = 'latest_30d' | 'custom';

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
  const [backtestParamError, setBacktestParamError] = useState('');
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
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 6 } }));

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
  const MAX_VISIBLE_TRADES = 6;
  const visiblePreviewTrades = previewTrades.slice(0, MAX_VISIBLE_TRADES);
  const foldedTradeCount = Math.max(0, previewTrades.length - visiblePreviewTrades.length);
  const livePositionCount = previewTrades.length;
  const liveWinRate = localBacktestSummary.winRate;
  const liveAveragePnl = averageClosedPnl;
  const backtestStats = localBacktestSummary.stats;
  const tradeSummary = localBacktestSummary.tradeSummary;
  const equitySummary = localBacktestSummary.equitySummary;
  const eventSummary = localBacktestSummary.eventSummary;
  const recentEvents = useMemo(
    () => [...(localBacktestSummary.events || [])].slice(-8).reverse(),
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
  const visibleDiagnostics = useMemo(
    () => (Array.isArray(localBacktestSummary.diagnostics) ? localBacktestSummary.diagnostics : []).slice(0, 14),
    [localBacktestSummary.diagnostics],
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
          <button type="button" className="strategy-workbench-topbar-button" onClick={onClose}>
            返回
          </button>
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
          <div className="strategy-workbench-main">
            <div className="strategy-workbench-left">
              <div className="strategy-workbench-card">
                <div className="strategy-workbench-card-header">
                  <span className="strategy-workbench-card-title">K线图</span>
                  <span className="strategy-workbench-card-meta">已联动指标：{selectedIndicators.length}</span>
                </div>
                <div className="strategy-workbench-kline-wrap">
                  <StrategyWorkbenchKline
                    exchange={selectedExchange}
                    symbol={selectedSymbol}
                    timeframeSec={selectedTimeframeSec}
                    selectedIndicators={selectedIndicators}
                    hoverValueId={activeHoverValueId}
                    hoverHasReference={activeHoverHasReference}
                    onBarsUpdate={setKlineBars}
                  />
                </div>
              </div>

              <div className="strategy-workbench-card">
                <div className="strategy-workbench-card-header">
                  <span className="strategy-workbench-card-title">仪表盘</span>
                  <div className="strategy-workbench-dashboard-header-actions">
                    <span className="strategy-workbench-card-meta">
                      {isBacktestRunning
                        ? `运行中 · ${progressPercent}% · 检测至 ${detectedTimeText}`
                        : `${headerRunText} · 检测至 ${detectedTimeText}`}
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

                    <div
                      className={`strategy-workbench-detect-status ${backtestParamError ? 'is-blocked' : 'is-ready'}`}
                    >
                      {backtestParamError || '参数已就绪，点击“确认并预览”后进入回测结果与仓位列表。'}
                    </div>
                  </>
                ) : (
                  <>
                    <div
                      className={`strategy-workbench-detect-status ${localBacktestSummary.status === 'running' ? 'is-ready' : 'is-blocked'}`}
                    >
                      {localBacktestSummary.message}
                      {' · '}
                      已检查 {backtestProgress.processedBars}/{backtestProgress.totalBars || localBacktestSummary.bars} 根 K 线
                      {' · '}
                      当前检测时间 {detectedTimeText}
                    </div>

                    <div className="strategy-workbench-live-preview">
                      <div className="strategy-workbench-live-summary">
                        <div className="strategy-workbench-live-stat-grid">
                          <div className="strategy-workbench-live-stat">
                            <span>仓位</span>
                            <strong>{livePositionCount}</strong>
                          </div>
                          <div className="strategy-workbench-live-stat">
                            <span>平仓笔数</span>
                            <strong>{tradeSummary.totalCount}</strong>
                          </div>
                          <div className="strategy-workbench-live-stat">
                            <span>胜率</span>
                            <strong>{formatPercentValue(liveWinRate)}</strong>
                          </div>
                          <div className="strategy-workbench-live-stat">
                            <span>平均盈亏</span>
                            <strong>{formatSignedNumber(liveAveragePnl)}</strong>
                          </div>
                          <div className="strategy-workbench-live-stat">
                            <span>累计收益</span>
                            <strong>{formatSignedNumber(localBacktestSummary.totalProfit)}</strong>
                          </div>
                          <div className="strategy-workbench-live-stat">
                            <span>最大回撤</span>
                            <strong>{formatPercentValue(backtestStats.maxDrawdown)}</strong>
                          </div>
                        </div>
                        <div className="strategy-workbench-live-progress">
                          <div className="strategy-workbench-live-progress-label">
                            回测进度 {backtestProgress.processedBars}/{backtestProgress.totalBars || localBacktestSummary.bars}
                          </div>
                          <div className="strategy-workbench-live-progress-track">
                            <div
                              className="strategy-workbench-live-progress-fill"
                              style={{ width: `${progressPercent}%` }}
                            />
                          </div>
                          <div className="strategy-workbench-live-progress-meta">
                            <span>{progressPercent}%</span>
                            <span>耗时 {formatDuration(backtestProgress.elapsedMs)}</span>
                            <span>{backtestProgress.done ? '已完成' : '进行中'}</span>
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
                        <div className="strategy-workbench-live-summary-extra">
                          <span>样本覆盖 {loadedDataDays.toFixed(1)} 天</span>
                          <span>交易汇总: 胜/负 {tradeSummary.winCount}/{tradeSummary.lossCount}，手续费 {formatNumberValue(tradeSummary.totalFee)}</span>
                          <span>资金曲线: 点数 {equitySummary.pointCount}，最高权益 {formatNumberValue(equitySummary.maxEquity)}</span>
                          <span>资金波动: 最大区间盈利 {formatSignedNumber(equitySummary.maxPeriodProfit)}，最大区间亏损 {formatSignedNumber(equitySummary.maxPeriodLoss)}</span>
                          <span>事件: 总数 {eventSummary.totalCount}，类型 {topEventTypesText}</span>
                          <span>资金费累计 {formatSignedNumber(tradeSummary.totalFunding)}</span>
                        </div>
                        <div className="strategy-workbench-live-events">
                          <div className="strategy-workbench-live-events-title">最近事件</div>
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
                          <div className="strategy-workbench-live-diagnostics-title">回测阶段诊断</div>
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

                      <div className="strategy-workbench-live-list">
                        <div className="strategy-workbench-live-list-header">
                          <span>仓位列表</span>
                          <span>{previewTrades.length} 条</span>
                        </div>

                        {previewTrades.length === 0 ? (
                          <div className="strategy-workbench-live-empty">
                            暂无仓位记录，回测执行后会实时出现在右侧列表。
                          </div>
                        ) : (
                          visiblePreviewTrades.map((trade, index) => (
                            <div
                              key={`${trade.entryTime}-${trade.exitTime}-${trade.side}-${index}`}
                              className={`strategy-workbench-live-item ${trade.isOpen ? 'is-open' : ''}`}
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
                          ))
                        )}
                        {foldedTradeCount > 0 ? (
                          <div className="strategy-workbench-live-folded-hint">
                            为保持单屏展示，已折叠较早仓位 {foldedTradeCount} 条（保留最近 {visiblePreviewTrades.length} 条）。
                          </div>
                        ) : null}
                      </div>
                    </div>
                  </>
                )}
              </div>
            </div>

            <div className="strategy-workbench-right">
              <div className="strategy-workbench-right-left">
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

