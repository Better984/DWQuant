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

import type { GeneratedIndicatorPayload } from '../indicator/IndicatorGeneratorSelector';
import type {
  ConditionContainer,
  ConditionItem,
  IndicatorOutputGroup,
  MethodOption,
  TimeframeOption,
  TradeOption,
} from './StrategyModule.types';
import StrategyWorkbenchKline from './StrategyWorkbenchKline';
import './StrategyWorkbench.css';

type DropSlot = 'left' | 'method' | 'right';
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
  slot: 'left' | 'right';
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
  onQuickUpdateIndicatorInput: (indicatorId: string, fieldValueId: string) => void;
  onQuickEditIndicatorParams: (indicatorId: string) => void;
  onQuickCreateCondition: (containerId: string, groupId: string | null, method: string) => void;
}

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

const methodShortLabel = (label: string, fallback: string) => {
  const matched = normalizeText(label).match(/^(.+?)\s*\(/);
  return normalizeText(matched?.[1]) || normalizeText(label) || fallback;
};

const stripParenthetical = (value?: string) =>
  normalizeText((value || '').replace(/\s*[\(\（][^\)\）]*[\)\）]/g, '').replace(/\s{2,}/g, ' '));

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
  if (slot !== 'left' && slot !== 'method' && slot !== 'right') {
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

const formatClock = (timestamp: number) => {
  const date = new Date(timestamp);
  const pad2 = (value: number) => value.toString().padStart(2, '0');
  return `${pad2(date.getHours())}:${pad2(date.getMinutes())}:${pad2(date.getSeconds())}`;
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
    onQuickUpdateIndicatorInput,
    onQuickEditIndicatorParams,
    onQuickCreateCondition,
  } = props;

  const [ready, setReady] = useState(false);
  const [conditionSide, setConditionSide] = useState<'long' | 'short'>('long');
  const [expandedGroupId, setExpandedGroupId] = useState<string | null>(null);
  const [expandedConditionKey, setExpandedConditionKey] = useState<string | null>(null);
  const [dashboardClock, setDashboardClock] = useState(Date.now());
  const [realtimeTicks, setRealtimeTicks] = useState(0);
  const [activeDrag, setActiveDrag] = useState<DragPayload | null>(null);
  const [dragCursor, setDragCursor] = useState<{ x: number; y: number } | null>(null);
  const [focusRightNumberKey, setFocusRightNumberKey] = useState('');
  const [activeHoverValueId, setActiveHoverValueId] = useState('');
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 6 } }));

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

  const indicatorOutputCount = useMemo(() => {
    return indicatorOutputGroups
      .filter((group) => group.id !== 'kline-fields')
      .reduce((sum, group) => sum + group.options.length, 0);
  }, [indicatorOutputGroups]);

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

  const conditionCount = useMemo(() => {
    return [...logicContainers, ...filterContainers].reduce((sum, container) => {
      return sum + container.groups.reduce((groupSum, group) => groupSum + group.conditions.length, 0);
    }, 0);
  }, [logicContainers, filterContainers]);

  const validRiskConfig =
    takeProfitPct > 0 && stopLossPct > 0 && leverage > 0 && orderQty > 0;

  useEffect(() => {
    if (!ready || !validRiskConfig) {
      return;
    }
    const timer = window.setInterval(() => {
      setDashboardClock(Date.now());
      setRealtimeTicks((prev) => prev + 1);
    }, 1000);
    return () => {
      window.clearInterval(timer);
    };
  }, [ready, validRiskConfig]);

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
    return compactValueLabelMap.get(valueId) || valueLabelMap.get(valueId) || valueId;
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

    if (payload.kind === 'output' && (conditionDrop.slot === 'left' || conditionDrop.slot === 'right')) {
      onQuickAssignConditionValue(
        conditionDrop.containerId,
        conditionDrop.groupId,
        conditionDrop.conditionId,
        conditionDrop.slot,
        payload.valueId,
      );
      return;
    }
    if (payload.kind === 'condition-value' && (conditionDrop.slot === 'left' || conditionDrop.slot === 'right')) {
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
      onQuickAssignConditionValue(
        conditionDrop.containerId,
        conditionDrop.groupId,
        conditionDrop.conditionId,
        conditionDrop.slot,
        payload.valueId,
        payload.source,
      );
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
    const canDropRight = argsCount >= 2 && rightMode !== 'number';
    const canUseRightNumber = argsCount >= 2 && rightMode !== 'field';
    const numberFocusKey = `${containerId}|${groupId}|${condition.id}`;
    const conditionRowKey = `${containerId}|${groupId}|${condition.id}`;
    const isConditionExpanded = expandedConditionKey === conditionRowKey;
    const leftLinkedActive = isLinkedActive(condition.leftValueId);
    const rightLinkedActive = condition.rightValueType === 'field' && isLinkedActive(condition.rightValueId);
    const rightCandidateCount = Array.from(valueLabelMap.keys()).filter(
      (valueId) => valueId !== condition.leftValueId,
    ).length;
    const rightNoTargetHint =
      canDropRight && rightCandidateCount === 0
        ? '当前没有可用右值'
        : '';

    const rightText =
      argsCount < 2
        ? '当前方法仅需左值'
        : condition.rightValueType === 'number'
          ? `数值 ${condition.rightNumber || '未填写'}`
          : renderValueText(condition.rightValueId);

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
            <div className="condition-item-dnd-row">
              <DroppableSlot
                id={toDropId(containerId, groupId, condition.id, 'left')}
                className="condition-dnd-slot"
              >
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

              <DroppableSlot
                id={toDropId(containerId, groupId, condition.id, 'method')}
                className="condition-dnd-slot condition-dnd-slot--operator"
              >
                <span className="condition-dnd-value">{methodBriefLabel}</span>
              </DroppableSlot>

              <DroppableSlot
                id={toDropId(containerId, groupId, condition.id, 'right')}
                className="condition-dnd-slot"
                disabled={!canDropRight}
              >
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
                ) : (
                  condition.rightValueType === 'field' && condition.rightValueId ? (
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
                  )
                )}
              </DroppableSlot>
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

  const metricRows = [
    { label: '本地时钟', value: formatClock(dashboardClock) },
    { label: '已选指标', value: selectedIndicators.length.toString() },
    { label: '指标输出', value: indicatorOutputCount.toString() },
    { label: '条件总数', value: conditionCount.toString() },
    { label: '止盈%', value: takeProfitPct.toFixed(2) },
    { label: '止损%', value: stopLossPct.toFixed(2) },
    { label: '杠杆', value: leverage.toString() },
    { label: '开仓量', value: orderQty.toString() },
    { label: '实时检测Tick', value: realtimeTicks.toString() },
    { label: '模式', value: validRiskConfig ? '已启用' : '待参数完善' },
    { label: '交易所', value: exchangeLabel },
    { label: '周期', value: timeframeLabel },
  ];

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
            <div className="strategy-workbench-title">策略指标工作台</div>
            <div className="strategy-workbench-subtitle">
              {exchangeLabel} · {symbolLabel} · {timeframeLabel}
            </div>
          </div>
          <button
            type="button"
            className="strategy-workbench-topbar-button is-primary"
            onClick={onOpenExport}
          >
            导出
          </button>
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
                    enableRealtime={validRiskConfig}
                    hoverValueId={activeHoverValueId}
                    hoverHasReference={activeHoverHasReference}
                  />
                </div>
              </div>

              <div className="strategy-workbench-card">
                <div className="strategy-workbench-card-header">
                  <span className="strategy-workbench-card-title">仪表盘</span>
                  <span className="strategy-workbench-card-meta">
                    {validRiskConfig ? '参数完整，实时检测已启用' : '请先填写止盈/止损/杠杆/数量'}
                  </span>
                </div>

                <div className="strategy-workbench-risk-form">
                  <label className="strategy-workbench-risk-field">
                    <span>止盈比例(%)</span>
                    <input
                      type="number"
                      step={0.1}
                      min={0}
                      value={takeProfitPct}
                      onChange={(event) => {
                        const next = parseNumberOrNull(event.target.value);
                        if (next !== null) {
                          onTakeProfitPctChange(next);
                        }
                      }}
                    />
                  </label>
                  <label className="strategy-workbench-risk-field">
                    <span>止损比例(%)</span>
                    <input
                      type="number"
                      step={0.1}
                      min={0}
                      value={stopLossPct}
                      onChange={(event) => {
                        const next = parseNumberOrNull(event.target.value);
                        if (next !== null) {
                          onStopLossPctChange(next);
                        }
                      }}
                    />
                  </label>
                  <label className="strategy-workbench-risk-field">
                    <span>杠杆倍数</span>
                    <input
                      type="number"
                      step={1}
                      min={1}
                      value={leverage}
                      onChange={(event) => {
                        const next = parseNumberOrNull(event.target.value);
                        if (next !== null) {
                          onLeverageChange(next);
                        }
                      }}
                    />
                  </label>
                  <label className="strategy-workbench-risk-field">
                    <span>单次开仓数量</span>
                    <input
                      type="number"
                      step={0.001}
                      min={0}
                      value={orderQty}
                      onChange={(event) => {
                        const next = parseNumberOrNull(event.target.value);
                        if (next !== null) {
                          onOrderQtyChange(next);
                        }
                      }}
                    />
                  </label>
                </div>

                <div
                  className={`strategy-workbench-detect-status ${validRiskConfig ? 'is-ready' : 'is-blocked'}`}
                >
                  {validRiskConfig
                    ? '参数已就绪，工作台正在进行本地实时检测。'
                    : '参数未就绪：请填写止盈、止损、杠杆、开仓数量后再进行实时检测。'}
                </div>

                <div className="strategy-workbench-dashboard-grid">
                  {metricRows.map((metric) => (
                    <div className="strategy-workbench-metric" key={metric.label}>
                      <div className="strategy-workbench-metric-label">{metric.label}</div>
                      <div className="strategy-workbench-metric-value">{metric.value}</div>
                    </div>
                  ))}
                </div>
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
