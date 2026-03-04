import React, { useMemo, useState, useRef, useEffect } from 'react';

import BinanceIcon from '../../assets/icons/cex/Binance.svg';
import BitgetIcon from '../../assets/icons/cex/bitget.svg';
import OkxIcon from '../../assets/icons/cex/OKX.svg';
import BnbIcon from '../../assets/icons/crypto/BNB.svg';
import BtcIcon from '../../assets/icons/crypto/BTC.svg';
import DogeIcon from '../../assets/icons/crypto/DOGE.svg';
import EthIcon from '../../assets/icons/crypto/ETH.svg';
import SolIcon from '../../assets/icons/crypto/SOL.svg';
import XrpIcon from '../../assets/icons/crypto/XRP.svg';
import IndicatorGeneratorSelector, { type GeneratedIndicatorPayload } from '../indicator/IndicatorGeneratorSelector';
import ConditionEditorDialog from './ConditionEditorDialog';
import StrategyConfigDialog from './StrategyConfigDialog';
import StrategyWorkbench from './StrategyWorkbench';
import type {
  ActionSetConfig,
  ConditionContainer,
  ConditionEditTarget,
  ConditionGroup,
  ConditionGroupConfig,
  ConditionGroupSetConfig,
  ConditionItem,
  ConditionSummarySection,
  IndicatorOutputGroup,
  MethodOption,
  StrategyConfig,
  StrategyLogicBranchConfig,
  StrategyMethodConfig,
  StrategyRuntimeCustomConfig,
  StrategyRuntimeConfig,
  StrategyRuntimeTemplateConfig,
  StrategyTradeConfig,
  StrategyValueRef,
  TimeframeOption,
  TradeOption,
  ValueOption,
} from './StrategyModule.types';
import { Dialog, useNotification } from '../ui/index.ts';
import { HttpClient, getToken } from '../../network/index.ts';
import {
  buildConditionFingerprint,
  validateConditionArgsSemantics,
} from './strategyConditionGuard';

export type StrategyEditorSubmitPayload = {
  name: string;
  description: string;
  configJson: StrategyConfig;
  exchangeApiKeyId?: number | null;
};

type StrategyEditorFlowProps = {
  onSubmit: (payload: StrategyEditorSubmitPayload) => Promise<void>;
  onClose?: () => void;
  submitLabel?: string;
  successMessage?: string;
  errorMessage?: string;
  initialName?: string;
  initialDescription?: string;
  initialTradeConfig?: StrategyTradeConfig;
  initialConfig?: StrategyConfig;
  initialExchangeApiKeyId?: number | null;
  disableMetaFields?: boolean;
  openConfigDirectly?: boolean;
};

type IndicatorDialogMode = 'create' | 'edit';

type IndicatorUsageItem = {
  id: string;
  containerTitle: string;
  groupTitle: string;
  positionLabel: string;
  outputLabel: string;
};

type IndicatorActionState = {
  action: 'edit' | 'remove' | 'quick-input' | 'quick-param-edit';
  indicator: GeneratedIndicatorPayload;
  usages: IndicatorUsageItem[];
  pendingIndicator?: GeneratedIndicatorPayload;
  pendingInputLabel?: string;
};

type ExchangeApiKeyItem = {
  id: number;
  exchangeType: string;
  label: string;
};

type StrategyEditSnapshot = {
  selectedIndicators: GeneratedIndicatorPayload[];
  conditionContainers: ConditionContainer[];
};

type StrategyHistoryRecord = {
  before: StrategyEditSnapshot;
  after: StrategyEditSnapshot;
  undoMessage: string;
  redoMessage: string;
};

type ConfigReviewData = {
  configJson: StrategyConfig;
  logicPreview: string;
  usedIndicatorOutputs: string[];
  conditionSummarySections: ConditionSummarySection[];
};

const KLINE_FIELD_DEFINITIONS = [
  { key: 'OPEN', label: '开盘价', hint: 'Open' },
  { key: 'HIGH', label: '最高价', hint: 'High' },
  { key: 'LOW', label: '最低价', hint: 'Low' },
  { key: 'CLOSE', label: '收盘价', hint: 'Close' },
  { key: 'VOLUME', label: '成交量', hint: 'Volume' },
  { key: 'HL2', label: '高低均价', hint: 'HL2' },
  { key: 'HLC3', label: '高低收均价', hint: 'HLC3' },
  { key: 'OHLC4', label: '四价均价', hint: 'OHLC4' },
  { key: 'OC2', label: '开收均价', hint: 'OC2' },
  { key: 'HLCC4', label: '高低收收均价', hint: 'HLCC4' },
];

const normalizeFieldKey = (raw?: string) => (raw || '').trim().toUpperCase();

const FIELD_KEY_TO_INPUT_VALUE: Record<string, string> = {
  OPEN: 'Open',
  HIGH: 'High',
  LOW: 'Low',
  CLOSE: 'Close',
  VOLUME: 'Volume',
  HL2: 'HL2',
  HLC3: 'HLC3',
  OHLC4: 'OHLC4',
  OC2: 'OC2',
  HLCC4: 'HLCC4',
};

const buildFieldValueId = (input?: string) => {
  const key = normalizeFieldKey(input);
  return key ? `field:${key}` : '';
};

const parseFieldValueId = (valueId?: string) => {
  const normalized = (valueId || '').trim().toUpperCase();
  if (!normalized.startsWith('FIELD:')) {
    return '';
  }
  return normalizeFieldKey(normalized.slice('FIELD:'.length));
};

const removeParentheticalText = (value?: string) => {
  return (value || '')
    .replace(/\s*[\(\（][^\)\）]*[\)\）]/g, '')
    .replace(/\s{2,}/g, ' ')
    .trim();
};

const deepCloneJson = <T,>(value: T): T => JSON.parse(JSON.stringify(value)) as T;

const isEditableElementTarget = (target: EventTarget | null) => {
  if (!(target instanceof HTMLElement)) {
    return false;
  }
  return Boolean(target.closest('input, textarea, select, [contenteditable="true"]'));
};

const normalizeTimeframeToken = (raw?: string) => {
  const value = (raw || '').trim().toLowerCase().replace(/\s+/g, '');
  if (!value) {
    return '';
  }
  if (/^\d+mo$/.test(value)) {
    return value;
  }
  if (/^mo\d+$/.test(value)) {
    return `${value.slice(2)}mo`;
  }
  if (/^\d+[mhdw]$/.test(value)) {
    return value;
  }
  if (/^[mhdw]\d+$/.test(value)) {
    return `${value.slice(1)}${value[0]}`;
  }
  return value;
};

const timeframeLabelFromSeconds = (timeframeSec?: number) => {
  return timeframeSec === 180 ? '3m'
    : timeframeSec === 300 ? '5m'
    : timeframeSec === 900 ? '15m'
    : timeframeSec === 1800 ? '30m'
    : timeframeSec === 3600 ? '1h'
    : timeframeSec === 7200 ? '2h'
    : timeframeSec === 14400 ? '4h'
    : timeframeSec === 21600 ? '6h'
    : timeframeSec === 28800 ? '8h'
    : timeframeSec === 43200 ? '12h'
    : timeframeSec === 86400 ? '1d'
    : timeframeSec === 259200 ? '3d'
    : timeframeSec === 604800 ? '1w'
    : timeframeSec === 2592000 ? '1mo'
    : '1m';
};

const resolveReferenceTimeframe = (raw?: string, fallback?: string) => {
  const normalized = normalizeTimeframeToken(raw);
  if (normalized) {
    return normalized;
  }
  return normalizeTimeframeToken(fallback);
};

const buildIndicatorRefKey = (ref: StrategyValueRef, fallbackTimeframe?: string) => {
  const paramsKey = (ref.params || []).join(',');
  const timeframe = resolveReferenceTimeframe(ref.timeframe, fallbackTimeframe);
  return `${ref.indicator}|${timeframe}|${ref.input}|${ref.output}|${paramsKey}`;
};

const normalizeConditionMethod = (raw?: string) => {
  const value = (raw || '').trim();
  if (!value) {
    return '';
  }
  if (value === 'CrossOver') {
    return 'CrossUp';
  }
  if (value === 'CrossUnder') {
    return 'CrossDown';
  }
  return value;
};

const FILTER_CONTAINER_IDS = new Set(['open-long-filter', 'open-short-filter']);

const createDefaultConditionContainers = (): ConditionContainer[] => ([
  { id: 'open-long-filter', title: '开多筛选器', enabled: false, required: false, groups: [] },
  { id: 'open-short-filter', title: '开空筛选器', enabled: false, required: false, groups: [] },
  { id: 'open-long', title: '开多条件', enabled: true, required: false, groups: [] },
  { id: 'open-short', title: '开空条件', enabled: true, required: false, groups: [] },
  { id: 'close-long', title: '平多条件', enabled: true, required: false, groups: [] },
  { id: 'close-short', title: '平空条件', enabled: true, required: false, groups: [] },
]);

const createDefaultTradeConfig = (): StrategyTradeConfig => ({
  exchange: 'binance',
  symbol: 'BTC/USDT',
  timeframeSec: 300,
  positionMode: 'Cross',
  openConflictPolicy: 'GiveUp',
  sizing: {
    orderQty: 0.001,
    maxPositionQty: 10,
    leverage: 100,
  },
  risk: {
    takeProfitPct: 2.0,
    stopLossPct: 1.0,
    trailing: {
      enabled: false,
      activationProfitPct: 1.0,
      closeOnDrawdownPct: 0.2,
    },
  },
});

const RUNTIME_TEMPLATE_OPTIONS: StrategyRuntimeTemplateConfig[] = [
  {
    id: 'cn.a_share.regular',
    name: 'A?????',
    timezone: 'Asia/Shanghai',
    days: ['mon', 'tue', 'wed', 'thu', 'fri'],
    timeRanges: [
      { start: '09:30', end: '11:30' },
      { start: '13:00', end: '15:00' },
    ],
  },
  {
    id: 'us.equity.et.extended',
    name: '???ET??? + ????',
    timezone: 'America/New_York',
    days: ['mon', 'tue', 'wed', 'thu', 'fri'],
    timeRanges: [
      { start: '04:00', end: '09:30' },
      { start: '09:30', end: '16:00' },
    ],
  },
];

const LEGACY_TEMPLATE_NAME_MAP: Record<string, string> = {
  'A?????': 'cn.a_share.regular',
  '??????': 'us.equity.et.extended',
  '???ET??? + ????': 'us.equity.et.extended',
};


const RUNTIME_TIMEZONE_OPTIONS = [
  { value: 'Asia/Shanghai', label: '中国/上海 (UTC+8)' },
  { value: 'America/New_York', label: '美国/纽约 (UTC-5/-4)' },
  { value: 'UTC', label: 'UTC' },
];

const resolveLocalTimezone = () => {
  if (typeof Intl === 'undefined' || typeof Intl.DateTimeFormat !== 'function') {
    return 'Asia/Shanghai';
  }
  const timezone = Intl.DateTimeFormat().resolvedOptions().timeZone || 'Asia/Shanghai';
  return RUNTIME_TIMEZONE_OPTIONS.some((option) => option.value === timezone)
    ? timezone
    : 'Asia/Shanghai';
};

const createDefaultRuntimeConfig = (): StrategyRuntimeConfig => ({
  scheduleType: 'Always',
  outOfSessionPolicy: 'BlockEntryAllowExit',
  templateIds: [],
  templates: [],
  custom: {
    mode: 'Deny',
    timezone: resolveLocalTimezone(),
    days: [],
    timeRanges: [],
  },
});

const normalizePositionMode = (raw?: string) => {
  const value = (raw || '').trim().toLowerCase();
  if (!value) {
    return '';
  }
  if (value.includes('cross') || value === '全仓') {
    return 'Cross';
  }
  if (value.includes('isolated') || value.includes('isolate') || value === '逐仓') {
    return 'Isolated';
  }
  if (value.includes('longshort') || value.includes('hedge')) {
    return 'Cross';
  }
  return raw || '';
};

const normalizeRuntimeScheduleType = (raw?: string): StrategyRuntimeConfig['scheduleType'] => {
  const value = (raw || '').trim().toLowerCase();
  if (value === 'template') {
    return 'Template';
  }
  if (value === 'custom') {
    return 'Custom';
  }
  return 'Always';
};

const normalizeTemplateIds = (initial?: StrategyRuntimeConfig): string[] => {
  if (!initial) {
    return [];
  }
  if (Array.isArray(initial.templateIds) && initial.templateIds.length > 0) {
    return initial.templateIds.filter((item) => typeof item === 'string' && item.trim());
  }
  if (Array.isArray(initial.templates)) {
    return initial.templates
      .map((template) => template.id || LEGACY_TEMPLATE_NAME_MAP[template.name] || '')
      .filter((item) => item);
  }
  return [];
};

const normalizeRuntimeCustomMode = (raw?: string): StrategyRuntimeCustomConfig['mode'] => {
  const value = (raw || '').trim().toLowerCase();
  return value === 'allow' ? 'Allow' : 'Deny';
};

const normalizeOutOfSessionPolicy = (
  raw?: string,
): StrategyRuntimeConfig['outOfSessionPolicy'] => {
  const value = (raw || '').trim().toLowerCase();
  return value === 'blockall' ? 'BlockAll' : 'BlockEntryAllowExit';
};

const mergeTradeConfig = (initial?: StrategyTradeConfig): StrategyTradeConfig => {
  const defaults = createDefaultTradeConfig();
  if (!initial) {
    return defaults;
  }

  return {
    ...defaults,
    ...initial,
    positionMode: normalizePositionMode(initial.positionMode) || defaults.positionMode,
    sizing: {
      ...defaults.sizing,
      ...initial.sizing,
    },
    risk: {
      ...defaults.risk,
      ...initial.risk,
      trailing: {
        ...defaults.risk.trailing,
        ...initial.risk?.trailing,
      },
    },
  };
};

const mergeRuntimeConfig = (initial?: StrategyRuntimeConfig): StrategyRuntimeConfig => {
  const defaults = createDefaultRuntimeConfig();
  if (!initial) {
    return defaults;
  }

  return {
    ...defaults,
    ...initial,
    scheduleType: normalizeRuntimeScheduleType(initial.scheduleType),
    outOfSessionPolicy: normalizeOutOfSessionPolicy(initial.outOfSessionPolicy),
    templateIds: normalizeTemplateIds(initial),
    templates: Array.isArray(initial.templates) ? initial.templates : defaults.templates,
    custom: {
      ...defaults.custom,
      ...(initial.custom || {}),
      mode: normalizeRuntimeCustomMode(initial.custom?.mode),
      timezone: initial.custom?.timezone?.trim() || defaults.custom.timezone,
      days: Array.isArray(initial.custom?.days) ? initial.custom?.days || [] : defaults.custom.days,
      timeRanges: Array.isArray(initial.custom?.timeRanges)
        ? initial.custom?.timeRanges || []
        : defaults.custom.timeRanges,
    },
  };
};

// 从 StrategyValueRef 创建 GeneratedIndicatorPayload
const createIndicatorFromRef = (
  ref: StrategyValueRef,
  id: string,
  defaultTimeframe: string,
): GeneratedIndicatorPayload => {
  const params = ref.params || [];
  const timeframe = resolveReferenceTimeframe(ref.timeframe, defaultTimeframe);
  const config = {
    indicator: ref.indicator,
    timeframe,
    input: ref.input,
    params,
    output: ref.output,
    offsetRange: ref.offsetRange || [0, 0],
    calcMode: ref.calcMode || 'OnBarClose',
  };
  return {
    id,
    code: ref.indicator || '',
    name: ref.indicator || '',
    category: 'Loaded',
    outputs: [{ key: ref.output || 'Value' }],
    config,
    configText: JSON.stringify(config),
  };
};

// 从配置中提取所有指标引用
const extractIndicatorsFromConfig = (
  config: StrategyConfig,
  defaultTimeframe: string,
): GeneratedIndicatorPayload[] => {
  const indicatorMap = new Map<string, GeneratedIndicatorPayload>();
  let indicatorCounter = 0;

  const addIndicator = (ref: StrategyValueRef | string | undefined) => {
    if (!ref || typeof ref === 'string') {
      return;
    }
    if ((ref.refType || '').toLowerCase() !== 'indicator') {
      return;
    }
    const key = buildIndicatorRefKey(ref, defaultTimeframe);
    if (!indicatorMap.has(key)) {
      indicatorCounter++;
      indicatorMap.set(key, createIndicatorFromRef(ref, `loaded-${indicatorCounter}`, defaultTimeframe));
    }
  };

  const traverseGroupSet = (groupSet?: ConditionGroupSetConfig) => {
    groupSet?.groups?.forEach((group) => {
      group.conditions?.forEach((condition) => {
        if (condition.args) {
          condition.args.forEach((arg) => {
            if (typeof arg === 'object' && 'refType' in arg) {
              addIndicator(arg as StrategyValueRef);
            }
          });
        }
      });
    });
  };

  // 遍历所有条件
  const traverseBranch = (branch: StrategyLogicBranchConfig) => {
    traverseGroupSet(branch.filters);
    branch.containers?.forEach((container) => {
      container.checks?.groups?.forEach((group) => {
        group.conditions?.forEach((condition) => {
          if (condition.args) {
            condition.args.forEach((arg) => {
              if (typeof arg === 'object' && 'refType' in arg) {
                addIndicator(arg as StrategyValueRef);
              }
            });
          }
        });
      });
    });
  };

  if (config.logic) {
    traverseBranch(config.logic.entry.long);
    traverseBranch(config.logic.entry.short);
    traverseBranch(config.logic.exit.long);
    traverseBranch(config.logic.exit.short);
  }

  return Array.from(indicatorMap.values());
};

// 从配置中解析条件容器
const parseConditionContainersFromConfig = (
  config: StrategyConfig,
  defaultTimeframe: string,
): ConditionContainer[] => {
  const containers: ConditionContainer[] = [
    { id: 'open-long-filter', title: '开多筛选器', enabled: false, required: false, groups: [] },
    { id: 'open-short-filter', title: '开空筛选器', enabled: false, required: false, groups: [] },
    { id: 'open-long', title: '开多条件', enabled: false, required: false, groups: [] },
    { id: 'open-short', title: '开空条件', enabled: false, required: false, groups: [] },
    { id: 'close-long', title: '平多条件', enabled: false, required: false, groups: [] },
    { id: 'close-short', title: '平空条件', enabled: false, required: false, groups: [] },
  ];

  const parseGroupSet = (
    containerId: string,
    groupSet?: ConditionGroupSetConfig,
    enabledFlag?: boolean,
  ) => {
    const container = containers.find((c) => c.id === containerId);
    if (!container || !groupSet || !groupSet.groups) {
      if (container && enabledFlag !== undefined) {
        container.enabled = enabledFlag;
      }
      return;
    }

    const hasAnyCondition = (groupSet.groups || []).some((group) => (group.conditions || []).length > 0);
    container.enabled = enabledFlag ?? groupSet.enabled ?? hasAnyCondition;
    let groupCounter = 0;
    container.groups = groupSet.groups.map((groupConfig, index) => {
      groupCounter++;
      const groupId = `${containerId}-group-${groupCounter}`;
      let conditionCounter = 0;

      const conditions: ConditionItem[] = (groupConfig.conditions || []).map((conditionConfig) => {
        conditionCounter++;
        const conditionId = `${groupId}-condition-${conditionCounter}`;
        const resolvedMethod = normalizeConditionMethod(conditionConfig.method) || 'GreaterThanOrEqual';
        const args = conditionConfig.args || [];
        const paramValues = Array.isArray(conditionConfig.param) ? conditionConfig.param.map((item) => String(item)) : [];
        const leftArg = args[0];
        const rightArg = args[1];
        const extraArg = args[2];

        let leftValueId = '';
        let rightValueType: 'field' | 'number' = 'number';
        let rightValueId: string | undefined;
        let rightNumber: string | undefined;
        let extraValueType: 'field' | 'number' = 'number';
        let extraValueId: string | undefined;
        let extraNumber: string | undefined;

        if (leftArg && typeof leftArg === 'object' && 'refType' in leftArg) {
          const ref = leftArg as StrategyValueRef;
          const refType = (ref.refType || '').toLowerCase();
          if (refType === 'field') {
            leftValueId = buildFieldValueId(ref.input);
          } else if (refType === 'const' || refType === 'number') {
            leftValueId = '';
          } else {
            leftValueId = buildIndicatorRefKey(ref, defaultTimeframe);
          }
        }

        if (rightArg) {
          if (typeof rightArg === 'string') {
            rightValueType = 'number';
            rightNumber = rightArg;
          } else if (typeof rightArg === 'object' && 'refType' in rightArg) {
            const ref = rightArg as StrategyValueRef;
            const refType = (ref.refType || '').toLowerCase();
            if (refType === 'const' || refType === 'number') {
              rightValueType = 'number';
              rightNumber = ref.input?.trim() || '';
            } else if (refType === 'field') {
              rightValueType = 'field';
              rightValueId = buildFieldValueId(ref.input);
            } else {
              rightValueType = 'field';
              rightValueId = buildIndicatorRefKey(ref, defaultTimeframe);
            }
          }
        }

        if (extraArg) {
          if (typeof extraArg === 'string') {
            extraValueType = 'number';
            extraNumber = extraArg;
          } else if (typeof extraArg === 'object' && 'refType' in extraArg) {
            const ref = extraArg as StrategyValueRef;
            const refType = (ref.refType || '').toLowerCase();
            if (refType === 'const' || refType === 'number') {
              extraValueType = 'number';
              extraNumber = ref.input?.trim() || '';
            } else if (refType === 'field') {
              extraValueType = 'field';
              extraValueId = buildFieldValueId(ref.input);
            } else {
              extraValueType = 'field';
              extraValueId = buildIndicatorRefKey(ref, defaultTimeframe);
            }
          }
        }

        return {
          id: conditionId,
          enabled: conditionConfig.enabled !== false,
          required: conditionConfig.required || false,
          method: resolvedMethod,
          leftValueId,
          rightValueType,
          rightValueId,
          rightNumber,
          extraValueType,
          extraValueId,
          extraNumber,
          paramValues,
        };
      });

      return {
        id: groupId,
        name: `条件组 ${index + 1}`,
        enabled: groupConfig.enabled !== false,
        required: false,
        conditions,
      };
    });
  };

  const parseBranch = (branch: StrategyLogicBranchConfig, containerId: string, filterContainerId?: string) => {
    const checks = branch.containers?.[0]?.checks;
    // 兼容历史配置：当 branch.enabled 缺失时，按 checks/groups 自动推断启用状态。
    parseGroupSet(containerId, checks, branch.enabled);
    if (filterContainerId) {
      parseGroupSet(filterContainerId, branch.filters, branch.filters?.enabled ?? false);
    }
  };

  if (config.logic) {
    parseBranch(config.logic.entry.long, 'open-long', 'open-long-filter');
    parseBranch(config.logic.entry.short, 'open-short', 'open-short-filter');
    parseBranch(config.logic.exit.long, 'close-long');
    parseBranch(config.logic.exit.short, 'close-short');
  }

  return containers;
};

const isHourTime = (value: string) => /^([01]\d|2[0-3]):00$/.test(value);

const parseMinutes = (value: string) => {
  const [hourText, minuteText] = value.split(':');
  const hours = Number(hourText);
  const minutes = Number(minuteText);
  if (Number.isNaN(hours) || Number.isNaN(minutes)) {
    return null;
  }
  return hours * 60 + minutes;
};

const calcRangeDurationMinutes = (start: string, end: string) => {
  const startMinutes = parseMinutes(start);
  const endMinutes = parseMinutes(end);
  if (startMinutes === null || endMinutes === null) {
    return null;
  }
  if (startMinutes === endMinutes) {
    return 24 * 60;
  }
  if (startMinutes < endMinutes) {
    return endMinutes - startMinutes;
  }
  return 24 * 60 - startMinutes + endMinutes;
};

const StrategyEditorFlow: React.FC<StrategyEditorFlowProps> = ({
  onSubmit,
  onClose,
  submitLabel = '创建策略',
  successMessage = '策略创建成功',
  errorMessage = '操作失败，请稍后重试',
  initialName,
  initialDescription,
  initialTradeConfig,
  initialConfig,
  initialExchangeApiKeyId = null,
  disableMetaFields = false,
  openConfigDirectly = false,
}) => {
  const client = useMemo(() => new HttpClient({ tokenProvider: getToken }), []);
  const [isIndicatorGeneratorOpen, setIsIndicatorGeneratorOpen] = useState(false);
  const [indicatorDialogMode, setIndicatorDialogMode] = useState<IndicatorDialogMode>('create');
  const [editingIndicator, setEditingIndicator] = useState<GeneratedIndicatorPayload | null>(null);
  const [indicatorAction, setIndicatorAction] = useState<IndicatorActionState | null>(null);
  const [skipQuickInputConfirm, setSkipQuickInputConfirm] = useState(false);
  const [preferIndicatorParamFocus, setPreferIndicatorParamFocus] = useState(false);
  const initialStrategyTimeframe = useMemo(
    () => timeframeLabelFromSeconds(initialConfig?.trade?.timeframeSec || initialTradeConfig?.timeframeSec || 60),
    [initialConfig?.trade?.timeframeSec, initialTradeConfig?.timeframeSec],
  );
  
  // 从配置中加载初始数据
  const loadedIndicators = useMemo(() => {
    if (!initialConfig) {
      return [];
    }
    return extractIndicatorsFromConfig(initialConfig, initialStrategyTimeframe);
  }, [initialConfig, initialStrategyTimeframe]);

  const loadedContainers = useMemo(() => {
    if (!initialConfig) {
      return createDefaultConditionContainers();
    }
    return parseConditionContainersFromConfig(initialConfig, initialStrategyTimeframe);
  }, [initialConfig, initialStrategyTimeframe]);

  const [selectedIndicators, setSelectedIndicators] = useState<GeneratedIndicatorPayload[]>(loadedIndicators);
  const [isConfigReviewOpen, setIsConfigReviewOpen] = useState(false);
  const [configReviewData, setConfigReviewData] = useState<ConfigReviewData | null>(null);
  const [isLogicPreviewVisible, setIsLogicPreviewVisible] = useState(false);
  const [configStep, setConfigStep] = useState(0); // 0: 基本信息, 1: 详细配置
  const [strategyName, setStrategyName] = useState(initialName ?? '');
  const [strategyDescription, setStrategyDescription] = useState(initialDescription ?? '');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const { error, success } = useNotification();

  const summaryListRef = useRef<HTMLDivElement>(null);
  const codeListRef = useRef<HTMLPreElement>(null);
  const tradeConfigRef = useRef<HTMLDivElement>(null);
  const [conditionContainers, setConditionContainers] = useState<ConditionContainer[]>(loadedContainers);
  const filterContainers = useMemo(
    () => conditionContainers.filter((item) => FILTER_CONTAINER_IDS.has(item.id)),
    [conditionContainers],
  );
  const logicContainers = useMemo(
    () => conditionContainers.filter((item) => !FILTER_CONTAINER_IDS.has(item.id)),
    [conditionContainers],
  );
  const [tradeConfig, setTradeConfig] = useState<StrategyTradeConfig>(() => 
    mergeTradeConfig(initialConfig?.trade || initialTradeConfig)
  );
  const [runtimeConfig, setRuntimeConfig] = useState<StrategyRuntimeConfig>(() =>
    mergeRuntimeConfig(initialConfig?.runtime)
  );
  const [exchangeApiKeys, setExchangeApiKeys] = useState<ExchangeApiKeyItem[]>([]);
  const [selectedExchangeApiKeyId, setSelectedExchangeApiKeyId] = useState<number | null>(
    initialExchangeApiKeyId,
  );
  const [showExchangeApiKeySelector, setShowExchangeApiKeySelector] = useState(false);
  const [isApiKeyLoaded, setIsApiKeyLoaded] = useState(false);
  const [isConditionModalOpen, setIsConditionModalOpen] = useState(false);
  const [conditionDraft, setConditionDraft] = useState<ConditionItem | null>(null);
  const [conditionEditTarget, setConditionEditTarget] = useState<ConditionEditTarget | null>(null);
  const [conditionError, setConditionError] = useState('');
  const [historyPast, setHistoryPast] = useState<StrategyHistoryRecord[]>([]);
  const [historyFuture, setHistoryFuture] = useState<StrategyHistoryRecord[]>([]);
  const pendingHistoryMetaRef = useRef<{ undoMessage: string; redoMessage: string } | null>(null);
  const historyReadyRef = useRef(false);
  const historyApplyingRef = useRef(false);
  const latestSnapshotRef = useRef<StrategyEditSnapshot | null>(null);
  const HISTORY_LIMIT = 160;
  const MAX_GROUPS_PER_CONTAINER = 3;
  const MAX_CONDITIONS_PER_GROUP = 6;
  const MAX_TOTAL_CONDITIONS =
    createDefaultConditionContainers().length * MAX_GROUPS_PER_CONTAINER * MAX_CONDITIONS_PER_GROUP;

  const exchangeOptions: TradeOption[] = [
    { value: 'binance', label: '币安', icon: BinanceIcon },
    { value: 'okx', label: 'OKX', icon: OkxIcon },
    { value: 'bitget', label: 'Bitget', icon: BitgetIcon },
  ];

  const symbolOptions: TradeOption[] = [
    { value: 'BTC/USDT', label: 'BTC', icon: BtcIcon },
    { value: 'ETH/USDT', label: 'ETH', icon: EthIcon },
    { value: 'XRP/USDT', label: 'XRP', icon: XrpIcon },
    { value: 'SOL/USDT', label: 'SOL', icon: SolIcon },
    { value: 'DOGE/USDT', label: 'DOGE', icon: DogeIcon },
    { value: 'BNB/USDT', label: 'BNB', icon: BnbIcon },
  ];

  const timeframeOptions: TimeframeOption[] = [
    { value: 60, label: '1m' },
    { value: 180, label: '3m' },
    { value: 300, label: '5m' },
    { value: 900, label: '15m' },
    { value: 1800, label: '30m' },
    { value: 3600, label: '1h' },
    { value: 7200, label: '2h' },
    { value: 14400, label: '4h' },
    { value: 21600, label: '6h' },
    { value: 28800, label: '8h' },
    { value: 43200, label: '12h' },
    { value: 86400, label: '1d' },
    { value: 259200, label: '3d' },
    { value: 604800, label: '1w' },
    { value: 2592000, label: '1mo' },
  ];

  const tradeDefaultTimeframe = useMemo(() => {
    const matched = timeframeOptions.find((option) => option.value === tradeConfig.timeframeSec);
    return (matched?.label || '1m').trim();
  }, [timeframeOptions, tradeConfig.timeframeSec]);
  const applyIndicatorTimeframe = (
    indicator: GeneratedIndicatorPayload,
    timeframe: string,
  ): GeneratedIndicatorPayload => {
    const config = { ...(indicator.config as Record<string, unknown>) };
    const normalized = resolveReferenceTimeframe(
      typeof config.timeframe === 'string' ? config.timeframe : '',
      timeframe,
    );
    if (config.timeframe === normalized) {
      return indicator;
    }
    const nextConfig = {
      ...config,
      timeframe: normalized,
    };
    return {
      ...indicator,
      config: nextConfig,
      configText: JSON.stringify(nextConfig, null, 2),
    };
  };

  useEffect(() => {
    if (!tradeDefaultTimeframe) {
      return;
    }
    setSelectedIndicators((prev) => {
      let changed = false;
      const next = prev.map((item) => {
        const updated = applyIndicatorTimeframe(item, tradeDefaultTimeframe);
        if (updated !== item) {
          changed = true;
        }
        return updated;
      });
      return changed ? next : prev;
    });
  }, [tradeDefaultTimeframe]);

  const leverageOptions = [10, 20, 50, 100];
  const positionModeOptions: TradeOption[] = [
    { value: 'Cross', label: '全仓' },
    { value: 'Isolated', label: '逐仓' },
  ];

  const [runtimeTemplateOptions, setRuntimeTemplateOptions] = useState<StrategyRuntimeTemplateConfig[]>(RUNTIME_TEMPLATE_OPTIONS);
  const [runtimeTimezoneOptions, setRuntimeTimezoneOptions] = useState(RUNTIME_TIMEZONE_OPTIONS);

  const validateRuntimeConfig = (config: StrategyRuntimeConfig): string | null => {
    if (config.scheduleType === 'Always') {
      return null;
    }

    if (config.scheduleType === 'Template') {
      if (!config.templateIds || config.templateIds.length === 0) {
        return '???????????';
      }
      return null;
    }
    if (config.scheduleType === 'Custom') {
      if (!config.custom.days || config.custom.days.length === 0) {
        return '自定义时间必须选择星期';
      }
      if (!config.custom.timeRanges || config.custom.timeRanges.length === 0) {
        return '自定义时间必须配置时间段';
      }
      for (const range of config.custom.timeRanges) {
        if (!isHourTime(range.start) || !isHourTime(range.end)) {
          return '自定义时间仅支持整点';
        }
        const duration = calcRangeDurationMinutes(range.start, range.end);
        if (duration === null || duration < 60) {
          return '自定义时间段最小为 1 小时';
        }
      }
      return null;
    }

    return null;
  };

  useEffect(() => {
    let isActive = true;
    const loadRuntimeTemplates = async () => {
      try {
        const data = await client.postProtocol<{ templates: StrategyRuntimeTemplateConfig[]; timezones: { value: string; label: string }[] }>(
          '/api/strategy/runtime/templates',
          'strategy.runtime.template.list',
        );
        if (!isActive) {
          return;
        }
        setRuntimeTemplateOptions(Array.isArray(data?.templates) ? data.templates : RUNTIME_TEMPLATE_OPTIONS);
        setRuntimeTimezoneOptions(Array.isArray(data?.timezones) ? data.timezones : RUNTIME_TIMEZONE_OPTIONS);
      } catch {
        if (isActive) {
          setRuntimeTemplateOptions(RUNTIME_TEMPLATE_OPTIONS);
          setRuntimeTimezoneOptions(RUNTIME_TIMEZONE_OPTIONS);
        }
      }
    };

    loadRuntimeTemplates();
    return () => {
      isActive = false;
    };
  }, [client]);

  useEffect(() => {
    let isActive = true;
    const loadExchangeApiKeys = async () => {
      try {
        const data = await client.postProtocol<ExchangeApiKeyItem[]>('/api/userexchangeapikeys/list', 'exchange.api_key.list');
        if (!isActive) {
          return;
        }
        setExchangeApiKeys(Array.isArray(data) ? data : []);
      } catch {
        if (isActive) {
          error('获取交易所API失败');
        }
      } finally {
        if (isActive) {
          setIsApiKeyLoaded(true);
        }
      }
    };

    loadExchangeApiKeys();
    return () => {
      isActive = false;
    };
  }, [client, error]);

  const exchangeApiKeyOptions = useMemo(() => {
    const exchange = tradeConfig.exchange?.trim().toLowerCase();
    if (!exchange) {
      return [];
    }
    return exchangeApiKeys
      .filter((item) => item.exchangeType?.trim().toLowerCase() === exchange)
      .map((item) => ({ id: item.id, label: item.label || '未命名API' }));
  }, [exchangeApiKeys, tradeConfig.exchange]);

  const selectedExchangeApiKeyLabel = useMemo(() => {
    if (!selectedExchangeApiKeyId) {
      return null;
    }
    const match = exchangeApiKeys.find((item) => item.id === selectedExchangeApiKeyId);
    return match?.label?.trim() ? match.label : '未命名API';
  }, [exchangeApiKeys, selectedExchangeApiKeyId]);

  useEffect(() => {
    if (!isApiKeyLoaded) {
      return;
    }

    const keys = exchangeApiKeyOptions;
    if (keys.length === 1) {
      setSelectedExchangeApiKeyId(keys[0].id);
      setShowExchangeApiKeySelector(false);
      return;
    }

    if (keys.length === 0) {
      setSelectedExchangeApiKeyId(null);
      setShowExchangeApiKeySelector(false);
      return;
    }

    if (selectedExchangeApiKeyId && keys.some((item) => item.id === selectedExchangeApiKeyId)) {
      return;
    }

    setSelectedExchangeApiKeyId(null);
    setShowExchangeApiKeySelector(true);
  }, [exchangeApiKeyOptions, isApiKeyLoaded, selectedExchangeApiKeyId]);

  const openExportReview = () => {
    setConfigReviewData({
      configJson: deepCloneJson(configPreview),
      logicPreview,
      usedIndicatorOutputs: [...usedIndicatorOutputs],
      conditionSummarySections: deepCloneJson(conditionSummarySections),
    });
    setIsConfigReviewOpen(true);
    setIsLogicPreviewVisible(false);
    setConfigStep(0);
    if (!strategyName.trim()) {
      const now = new Date();
      const month = now.getMonth() + 1;
      const day = now.getDate();
      const hour = now.getHours();
      const minute = now.getMinutes();
      const defaultStrategyName = `${month}月${day}日${hour}时${minute}分创建的策略`;
      setStrategyName(defaultStrategyName);
    }
  };

  const closeConfigReview = () => {
    setIsConfigReviewOpen(false);
    setConfigStep(0);
    setConfigReviewData(null);
  };

  const handleConfigClose = () => {
    closeConfigReview();
    if (openConfigDirectly) {
      onClose?.();
    }
  };

  const handleNextStep = () => {
    setConfigStep(1);
  };

  const handlePrevStep = () => {
    setConfigStep(0);
  };

  const toggleLogicPreview = () => {
    setIsLogicPreviewVisible((prev) => !prev);
  };

  useEffect(() => {
    if (openConfigDirectly) {
      openExportReview();
    }
  }, [openConfigDirectly]);

  // 切换步骤时重置交易规则区滚动位置，避免显示错位
  useEffect(() => {
    if (!isConfigReviewOpen) {
      return;
    }
    const tradeElement = tradeConfigRef.current;
    if (tradeElement) {
      tradeElement.scrollTop = 0;
    }
  }, [configStep, isConfigReviewOpen]);

  const generateId = () => `${Date.now().toString(36)}_${Math.random().toString(36).slice(2, 8)}`;

  const promoteToTop = <T,>(items: T[], index: number) => {
    if (index <= 0 || index >= items.length) {
      return items;
    }
    const selected = items[index];
    const rest = items.filter((_, idx) => idx !== index);
    return [selected, ...rest];
  };

  const getIndicatorConfig = (indicator: GeneratedIndicatorPayload) => {
    const config = indicator.config as {
      indicator?: string;
      timeframe?: string;
      input?: string;
      params?: number[];
      offsetRange?: number[];
      calcMode?: string;
      output?: string;
    };
    return {
      indicatorCode: (config.indicator || indicator.code || '').trim(),
      timeframe: resolveReferenceTimeframe(config.timeframe, tradeDefaultTimeframe),
      input: (config.input || '').trim(),
      params: Array.isArray(config.params) ? config.params.map(Number) : [],
      offsetRange: Array.isArray(config.offsetRange) ? config.offsetRange.map(Number) : [],
      calcMode: (config.calcMode || '').trim(),
      output: (config.output || '').trim(),
    };
  };

  const buildIndicatorSignature = (indicator: GeneratedIndicatorPayload) => {
    const config = getIndicatorConfig(indicator);
    const params = config.params.length > 0 ? config.params.join(',') : '';
    const offsetRange = config.offsetRange.length > 0 ? config.offsetRange.join(',') : '';
    return [
      config.indicatorCode,
      config.input,
      params,
      offsetRange,
      config.calcMode,
    ].join('|');
  };

  const rewriteIndicatorInputSource = (rawInput: string, nextInput: string) => {
    const text = (rawInput || '').trim();
    if (!text || !text.includes('=')) {
      return nextInput;
    }

    const segments = text
      .split(';')
      .map((segment) => segment.trim())
      .filter((segment) => segment.length > 0);
    if (segments.length === 0) {
      return nextInput;
    }

    let hasPatched = false;
    const patched = segments.map((segment, index) => {
      const [rawKey, ...rest] = segment.split('=');
      const key = (rawKey || '').trim();
      if (!key || rest.length === 0) {
        return segment;
      }
      const lowerKey = key.toLowerCase();
      const shouldPatch = lowerKey === 'real' || (!hasPatched && index === 0);
      if (shouldPatch) {
        hasPatched = true;
        return `${key}=${nextInput}`;
      }
      return segment;
    });

    return patched.join(';');
  };

  const commitIndicatorUpdate = (nextIndicator: GeneratedIndicatorPayload) => {
    setSelectedIndicators((prev) =>
      prev.map((item) => (item.id === nextIndicator.id ? nextIndicator : item)),
    );
    syncIndicatorReferences(nextIndicator);
  };

  const validateIndicator = (indicator: GeneratedIndicatorPayload, mode: IndicatorDialogMode) => {
    const signature = buildIndicatorSignature(indicator);
    const duplicate = selectedIndicators.some((item) => {
      if (mode === 'edit' && item.id === indicator.id) {
        return false;
      }
      return buildIndicatorSignature(item) === signature;
    });
    return duplicate ? '已拥有该指标，请调整参数或删除旧指标。' : null;
  };

  const collectIndicatorUsages = (indicatorId: string): IndicatorUsageItem[] => {
    const usages: IndicatorUsageItem[] = [];
    const prefix = `${indicatorId}:`;
    conditionContainers.forEach((container) => {
      container.groups.forEach((group) => {
        group.conditions.forEach((condition) => {
          const addUsage = (valueId: string | undefined, positionLabel: string) => {
            if (!valueId || !valueId.startsWith(prefix)) {
              return;
            }
            const option = indicatorValueMap.get(valueId);
            const outputLabel =
              option?.label || option?.fullLabel || valueId.slice(prefix.length) || 'Value';
            usages.push({
              id: `${container.id}-${group.id}-${condition.id}-${positionLabel}-${valueId}`,
              containerTitle: container.title,
              groupTitle: group.name,
              positionLabel,
              outputLabel,
            });
          };
          addUsage(condition.leftValueId, '左值');
          if (condition.rightValueType === 'field') {
            addUsage(condition.rightValueId, '右值');
          }
          if (condition.extraValueType === 'field') {
            addUsage(condition.extraValueId, '第三参数');
          }
        });
      });
    });
    return usages;
  };

  const syncIndicatorReferences = (indicator: GeneratedIndicatorPayload) => {
    const outputs = indicator.outputs || [];
    const outputKeys = new Set(outputs.map((item) => item.key));
    const fallbackKey = outputs[0]?.key || getIndicatorConfig(indicator).output || 'Value';
    if (outputKeys.size === 0) {
      outputKeys.add(fallbackKey);
    }

    setConditionContainers((prev) =>
      prev.map((container) => ({
        ...container,
        groups: container.groups.map((group) => ({
          ...group,
          conditions: group.conditions.map((condition) => {
            let leftValueId = condition.leftValueId;
            let rightValueId = condition.rightValueId;
            let extraValueId = condition.extraValueId;

            if (leftValueId?.startsWith(`${indicator.id}:`)) {
              const outputKey = leftValueId.split(':')[1] || '';
              if (!outputKeys.has(outputKey)) {
                leftValueId = `${indicator.id}:${fallbackKey}`;
              }
            }

            if (condition.rightValueType === 'field' && rightValueId?.startsWith(`${indicator.id}:`)) {
              const outputKey = rightValueId.split(':')[1] || '';
              if (!outputKeys.has(outputKey)) {
                rightValueId = `${indicator.id}:${fallbackKey}`;
              }
            }

            if (condition.extraValueType === 'field' && extraValueId?.startsWith(`${indicator.id}:`)) {
              const outputKey = extraValueId.split(':')[1] || '';
              if (!outputKeys.has(outputKey)) {
                extraValueId = `${indicator.id}:${fallbackKey}`;
              }
            }

            if (
              leftValueId === condition.leftValueId &&
              rightValueId === condition.rightValueId &&
              extraValueId === condition.extraValueId
            ) {
              return condition;
            }
            return {
              ...condition,
              leftValueId,
              rightValueId,
              extraValueId,
            };
          }),
        })),
      })),
    );
  };

  const handleAddIndicator = (indicator: GeneratedIndicatorPayload) => {
    const nextIndicator = applyIndicatorTimeframe(indicator, tradeDefaultTimeframe);
    const name = describeIndicatorForHistory(nextIndicator);
    markHistoryAction(`新增指标 ${name}`);
    setSelectedIndicators((prev) => [nextIndicator, ...prev]);
  };

  const handleUpdateIndicator = (indicator: GeneratedIndicatorPayload) => {
    const nextIndicator = applyIndicatorTimeframe(indicator, tradeDefaultTimeframe);
    const previous = selectedIndicators.find((item) => item.id === nextIndicator.id);
    const previousConfig = (previous?.config || {}) as { input?: string; params?: number[] };
    const nextConfig = (nextIndicator.config || {}) as { input?: string; params?: number[] };
    const previousInput = String(previousConfig.input || '').trim();
    const nextInput = String(nextConfig.input || '').trim();
    const previousParams = Array.isArray(previousConfig.params) ? previousConfig.params.join(',') : '';
    const nextParams = Array.isArray(nextConfig.params) ? nextConfig.params.join(',') : '';
    let detail = `修改指标 ${describeIndicatorForHistory(nextIndicator)}`;
    if (previousInput !== nextInput) {
      detail = `修改指标输入源 ${describeIndicatorForHistory(nextIndicator)}: ${previousInput || '-'} -> ${nextInput || '-'}`;
    } else if (previousParams !== nextParams) {
      detail = `修改指标参数 ${describeIndicatorForHistory(nextIndicator)}: ${previousParams || '-'} -> ${nextParams || '-'}`;
    }
    markHistoryAction(detail);
    commitIndicatorUpdate(nextIndicator);
  };

  const removeIndicator = (indicatorId: string) => {
    const indicator = selectedIndicators.find((item) => item.id === indicatorId);
    if (indicator) {
      markHistoryAction(`删除指标 ${describeIndicatorForHistory(indicator)}`);
    }
    setSelectedIndicators((prev) => prev.filter((item) => item.id !== indicatorId));
    setConditionContainers((prev) =>
      prev.map((container) => ({
        ...container,
        groups: container.groups.map((group) => ({
          ...group,
          conditions: group.conditions.map((condition) => {
            let leftValueId = condition.leftValueId;
            let rightValueId = condition.rightValueId;
            let extraValueId = condition.extraValueId;
            if (leftValueId?.startsWith(`${indicatorId}:`)) {
              leftValueId = '';
            }
            if (rightValueId?.startsWith(`${indicatorId}:`)) {
              rightValueId = '';
            }
            if (extraValueId?.startsWith(`${indicatorId}:`)) {
              extraValueId = '';
            }
            if (
              leftValueId === condition.leftValueId &&
              rightValueId === condition.rightValueId &&
              extraValueId === condition.extraValueId
            ) {
              return condition;
            }
            return {
              ...condition,
              leftValueId,
              rightValueId,
              extraValueId,
            };
          }),
        })),
      })),
    );
  };

  const openCreateIndicator = () => {
    setIndicatorDialogMode('create');
    setEditingIndicator(null);
    setIsIndicatorGeneratorOpen(true);
  };

  const openIndicatorEditor = (indicator: GeneratedIndicatorPayload, focusParam = false) => {
    setIndicatorDialogMode('edit');
    setEditingIndicator(indicator);
    setPreferIndicatorParamFocus(focusParam);
    setIsIndicatorGeneratorOpen(true);
  };

  const closeIndicatorDialog = () => {
    setIsIndicatorGeneratorOpen(false);
    setEditingIndicator(null);
    setIndicatorDialogMode('create');
    setPreferIndicatorParamFocus(false);
  };

  const requestEditIndicator = (indicatorId: string) => {
    const indicator = selectedIndicators.find((item) => item.id === indicatorId);
    if (!indicator) {
      return;
    }
    const usages = collectIndicatorUsages(indicatorId);
    if (usages.length > 0) {
      setIndicatorAction({ action: 'edit', indicator, usages });
      return;
    }
    openIndicatorEditor(indicator);
  };

  const requestRemoveIndicator = (indicatorId: string) => {
    const indicator = selectedIndicators.find((item) => item.id === indicatorId);
    if (!indicator) {
      return;
    }
    const usages = collectIndicatorUsages(indicatorId);
    if (usages.length > 0) {
      setIndicatorAction({ action: 'remove', indicator, usages });
      return;
    }
    removeIndicator(indicatorId);
  };

  const requestQuickEditIndicatorParams = (indicatorId: string) => {
    const indicator = selectedIndicators.find((item) => item.id === indicatorId);
    if (!indicator) {
      error('目标指标不存在，请刷新后重试');
      return;
    }

    const config = (indicator.config || {}) as { params?: unknown[] };
    const hasParams = Array.isArray(config.params) && config.params.length > 0;
    if (!hasParams) {
      success('该指标没有可编辑参数，请使用编辑功能调整其他配置');
      return;
    }

    const usages = collectIndicatorUsages(indicatorId);
    if (usages.length > 0) {
      setIndicatorAction({ action: 'quick-param-edit', indicator, usages });
      return;
    }
    openIndicatorEditor(indicator, true);
  };

  const quickUpdateIndicatorInput = (
    indicatorId: string,
    fieldValueId: string,
    skipConfirmCheck = false,
  ) => {
    const indicator = selectedIndicators.find((item) => item.id === indicatorId);
    if (!indicator) {
      error('目标指标不存在，请刷新后重试');
      return;
    }

    const fieldKey = parseFieldValueId(fieldValueId);
    if (!fieldKey) {
      error('仅支持将K线字段拖拽到指标上修改输入源');
      return;
    }
    const nextInput = FIELD_KEY_TO_INPUT_VALUE[fieldKey] || fieldKey;
    const currentConfig = (indicator.config || {}) as Record<string, unknown>;
    const currentInput = String(currentConfig.input || '').trim();
    const patchedInput = rewriteIndicatorInputSource(currentInput, nextInput);
    if (!patchedInput || patchedInput === currentInput) {
      return;
    }

    const nextConfig = {
      ...currentConfig,
      input: patchedInput,
    };
    const nextIndicator: GeneratedIndicatorPayload = {
      ...indicator,
      config: nextConfig,
      configText: JSON.stringify(nextConfig),
    };

    const duplicate = selectedIndicators.some((item) => {
      if (item.id === nextIndicator.id) {
        return false;
      }
      return buildIndicatorSignature(item) === buildIndicatorSignature(nextIndicator);
    });
    if (duplicate) {
      error('修改后会与现有指标重复，请先删除重复指标或调整参数');
      return;
    }

    const usages = collectIndicatorUsages(indicatorId);
    if (usages.length > 0 && !skipConfirmCheck && !skipQuickInputConfirm) {
      setIndicatorAction({
        action: 'quick-input',
        indicator,
        usages,
        pendingIndicator: nextIndicator,
        pendingInputLabel: nextInput,
      });
      return;
    }

    markHistoryAction(`修改指标输入源 ${describeIndicatorForHistory(indicator)}: ${currentInput || '-'} -> ${patchedInput}`);
    commitIndicatorUpdate(nextIndicator);
  };

  const closeIndicatorActionDialog = () => {
    setIndicatorAction(null);
  };

  const confirmIndicatorAction = () => {
    if (!indicatorAction) {
      return;
    }
    if (indicatorAction.action === 'edit') {
      openIndicatorEditor(indicatorAction.indicator);
      closeIndicatorActionDialog();
      return;
    }
    if (indicatorAction.action === 'quick-param-edit') {
      openIndicatorEditor(indicatorAction.indicator, true);
      closeIndicatorActionDialog();
      return;
    }
    if (indicatorAction.action === 'quick-input') {
      if (indicatorAction.pendingIndicator) {
        const currentInput = String((indicatorAction.indicator.config as { input?: string })?.input || '').trim();
        const nextInput = String((indicatorAction.pendingIndicator.config as { input?: string })?.input || '').trim();
        markHistoryAction(
          `修改指标输入源 ${describeIndicatorForHistory(indicatorAction.indicator)}: ${currentInput || '-'} -> ${nextInput || '-'}`,
        );
        commitIndicatorUpdate(indicatorAction.pendingIndicator);
      }
      closeIndicatorActionDialog();
      return;
    }
    removeIndicator(indicatorAction.indicator.id);
    closeIndicatorActionDialog();
  };

  const confirmIndicatorActionAndSkipPrompt = () => {
    if (!indicatorAction || indicatorAction.action !== 'quick-input') {
      return;
    }
    setSkipQuickInputConfirm(true);
    if (indicatorAction.pendingIndicator) {
      const currentInput = String((indicatorAction.indicator.config as { input?: string })?.input || '').trim();
      const nextInput = String((indicatorAction.pendingIndicator.config as { input?: string })?.input || '').trim();
      markHistoryAction(
        `修改指标输入源 ${describeIndicatorForHistory(indicatorAction.indicator)}: ${currentInput || '-'} -> ${nextInput || '-'}`,
      );
      commitIndicatorUpdate(indicatorAction.pendingIndicator);
    }
    closeIndicatorActionDialog();
  };

  const updateTradeSizing = (key: keyof StrategyTradeConfig['sizing'], value: number) => {
    setTradeConfig((prev) => ({
      ...prev,
      sizing: {
        ...prev.sizing,
        [key]: value,
      },
    }));
  };

  const updateTradeRisk = (key: keyof StrategyTradeConfig['risk'], value: number) => {
    setTradeConfig((prev) => ({
      ...prev,
      risk: {
        ...prev.risk,
        [key]: value,
      },
    }));
  };

  const updateTrailingRisk = (
    key: keyof StrategyTradeConfig['risk']['trailing'],
    value: number | boolean,
  ) => {
    setTradeConfig((prev) => ({
      ...prev,
      risk: {
        ...prev.risk,
        trailing: {
          ...prev.risk.trailing,
          [key]: value,
        },
      },
    }));
  };

  const handleStrategyNameChange = (value: string) => {
    setStrategyName(value);
  };

  const handleStrategyDescriptionChange = (value: string) => {
    setStrategyDescription(value);
  };

  const handleExchangeChange = (exchange: string) => {
    if (isApiKeyLoaded) {
      const keys = exchangeApiKeys.filter(
        (item) => item.exchangeType?.trim().toLowerCase() === exchange.trim().toLowerCase(),
      );
      if (keys.length === 1) {
        setSelectedExchangeApiKeyId(keys[0].id);
        setShowExchangeApiKeySelector(false);
      } else if (keys.length > 1) {
        setSelectedExchangeApiKeyId(null);
        setShowExchangeApiKeySelector(true);
      } else {
        setSelectedExchangeApiKeyId(null);
        setShowExchangeApiKeySelector(false);
      }
    } else {
      setSelectedExchangeApiKeyId(null);
      setShowExchangeApiKeySelector(false);
    }

    setTradeConfig((prev) => ({
      ...prev,
      exchange,
    }));
  };

  const handleExchangeApiKeySelect = (id: number) => {
    setSelectedExchangeApiKeyId(id);
  };

  const handleExchangeApiKeyBack = () => {
    setShowExchangeApiKeySelector(false);
    setSelectedExchangeApiKeyId(null);
  };

  const handleSymbolChange = (symbol: string) => {
    setTradeConfig((prev) => ({
      ...prev,
      symbol,
    }));
  };

  const handlePositionModeChange = (positionMode: string) => {
    setTradeConfig((prev) => ({
      ...prev,
      positionMode,
    }));
  };

  const handleTimeframeChange = (timeframeSec: number) => {
    setTradeConfig((prev) => ({
      ...prev,
      timeframeSec,
    }));
  };

  const formatIndicatorName = (indicator: GeneratedIndicatorPayload) => {
    const config = indicator.config as {
      params?: number[];
    };
    const params = Array.isArray(config.params) && config.params.length > 0
      ? config.params.join(',')
      : '';
    return params ? `${indicator.code} ${params}` : indicator.code;
  };

  const methodOptions: MethodOption[] = [
    { value: 'GreaterThanOrEqual', label: '大于等于 (>=)', category: 'compare', argsCount: 2, argValueTypes: ['field', 'both'] },
    { value: 'GreaterThan', label: '大于 (>)', category: 'compare', argsCount: 2, argValueTypes: ['field', 'both'] },
    { value: 'LessThan', label: '小于 (<)', category: 'compare', argsCount: 2, argValueTypes: ['field', 'both'] },
    { value: 'LessThanOrEqual', label: '小于等于 (<=)', category: 'compare', argsCount: 2, argValueTypes: ['field', 'both'] },
    { value: 'Equal', label: '等于 (=)', category: 'compare', argsCount: 2, argValueTypes: ['field', 'both'] },
    { value: 'NotEqual', label: '不等于 (!=)', category: 'compare', argsCount: 2, argValueTypes: ['field', 'both'] },
    { value: 'CrossUp', label: '上穿 (CrossUp)', category: 'cross', argsCount: 2, argValueTypes: ['field', 'both'] },
    { value: 'CrossDown', label: '下穿 (CrossDown)', category: 'cross', argsCount: 2, argValueTypes: ['field', 'both'] },
    { value: 'CrossAny', label: '任意穿越 (CrossAny)', category: 'cross', argsCount: 2, argValueTypes: ['field', 'both'] },
    {
      value: 'Between',
      label: '区间内 (Between)',
      category: 'range',
      argsCount: 3,
      argLabels: ['数值', '下界', '上界'],
      argValueTypes: ['field', 'both', 'both'],
    },
    {
      value: 'Outside',
      label: '区间外 (Outside)',
      category: 'range',
      argsCount: 3,
      argLabels: ['数值', '下界', '上界'],
      argValueTypes: ['field', 'both', 'both'],
    },
    {
      value: 'Rising',
      label: '连续上升 (Rising)',
      category: 'trend',
      argsCount: 1,
      argLabels: ['数值'],
      argValueTypes: ['field'],
      params: [{ key: 'period', label: '窗口长度N', placeholder: '例如 3', required: true, defaultValue: '3' }],
    },
    {
      value: 'Falling',
      label: '连续下降 (Falling)',
      category: 'trend',
      argsCount: 1,
      argLabels: ['数值'],
      argValueTypes: ['field'],
      params: [{ key: 'period', label: '窗口长度N', placeholder: '例如 3', required: true, defaultValue: '3' }],
    },
    {
      value: 'AboveFor',
      label: '连续高于阈值 (AboveFor)',
      category: 'trend',
      argsCount: 2,
      argLabels: ['数值', '阈值'],
      argValueTypes: ['field', 'both'],
      params: [{ key: 'period', label: '窗口长度N', placeholder: '例如 3', required: true, defaultValue: '3' }],
    },
    {
      value: 'BelowFor',
      label: '连续低于阈值 (BelowFor)',
      category: 'trend',
      argsCount: 2,
      argLabels: ['数值', '阈值'],
      argValueTypes: ['field', 'both'],
      params: [{ key: 'period', label: '窗口长度N', placeholder: '例如 3', required: true, defaultValue: '3' }],
    },
    {
      value: 'ROC',
      label: '变化率大于阈值 (ROC)',
      category: 'change',
      argsCount: 2,
      argLabels: ['数值', '阈值'],
      argValueTypes: ['field', 'number'],
      params: [{ key: 'period', label: '窗口长度N', placeholder: '例如 14', required: true, defaultValue: '14' }],
    },
    {
      value: 'Slope',
      label: '斜率大于阈值 (Slope)',
      category: 'change',
      argsCount: 2,
      argLabels: ['数值', '阈值'],
      argValueTypes: ['field', 'number'],
      params: [{ key: 'period', label: '窗口长度N', placeholder: '例如 14', required: true, defaultValue: '14' }],
    },
    {
      value: 'ZScore',
      label: 'ZScore大于阈值',
      category: 'stats',
      argsCount: 2,
      argLabels: ['数值', '阈值'],
      argValueTypes: ['field', 'number'],
      params: [{ key: 'period', label: '窗口长度N', placeholder: '例如 20', required: true, defaultValue: '20' }],
    },
    {
      value: 'StdDevGreater',
      label: '标准差大于阈值',
      category: 'stats',
      argsCount: 2,
      argLabels: ['数值', '阈值'],
      argValueTypes: ['field', 'number'],
      params: [{ key: 'period', label: '窗口长度N', placeholder: '例如 20', required: true, defaultValue: '20' }],
    },
    {
      value: 'StdDevLess',
      label: '标准差小于阈值',
      category: 'stats',
      argsCount: 2,
      argLabels: ['数值', '阈值'],
      argValueTypes: ['field', 'number'],
      params: [{ key: 'period', label: '窗口长度N', placeholder: '例如 20', required: true, defaultValue: '20' }],
    },
    { value: 'TouchUpper', label: '触碰上轨 (>=)', category: 'channel', argsCount: 2, argLabels: ['数值', '上轨'], argValueTypes: ['field', 'field'] },
    { value: 'TouchLower', label: '触碰下轨 (<=)', category: 'channel', argsCount: 2, argLabels: ['数值', '下轨'], argValueTypes: ['field', 'field'] },
    { value: 'BreakoutUp', label: '突破上轨 (>)', category: 'channel', argsCount: 2, argLabels: ['数值', '上轨'], argValueTypes: ['field', 'field'] },
    { value: 'BreakoutDown', label: '突破下轨 (<)', category: 'channel', argsCount: 2, argLabels: ['数值', '下轨'], argValueTypes: ['field', 'field'] },
    {
      value: 'BandwidthExpand',
      label: '带宽扩张 (BandwidthExpand)',
      category: 'bandwidth',
      argsCount: 3,
      argLabels: ['上轨', '下轨', '中轨'],
      argValueTypes: ['field', 'field', 'field'],
      params: [{ key: 'period', label: '窗口长度N', placeholder: '例如 3', required: true, defaultValue: '3' }],
    },
    {
      value: 'BandwidthContract',
      label: '带宽收敛 (BandwidthContract)',
      category: 'bandwidth',
      argsCount: 3,
      argLabels: ['上轨', '下轨', '中轨'],
      argValueTypes: ['field', 'field', 'field'],
      params: [{ key: 'period', label: '窗口长度N', placeholder: '例如 3', required: true, defaultValue: '3' }],
    },
  ];

  const resolveMethodMeta = (method?: string) => {
    const resolved = methodOptions.find((option) => option.value === method);
    return resolved || methodOptions[0];
  };

  const tryResolveMethodMeta = (method?: string) => {
    return methodOptions.find((option) => option.value === method) || null;
  };

  const resolveArgValueMode = (
    method: MethodOption | null | undefined,
    index: number,
  ): 'field' | 'number' | 'both' => {
    const mode = method?.argValueTypes?.[index];
    return mode || 'both';
  };

  const describeIndicatorForHistory = (indicator: GeneratedIndicatorPayload) => {
    return formatIndicatorName(indicator);
  };

  const describeMethodForHistory = (method?: string) => {
    const meta = tryResolveMethodMeta(method);
    return meta?.label || method || '未命名方法';
  };

  const buildEditSnapshot = (
    indicators: GeneratedIndicatorPayload[],
    containers: ConditionContainer[],
  ): StrategyEditSnapshot => ({
    selectedIndicators: deepCloneJson(indicators),
    conditionContainers: deepCloneJson(containers),
  });

  const snapshotSignature = (snapshot: StrategyEditSnapshot) => JSON.stringify(snapshot);

  const markHistoryAction = (undoMessage: string, redoMessage?: string) => {
    pendingHistoryMetaRef.current = {
      undoMessage,
      redoMessage: redoMessage || undoMessage,
    };
  };

  const applyHistorySnapshot = (snapshot: StrategyEditSnapshot) => {
    historyApplyingRef.current = true;
    setSelectedIndicators(deepCloneJson(snapshot.selectedIndicators));
    setConditionContainers(deepCloneJson(snapshot.conditionContainers));
  };

  // 创建指标引用到指标值 ID 的映射
  const indicatorRefToValueIdMap = useMemo(() => {
    const map = new Map<string, string>();
    selectedIndicators.forEach((indicator) => {
      const config = indicator.config as {
        indicator?: string;
        timeframe?: string;
        input?: string;
        params?: number[];
        output?: string;
        offsetRange?: number[];
        calcMode?: string;
      };
      const params = Array.isArray(config.params) ? config.params.map(Number) : [];
      const indicatorCode = config.indicator || indicator.code;
      const outputs =
        indicator.outputs && indicator.outputs.length > 0
          ? indicator.outputs
          : [{ key: config.output || 'Value', hint: config.output || 'Value' }];

      outputs.forEach((output) => {
        const refKey = `${indicatorCode}|${resolveReferenceTimeframe(config.timeframe, tradeDefaultTimeframe)}|${config.input || ''}|${output.key}|${params.join(',')}`;
        const valueId = `${indicator.id}:${output.key}`;
        map.set(refKey, valueId);
      });
    });
    return map;
  }, [selectedIndicators, tradeDefaultTimeframe]);

  // 当指标加载后，更新条件容器中的 ID 映射
  useEffect(() => {
    if (selectedIndicators.length === 0 || !initialConfig) {
      return;
    }

    setConditionContainers((prevContainers) => {
      return prevContainers.map((container) => {
        const updatedGroups = container.groups.map((group) => {
          const updatedConditions = group.conditions.map((condition) => {
            let updatedLeftValueId = condition.leftValueId;
            let updatedRightValueId = condition.rightValueId;
            let updatedExtraValueId = condition.extraValueId;

            // 映射左侧值 ID
            if (condition.leftValueId && indicatorRefToValueIdMap.has(condition.leftValueId)) {
              updatedLeftValueId = indicatorRefToValueIdMap.get(condition.leftValueId) || condition.leftValueId;
            }

            // 映射右侧值 ID
            if (condition.rightValueType === 'field' && condition.rightValueId && indicatorRefToValueIdMap.has(condition.rightValueId)) {
              updatedRightValueId = indicatorRefToValueIdMap.get(condition.rightValueId) || condition.rightValueId;
            }

            // 映射第三参数值 ID
            if (condition.extraValueType === 'field' && condition.extraValueId && indicatorRefToValueIdMap.has(condition.extraValueId)) {
              updatedExtraValueId = indicatorRefToValueIdMap.get(condition.extraValueId) || condition.extraValueId;
            }

            return {
              ...condition,
              leftValueId: updatedLeftValueId,
              rightValueId: updatedRightValueId,
              extraValueId: updatedExtraValueId,
            };
          });

          return {
            ...group,
            conditions: updatedConditions,
          };
        });

        return {
          ...container,
          groups: updatedGroups,
        };
      });
    });
  }, [selectedIndicators, indicatorRefToValueIdMap, initialConfig]);

  const indicatorOutputGroups = useMemo<IndicatorOutputGroup[]>(() => {
    const fieldOptions: ValueOption[] = KLINE_FIELD_DEFINITIONS.map((field) => {
      const fieldLabel = field.hint ? `${field.label} (${field.hint})` : field.label;
      return {
        id: buildFieldValueId(field.key),
        label: fieldLabel,
        fullLabel: `K线字段 - ${fieldLabel}`,
        ref: {
          refType: 'Field',
          indicator: '',
          timeframe: '',
          input: field.key,
          params: [],
          output: 'Value',
          offsetRange: [0, 0],
          calcMode: 'OnBarClose',
        },
      };
    });

    const indicatorGroups = selectedIndicators.map((indicator) => {
      const config = indicator.config as {
        indicator?: string;
        timeframe?: string;
        input?: string;
        params?: number[];
        output?: string;
        offsetRange?: number[];
        calcMode?: string;
      };
      const params = Array.isArray(config.params) ? config.params.map(Number) : [];
      const offsetRange = Array.isArray(config.offsetRange) ? config.offsetRange.map(Number) : [0, 0];
      const input = config.input || '';
      const paramLabel = params.length > 0 ? params.join(',') : '默认参数';
      const indicatorCode = config.indicator || indicator.code;
      const groupLabel = [indicatorCode, paramLabel, input].filter(Boolean).join(' ');
      const outputs =
        indicator.outputs && indicator.outputs.length > 0
          ? indicator.outputs
          : [{ key: config.output || 'Value', hint: config.output || 'Value' }];

      const options = outputs.map((output) => {
        const outputLabel =
          removeParentheticalText(output.hint || output.key) ||
          removeParentheticalText(output.key) ||
          'Value';
        const fullLabel = `${groupLabel} - ${outputLabel}`;
        return {
          id: `${indicator.id}:${output.key}`,
          label: outputLabel,
          fullLabel,
          ref: {
            refType: 'Indicator',
            indicator: indicatorCode,
            timeframe: resolveReferenceTimeframe(config.timeframe, tradeDefaultTimeframe),
            input: config.input || '',
            params,
            output: output.key,
            offsetRange,
            calcMode: config.calcMode || 'OnBarClose',
          },
        };
      });

      return {
        id: indicator.id,
        label: groupLabel,
        options,
      };
    });

    const fieldGroup: IndicatorOutputGroup = {
      id: 'kline-fields',
      label: 'K线字段',
      options: fieldOptions,
    };

    return indicatorGroups.length > 0 ? [...indicatorGroups, fieldGroup] : [fieldGroup];
  }, [selectedIndicators, tradeDefaultTimeframe]);

  const indicatorOutputOptions = useMemo<ValueOption[]>(() => {
    return indicatorOutputGroups.flatMap((group) => group.options);
  }, [indicatorOutputGroups]);

  const indicatorValueMap = useMemo(() => {
    return new Map(indicatorOutputOptions.map((option) => [option.id, option]));
  }, [indicatorOutputOptions]);

  const outputHintMap = useMemo(() => {
    const map = new Map<string, string>();
    selectedIndicators.forEach((indicator) => {
      (indicator.outputs || []).forEach((output) => {
        map.set(
          `${indicator.code}:${output.key}`,
          removeParentheticalText(output.hint || output.key) || output.key,
        );
      });
    });
    return map;
  }, [selectedIndicators]);

  useEffect(() => {
    const snapshot = buildEditSnapshot(selectedIndicators, conditionContainers);

    if (!historyReadyRef.current) {
      latestSnapshotRef.current = snapshot;
      historyReadyRef.current = true;
      return;
    }

    if (historyApplyingRef.current) {
      latestSnapshotRef.current = snapshot;
      historyApplyingRef.current = false;
      pendingHistoryMetaRef.current = null;
      return;
    }

    const previous = latestSnapshotRef.current;
    if (!previous) {
      latestSnapshotRef.current = snapshot;
      pendingHistoryMetaRef.current = null;
      return;
    }
    if (snapshotSignature(previous) === snapshotSignature(snapshot)) {
      pendingHistoryMetaRef.current = null;
      return;
    }

    const meta = pendingHistoryMetaRef.current;
    if (meta) {
      const record: StrategyHistoryRecord = {
        before: previous,
        after: snapshot,
        undoMessage: meta.undoMessage,
        redoMessage: meta.redoMessage,
      };
      setHistoryPast((prev) => [...prev.slice(-(HISTORY_LIMIT - 1)), record]);
      setHistoryFuture([]);
    }

    latestSnapshotRef.current = snapshot;
    pendingHistoryMetaRef.current = null;
  }, [conditionContainers, selectedIndicators]);

  const formatValueRefLabel = (ref?: StrategyValueRef | null) => {
    if (!ref) {
      return '未配置';
    }
    const refType = (ref.refType || '').toLowerCase();
    if (refType === 'const' || refType === 'number') {
      return ref.input?.trim() || '0';
    }
    if (refType === 'field') {
      const inputLabel = ref.input || 'Field';
      return inputLabel;
    }
    const paramsLabel = ref.params && ref.params.length > 0 ? ref.params.join(',') : '默认参数';
    const outputKey = ref.output || 'Value';
    const outputLabel = outputHintMap.get(`${ref.indicator}:${outputKey}`) || outputKey;
    const indicatorLabel = ref.indicator || 'Indicator';
    return `${indicatorLabel} ${paramsLabel} ${outputLabel}`.replace(/\s{2,}/g, ' ').trim();
  };

  const buildConditionPreview = (draft: ConditionItem | null) => {
    if (!draft) {
      return '请先配置字段与操作符';
    }
    const leftLabel =
      indicatorValueMap.get(draft.leftValueId)?.fullLabel ||
      indicatorValueMap.get(draft.leftValueId)?.label ||
      '未选择字段';
    const methodMeta = resolveMethodMeta(draft.method);
    const methodValue = methodMeta.value || draft.method;
    const methodLabel =
      (methodMeta.label || draft.method).replace(/\s*[\(（][^\)）]*[\)）]/g, '').trim()
      || draft.method;
    const argsCount = methodMeta.argsCount ?? 2;

    const rightLabel =
      draft.rightValueType === 'number'
        ? draft.rightNumber || '未填写数值'
        : indicatorValueMap.get(draft.rightValueId || '')?.fullLabel ||
          indicatorValueMap.get(draft.rightValueId || '')?.label ||
          '未选择字段';
    const extraLabel =
      draft.extraValueType === 'number'
        ? draft.extraNumber || '未填写数值'
        : indicatorValueMap.get(draft.extraValueId || '')?.fullLabel ||
          indicatorValueMap.get(draft.extraValueId || '')?.label ||
          '未选择字段';

    const paramDefs = methodMeta.params || [];
    const paramValues = draft.paramValues || [];
    const resolveParamValue = (index: number) => {
      const rawValue = paramValues[index];
      return rawValue !== undefined && rawValue !== ''
        ? rawValue
        : (paramDefs[index]?.defaultValue ?? '未填写');
    };
    const periodParamIndex = paramDefs.findIndex((param) =>
      (param.key || '').toLowerCase().includes('period'),
    );
    const periodValue = periodParamIndex >= 0 ? resolveParamValue(periodParamIndex) : '';
    const consumedParamIndexes = new Set<number>();

    let text = '';
    if (methodValue === 'Between') {
      text = `${leftLabel} 在 ${rightLabel} 和 ${extraLabel} 区间内`;
    } else if (methodValue === 'Outside') {
      text = `${leftLabel} 在 ${rightLabel} 和 ${extraLabel} 区间外`;
    } else if (methodValue === 'Rising' || methodValue === 'Falling') {
      text = `${leftLabel} ${methodLabel}`;
      if (periodValue) {
        text = `${text} ${periodValue}个周期`;
      }
      if (periodParamIndex >= 0) {
        consumedParamIndexes.add(periodParamIndex);
      }
    } else if (methodValue === 'AboveFor' || methodValue === 'BelowFor') {
      text = `${leftLabel} ${methodLabel} ${rightLabel}`;
      if (periodValue) {
        text = `${text} ${periodValue}个周期`;
      }
      if (periodParamIndex >= 0) {
        consumedParamIndexes.add(periodParamIndex);
      }
    } else {
      text = `${leftLabel} ${methodLabel}`;
      if (argsCount >= 2) {
        text = `${text} ${rightLabel}`;
      }
      if (argsCount >= 3) {
        text = `${text} ${extraLabel}`;
      }
    }

    const remainParams = paramDefs
      .map((param, index) => ({ param, index }))
      .filter(({ index }) => !consumedParamIndexes.has(index))
      .map(({ param, index }) => `${param.label}:${resolveParamValue(index)}`);
    if (remainParams.length > 0) {
      text = `${text}（${remainParams.join('，')}）`;
    }

    return text.replace(/\s{2,}/g, ' ').trim();
  };

  const conditionSummarySections = useMemo<ConditionSummarySection[]>(() => {
    const sections = [
      { id: 'open-long-filter', label: '开多筛选器' },
      { id: 'open-short-filter', label: '开空筛选器' },
      { id: 'open-long', label: '开多' },
      { id: 'open-short', label: '开空' },
      { id: 'close-long', label: '平多' },
      { id: 'close-short', label: '平空' },
    ];

    return sections.map((section) => {
      const container = conditionContainers.find((item) => item.id === section.id);
      const groups = container?.groups ?? [];
      const isFilterSection = FILTER_CONTAINER_IDS.has(section.id);
      const sectionEnabled = isFilterSection
        ? groups.some((group) => group.enabled)
        : container?.enabled ?? false;
      const groupSummaries = groups.map((group) => {
        const lines = group.conditions.map((condition) => buildConditionPreview(condition));
        return {
          title: `${group.name}${group.enabled ? '' : ' (未启用)'}`,
          conditions: lines,
        };
      });
      return {
        title: `${section.label} 共${groups.length}个条件组${sectionEnabled ? '' : ' (未启用)'}`,
        groups: groupSummaries.length > 0 ? groupSummaries : [{ title: '暂无条件组', conditions: [] }],
      };
    });
  }, [conditionContainers, indicatorValueMap, methodOptions, outputHintMap]);

  const usedIndicatorOutputs = useMemo(() => {
    const map = new Map<string, string>();
    const addIndicator = (ref?: StrategyValueRef | null) => {
      if (!ref) {
        return;
      }
      if ((ref.refType || '').toLowerCase() !== 'indicator') {
        return;
      }
      const paramsKey = ref.params && ref.params.length > 0 ? ref.params.join(',') : 'default';
      const key = [ref.indicator, ref.timeframe, ref.input, ref.output, ref.calcMode, paramsKey].join('|');
      if (!map.has(key)) {
        map.set(key, formatValueRefLabel(ref));
      }
    };

    conditionContainers.forEach((container) => {
      container.groups.forEach((group) => {
        group.conditions.forEach((condition) => {
          addIndicator(indicatorValueMap.get(condition.leftValueId)?.ref);
          if (condition.rightValueType === 'field') {
            addIndicator(indicatorValueMap.get(condition.rightValueId || '')?.ref);
          }
          if (condition.extraValueType === 'field') {
            addIndicator(indicatorValueMap.get(condition.extraValueId || '')?.ref);
          }
        });
      });
    });

    return Array.from(map.values());
  }, [conditionContainers, indicatorValueMap, outputHintMap]);

  const buildConstantValueRef = (
    rawValue: string | undefined,
    fallback?: StrategyValueRef,
  ): StrategyValueRef => {
    return {
      refType: 'Const',
      indicator: '',
      timeframe: fallback?.timeframe ?? '',
      input: (rawValue || '0').trim(),
      params: [],
      output: 'Value',
      offsetRange: [0, 0],
      calcMode: fallback?.calcMode || 'OnBarClose',
    };
  };

  const buildStrategyMethod = (condition: ConditionItem): StrategyMethodConfig | null => {
    const methodMeta = resolveMethodMeta(condition.method);
    const argsCount = methodMeta.argsCount ?? 2;
    const leftOption = indicatorValueMap.get(condition.leftValueId);
    if (argsCount >= 1 && !leftOption) {
      return null;
    }
    const args: StrategyValueRef[] = [];
    if (leftOption && argsCount >= 1) {
      args.push(leftOption.ref);
    }

    if (argsCount >= 2) {
      let rightRef: StrategyValueRef | null = null;
      const rightValueType = condition.rightValueType || 'number';
      if (rightValueType === 'number') {
        if (!condition.rightNumber || condition.rightNumber.trim() === '') {
          return null;
        }
        rightRef = buildConstantValueRef(condition.rightNumber, leftOption?.ref);
      } else {
        const rightOption = indicatorValueMap.get(condition.rightValueId || '');
        if (!rightOption) {
          return null;
        }
        rightRef = rightOption.ref;
      }
      if (rightRef) {
        args.push(rightRef);
      }
    }

    if (argsCount >= 3) {
      let extraRef: StrategyValueRef | null = null;
      const extraValueType = condition.extraValueType || 'number';
      if (extraValueType === 'number') {
        if (!condition.extraNumber || condition.extraNumber.trim() === '') {
          return null;
        }
        extraRef = buildConstantValueRef(condition.extraNumber, leftOption?.ref);
      } else {
        const extraOption = indicatorValueMap.get(condition.extraValueId || '');
        if (!extraOption) {
          return null;
        }
        extraRef = extraOption.ref;
      }
      if (extraRef) {
        args.push(extraRef);
      }
    }

    const paramDefs = methodMeta.params || [];
    const rawParamValues = condition.paramValues || [];
    const paramValues = paramDefs.map((def, index) => {
      const rawValue = rawParamValues[index];
      const value =
        rawValue !== undefined && rawValue !== ''
          ? rawValue
          : def.defaultValue ?? '';
      return String(value).trim();
    });
    const hasParam = paramValues.some((value) => value.length > 0);

    return {
      enabled: condition.enabled,
      required: condition.required,
      method: condition.method,
      args,
      param: hasParam ? paramValues : undefined,
    };
  };

  const validateBuiltConditionMethod = (
    condition: ConditionItem,
    methodConfig: StrategyMethodConfig,
  ): string | null => {
    const methodMeta = tryResolveMethodMeta(condition.method);
    if (!methodMeta) {
      return `不支持的条件方法：${condition.method}`;
    }

    const expectedArgsCount = methodMeta.argsCount ?? 2;
    const args = (methodConfig.args || []).filter(
      (arg): arg is StrategyValueRef => typeof arg === 'object' && arg !== null,
    );
    if (args.length < expectedArgsCount) {
      return '条件参数数量不足';
    }

    const semanticError = validateConditionArgsSemantics(args, expectedArgsCount, tradeDefaultTimeframe);
    if (semanticError) {
      return semanticError;
    }

    return null;
  };

  const buildConditionMethodFingerprint = (
    methodConfig: StrategyMethodConfig,
  ) => {
    const args = (methodConfig.args || []).filter(
      (arg): arg is StrategyValueRef => typeof arg === 'object' && arg !== null,
    );
    return buildConditionFingerprint(methodConfig.method, args, methodConfig.param, tradeDefaultTimeframe);
  };

  const validateConditionDraftByRules = (draft: ConditionItem): string | null => {
    const methodConfig = buildStrategyMethod(draft);
    if (!methodConfig) {
      return null;
    }
    return validateBuiltConditionMethod(draft, methodConfig);
  };

  const applyConditionQuickPatch = (
    containerId: string,
    groupId: string,
    conditionId: string,
    updater: (condition: ConditionItem) => ConditionItem | null,
    historyMessage?: string,
  ): boolean => {
    let matched = false;
    let changed = false;
    let ruleError = '';
    setConditionContainers((prev) =>
      prev.map((container) => {
        if (container.id !== containerId) {
          return container;
        }
        const groups = container.groups.map((group) => {
          if (group.id !== groupId) {
            return group;
          }
          const conditions = group.conditions.map((condition) => {
            if (condition.id !== conditionId) {
              return condition;
            }
            matched = true;
            const next = updater(condition);
            if (!next) {
              return condition;
            }
            const nextRuleError = validateConditionDraftByRules(next);
            if (nextRuleError) {
              ruleError = nextRuleError;
              return condition;
            }
            changed = true;
            return next;
          });
          return { ...group, conditions };
        });
        return { ...container, groups };
      }),
    );

    if (!matched) {
      error('目标条件不存在，请刷新后重试');
      return false;
    }
    if (ruleError) {
      error(ruleError);
      return false;
    }
    if (changed && historyMessage) {
      markHistoryAction(historyMessage);
    }
    return changed;
  };

  const quickAssignConditionMethod = (
    containerId: string,
    groupId: string,
    conditionId: string,
    method: string,
  ) => {
    const methodMeta = tryResolveMethodMeta(method);
    if (!methodMeta) {
      error('不支持的条件方法');
      return;
    }
    const argsCount = methodMeta.argsCount ?? 2;
    if (false && argsCount > 2) {
      error('当前拖拽修改暂不支持三参数条件');
      return;
    }

    applyConditionQuickPatch(containerId, groupId, conditionId, (condition) => {
      const rightMode = resolveArgValueMode(methodMeta, 1);
      const next: ConditionItem = {
        ...condition,
        method: methodMeta.value,
        paramValues: methodMeta.params?.map((param) => param.defaultValue || '') || [],
        extraValueType: 'number',
        extraValueId: '',
        extraNumber: '',
      };
      if (argsCount < 2) {
        next.rightValueType = 'number';
        next.rightValueId = '';
      } else if (rightMode === 'field') {
        next.rightValueType = 'field';
        next.rightValueId = next.rightValueId || indicatorOutputOptions[0]?.id || '';
      } else if (rightMode === 'number') {
        next.rightValueType = 'number';
        next.rightValueId = '';
        next.rightNumber = next.rightNumber && next.rightNumber.trim() ? next.rightNumber : '0';
      } else if (next.rightValueType === 'field') {
        next.rightValueId = next.rightValueId || indicatorOutputOptions[0]?.id || '';
      } else {
        next.rightValueType = 'field';
        next.rightValueId = indicatorOutputOptions[0]?.id || '';
      }
      if (argsCount >= 3) {
        const extraMode = resolveArgValueMode(methodMeta, 2);
        if (extraMode === 'field') {
          next.extraValueType = 'field';
          next.extraValueId = next.extraValueId || indicatorOutputOptions[0]?.id || '';
          next.extraNumber = '';
        } else if (extraMode === 'number') {
          next.extraValueType = 'number';
          next.extraValueId = '';
          next.extraNumber = next.extraNumber && next.extraNumber.trim() ? next.extraNumber : '0';
        } else if (next.extraValueType === 'field') {
          next.extraValueId = next.extraValueId || indicatorOutputOptions[0]?.id || '';
          next.extraNumber = '';
        } else {
          next.extraValueType = 'field';
          next.extraValueId = indicatorOutputOptions[0]?.id || '';
          next.extraNumber = '';
        }
      }
      return next;
    }, `修改条件操作符为 ${describeMethodForHistory(methodMeta.value)}`);
  };

  const quickAssignConditionValue = (
    containerId: string,
    groupId: string,
    conditionId: string,
    slot: 'left' | 'right',
    valueId: string,
    source?: {
      containerId: string;
      groupId: string;
      conditionId: string;
      slot: 'left' | 'right' | 'extra';
    },
  ) => {
    if (!indicatorValueMap.has(valueId)) {
      error('拖拽值无效，请重试');
      return;
    }

    const valueLabel = indicatorValueMap.get(valueId)?.fullLabel || indicatorValueMap.get(valueId)?.label || valueId;
    let degradeByRule = false;
    const sourceIsSameCondition = Boolean(
      source
      && source.containerId === containerId
      && source.groupId === groupId
      && source.conditionId === conditionId,
    );
    const changed = applyConditionQuickPatch(containerId, groupId, conditionId, (condition) => {
      const methodMeta = resolveMethodMeta(condition.method);
      const argsCount = methodMeta.argsCount ?? 2;
      const rightMode = resolveArgValueMode(methodMeta, 1);
      if (slot === 'left') {
        const fromRightToLeft = sourceIsSameCondition && source?.slot === 'right';
        const next: ConditionItem = fromRightToLeft
          ? (() => {
              const oldLeft = condition.leftValueId || '';
              const oldRight = condition.rightValueType === 'field' ? condition.rightValueId || '' : '';
              const movedValue = oldRight || valueId;
              if (!movedValue) {
                return condition;
              }
              const draft: ConditionItem = {
                ...condition,
                leftValueId: movedValue,
                rightValueType: 'field',
                rightValueId: oldLeft,
                rightNumber: '',
              };
              if (!oldLeft) {
                draft.rightValueId = '';
              }
              return draft;
            })()
          : {
              ...condition,
              leftValueId: valueId,
            };
        const leftRuleError = validateConditionDraftByRules(next);
        if (!leftRuleError) {
          return next;
        }
        degradeByRule = true;
        return {
          ...next,
          rightValueType: 'field',
          rightValueId: '',
          rightNumber: '',
        };
      }

      if (argsCount < 2) {
        error('该条件当前不支持右值字段');
        return null;
      }
      if (rightMode === 'number') {
        error('该条件右值仅支持数值，不支持字段拖拽');
        return null;
      }
      const fromLeftToRight = sourceIsSameCondition && source?.slot === 'left';
      const next: ConditionItem = fromLeftToRight
        ? (() => {
            const oldLeft = condition.leftValueId || '';
            const oldRight = condition.rightValueType === 'field' ? condition.rightValueId || '' : '';
            const movedValue = oldLeft || valueId;
            if (!movedValue) {
              return condition;
            }
            return {
              ...condition,
              leftValueId: oldRight,
              rightValueType: 'field',
              rightValueId: movedValue,
              rightNumber: '',
            };
          })()
        : {
            ...condition,
            rightValueType: 'field',
            rightValueId: valueId,
          };
      const nextRuleError = validateConditionDraftByRules(next);
      if (!nextRuleError) {
        return next;
      }
      degradeByRule = true;
      return {
        ...next,
        leftValueId: '',
      };
    }, `修改条件${slot === 'left' ? '左值' : '右值'}为 ${valueLabel}`);
    if (changed && degradeByRule) {
      success(`提示：该修改暂不合法，已自动将${slot === 'left' ? '右值' : '左值'}置为未配置，可继续编辑或按 Ctrl+Z 撤回。`);
    }
  };

  const quickAssignConditionNumber = (
    containerId: string,
    groupId: string,
    conditionId: string,
  ) => {
    applyConditionQuickPatch(containerId, groupId, conditionId, (condition) => {
      const methodMeta = resolveMethodMeta(condition.method);
      const argsCount = methodMeta.argsCount ?? 2;
      if (argsCount < 2) {
        error('该条件不支持右值配置');
        return null;
      }
      const rightMode = resolveArgValueMode(methodMeta, 1);
      if (rightMode === 'field') {
        error('该条件右值仅支持字段，不支持数值');
        return null;
      }
      return {
        ...condition,
        rightValueType: 'number',
        rightValueId: '',
        rightNumber: condition.rightNumber?.trim() ? condition.rightNumber : '0',
      };
    }, '将条件右值切换为数值');
  };

  const quickUpdateConditionRightNumber = (
    containerId: string,
    groupId: string,
    conditionId: string,
    value: string,
  ) => {
    const raw = (value || '').trim();
    if (raw && !/^-?\d*\.?\d*$/.test(raw)) {
      return;
    }

    let changed = false;
    setConditionContainers((prev) =>
      prev.map((container) => {
        if (container.id !== containerId) {
          return container;
        }
        return {
          ...container,
          groups: container.groups.map((group) => {
            if (group.id !== groupId) {
              return group;
            }
            return {
              ...group,
              conditions: group.conditions.map((condition) => {
                if (condition.id !== conditionId) {
                  return condition;
                }
                if (
                  condition.rightValueType === 'number'
                  && (condition.rightNumber || '') === value
                ) {
                  return condition;
                }
                changed = true;
                return {
                  ...condition,
                  rightValueType: 'number',
                  rightValueId: '',
                  rightNumber: value,
                };
              }),
            };
          }),
        };
      }),
    );
    if (changed) {
      markHistoryAction(`修改条件右值数值为 ${value || '空值'}`);
    }
  };

  const quickAssignConditionExtraValue = (
    containerId: string,
    groupId: string,
    conditionId: string,
    valueId: string,
    source?: {
      containerId: string;
      groupId: string;
      conditionId: string;
      slot: 'left' | 'right' | 'extra';
    },
  ) => {
    if (!indicatorValueMap.has(valueId)) {
      error('拖拽值无效，请重试');
      return;
    }

    const valueLabel = indicatorValueMap.get(valueId)?.fullLabel || indicatorValueMap.get(valueId)?.label || valueId;
    applyConditionQuickPatch(containerId, groupId, conditionId, (condition) => {
      const methodMeta = resolveMethodMeta(condition.method);
      const argsCount = methodMeta.argsCount ?? 2;
      if (argsCount < 3) {
        error('该条件当前不支持第三参数');
        return null;
      }
      const extraMode = resolveArgValueMode(methodMeta, 2);
      if (extraMode === 'number') {
        error('该条件第三参数仅支持数值，不支持字段拖拽');
        return null;
      }
      const sameCondition = Boolean(
        source
        && source.containerId === containerId
        && source.groupId === groupId
        && source.conditionId === conditionId,
      );
      const fromLeftOrRight = sameCondition && (source?.slot === 'left' || source?.slot === 'right');
      if (!fromLeftOrRight) {
        return {
          ...condition,
          extraValueType: 'field',
          extraValueId: valueId,
          extraNumber: '',
        };
      }
      if (source?.slot === 'left') {
        return {
          ...condition,
          leftValueId: condition.extraValueType === 'field' ? condition.extraValueId || '' : '',
          extraValueType: 'field',
          extraValueId: valueId,
          extraNumber: '',
        };
      }
      return {
        ...condition,
        rightValueType: 'field',
        rightValueId: condition.extraValueType === 'field' ? condition.extraValueId || '' : '',
        rightNumber: '',
        extraValueType: 'field',
        extraValueId: valueId,
        extraNumber: '',
      };
    }, `修改条件第三参数为 ${valueLabel}`);
  };

  const quickAssignConditionExtraNumber = (
    containerId: string,
    groupId: string,
    conditionId: string,
  ) => {
    applyConditionQuickPatch(containerId, groupId, conditionId, (condition) => {
      const methodMeta = resolveMethodMeta(condition.method);
      const argsCount = methodMeta.argsCount ?? 2;
      if (argsCount < 3) {
        error('该条件当前不支持第三参数');
        return null;
      }
      const extraMode = resolveArgValueMode(methodMeta, 2);
      if (extraMode === 'field') {
        error('该条件第三参数仅支持字段，不支持数值');
        return null;
      }
      return {
        ...condition,
        extraValueType: 'number',
        extraValueId: '',
        extraNumber: condition.extraNumber?.trim() ? condition.extraNumber : '0',
      };
    }, '将条件第三参数切换为数值');
  };

  const quickUpdateConditionExtraNumber = (
    containerId: string,
    groupId: string,
    conditionId: string,
    value: string,
  ) => {
    const raw = (value || '').trim();
    if (raw && !/^-?\d*\.?\d*$/.test(raw)) {
      return;
    }

    let changed = false;
    setConditionContainers((prev) =>
      prev.map((container) => {
        if (container.id !== containerId) {
          return container;
        }
        return {
          ...container,
          groups: container.groups.map((group) => {
            if (group.id !== groupId) {
              return group;
            }
            return {
              ...group,
              conditions: group.conditions.map((condition) => {
                if (condition.id !== conditionId) {
                  return condition;
                }
                if (
                  condition.extraValueType === 'number'
                  && (condition.extraNumber || '') === value
                ) {
                  return condition;
                }
                changed = true;
                return {
                  ...condition,
                  extraValueType: 'number',
                  extraValueId: '',
                  extraNumber: value,
                };
              }),
            };
          }),
        };
      }),
    );
    if (changed) {
      markHistoryAction(`修改条件第三参数数值为 ${value || '空值'}`);
    }
  };

  const quickUpdateConditionParamValue = (
    containerId: string,
    groupId: string,
    conditionId: string,
    paramIndex: number,
    value: string,
  ) => {
    let changed = false;
    setConditionContainers((prev) =>
      prev.map((container) => {
        if (container.id !== containerId) {
          return container;
        }
        return {
          ...container,
          groups: container.groups.map((group) => {
            if (group.id !== groupId) {
              return group;
            }
            return {
              ...group,
              conditions: group.conditions.map((condition) => {
                if (condition.id !== conditionId) {
                  return condition;
                }
                const nextValues = [...(condition.paramValues || [])];
                if ((nextValues[paramIndex] || '') === value) {
                  return condition;
                }
                nextValues[paramIndex] = value;
                changed = true;
                return {
                  ...condition,
                  paramValues: nextValues,
                };
              }),
            };
          }),
        };
      }),
    );
    if (changed) {
      markHistoryAction(`修改条件参数${paramIndex + 1}为 ${value || '空值'}`);
    }
  };

  const createQuickConditionDraft = (methodMeta: MethodOption): ConditionItem => {
    const argsCount = methodMeta.argsCount ?? 2;
    const leftValueId = indicatorOutputOptions[0]?.id || '';
    const candidateFieldIds = indicatorOutputOptions.map((item) => item.id);
    const next: ConditionItem = {
      id: generateId(),
      enabled: true,
      required: false,
      method: methodMeta.value,
      leftValueId,
      rightValueType: 'number',
      rightValueId: '',
      rightNumber: '0',
      extraValueType: 'number',
      extraValueId: '',
      extraNumber: '0',
      paramValues: methodMeta.params?.map((param) => param.defaultValue || '') || [],
    };

    if (argsCount >= 2) {
      const rightMode = resolveArgValueMode(methodMeta, 1);
      const rightCandidate = candidateFieldIds.find((item) => item !== leftValueId) || '';
      if (rightMode === 'field') {
        next.rightValueType = 'field';
        next.rightValueId = rightCandidate;
        next.rightNumber = '';
      } else if (rightMode === 'number') {
        next.rightValueType = 'number';
        next.rightValueId = '';
        next.rightNumber = '0';
      } else if (rightCandidate) {
        next.rightValueType = 'field';
        next.rightValueId = rightCandidate;
        next.rightNumber = '';
      } else {
        next.rightValueType = 'field';
        next.rightValueId = '';
        next.rightNumber = '';
      }
    }

    if (argsCount >= 3) {
      const extraMode = resolveArgValueMode(methodMeta, 2);
      const unavailableIds = new Set<string>([
        leftValueId,
        next.rightValueType === 'field' ? next.rightValueId || '' : '',
      ]);
      const extraCandidate = candidateFieldIds.find((item) => !unavailableIds.has(item)) || '';
      if (extraMode === 'field') {
        next.extraValueType = 'field';
        next.extraValueId = extraCandidate;
        next.extraNumber = '';
      } else if (extraMode === 'number') {
        next.extraValueType = 'number';
        next.extraValueId = '';
        next.extraNumber = '0';
      } else if (extraCandidate) {
        next.extraValueType = 'field';
        next.extraValueId = extraCandidate;
        next.extraNumber = '';
      } else {
        next.extraValueType = 'field';
        next.extraValueId = '';
        next.extraNumber = '';
      }
    }

    return next;
  };

  const buildUnconfiguredQuickConditionDraft = (
    methodMeta: MethodOption,
    base?: ConditionItem,
  ): ConditionItem => {
    const argsCount = methodMeta.argsCount ?? 2;
    const rightMode = resolveArgValueMode(methodMeta, 1);
    const extraMode = resolveArgValueMode(methodMeta, 2);
    const next: ConditionItem = {
      id: base?.id || generateId(),
      enabled: base?.enabled ?? true,
      required: base?.required ?? false,
      method: methodMeta.value,
      leftValueId: '',
      rightValueType: 'field',
      rightValueId: '',
      rightNumber: '',
      extraValueType: 'field',
      extraValueId: '',
      extraNumber: '',
      paramValues: methodMeta.params?.map((param) => param.defaultValue || '') || [],
    };

    if (argsCount >= 2) {
      if (rightMode === 'number') {
        next.rightValueType = 'number';
      } else {
        next.rightValueType = 'field';
      }
    }

    if (argsCount >= 3) {
      if (extraMode === 'number') {
        next.extraValueType = 'number';
      } else {
        next.extraValueType = 'field';
      }
    } else {
      next.extraValueType = 'number';
      next.extraValueId = '';
      next.extraNumber = '';
    }

    return next;
  };

  const quickCreateCondition = (containerId: string, groupId: string | null, method: string) => {
    const methodMeta = tryResolveMethodMeta(method);
    if (!methodMeta) {
      error('不支持的条件方法');
      return;
    }

    let draft = createQuickConditionDraft(methodMeta);
    let degradedBySemantic = false;
    const semanticError = validateConditionDraftByRules(draft);
    if (semanticError) {
      draft = buildUnconfiguredQuickConditionDraft(methodMeta, draft);
      degradedBySemantic = true;
    }

    let matched = false;
    let changed = false;
    let createdGroupName = '';
    let targetGroupName = '';
    let ruleError = '';
    setConditionContainers((prev) =>
      prev.map((container) => {
        if (container.id !== containerId) {
          return container;
        }
        matched = true;

        let targetGroupId = groupId;
        const nextGroups = [...container.groups];
        if (!targetGroupId) {
          if (nextGroups.length === 0) {
            if (container.groups.length >= MAX_GROUPS_PER_CONTAINER) {
              ruleError = `${container.title}最多只能创建三个条件组`;
              return container;
            }
            const newGroup: ConditionGroup = {
              id: generateId(),
              name: `条件组${nextGroups.length + 1}`,
              enabled: true,
              required: false,
              conditions: [],
            };
            nextGroups.push(newGroup);
            targetGroupId = newGroup.id;
            createdGroupName = newGroup.name;
          } else {
            targetGroupId = nextGroups[0].id;
          }
        }

        const groupIndex = nextGroups.findIndex((group) => group.id === targetGroupId);
        if (groupIndex < 0) {
          ruleError = '目标条件组不存在，请刷新后重试';
          return container;
        }

        const group = nextGroups[groupIndex];
        targetGroupName = group.name;
        if (group.conditions.length >= MAX_CONDITIONS_PER_GROUP) {
          ruleError = `${container.title}-${group.name}最多只能创建6个条件判断`;
          return container;
        }

        const methodConfig = buildStrategyMethod(draft);
        if (methodConfig) {
          const nextFingerprint = buildConditionMethodFingerprint(methodConfig);
          const hasDuplicate = group.conditions.some((condition) => {
            const currentMethod = buildStrategyMethod(condition);
            if (!currentMethod) {
              return false;
            }
            return buildConditionMethodFingerprint(currentMethod) === nextFingerprint;
          });
          if (hasDuplicate) {
            ruleError = `${container.title}-${group.name}存在重复条件`;
            return container;
          }
        }

        nextGroups[groupIndex] = {
          ...group,
          conditions: [...group.conditions, draft],
        };
        changed = true;
        return {
          ...container,
          enabled: true,
          groups: nextGroups,
        };
      }),
    );

    if (!matched) {
      error('目标条件容器不存在，请刷新后重试');
      return;
    }
    if (ruleError) {
      error(ruleError);
      return;
    }
    if (changed) {
      const methodLabel = describeMethodForHistory(methodMeta.value);
      const groupText = createdGroupName
        ? `${createdGroupName}（自动创建）`
        : targetGroupName || '目标条件组';
      markHistoryAction(`在${groupText}新增条件 ${methodLabel}`);
      if (degradedBySemantic) {
        success('已创建条件：自动补全失败，参与值已置为未配置，请继续拖拽或编辑补全');
      }
    }
  };

  const validateConditionContainersBeforeSubmit = (): string | null => {
    let totalConditions = 0;
    for (const container of conditionContainers) {
      if (container.groups.length > MAX_GROUPS_PER_CONTAINER) {
        return `${container.title}的条件组不能超过${MAX_GROUPS_PER_CONTAINER}个`;
      }

      for (const group of container.groups) {
        if (group.conditions.length > MAX_CONDITIONS_PER_GROUP) {
          return `${container.title}-${group.name}的条件数量不能超过${MAX_CONDITIONS_PER_GROUP}个`;
        }

        const duplicateGuard = new Set<string>();
        for (let index = 0; index < group.conditions.length; index += 1) {
          totalConditions += 1;
          if (totalConditions > MAX_TOTAL_CONDITIONS) {
            return `条件总数不能超过${MAX_TOTAL_CONDITIONS}个`;
          }

          const condition = group.conditions[index];
          const methodConfig = buildStrategyMethod(condition);
          if (!methodConfig) {
            return `${container.title}-${group.name} 第${index + 1}条条件配置不完整`;
          }

          const semanticError = validateBuiltConditionMethod(condition, methodConfig);
          if (semanticError) {
            return `${container.title}-${group.name} 第${index + 1}条条件不合法：${semanticError}`;
          }

          const fingerprint = buildConditionMethodFingerprint(methodConfig);
          if (duplicateGuard.has(fingerprint)) {
            return `${container.title}-${group.name} 存在重复条件，请删除重复项`;
          }
          duplicateGuard.add(fingerprint);
        }
      }
    }

    return null;
  };

  const buildConditionGroupConfig = (group: ConditionGroup): ConditionGroupConfig => {
    const conditions = group.conditions
      .map(buildStrategyMethod)
      .filter((item): item is StrategyMethodConfig => Boolean(item));
    const enabledConditions = conditions.filter((condition) => condition.enabled);
    const optionalConditions = enabledConditions.filter((condition) => !condition.required);
    // 空条件组不应阻断执行，保持与本地回测引擎一致。
    const minPassConditions =
      enabledConditions.length === 0 ? 0 : optionalConditions.length === 0 ? 0 : 1;
    return {
      enabled: group.enabled,
      minPassConditions,
      conditions,
    };
  };

  const buildConditionGroupSetConfig = (container: ConditionContainer): ConditionGroupSetConfig => {
    const groups = container.groups.map(buildConditionGroupConfig);
    const requiredGroupsCount = container.groups.filter((group) => group.enabled && group.required).length;
    const minPassGroups = requiredGroupsCount > 0 ? requiredGroupsCount : 1;
    return {
      enabled: container.enabled,
      minPassGroups,
      groups,
    };
  };

  const buildFilterGroupSetConfig = (containerId: string): ConditionGroupSetConfig | undefined => {
    const container = conditionContainers.find((item) => item.id === containerId);
    if (!container) {
      return undefined;
    }

    const enabledGroups = container.groups.filter((group) => group.enabled);
    if (enabledGroups.length === 0) {
      return undefined;
    }

    const groups = enabledGroups.map(buildConditionGroupConfig);
    const requiredGroupsCount = enabledGroups.filter((group) => group.required).length;
    const minPassGroups = requiredGroupsCount > 0 ? requiredGroupsCount : 1;
    return {
      enabled: true,
      minPassGroups,
      groups,
    };
  };

  const buildActionSetConfig = (action: string): ActionSetConfig => ({
    enabled: true,
    minPassConditions: 1,
    conditions: [
      {
        enabled: true,
        required: false,
        method: 'MakeTrade',
        args: [action],
      },
    ],
  });

  const buildBranchConfig = (
    containerId: string,
    action: string,
    filterContainerId?: string,
  ): StrategyLogicBranchConfig => {
    const container = conditionContainers.find((item) => item.id === containerId);
    const checks = container
      ? buildConditionGroupSetConfig(container)
      : { enabled: false, minPassGroups: 1, groups: [] };
    const filters = filterContainerId ? buildFilterGroupSetConfig(filterContainerId) : undefined;
    return {
      enabled: container?.enabled ?? false,
      minPassConditionContainer: 1,
      containers: [{ checks }],
      filters,
      onPass: buildActionSetConfig(action),
    };
  };

  const buildStrategyConfig = (): StrategyConfig => ({
    trade: {
      exchange: tradeConfig.exchange,
      symbol: tradeConfig.symbol,
      timeframeSec: tradeConfig.timeframeSec,
      positionMode: tradeConfig.positionMode,
      openConflictPolicy: tradeConfig.openConflictPolicy,
      sizing: { ...tradeConfig.sizing },
      risk: {
        takeProfitPct: tradeConfig.risk.takeProfitPct,
        stopLossPct: tradeConfig.risk.stopLossPct,
        trailing: { ...tradeConfig.risk.trailing },
      },
    },
    logic: {
      entry: {
        long: buildBranchConfig('open-long', 'Long', 'open-long-filter'),
        short: buildBranchConfig('open-short', 'Short', 'open-short-filter'),
      },
      exit: {
        long: buildBranchConfig('close-long', 'CloseLong'),
        short: buildBranchConfig('close-short', 'CloseShort'),
      },
    },
    runtime: runtimeConfig,
  });

  const configPreview = useMemo(() => {
    return buildStrategyConfig();
  }, [conditionContainers, tradeConfig, runtimeConfig, indicatorValueMap]);

  const logicPreview = useMemo(() => {
    return JSON.stringify(configPreview.logic, null, 2);
  }, [configPreview]);

  useEffect(() => {
    if (!isConfigReviewOpen) {
      return;
    }
    setConfigReviewData({
      configJson: deepCloneJson(configPreview),
      logicPreview,
      usedIndicatorOutputs: [...usedIndicatorOutputs],
      conditionSummarySections: deepCloneJson(conditionSummarySections),
    });
  }, [
    conditionSummarySections,
    configPreview,
    isConfigReviewOpen,
    logicPreview,
    usedIndicatorOutputs,
  ]);

  const handleConfirmGenerate = async () => {
    if (isSubmitting) {
      return;
    }
    const trimmedName = strategyName.trim();
    if (!trimmedName) {
      error('请输入策略名称');
      return;
    }
    if (exchangeApiKeyOptions.length > 1 && !selectedExchangeApiKeyId) {
      error('请选择交易所API');
      return;
    }
    const conditionValidationError = validateConditionContainersBeforeSubmit();
    if (conditionValidationError) {
      error(conditionValidationError);
      return;
    }
    const runtimeError = validateRuntimeConfig(runtimeConfig);
    if (runtimeError) {
      error(runtimeError);
      return;
    }
    setIsSubmitting(true);
    try {
      await onSubmit({
        name: trimmedName,
        description: strategyDescription.trim(),
        configJson: configReviewData?.configJson || configPreview,
        exchangeApiKeyId: selectedExchangeApiKeyId ?? undefined,
      });
      success(successMessage);
      window.dispatchEvent(new CustomEvent('strategy:changed', { detail: { skipReload: true } }));
      closeConfigReview();
      onClose?.();
    } catch (err) {
      const message = err instanceof Error ? err.message : errorMessage;
      error(message || errorMessage);
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleUndo = () => {
    setHistoryPast((prev) => {
      if (prev.length === 0) {
        success('没有可撤销的操作');
        return prev;
      }
      const nextPast = [...prev];
      const record = nextPast.pop() as StrategyHistoryRecord;
      setHistoryFuture((future) => [record, ...future]);
      applyHistorySnapshot(record.before);
      success(`撤销：${record.undoMessage}`);
      return nextPast;
    });
  };

  const handleRedo = () => {
    setHistoryFuture((prev) => {
      if (prev.length === 0) {
        success('没有可重做的操作');
        return prev;
      }
      const [record, ...rest] = prev;
      setHistoryPast((past) => [...past.slice(-(HISTORY_LIMIT - 1)), record]);
      applyHistorySnapshot(record.after);
      success(`还原：${record.redoMessage}`);
      return rest;
    });
  };

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      const ctrlOrCmd = event.ctrlKey || event.metaKey;
      if (!ctrlOrCmd || event.altKey) {
        return;
      }
      if (isEditableElementTarget(event.target)) {
        return;
      }
      const key = event.key.toLowerCase();
      if (key === 'z' && !event.shiftKey) {
        event.preventDefault();
        handleUndo();
        return;
      }
      if (key === 'y' || (key === 'z' && event.shiftKey)) {
        event.preventDefault();
        handleRedo();
      }
    };

    window.addEventListener('keydown', onKeyDown);
    return () => {
      window.removeEventListener('keydown', onKeyDown);
    };
  }, [historyPast.length, historyFuture.length]);

  const addConditionGroup = (containerId: string) => {
    const targetContainer = conditionContainers.find((container) => container.id === containerId);
    if (!targetContainer) {
      error('目标条件容器不存在，请刷新后重试');
      return;
    }
    if (targetContainer.groups.length >= MAX_GROUPS_PER_CONTAINER) {
      error(`${targetContainer.title}最多只能创建三个条件组`);
      return;
    }
    const nextIndex = targetContainer.groups.length + 1;
    const newGroup: ConditionGroup = {
      id: generateId(),
      name: `条件组${nextIndex}`,
      enabled: true,
      required: false,
      conditions: [],
    };
    markHistoryAction(`在${targetContainer.title}新增条件组 ${newGroup.name}`);
    setConditionContainers((prev) =>
      prev.map((container) => {
        if (container.id !== containerId) {
          return container;
        }
        return { ...container, enabled: true, groups: [...container.groups, newGroup] };
      }),
    );
  };

  const toggleGroupFlag = (containerId: string, groupId: string, key: 'enabled' | 'required') => {
    const targetContainer = conditionContainers.find((container) => container.id === containerId);
    const targetGroup = targetContainer?.groups.find((group) => group.id === groupId);
    if (!targetContainer || !targetGroup) {
      error('目标条件组不存在，请刷新后重试');
      return;
    }
    const nextValue = !targetGroup[key];
    markHistoryAction(
      `切换${targetContainer.title}-${targetGroup.name}的${key === 'enabled' ? '启用' : '必选'}为${nextValue ? '开启' : '关闭'}`,
    );
    setConditionContainers((prev) =>
      prev.map((container) => {
        if (container.id !== containerId) {
          return container;
        }
        const groupIndex = container.groups.findIndex((group) => group.id === groupId);
        if (groupIndex < 0) {
          return container;
        }
        const group = container.groups[groupIndex];
        const nextValue = !group[key];
        const nextGroup = { ...group, [key]: nextValue };
        const nextGroups = container.groups.map((item, idx) =>
          idx === groupIndex ? nextGroup : item,
        );
        const reordered = key === 'required' && nextValue ? promoteToTop(nextGroups, groupIndex) : nextGroups;
        return { ...container, groups: reordered };
      }),
    );
  };

  const toggleConditionFlag = (
    containerId: string,
    groupId: string,
    conditionId: string,
    key: 'enabled' | 'required',
  ) => {
    const targetContainer = conditionContainers.find((container) => container.id === containerId);
    const targetGroup = targetContainer?.groups.find((group) => group.id === groupId);
    const targetCondition = targetGroup?.conditions.find((condition) => condition.id === conditionId);
    if (!targetContainer || !targetGroup || !targetCondition) {
      error('目标条件不存在，请刷新后重试');
      return;
    }
    const nextValue = !targetCondition[key];
    markHistoryAction(
      `切换${targetContainer.title}-${targetGroup.name}条件${describeMethodForHistory(targetCondition.method)}的${key === 'enabled' ? '启用' : '必选'}为${nextValue ? '开启' : '关闭'}`,
    );
    setConditionContainers((prev) =>
      prev.map((container) => {
        if (container.id !== containerId) {
          return container;
        }
        const groups = container.groups.map((group) => {
          if (group.id !== groupId) {
            return group;
          }
          const conditionIndex = group.conditions.findIndex((condition) => condition.id === conditionId);
          if (conditionIndex < 0) {
            return group;
          }
          const condition = group.conditions[conditionIndex];
          const nextValue = !condition[key];
          const nextCondition = { ...condition, [key]: nextValue };
          const nextConditions = group.conditions.map((item, idx) =>
            idx === conditionIndex ? nextCondition : item,
          );
          const reordered =
            key === 'required' && nextValue ? promoteToTop(nextConditions, conditionIndex) : nextConditions;
          return { ...group, conditions: reordered };
        });
        return { ...container, groups };
      }),
    );
  };

  const removeGroup = (containerId: string, groupId: string) => {
    const targetContainer = conditionContainers.find((container) => container.id === containerId);
    const targetGroup = targetContainer?.groups.find((group) => group.id === groupId);
    if (!targetContainer || !targetGroup) {
      error('目标条件组不存在，请刷新后重试');
      return;
    }
    markHistoryAction(`删除${targetContainer.title}中的条件组 ${targetGroup.name}`);
    setConditionContainers((prev) =>
      prev.map((container) => {
        if (container.id !== containerId) {
          return container;
        }
        return { ...container, groups: container.groups.filter((group) => group.id !== groupId) };
      }),
    );
  };

  const removeCondition = (containerId: string, groupId: string, conditionId: string) => {
    const targetContainer = conditionContainers.find((container) => container.id === containerId);
    const targetGroup = targetContainer?.groups.find((group) => group.id === groupId);
    const targetCondition = targetGroup?.conditions.find((condition) => condition.id === conditionId);
    if (!targetContainer || !targetGroup || !targetCondition) {
      error('目标条件不存在，请刷新后重试');
      return;
    }
    markHistoryAction(`删除${targetContainer.title}-${targetGroup.name}条件 ${describeMethodForHistory(targetCondition.method)}`);
    setConditionContainers((prev) =>
      prev.map((container) => {
        if (container.id !== containerId) {
          return container;
        }
        const groups = container.groups.map((group) => {
          if (group.id !== groupId) {
            return group;
          }
          return {
            ...group,
            conditions: group.conditions.filter((condition) => condition.id !== conditionId),
          };
        });
        return { ...container, groups };
      }),
    );
  };

  const openConditionModal = (containerId: string, groupId: string, conditionId?: string) => {
    setConditionError('');
    setConditionEditTarget({ containerId, groupId, conditionId });

    if (conditionId) {
      const container = conditionContainers.find((item) => item.id === containerId);
      const group = container?.groups.find((item) => item.id === groupId);
      const condition = group?.conditions.find((item) => item.id === conditionId);
      if (condition) {
        setConditionDraft({ ...condition });
        setIsConditionModalOpen(true);
        return;
      }
    }

    const groupLimitContainer = conditionContainers.find((container) => container.id === containerId);
    const groupLimit = groupLimitContainer?.groups.find((group) => group.id === groupId);
    if (!conditionId && groupLimit && groupLimit.conditions.length >= MAX_CONDITIONS_PER_GROUP) {
      error('最多只能创建6个条件判断');
      return;
    }

    const defaultLeft = indicatorOutputOptions[0]?.id || '';
    const defaultMethod = methodOptions[0];
    const defaultParams = defaultMethod.params?.map((param) => param.defaultValue || '') || [];
    setConditionDraft({
      id: generateId(),
      enabled: true,
      required: false,
      method: defaultMethod.value,
      leftValueId: defaultLeft,
      rightValueType: 'number',
      rightValueId: '',
      rightNumber: '',
      extraValueType: 'number',
      extraValueId: '',
      extraNumber: '',
      paramValues: defaultParams,
    });
    setIsConditionModalOpen(true);
  };

  const closeConditionModal = () => {
    setIsConditionModalOpen(false);
    setConditionDraft(null);
    setConditionEditTarget(null);
    setConditionError('');
  };

  const conditionRuleError = conditionDraft ? validateConditionDraftByRules(conditionDraft) || '' : '';

  const handleSaveCondition = () => {
    if (!conditionDraft || !conditionEditTarget) {
      return;
    }
    const methodMeta = resolveMethodMeta(conditionDraft.method);
    const argsCount = methodMeta.argsCount ?? 2;
    const paramDefs = methodMeta.params || [];

    if (argsCount >= 1 && !conditionDraft.leftValueId) {
      setConditionError('请选择字段');
      return;
    }
    if (argsCount >= 2 && conditionDraft.rightValueType === 'field' && !conditionDraft.rightValueId) {
      setConditionError('请选择比较字段');
      return;
    }
    if (argsCount >= 2 && conditionDraft.rightValueType === 'number') {
      const numberValue = conditionDraft.rightNumber?.trim() || '';
      if (!numberValue || Number.isNaN(Number(numberValue))) {
        setConditionError('请输入有效数值');
        return;
      }
    }
    if (argsCount >= 3 && conditionDraft.extraValueType === 'field' && !conditionDraft.extraValueId) {
      setConditionError('请选择第三参数字段');
      return;
    }
    if (argsCount >= 3 && conditionDraft.extraValueType === 'number') {
      const numberValue = conditionDraft.extraNumber?.trim() || '';
      if (!numberValue || Number.isNaN(Number(numberValue))) {
        setConditionError('请输入有效的第三参数数值');
        return;
      }
    }

    if (paramDefs.length > 0) {
      const paramValues = conditionDraft.paramValues || [];
      for (let i = 0; i < paramDefs.length; i += 1) {
        const def = paramDefs[i];
        const value = (paramValues[i] || def.defaultValue || '').trim();
        if (def.required && !value) {
          setConditionError(`请填写${def.label}`);
          return;
        }
        if (value && Number.isNaN(Number(value))) {
          setConditionError(`${def.label}必须为数值`);
          return;
        }
      }
    }

    const methodConfig = buildStrategyMethod(conditionDraft);
    if (!methodConfig) {
      setConditionError('条件参数不完整，请检查后重试');
      return;
    }

    const semanticError = validateBuiltConditionMethod(conditionDraft, methodConfig);
    if (semanticError) {
      setConditionError(semanticError);
      return;
    }

    const targetContainer = conditionContainers.find((container) => container.id === conditionEditTarget.containerId);
    const targetGroup = targetContainer?.groups.find((group) => group.id === conditionEditTarget.groupId);
    if (!targetContainer || !targetGroup) {
      setConditionError('目标条件组不存在，请刷新后重试');
      return;
    }

    const nextFingerprint = buildConditionMethodFingerprint(methodConfig);
    const duplicateInGroup = targetGroup.conditions.some((condition) => {
      if (condition.id === conditionEditTarget.conditionId) {
        return false;
      }
      const currentMethod = buildStrategyMethod(condition);
      if (!currentMethod) {
        return false;
      }
      return buildConditionMethodFingerprint(currentMethod) === nextFingerprint;
    });
    if (duplicateInGroup) {
      setConditionError('同一条件组内不允许重复条件');
      return;
    }

    const saveActionLabel = conditionEditTarget.conditionId ? '修改' : '新增';
    markHistoryAction(
      `${saveActionLabel}${targetContainer.title}-${targetGroup.name}条件 ${describeMethodForHistory(conditionDraft.method)}`,
    );

    setConditionContainers((prev) =>
      prev.map((container) => {
        if (container.id !== conditionEditTarget.containerId) {
          return container;
        }
        const groups = container.groups.map((group) => {
          if (group.id !== conditionEditTarget.groupId) {
            return group;
          }
          const isEditing = Boolean(conditionEditTarget.conditionId);
          const nextConditions = isEditing
            ? group.conditions.map((condition) =>
                condition.id === conditionEditTarget.conditionId ? conditionDraft : condition,
              )
            : [...group.conditions, conditionDraft];
          const currentIndex = nextConditions.findIndex((condition) => condition.id === conditionDraft.id);
          const conditions =
            conditionDraft.required && currentIndex >= 0
              ? promoteToTop(nextConditions, currentIndex)
              : nextConditions;
          return { ...group, conditions };
        });
        return { ...container, enabled: true, groups };
      }),
    );
    closeConditionModal();
  };

  const renderToggle = (checked: boolean, onChange: () => void, label: string) => (
    <div className="condition-toggle-item">
      <span className="condition-toggle-label">{label}</span>
      <label className="condition-toggle">
        <input type="checkbox" checked={checked} onChange={onChange} />
        <span className="condition-toggle-slider" />
      </label>
    </div>
  );

  return (
    <>
      {!openConfigDirectly && (
        <StrategyWorkbench
          selectedIndicators={selectedIndicators}
          formatIndicatorName={formatIndicatorName}
          onOpenIndicatorGenerator={openCreateIndicator}
          onEditIndicator={requestEditIndicator}
          onRemoveIndicator={requestRemoveIndicator}
          logicContainers={logicContainers}
          filterContainers={filterContainers}
          maxGroupsPerContainer={MAX_GROUPS_PER_CONTAINER}
          onAddConditionGroup={addConditionGroup}
          onToggleGroupFlag={toggleGroupFlag}
          onOpenConditionModal={openConditionModal}
          onRemoveGroup={removeGroup}
          onToggleConditionFlag={toggleConditionFlag}
          onRemoveCondition={removeCondition}
          renderToggle={renderToggle}
          onClose={() => onClose?.()}
          onOpenExport={openExportReview}
          exchangeOptions={exchangeOptions}
          selectedExchange={tradeConfig.exchange}
          onExchangeChange={handleExchangeChange}
          symbolOptions={symbolOptions}
          selectedSymbol={tradeConfig.symbol}
          onSymbolChange={handleSymbolChange}
          timeframeOptions={timeframeOptions}
          selectedTimeframeSec={tradeConfig.timeframeSec}
          onTimeframeChange={handleTimeframeChange}
          takeProfitPct={tradeConfig.risk.takeProfitPct}
          stopLossPct={tradeConfig.risk.stopLossPct}
          leverage={tradeConfig.sizing.leverage}
          orderQty={tradeConfig.sizing.orderQty}
          onTakeProfitPctChange={(value) => updateTradeRisk('takeProfitPct', value)}
          onStopLossPctChange={(value) => updateTradeRisk('stopLossPct', value)}
          onLeverageChange={(value) => updateTradeSizing('leverage', value)}
          onOrderQtyChange={(value) => updateTradeSizing('orderQty', value)}
          indicatorOutputGroups={indicatorOutputGroups}
          methodOptions={methodOptions}
          onQuickAssignConditionMethod={quickAssignConditionMethod}
          onQuickAssignConditionValue={quickAssignConditionValue}
          onQuickAssignConditionNumber={quickAssignConditionNumber}
          onQuickUpdateConditionRightNumber={quickUpdateConditionRightNumber}
          onQuickAssignConditionExtraValue={quickAssignConditionExtraValue}
          onQuickAssignConditionExtraNumber={quickAssignConditionExtraNumber}
          onQuickUpdateConditionExtraNumber={quickUpdateConditionExtraNumber}
          onQuickUpdateConditionParamValue={quickUpdateConditionParamValue}
          onQuickUpdateIndicatorInput={quickUpdateIndicatorInput}
          onQuickEditIndicatorParams={requestQuickEditIndicatorParams}
          onQuickCreateCondition={quickCreateCondition}
        />
      )}
      <Dialog
        open={Boolean(indicatorAction)}
        onClose={closeIndicatorActionDialog}
        title={
          indicatorAction?.action === 'edit'
            ? '修改指标确认'
            : indicatorAction?.action === 'quick-param-edit'
              ? '修改指标参数确认'
            : indicatorAction?.action === 'quick-input'
              ? '修改输入源确认'
              : '移除指标确认'
        }
        cancelText="取消"
        confirmText={
          indicatorAction?.action === 'remove'
            ? '确认移除'
            : '继续修改'
        }
        onCancel={closeIndicatorActionDialog}
        onConfirm={confirmIndicatorAction}
        className="indicator-usage-dialog"
        footer={
          indicatorAction?.action === 'quick-input' ? (
            <>
              <button
                className="ui-dialog__button ui-dialog__button--cancel"
                onClick={closeIndicatorActionDialog}
              >
                取消
              </button>
              <button
                className="ui-dialog__button ui-dialog__button--confirm"
                onClick={confirmIndicatorAction}
              >
                继续修改
              </button>
              <button
                className="ui-dialog__button ui-dialog__button--confirm indicator-usage-dialog__confirm-once"
                onClick={confirmIndicatorActionAndSkipPrompt}
              >
                修改且本次不再提示
              </button>
            </>
          ) : undefined
        }
      >
        {indicatorAction && (
          <div className="indicator-usage-dialog__content">
            <div className="indicator-usage-dialog__title">
              指标 {formatIndicatorName(indicatorAction.indicator)}
              {indicatorAction.action === 'quick-input' && indicatorAction.pendingInputLabel
                ? ` 将改为输入源 ${indicatorAction.pendingInputLabel}`
                : ''}
              已在以下位置使用：
            </div>
            <div className="indicator-usage-dialog__hint">
              {indicatorAction.action === 'remove'
                ? '移除会清空对应的引用字段。'
                : indicatorAction.action === 'quick-param-edit'
                  ? '参数修改将同步影响这些引用。'
                  : '修改将同步影响这些引用。'}
            </div>
            <ul className="indicator-usage-dialog__list">
              {indicatorAction.usages.map((usage) => (
                <li key={usage.id} className="indicator-usage-dialog__item">
                  <div className="indicator-usage-dialog__item-title">
                    {usage.containerTitle} / {usage.groupTitle}
                  </div>
                  <div className="indicator-usage-dialog__item-meta">
                    {usage.positionLabel} · 输出：{usage.outputLabel}
                  </div>
                </li>
              ))}
            </ul>
          </div>
        )}
      </Dialog>
      <IndicatorGeneratorSelector
        open={isIndicatorGeneratorOpen}
        onClose={closeIndicatorDialog}
        onGenerated={handleAddIndicator}
        onUpdated={handleUpdateIndicator}
        mode={indicatorDialogMode}
        initialIndicator={editingIndicator}
        validateIndicator={validateIndicator}
        fixedTimeframe={tradeDefaultTimeframe}
        hideTimeframeSelector={true}
        autoCloseOnGenerate={true}
        preferParamFocus={preferIndicatorParamFocus}
      />
      <ConditionEditorDialog
        open={isConditionModalOpen}
        onClose={closeConditionModal}
        onConfirm={handleSaveCondition}
        conditionEditTarget={conditionEditTarget}
        conditionDraft={conditionDraft}
        conditionError={conditionError}
        conditionRuleError={conditionRuleError}
        indicatorOutputGroups={indicatorOutputGroups}
        methodOptions={methodOptions}
        defaultTimeframe={tradeDefaultTimeframe}
        setConditionDraft={setConditionDraft}
        buildConditionPreview={buildConditionPreview}
        renderToggle={renderToggle}
      />
      <StrategyConfigDialog
        open={isConfigReviewOpen}
        onClose={handleConfigClose}
        configStep={configStep}
        onNextStep={handleNextStep}
        onPrevStep={handlePrevStep}
        onConfirmGenerate={handleConfirmGenerate}
        isLogicPreviewVisible={isLogicPreviewVisible}
        onToggleLogicPreview={toggleLogicPreview}
        logicPreview={configReviewData?.logicPreview || logicPreview}
        usedIndicatorOutputs={configReviewData?.usedIndicatorOutputs || usedIndicatorOutputs}
        conditionSummarySections={configReviewData?.conditionSummarySections || conditionSummarySections}
        summaryListRef={summaryListRef}
        codeListRef={codeListRef}
        tradeConfigRef={tradeConfigRef}
        tradeConfig={tradeConfig}
        runtimeConfig={runtimeConfig}
        runtimeTemplateOptions={runtimeTemplateOptions}
        runtimeTimezoneOptions={runtimeTimezoneOptions}
        onRuntimeConfigChange={setRuntimeConfig}
        strategyName={strategyName}
        strategyDescription={strategyDescription}
        exchangeOptions={exchangeOptions}
        symbolOptions={symbolOptions}
        positionModeOptions={positionModeOptions}
        timeframeOptions={timeframeOptions}
        leverageOptions={leverageOptions}
        exchangeApiKeyOptions={exchangeApiKeyOptions}
        selectedExchangeApiKeyId={selectedExchangeApiKeyId}
        selectedExchangeApiKeyLabel={selectedExchangeApiKeyLabel}
        showExchangeApiKeySelector={showExchangeApiKeySelector}
        onExchangeApiKeySelect={handleExchangeApiKeySelect}
        onExchangeApiKeyBack={handleExchangeApiKeyBack}
        onStrategyNameChange={handleStrategyNameChange}
        onStrategyDescriptionChange={handleStrategyDescriptionChange}
        onExchangeChange={handleExchangeChange}
        onSymbolChange={handleSymbolChange}
        onPositionModeChange={handlePositionModeChange}
        onTimeframeChange={handleTimeframeChange}
        updateTradeSizing={updateTradeSizing}
        updateTradeRisk={updateTradeRisk}
        updateTrailingRisk={updateTrailingRisk}
        confirmLabel={submitLabel}
        isSubmitting={isSubmitting}
        disableMetaFields={disableMetaFields}
      />
    </>
  );
};

export default StrategyEditorFlow;
