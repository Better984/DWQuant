import type { KLineData } from 'klinecharts';

import type { GeneratedIndicatorPayload } from '../indicator/IndicatorGeneratorSelector';
import type {
  ConditionContainer,
  ConditionGroup,
  ConditionItem,
  IndicatorOutputGroup,
  MethodOption,
  StrategyValueRef,
  ValueOption,
} from './StrategyModule.types';
import { calcTalibIndicator, type TalibCalcResult } from '../../lib/talibIndicatorAdapter';
import {
  getTalibIndicatorEditorSchema,
  getTalibIndicatorMetaList,
  getTalibRuntimeCalcSpec,
  normalizeTalibInputSource,
} from '../../lib/registerTalibIndicators';

type IndicatorInputMap = Record<string, string>;

type RuntimeArg =
  | { kind: 'value'; valueId: string }
  | { kind: 'const'; value: number };

type RuntimeCondition = {
  id: string;
  enabled: boolean;
  required: boolean;
  method: string;
  args: RuntimeArg[];
  params: string[];
  runtimeKey: string;
  invalid: boolean;
};

type RuntimeGroup = {
  enabled: boolean;
  minPassConditions: number;
  conditions: RuntimeCondition[];
};

type RuntimeGroupSet = {
  enabled: boolean;
  minPassGroups: number;
  groups: RuntimeGroup[];
};

type RuntimeContainer = {
  checks: RuntimeGroupSet;
};

type RuntimeAction = {
  enabled: boolean;
  required: boolean;
  method: string;
  params: string[];
};

type RuntimeActionSet = {
  enabled: boolean;
  minPassConditions: number;
  actions: RuntimeAction[];
};

type RuntimeBranch = {
  enabled: boolean;
  minPassConditionContainer: number;
  containers: RuntimeContainer[];
  filters?: RuntimeGroupSet;
  onPass: RuntimeActionSet;
};

type RuntimeBundle = {
  entryLong: RuntimeBranch;
  exitLong: RuntimeBranch;
  entryShort: RuntimeBranch;
  exitShort: RuntimeBranch;
};

type BacktestPosition = {
  side: 'Long' | 'Short';
  entryPrice: number;
  entryTime: number;
  qty: number;
  entryFee: number;
  stopLossPrice: number | null;
  takeProfitPrice: number | null;
  fundingAccrued: number;
};

export type LocalBacktestTrade = {
  side: 'Long' | 'Short';
  entryTime: number;
  exitTime: number;
  entryPrice: number;
  exitPrice: number;
  stopLossPrice: number | null;
  takeProfitPrice: number | null;
  qty: number;
  fee: number;
  funding: number;
  pnl: number;
  exitReason: string;
  isOpen: boolean;
  slippageBps: number;
};

export type LocalBacktestEvent = {
  timestamp: number;
  type: string;
  message: string;
};

export type LocalBacktestStats = {
  totalProfit: number;
  totalReturn: number;
  maxDrawdown: number;
  winRate: number;
  tradeCount: number;
  avgProfit: number;
  profitFactor: number;
  avgWin: number;
  avgLoss: number;
  sharpeRatio: number;
  sortinoRatio: number;
  annualizedReturn: number;
  maxConsecutiveLosses: number;
  maxConsecutiveWins: number;
  avgHoldingMs: number;
  maxDrawdownDurationMs: number;
  calmarRatio: number;
};

export type LocalBacktestTradeSummary = {
  totalCount: number;
  winCount: number;
  lossCount: number;
  maxProfit: number;
  maxLoss: number;
  totalFee: number;
  totalFunding: number;
  avgPnl: number;
  avgHoldingMs: number;
};

export type LocalBacktestEquityPoint = {
  timestamp: number;
  equity: number;
  realizedPnl: number;
  unrealizedPnl: number;
  periodPnl: number;
  drawdown: number;
};

export type LocalBacktestEquitySummary = {
  pointCount: number;
  maxEquity: number;
  maxEquityAt: number;
  minEquity: number;
  minEquityAt: number;
  maxPeriodProfit: number;
  maxPeriodProfitAt: number;
  maxPeriodLoss: number;
  maxPeriodLossAt: number;
};

export type LocalBacktestEventSummary = {
  totalCount: number;
  firstTimestamp: number;
  lastTimestamp: number;
  typeCounts: Record<string, number>;
};

type MethodRuntimeMeta = {
  argsCount: number;
  paramDefaults: string[];
};

type BacktestStageCounters = {
  barsTotal: number;
  barsProcessed: number;
  barsInvalidPrice: number;
  filterLongPass: number;
  filterLongBlock: number;
  filterShortPass: number;
  filterShortBlock: number;
  entryLongBranchPass: number;
  entryShortBranchPass: number;
  exitLongBranchPass: number;
  exitShortBranchPass: number;
  actionMakeTradeCalls: number;
  openLongAttempt: number;
  openShortAttempt: number;
  closeLongAttempt: number;
  closeShortAttempt: number;
  openLongSuccess: number;
  openShortSuccess: number;
  closeLongSuccess: number;
  closeShortSuccess: number;
  failUnknownAction: number;
  failPriceInvalid: number;
  failSameSidePosition: number;
  failNeedAutoReverse: number;
  failOrderQtyInvalid: number;
  failCloseWithoutPosition: number;
  failCloseSideMismatch: number;
  riskCloseStopLoss: number;
  riskCloseTakeProfit: number;
};

type BranchConfigDiagnostic = {
  label: string;
  line: string;
  enabledConditionCount: number;
  unresolvedValueCount: number;
  invalidConditionCount: number;
};

type ExecuteMakeTradeResult = {
  success: boolean;
  kind: 'open' | 'close' | 'none';
  side: 'Long' | 'Short' | '';
  reason:
    | 'ok'
    | 'unknown_action'
    | 'invalid_price'
    | 'same_side_position'
    | 'need_auto_reverse'
    | 'invalid_order_qty'
    | 'close_without_position'
    | 'close_side_mismatch';
};

type LocalBacktestTradeAggregates = {
  closedTradeCount: number;
  winCount: number;
  lossCount: number;
  sumPnl: number;
  sumWinPnl: number;
  sumLossAbsPnl: number;
  grossProfit: number;
  grossLossAbs: number;
  maxProfit: number;
  maxLoss: number;
  totalHoldingMs: number;
  maxConsecutiveWins: number;
  maxConsecutiveLosses: number;
  currentConsecutiveWins: number;
  currentConsecutiveLosses: number;
  totalFee: number;
};

type LocalBacktestState = {
  position: BacktestPosition | null;
  realizedPnl: number;
  trades: LocalBacktestTrade[];
  events: LocalBacktestEvent[];
  eventTypeCounts: Record<string, number>;
  signalCount: number;
  accumulatedFunding: number;
  tradeAggregates: LocalBacktestTradeAggregates;
  stageCounters: BacktestStageCounters;
};

type LocalBacktestResolver = {
  resolveArgValue: (arg: RuntimeArg, index: number, offset: number) => number | null;
};

type LocalBacktestTiming = {
  timestamp: number;
  bar: KLineData;
};

type LocalBacktestRiskConfig = {
  orderQty: number;
  leverage: number;
  stopLossPct: number;
  takeProfitPct: number;
  feeRate: number;
  fundingRate: number;
  slippageBps: number;
  autoReverse: boolean;
};

type LocalBacktestMetrics = {
  maxDrawdown: number;
  peakEquity: number;
  peakEquityAt: number;
  drawdownStartAt: number;
  maxDrawdownDurationMs: number;
  maxEquity: number;
  maxEquityAt: number;
  minEquity: number;
  minEquityAt: number;
  maxPeriodProfit: number;
  maxPeriodProfitAt: number;
  maxPeriodLoss: number;
  maxPeriodLossAt: number;
  equityPointCount: number;
  equityPreview: LocalBacktestEquityPoint[];
  firstTimestamp: number;
  lastTimestamp: number;
  lastEquity: number;
  returnCount: number;
  returnSum: number;
  returnSquareSum: number;
  downsideCount: number;
  downsideSquareSum: number;
  timeframeMsTotal: number;
  timeframeMsCount: number;
  lastUnrealizedPnl: number;
};

const CROSS_METHODS = new Set(['CrossUp', 'CrossDown', 'CrossOver', 'CrossUnder', 'CrossAny']);

const EPS_COMPARE = 1e-10;
const EPS_ZERO = 1e-12;
const DEFAULT_FEE_RATE = 0.0004;
const DEFAULT_FUNDING_RATE = 0;
const DEFAULT_SLIPPAGE_BPS = 0;
const DEFAULT_INITIAL_CAPITAL = 10_000;
const FUNDING_INTERVAL_MS = 8 * 60 * 60 * 1000;
const YEAR_MS = 365 * 24 * 60 * 60 * 1000;
const MAX_EQUITY_PREVIEW_POINTS = 360;

const createStageCounters = (barsTotal: number): BacktestStageCounters => ({
  barsTotal,
  barsProcessed: 0,
  barsInvalidPrice: 0,
  filterLongPass: 0,
  filterLongBlock: 0,
  filterShortPass: 0,
  filterShortBlock: 0,
  entryLongBranchPass: 0,
  entryShortBranchPass: 0,
  exitLongBranchPass: 0,
  exitShortBranchPass: 0,
  actionMakeTradeCalls: 0,
  openLongAttempt: 0,
  openShortAttempt: 0,
  closeLongAttempt: 0,
  closeShortAttempt: 0,
  openLongSuccess: 0,
  openShortSuccess: 0,
  closeLongSuccess: 0,
  closeShortSuccess: 0,
  failUnknownAction: 0,
  failPriceInvalid: 0,
  failSameSidePosition: 0,
  failNeedAutoReverse: 0,
  failOrderQtyInvalid: 0,
  failCloseWithoutPosition: 0,
  failCloseSideMismatch: 0,
  riskCloseStopLoss: 0,
  riskCloseTakeProfit: 0,
});

const createTradeAggregates = (): LocalBacktestTradeAggregates => ({
  closedTradeCount: 0,
  winCount: 0,
  lossCount: 0,
  sumPnl: 0,
  sumWinPnl: 0,
  sumLossAbsPnl: 0,
  grossProfit: 0,
  grossLossAbs: 0,
  maxProfit: Number.NEGATIVE_INFINITY,
  maxLoss: Number.POSITIVE_INFINITY,
  totalHoldingMs: 0,
  maxConsecutiveWins: 0,
  maxConsecutiveLosses: 0,
  currentConsecutiveWins: 0,
  currentConsecutiveLosses: 0,
  totalFee: 0,
});

const collectBranchConfigDiagnostic = (
  label: string,
  branch: RuntimeBranch,
  knownValueIds: Set<string>,
): BranchConfigDiagnostic => {
  let checkGroupTotal = 0;
  let checkGroupEnabled = 0;
  let checkConditionEnabled = 0;
  let invalidConditionCount = 0;
  let unresolvedValueCount = 0;
  const methodSet = new Set<string>();

  branch.containers.forEach((container) => {
    const checks = container?.checks;
    if (!checks) {
      return;
    }
    checkGroupTotal += checks.groups.length;
    checks.groups.forEach((group) => {
      if (!group.enabled) {
        return;
      }
      checkGroupEnabled += 1;
      group.conditions.forEach((condition) => {
        if (!condition.enabled) {
          return;
        }
        checkConditionEnabled += 1;
        methodSet.add(condition.method);
        if (condition.invalid) {
          invalidConditionCount += 1;
        }
        condition.args.forEach((arg) => {
          if (arg.kind === 'value' && !knownValueIds.has(arg.valueId)) {
            unresolvedValueCount += 1;
          }
        });
      });
    });
  });

  let filterGroupEnabled = 0;
  let filterConditionEnabled = 0;
  if (branch.filters?.enabled) {
    branch.filters.groups.forEach((group) => {
      if (!group.enabled) {
        return;
      }
      filterGroupEnabled += 1;
      group.conditions.forEach((condition) => {
        if (condition.enabled) {
          filterConditionEnabled += 1;
        }
      });
    });
  }

  const methods = Array.from(methodSet.values()).slice(0, 6).join(',') || '-';
  return {
    label,
    enabledConditionCount: checkConditionEnabled,
    unresolvedValueCount,
    invalidConditionCount,
    line: `配置-${label}: 分支启用=${branch.enabled ? '是' : '否'} 条件组(启用/总)=${checkGroupEnabled}/${checkGroupTotal} 条件数=${checkConditionEnabled} 无效条件=${invalidConditionCount} 未解析值=${unresolvedValueCount} 筛选组=${filterGroupEnabled} 筛选条件=${filterConditionEnabled} 方法=${methods}`,
  };
};

const normalizeText = (value?: string) => (value || '').trim();
const normalizeUpper = (value?: string) => normalizeText(value).toUpperCase();
const normalizeLower = (value?: string) => normalizeText(value).toLowerCase();
const normalizeLookupKey = (value: string) => value.trim().toUpperCase().replace(/[^A-Z0-9]/g, '');

const toFiniteNumber = (value: unknown): number | null => {
  if (typeof value === 'number' && Number.isFinite(value)) {
    return value;
  }
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
};

const roundAwayFromZero = (value: number) => {
  if (!Number.isFinite(value) || value === 0) {
    return 0;
  }
  return Math.sign(value) * Math.round(Math.abs(value));
};

const toTimestamp = (bar: KLineData): number => {
  const parsed = Number(bar.timestamp);
  return Number.isFinite(parsed) ? parsed : 0;
};

const getClosePrice = (bar: KLineData): number => {
  const close = toFiniteNumber(bar.close);
  if (close !== null && close > 0) {
    return close;
  }
  const open = toFiniteNumber(bar.open);
  return open !== null && open > 0 ? open : 0;
};

const getHighPrice = (bar: KLineData): number => {
  const high = toFiniteNumber(bar.high);
  return high !== null && high > 0 ? high : getClosePrice(bar);
};

const getLowPrice = (bar: KLineData): number => {
  const low = toFiniteNumber(bar.low);
  return low !== null && low > 0 ? low : getClosePrice(bar);
};

const resolveFieldValue = (bar: KLineData, input: string): number | null => {
  const normalized = normalizeUpper(input);
  if (normalized === 'OPEN') return toFiniteNumber(bar.open);
  if (normalized === 'HIGH') return toFiniteNumber(bar.high);
  if (normalized === 'LOW') return toFiniteNumber(bar.low);
  if (normalized === 'CLOSE') return toFiniteNumber(bar.close);
  if (normalized === 'VOLUME') return toFiniteNumber(bar.volume);
  return toFiniteNumber(bar.close);
};

const getBaseOffset = (offsetRange?: number[]) => {
  if (!Array.isArray(offsetRange) || offsetRange.length === 0) {
    return 0;
  }
  const first = Number(offsetRange[0]);
  const second = Number(offsetRange.length > 1 ? offsetRange[1] : offsetRange[0]);
  const min = Number.isFinite(first) ? first : 0;
  const max = Number.isFinite(second) ? second : min;
  return Math.max(0, Math.min(min, max));
};

const buildRefKey = (ref?: StrategyValueRef | null) => {
  if (!ref) {
    return '';
  }
  return [
    normalizeUpper(ref.refType),
    normalizeUpper(ref.indicator),
    normalizeUpper(ref.timeframe),
    normalizeUpper(ref.input),
    normalizeUpper(ref.output),
    Array.isArray(ref.params) ? ref.params.map((item) => Number(item)).filter(Number.isFinite).join(',') : '',
    normalizeUpper(ref.calcMode),
  ].join('|');
};

const parseIndicatorInputMap = (
  rawInput: string,
  slotKeys: string[],
): IndicatorInputMap | undefined => {
  if (slotKeys.length === 0) {
    return undefined;
  }
  const normalizedSlots = new Map(slotKeys.map((slot) => [normalizeLookupKey(slot), slot]));
  const map: IndicatorInputMap = {};
  const text = normalizeText(rawInput);

  if (!text) {
    return undefined;
  }

  if (text.includes('=')) {
    text.split(';').forEach((segment) => {
      const [rawKey, rawValue] = segment.split('=');
      if (!rawKey || !rawValue) return;
      const matchedSlot = normalizedSlots.get(normalizeLookupKey(rawKey));
      if (!matchedSlot) return;
      map[matchedSlot] = normalizeTalibInputSource(rawValue);
    });
    return Object.keys(map).length > 0 ? map : undefined;
  }

  map[slotKeys[0]] = normalizeTalibInputSource(text);
  return map;
};

const resolveOutputValue = (row: TalibCalcResult | undefined, outputKey: string): number | undefined => {
  if (!row) {
    return undefined;
  }
  const direct = row[outputKey];
  if (typeof direct === 'number' && Number.isFinite(direct)) {
    return direct;
  }
  const target = normalizeLookupKey(outputKey);
  let firstFinite: number | undefined;
  for (const [key, value] of Object.entries(row)) {
    if (typeof value === 'number' && Number.isFinite(value)) {
      if (firstFinite === undefined) {
        firstFinite = value;
      }
      if (normalizeLookupKey(key) === target) {
        return value;
      }
    }
  }
  return firstFinite;
};

const resolveTalibMeta = (
  rawCode: string,
  byCode: Map<string, ReturnType<typeof getTalibIndicatorMetaList>[number]>,
  byChartName: Map<string, ReturnType<typeof getTalibIndicatorMetaList>[number]>,
) => {
  const normalized = normalizeUpper(rawCode);
  if (!normalized) {
    return null;
  }
  return byCode.get(normalized)
    || byChartName.get(normalized)
    || byChartName.get(`TA_${normalized}`)
    || null;
};

type BuiltSeriesResult = {
  seriesByValueId: Map<string, Array<number | undefined>>;
  warnings: string[];
};

// 本地运行缓存：用于高频编辑场景下复用中间结果，减少重复 TALib 计算与运行时编译。
const INDICATOR_SERIES_CACHE_LIMIT = 10;
const RUNTIME_BUNDLE_CACHE_LIMIT = 12;
const indicatorSeriesCache = new Map<string, BuiltSeriesResult>();
const runtimeBundleCache = new Map<string, RuntimeBundle>();

const setLimitedCache = <T,>(cache: Map<string, T>, key: string, value: T, limit: number) => {
  if (cache.has(key)) {
    cache.delete(key);
  }
  cache.set(key, value);
  while (cache.size > limit) {
    const oldestKey = cache.keys().next().value;
    if (typeof oldestKey !== 'string') {
      break;
    }
    cache.delete(oldestKey);
  }
};

const buildBarsSignature = (bars: KLineData[]) => {
  if (bars.length === 0) {
    return 'bars:0';
  }
  const first = toTimestamp(bars[0]);
  const last = toTimestamp(bars[bars.length - 1]);
  return `bars:${bars.length}:${first}:${last}`;
};

const buildIndicatorsSignature = (selectedIndicators: GeneratedIndicatorPayload[]) => {
  const parts = selectedIndicators.map((indicator) => {
    const config = indicator.config || {};
    const indicatorCodeText = normalizeText(String((config as { indicator?: unknown }).indicator || indicator.code || ''));
    const inputText = normalizeText(String((config as { input?: unknown }).input || ''));
    const paramsText = Array.isArray((config as { params?: unknown[] }).params)
      ? ((config as { params?: unknown[] }).params || []).map((item) => normalizeText(String(item))).join(',')
      : '';
    const outputsText = Array.isArray(indicator.outputs)
      ? indicator.outputs.map((output) => normalizeText(output.key)).join(',')
      : '';
    return `${indicator.id}|${indicatorCodeText}|${inputText}|${paramsText}|${outputsText}`;
  });
  return parts.join('||');
};

const buildRuntimeSignature = (
  logicContainers: ConditionContainer[],
  filterContainers: ConditionContainer[],
  methodOptions: MethodOption[],
) => {
  const serializeCondition = (condition: ConditionItem) => ([
    condition.id,
    condition.enabled ? 1 : 0,
    condition.required ? 1 : 0,
    normalizeText(condition.method),
    normalizeText(condition.leftValueId),
    condition.rightValueType,
    normalizeText(condition.rightValueId),
    normalizeText(condition.rightNumber),
    condition.extraValueType,
    normalizeText(condition.extraValueId),
    normalizeText(condition.extraNumber),
    (condition.paramValues || []).map((value) => normalizeText(value)).join(','),
  ].join(':'));

  const serializeContainer = (container: ConditionContainer) => {
    const groupText = container.groups.map((group) => {
      const conditionText = group.conditions.map((condition) => serializeCondition(condition)).join('~');
      return `${group.id}:${group.enabled ? 1 : 0}:${group.required ? 1 : 0}:${conditionText}`;
    }).join('|');
    return `${container.id}:${container.enabled ? 1 : 0}:${groupText}`;
  };

  const logicText = logicContainers.map((container) => serializeContainer(container)).join('#');
  const filterText = filterContainers.map((container) => serializeContainer(container)).join('#');
  const methodText = methodOptions.map((method) => {
    const defaults = (method.params || []).map((param) => normalizeText(param.defaultValue)).join(',');
    return `${method.value}:${method.argsCount ?? 2}:${defaults}`;
  }).join('#');
  return `${logicText}@@${filterText}@@${methodText}`;
};

const buildIndicatorSeries = (
  bars: KLineData[],
  selectedIndicators: GeneratedIndicatorPayload[],
): BuiltSeriesResult => {
  const seriesByValueId = new Map<string, Array<number | undefined>>();
  const warnings: string[] = [];

  if (bars.length === 0 || selectedIndicators.length === 0) {
    return { seriesByValueId, warnings };
  }

  const metaList = getTalibIndicatorMetaList();
  const byCode = new Map(metaList.map((item) => [normalizeUpper(item.code), item]));
  const byChartName = new Map(metaList.map((item) => [normalizeUpper(item.name), item]));

  selectedIndicators.forEach((indicator) => {
    const config = (indicator.config || {}) as {
      indicator?: string;
      params?: unknown[];
      input?: string;
      output?: string;
    };
    const rawCode = String(config.indicator || indicator.code || '').trim();
    if (!rawCode) {
      warnings.push(`指标 ${indicator.name || indicator.id} 未配置代码`);
      return;
    }

    const matchedMeta = resolveTalibMeta(rawCode, byCode, byChartName);
    if (!matchedMeta) {
      warnings.push(`指标 ${rawCode} 未在 TALib 元信息中找到映射`);
      return;
    }

    const calcSpec = getTalibRuntimeCalcSpec(matchedMeta.name);
    if (!calcSpec) {
      warnings.push(`指标 ${rawCode} 尚未完成前端计算规格注册`);
      return;
    }

    const params = Array.isArray(config.params)
      ? config.params.map((value) => Number(value)).filter((value) => Number.isFinite(value))
      : [];
    const schema = getTalibIndicatorEditorSchema(matchedMeta.name);
    const inputMap = parseIndicatorInputMap(
      typeof config.input === 'string' ? config.input : '',
      (schema?.inputSlots || []).map((slot) => slot.key),
    );
    const rows = calcTalibIndicator(
      calcSpec,
      bars,
      params,
      inputMap && Object.keys(inputMap).length > 0 ? { taInputMap: inputMap } : undefined,
    );
    if (rows.length === 0) {
      warnings.push(`指标 ${rawCode} 计算结果为空`);
      return;
    }

    const outputs = Array.isArray(indicator.outputs) && indicator.outputs.length > 0
      ? indicator.outputs
      : [{ key: typeof config.output === 'string' ? config.output : 'Value' }];

    outputs.forEach((output) => {
      const outputKey = normalizeText(output.key) || 'Value';
      const valueId = `${indicator.id}:${outputKey}`;
      const series = rows.map((row) => resolveOutputValue(row, outputKey));
      seriesByValueId.set(valueId, series);
    });
  });

  return { seriesByValueId, warnings };
};

const getIndicatorSeriesWithCache = (
  bars: KLineData[],
  selectedIndicators: GeneratedIndicatorPayload[],
): BuiltSeriesResult => {
  const cacheKey = `${buildBarsSignature(bars)}|${buildIndicatorsSignature(selectedIndicators)}`;
  const cached = indicatorSeriesCache.get(cacheKey);
  if (cached) {
    return cached;
  }
  const built = buildIndicatorSeries(bars, selectedIndicators);
  setLimitedCache(indicatorSeriesCache, cacheKey, built, INDICATOR_SERIES_CACHE_LIMIT);
  return built;
};

const buildMethodMetaMap = (methodOptions: MethodOption[]) => {
  const map = new Map<string, MethodRuntimeMeta>();
  methodOptions.forEach((item) => {
    const defaults = (item.params || []).map((param) => normalizeText(param.defaultValue));
    map.set(item.value, {
      argsCount: item.argsCount ?? 2,
      paramDefaults: defaults,
    });
  });
  return map;
};

const buildRuntimeCondition = (
  condition: ConditionItem,
  methodMetaMap: Map<string, MethodRuntimeMeta>,
): RuntimeCondition => {
  const meta = methodMetaMap.get(condition.method) || { argsCount: 2, paramDefaults: [] };
  const args: RuntimeArg[] = [];
  let invalid = false;

  const leftId = normalizeText(condition.leftValueId);
  if (meta.argsCount >= 1) {
    if (!leftId) {
      invalid = true;
    } else {
      args.push({ kind: 'value', valueId: leftId });
    }
  }

  if (meta.argsCount >= 2) {
    if (condition.rightValueType === 'number') {
      const numeric = toFiniteNumber(normalizeText(condition.rightNumber));
      if (numeric === null) {
        invalid = true;
      } else {
        args.push({ kind: 'const', value: numeric });
      }
    } else {
      const rightId = normalizeText(condition.rightValueId);
      if (!rightId) {
        invalid = true;
      } else {
        args.push({ kind: 'value', valueId: rightId });
      }
    }
  }

  if (meta.argsCount >= 3) {
    if (condition.extraValueType === 'number') {
      const numeric = toFiniteNumber(normalizeText(condition.extraNumber));
      if (numeric === null) {
        invalid = true;
      } else {
        args.push({ kind: 'const', value: numeric });
      }
    } else {
      const extraId = normalizeText(condition.extraValueId);
      if (!extraId) {
        invalid = true;
      } else {
        args.push({ kind: 'value', valueId: extraId });
      }
    }
  }

  const rawParams = condition.paramValues || [];
  const paramLength = Math.max(rawParams.length, meta.paramDefaults.length);
  const params: string[] = [];
  for (let i = 0; i < paramLength; i += 1) {
    const raw = normalizeText(rawParams[i]);
    params.push(raw || meta.paramDefaults[i] || '');
  }

  const runtimeKey = [
    condition.id,
    condition.method,
    args.map((arg) => (arg.kind === 'const' ? `const:${arg.value}` : `value:${arg.valueId}`)).join('#'),
    params.join(','),
  ].join('|');

  return {
    id: condition.id,
    enabled: condition.enabled,
    required: condition.required,
    method: condition.method,
    args,
    params,
    runtimeKey,
    invalid,
  };
};

const buildRuntimeGroup = (
  group: ConditionGroup,
  methodMetaMap: Map<string, MethodRuntimeMeta>,
): RuntimeGroup => {
  const conditions = group.conditions.map((item) => buildRuntimeCondition(item, methodMetaMap));
  const enabledConditions = conditions.filter((condition) => condition.enabled);
  const optionalConditions = enabledConditions.filter((condition) => !condition.required);
  // 空条件组不应阻断分支执行，避免“启用但无条件”导致全量拦截。
  const minPassConditions =
    enabledConditions.length === 0 ? 0 : optionalConditions.length === 0 ? 0 : 1;

  return {
    enabled: group.enabled,
    minPassConditions,
    conditions,
  };
};

const buildBranchRuntime = (
  logicContainers: ConditionContainer[],
  filterContainers: ConditionContainer[],
  methodMetaMap: Map<string, MethodRuntimeMeta>,
  containerId: string,
  action: string,
  filterContainerId?: string,
): RuntimeBranch => {
  const container = logicContainers.find((item) => item.id === containerId);
  const checks = container
    ? {
      enabled: container.enabled,
      minPassGroups: (() => {
        const requiredGroupsCount = container.groups.filter((group) => group.enabled && group.required).length;
        return requiredGroupsCount > 0 ? requiredGroupsCount : 1;
      })(),
      groups: container.groups.map((group) => buildRuntimeGroup(group, methodMetaMap)),
    }
    : { enabled: false, minPassGroups: 1, groups: [] as RuntimeGroup[] };

  let filters: RuntimeGroupSet | undefined;
  if (filterContainerId) {
    const filterContainer = filterContainers.find((item) => item.id === filterContainerId);
    if (filterContainer) {
      const enabledGroups = filterContainer.groups.filter((group) => group.enabled);
      if (enabledGroups.length > 0) {
        const runtimeFilterGroups = enabledGroups.map((group) => buildRuntimeGroup(group, methodMetaMap));
        const requiredGroupsCount = enabledGroups.filter((group) => group.required).length;
        filters = {
          enabled: true,
          minPassGroups: requiredGroupsCount > 0 ? requiredGroupsCount : 1,
          groups: runtimeFilterGroups,
        };
      }
    }
  }

  return {
    enabled: container?.enabled ?? false,
    minPassConditionContainer: 1,
    containers: [{ checks }],
    filters,
    onPass: {
      enabled: true,
      minPassConditions: 1,
      actions: [{
        enabled: true,
        required: false,
        method: 'MakeTrade',
        params: [action],
      }],
    },
  };
};

const buildRuntimeBundle = (
  logicContainers: ConditionContainer[],
  filterContainers: ConditionContainer[],
  methodOptions: MethodOption[],
): RuntimeBundle => {
  const methodMetaMap = buildMethodMetaMap(methodOptions);
  return {
    entryLong: buildBranchRuntime(
      logicContainers,
      filterContainers,
      methodMetaMap,
      'open-long',
      'Long',
      'open-long-filter',
    ),
    exitLong: buildBranchRuntime(
      logicContainers,
      filterContainers,
      methodMetaMap,
      'close-long',
      'CloseLong',
    ),
    entryShort: buildBranchRuntime(
      logicContainers,
      filterContainers,
      methodMetaMap,
      'open-short',
      'Short',
      'open-short-filter',
    ),
    exitShort: buildBranchRuntime(
      logicContainers,
      filterContainers,
      methodMetaMap,
      'close-short',
      'CloseShort',
    ),
  };
};

const getRuntimeBundleWithCache = (
  logicContainers: ConditionContainer[],
  filterContainers: ConditionContainer[],
  methodOptions: MethodOption[],
) => {
  const cacheKey = buildRuntimeSignature(logicContainers, filterContainers, methodOptions);
  const cached = runtimeBundleCache.get(cacheKey);
  if (cached) {
    return cached;
  }
  const built = buildRuntimeBundle(logicContainers, filterContainers, methodOptions);
  setLimitedCache(runtimeBundleCache, cacheKey, built, RUNTIME_BUNDLE_CACHE_LIMIT);
  return built;
};

const resolvePeriod = (
  condition: RuntimeCondition,
  resolver: LocalBacktestResolver,
  index: number,
  paramIndex: number,
  fallbackArgIndex: number,
) => {
  const raw = toFiniteNumber(condition.params[paramIndex]);
  if (raw !== null) {
    const period = Math.max(0, roundAwayFromZero(raw));
    if (period > 0) {
      return period;
    }
  }
  const fallbackArg = condition.args[fallbackArgIndex];
  if (fallbackArg) {
    const value = resolver.resolveArgValue(fallbackArg, index, 0);
    if (value !== null) {
      const period = Math.max(0, roundAwayFromZero(value));
      if (period > 0) {
        return period;
      }
    }
  }
  return null;
};

const resolveThreshold = (
  condition: RuntimeCondition,
  resolver: LocalBacktestResolver,
  index: number,
  argIndex: number,
  paramIndex: number,
) => {
  const arg = condition.args[argIndex];
  if (arg) {
    const value = resolver.resolveArgValue(arg, index, 0);
    if (value !== null) {
      return value;
    }
  }
  return toFiniteNumber(condition.params[paramIndex]);
};

const resolveSeriesFromArg = (
  condition: RuntimeCondition,
  resolver: LocalBacktestResolver,
  index: number,
  argIndex: number,
  count: number,
): number[] | null => {
  const arg = condition.args[argIndex];
  if (!arg || count <= 0) {
    return null;
  }
  const values: number[] = [];
  for (let offset = 0; offset < count; offset += 1) {
    const value = resolver.resolveArgValue(arg, index, offset);
    if (value === null) {
      return null;
    }
    values.push(value);
  }
  return values;
};

const computeSlope = (series: number[]) => {
  if (series.length < 2) {
    return 0;
  }
  const n = series.length;
  let sumX = 0;
  let sumY = 0;
  let sumXY = 0;
  let sumXX = 0;

  for (let i = 0; i < n; i += 1) {
    const x = i;
    const y = series[n - 1 - i];
    sumX += x;
    sumY += y;
    sumXY += x * y;
    sumXX += x * x;
  }

  const denominator = n * sumXX - sumX * sumX;
  if (Math.abs(denominator) < EPS_ZERO) {
    return 0;
  }
  return (n * sumXY - sumX * sumY) / denominator;
};

const computeMeanStd = (series: number[]) => {
  if (series.length === 0) {
    return { mean: 0, std: 0 };
  }
  const mean = series.reduce((sum, value) => sum + value, 0) / series.length;
  let variance = 0;
  for (let i = 0; i < series.length; i += 1) {
    const diff = series[i] - mean;
    variance += diff * diff;
  }
  variance /= series.length;
  return { mean, std: Math.sqrt(variance) };
};

const evaluateCondition = (
  condition: RuntimeCondition,
  resolver: LocalBacktestResolver,
  index: number,
  cache: Map<string, boolean>,
) => {
  const cacheKey = `${index}|${condition.runtimeKey}`;
  if (cache.has(cacheKey)) {
    return cache.get(cacheKey) || false;
  }

  let success = false;
  if (!condition.invalid) {
    const resolveArg = (argIndex: number, offset: number) => {
      const arg = condition.args[argIndex];
      if (!arg) {
        return null;
      }
      return resolver.resolveArgValue(arg, index, offset);
    };
    const left = resolveArg(0, 0);
    const right = resolveArg(1, 0);

    switch (condition.method) {
      case 'GreaterThanOrEqual':
        success = left !== null && right !== null && left >= right;
        break;
      case 'GreaterThan':
        success = left !== null && right !== null && left > right;
        break;
      case 'LessThan':
        success = left !== null && right !== null && left < right;
        break;
      case 'LessThanOrEqual':
        success = left !== null && right !== null && left <= right;
        break;
      case 'Equal':
        success = left !== null && right !== null && Math.abs(left - right) < EPS_COMPARE;
        break;
      case 'NotEqual':
        success = left !== null && right !== null && Math.abs(left - right) >= EPS_COMPARE;
        break;
      case 'CrossUp':
      case 'CrossOver': {
        const prevLeft = resolver.resolveArgValue(condition.args[0], index, 1);
        const prevRight = resolver.resolveArgValue(condition.args[1], index, 1);
        success = left !== null
          && right !== null
          && prevLeft !== null
          && prevRight !== null
          && prevLeft <= prevRight
          && left > right;
        break;
      }
      case 'CrossDown':
      case 'CrossUnder': {
        const prevLeft = resolver.resolveArgValue(condition.args[0], index, 1);
        const prevRight = resolver.resolveArgValue(condition.args[1], index, 1);
        success = left !== null
          && right !== null
          && prevLeft !== null
          && prevRight !== null
          && prevLeft >= prevRight
          && left < right;
        break;
      }
      case 'CrossAny': {
        const prevLeft = resolver.resolveArgValue(condition.args[0], index, 1);
        const prevRight = resolver.resolveArgValue(condition.args[1], index, 1);
        success = left !== null
          && right !== null
          && prevLeft !== null
          && prevRight !== null
          && ((prevLeft <= prevRight && left > right) || (prevLeft >= prevRight && left < right));
        break;
      }
      case 'Between': {
        const x = resolver.resolveArgValue(condition.args[0], index, 0);
        const low = resolver.resolveArgValue(condition.args[1], index, 0);
        const high = resolver.resolveArgValue(condition.args[2], index, 0);
        if (x !== null && low !== null && high !== null) {
          const min = Math.min(low, high);
          const max = Math.max(low, high);
          success = x >= min && x <= max;
        }
        break;
      }
      case 'Outside': {
        const x = resolver.resolveArgValue(condition.args[0], index, 0);
        const low = resolver.resolveArgValue(condition.args[1], index, 0);
        const high = resolver.resolveArgValue(condition.args[2], index, 0);
        if (x !== null && low !== null && high !== null) {
          const min = Math.min(low, high);
          const max = Math.max(low, high);
          success = x < min || x > max;
        }
        break;
      }
      case 'Rising': {
        const period = resolvePeriod(condition, resolver, index, 0, 1);
        if (period !== null) {
          const series = resolveSeriesFromArg(condition, resolver, index, 0, period + 1);
          if (series) {
            success = true;
            for (let i = 0; i < period; i += 1) {
              if (series[i] <= series[i + 1]) {
                success = false;
                break;
              }
            }
          }
        }
        break;
      }
      case 'Falling': {
        const period = resolvePeriod(condition, resolver, index, 0, 1);
        if (period !== null) {
          const series = resolveSeriesFromArg(condition, resolver, index, 0, period + 1);
          if (series) {
            success = true;
            for (let i = 0; i < period; i += 1) {
              if (series[i] >= series[i + 1]) {
                success = false;
                break;
              }
            }
          }
        }
        break;
      }
      case 'AboveFor': {
        const period = resolvePeriod(condition, resolver, index, 0, 2);
        if (period !== null) {
          success = true;
          for (let offset = 0; offset < period; offset += 1) {
            const value = resolver.resolveArgValue(condition.args[0], index, offset);
            const threshold = resolveThreshold(condition, resolver, index - offset, 1, 1);
            if (value === null || threshold === null || value <= threshold) {
              success = false;
              break;
            }
          }
        }
        break;
      }
      case 'BelowFor': {
        const period = resolvePeriod(condition, resolver, index, 0, 2);
        if (period !== null) {
          success = true;
          for (let offset = 0; offset < period; offset += 1) {
            const value = resolver.resolveArgValue(condition.args[0], index, offset);
            const threshold = resolveThreshold(condition, resolver, index - offset, 1, 1);
            if (value === null || threshold === null || value >= threshold) {
              success = false;
              break;
            }
          }
        }
        break;
      }
      case 'ROC': {
        const period = resolvePeriod(condition, resolver, index, 0, 2);
        const threshold = resolveThreshold(condition, resolver, index, 1, 1);
        if (period !== null && threshold !== null) {
          const current = resolver.resolveArgValue(condition.args[0], index, 0);
          const previous = resolver.resolveArgValue(condition.args[0], index, period);
          if (current !== null && previous !== null && Math.abs(previous) >= EPS_ZERO) {
            success = (current / previous - 1) > threshold;
          }
        }
        break;
      }
      case 'Slope': {
        const period = resolvePeriod(condition, resolver, index, 0, 2);
        const threshold = resolveThreshold(condition, resolver, index, 1, 1);
        if (period !== null && threshold !== null) {
          const series = resolveSeriesFromArg(condition, resolver, index, 0, period);
          if (series) {
            success = computeSlope(series) > threshold;
          }
        }
        break;
      }
      case 'ZScore': {
        const period = resolvePeriod(condition, resolver, index, 0, 2);
        const threshold = resolveThreshold(condition, resolver, index, 1, 1);
        if (period !== null && threshold !== null) {
          const series = resolveSeriesFromArg(condition, resolver, index, 0, period);
          if (series) {
            const { mean, std } = computeMeanStd(series);
            if (std > 0) {
              success = (series[0] - mean) / std > threshold;
            }
          }
        }
        break;
      }
      case 'StdDevGreater':
      case 'StdDevLess': {
        const period = resolvePeriod(condition, resolver, index, 0, 2);
        const threshold = resolveThreshold(condition, resolver, index, 1, 1);
        if (period !== null && threshold !== null) {
          const series = resolveSeriesFromArg(condition, resolver, index, 0, period);
          if (series) {
            const { std } = computeMeanStd(series);
            success = condition.method === 'StdDevGreater' ? std > threshold : std < threshold;
          }
        }
        break;
      }
      case 'TouchUpper':
        success = left !== null && right !== null && left >= right;
        break;
      case 'TouchLower':
        success = left !== null && right !== null && left <= right;
        break;
      case 'BreakoutUp':
        success = left !== null && right !== null && left > right;
        break;
      case 'BreakoutDown':
        success = left !== null && right !== null && left < right;
        break;
      case 'BandwidthExpand':
      case 'BandwidthContract': {
        const period = resolvePeriod(condition, resolver, index, 0, 3);
        if (period !== null) {
          success = true;
          const values: number[] = [];
          for (let offset = 0; offset <= period; offset += 1) {
            const upper = resolver.resolveArgValue(condition.args[0], index, offset);
            const lower = resolver.resolveArgValue(condition.args[1], index, offset);
            const middle = resolver.resolveArgValue(condition.args[2], index, offset);
            if (upper === null || lower === null || middle === null || Math.abs(middle) < EPS_ZERO) {
              success = false;
              break;
            }
            values.push((upper - lower) / middle);
          }
          if (success) {
            for (let i = 0; i < period; i += 1) {
              if (condition.method === 'BandwidthExpand') {
                if (values[i] <= values[i + 1]) {
                  success = false;
                  break;
                }
              } else if (values[i] >= values[i + 1]) {
                success = false;
                break;
              }
            }
          }
        }
        break;
      }
      default:
        success = false;
        break;
    }
  }

  cache.set(cacheKey, success);
  return success;
};

const splitByPriority = (conditions: RuntimeCondition[]) => {
  const crossRequired: RuntimeCondition[] = [];
  const crossOptional: RuntimeCondition[] = [];
  const required: RuntimeCondition[] = [];
  const optional: RuntimeCondition[] = [];

  conditions.forEach((condition) => {
    if (!condition.enabled) return;
    if (CROSS_METHODS.has(condition.method)) {
      if (condition.required) crossRequired.push(condition);
      else crossOptional.push(condition);
      return;
    }
    if (condition.required) required.push(condition);
    else optional.push(condition);
  });

  return { crossRequired, crossOptional, required, optional };
};

const precomputeRequiredConditions = (
  checks: RuntimeGroupSet | undefined,
  resolver: LocalBacktestResolver,
  index: number,
  cache: Map<string, boolean>,
) => {
  if (!checks || !checks.enabled) {
    return;
  }
  checks.groups.forEach((group) => {
    if (!group.enabled) return;
    group.conditions.forEach((condition) => {
      if (!condition.enabled || !condition.required) return;
      evaluateCondition(condition, resolver, index, cache);
    });
  });
};

const evaluateChecks = (
  checks: RuntimeGroupSet | undefined,
  resolver: LocalBacktestResolver,
  index: number,
  cache: Map<string, boolean>,
) => {
  if (!checks || !checks.enabled) {
    return false;
  }

  let passGroups = 0;
  checks.groups.forEach((group) => {
    if (!group.enabled) {
      return;
    }
    const { crossRequired, crossOptional, required, optional } = splitByPriority(group.conditions);
    const hasEnabled = crossRequired.length + crossOptional.length + required.length + optional.length > 0;
    if (!hasEnabled) {
      if (group.minPassConditions <= 0) {
        passGroups += 1;
      }
      return;
    }

    let requiredFailed = false;
    let optionalPassCount = 0;
    const runCondition = (condition: RuntimeCondition, isRequired: boolean) => {
      const passed = evaluateCondition(condition, resolver, index, cache);
      if (isRequired && !passed) {
        requiredFailed = true;
      } else if (!isRequired && passed) {
        optionalPassCount += 1;
      }
    };

    for (let i = 0; i < crossRequired.length; i += 1) {
      runCondition(crossRequired[i], true);
      if (requiredFailed) break;
    }
    if (requiredFailed) return;

    for (let i = 0; i < crossOptional.length; i += 1) {
      runCondition(crossOptional[i], false);
    }

    for (let i = 0; i < required.length; i += 1) {
      runCondition(required[i], true);
      if (requiredFailed) break;
    }
    if (requiredFailed) return;

    if (optionalPassCount < group.minPassConditions) {
      for (let i = 0; i < optional.length; i += 1) {
        runCondition(optional[i], false);
        if (optionalPassCount >= group.minPassConditions) break;
      }
    }

    if (optionalPassCount >= group.minPassConditions) {
      passGroups += 1;
    }
  });

  return passGroups >= checks.minPassGroups;
};

const applySlippage = (price: number, orderSide: 'buy' | 'sell', slippageBps: number) => {
  if (slippageBps === 0) {
    return price;
  }
  const ratio = slippageBps / 10_000;
  return orderSide === 'buy' ? price * (1 + ratio) : price * (1 - ratio);
};

const calculateFee = (price: number, qty: number, feeRate: number) => {
  if (price <= 0 || qty <= 0 || feeRate <= 0) {
    return 0;
  }
  return price * qty * feeRate;
};

const buildStopLossPrice = (entryPrice: number, stopLossPct: number, leverage: number, side: 'Long' | 'Short') => {
  if (stopLossPct <= 0 || entryPrice <= 0) {
    return null;
  }
  const effectiveLeverage = Math.max(1, leverage);
  const movePct = stopLossPct / effectiveLeverage;
  return side === 'Long' ? entryPrice * (1 - movePct) : entryPrice * (1 + movePct);
};

const buildTakeProfitPrice = (entryPrice: number, takeProfitPct: number, leverage: number, side: 'Long' | 'Short') => {
  if (takeProfitPct <= 0 || entryPrice <= 0) {
    return null;
  }
  const effectiveLeverage = Math.max(1, leverage);
  const movePct = takeProfitPct / effectiveLeverage;
  return side === 'Long' ? entryPrice * (1 + movePct) : entryPrice * (1 - movePct);
};

const checkStopLoss = (position: BacktestPosition, high: number, low: number) => {
  if (position.stopLossPrice === null) {
    return false;
  }
  return position.side === 'Long' ? low <= position.stopLossPrice : high >= position.stopLossPrice;
};

const checkTakeProfit = (position: BacktestPosition, high: number, low: number) => {
  if (position.takeProfitPrice === null) {
    return false;
  }
  return position.side === 'Long' ? high >= position.takeProfitPrice : low <= position.takeProfitPrice;
};

const calculatePositionPnl = (position: BacktestPosition, exitPrice: number) => {
  if (position.side === 'Long') {
    return (exitPrice - position.entryPrice) * position.qty;
  }
  return (position.entryPrice - exitPrice) * position.qty;
};

const tryMapAction = (action: string): { positionSide: 'Long' | 'Short'; orderSide: 'buy' | 'sell'; isClose: boolean } | null => {
  const normalized = normalizeUpper(action);
  if (normalized === 'LONG') return { positionSide: 'Long', orderSide: 'buy', isClose: false };
  if (normalized === 'SHORT') return { positionSide: 'Short', orderSide: 'sell', isClose: false };
  if (normalized === 'CLOSELONG') return { positionSide: 'Long', orderSide: 'sell', isClose: true };
  if (normalized === 'CLOSESHORT') return { positionSide: 'Short', orderSide: 'buy', isClose: true };
  return null;
};

const pushBacktestEvent = (state: LocalBacktestState, event: LocalBacktestEvent) => {
  state.events.push(event);
  const eventType = normalizeText(event.type) || 'Unknown';
  state.eventTypeCounts[eventType] = (state.eventTypeCounts[eventType] || 0) + 1;
};

const updateTradeAggregatesOnClose = (
  aggregates: LocalBacktestTradeAggregates,
  trade: LocalBacktestTrade,
  holdingMs: number,
) => {
  if (trade.isOpen) {
    return;
  }

  const pnl = trade.pnl;
  aggregates.closedTradeCount += 1;
  aggregates.sumPnl += pnl;
  aggregates.totalFee += trade.fee;
  aggregates.totalHoldingMs += Math.max(0, holdingMs);
  aggregates.maxProfit = Math.max(aggregates.maxProfit, pnl);
  aggregates.maxLoss = Math.min(aggregates.maxLoss, pnl);

  if (pnl > 0) {
    aggregates.winCount += 1;
    aggregates.sumWinPnl += pnl;
    aggregates.grossProfit += pnl;
    aggregates.currentConsecutiveWins += 1;
    aggregates.currentConsecutiveLosses = 0;
    if (aggregates.currentConsecutiveWins > aggregates.maxConsecutiveWins) {
      aggregates.maxConsecutiveWins = aggregates.currentConsecutiveWins;
    }
    return;
  }

  if (pnl < 0) {
    const absPnl = Math.abs(pnl);
    aggregates.lossCount += 1;
    aggregates.sumLossAbsPnl += absPnl;
    aggregates.grossLossAbs += absPnl;
    aggregates.currentConsecutiveLosses += 1;
    aggregates.currentConsecutiveWins = 0;
    if (aggregates.currentConsecutiveLosses > aggregates.maxConsecutiveLosses) {
      aggregates.maxConsecutiveLosses = aggregates.currentConsecutiveLosses;
    }
    return;
  }

  aggregates.currentConsecutiveWins = 0;
  aggregates.currentConsecutiveLosses = 0;
};

const closePosition = (
  state: LocalBacktestState,
  timing: LocalBacktestTiming,
  execPrice: number,
  feeRate: number,
  reason: string,
  slippageBps: number,
) => {
  if (!state.position) {
    return false;
  }
  const position = state.position;
  const exitFee = calculateFee(execPrice, position.qty, feeRate);
  const pnl = calculatePositionPnl(position, execPrice) - position.entryFee - exitFee;
  state.realizedPnl += pnl;
  const closedTrade: LocalBacktestTrade = {
    side: position.side,
    entryTime: position.entryTime,
    exitTime: timing.timestamp,
    entryPrice: position.entryPrice,
    exitPrice: execPrice,
    stopLossPrice: position.stopLossPrice,
    takeProfitPrice: position.takeProfitPrice,
    qty: position.qty,
    fee: position.entryFee + exitFee,
    funding: position.fundingAccrued,
    pnl,
    exitReason: reason,
    isOpen: false,
    slippageBps,
  };
  state.trades.push(closedTrade);
  updateTradeAggregatesOnClose(
    state.tradeAggregates,
    closedTrade,
    Math.max(0, timing.timestamp - position.entryTime),
  );
  pushBacktestEvent(state, {
    timestamp: timing.timestamp,
    type: 'Close',
    message: `平仓 ${position.side} 价格=${execPrice.toFixed(4)} 原因=${reason} 盈亏=${pnl.toFixed(4)}`,
  });
  state.position = null;
  return true;
};

const executeMakeTrade = (
  state: LocalBacktestState,
  timing: LocalBacktestTiming,
  action: string,
  risk: LocalBacktestRiskConfig,
) : ExecuteMakeTradeResult => {
  state.stageCounters.actionMakeTradeCalls += 1;
  const mapped = tryMapAction(action);
  if (!mapped) {
    state.stageCounters.failUnknownAction += 1;
    return { success: false, kind: 'none', side: '', reason: 'unknown_action' };
  }
  if (mapped.isClose) {
    if (mapped.positionSide === 'Long') {
      state.stageCounters.closeLongAttempt += 1;
    } else {
      state.stageCounters.closeShortAttempt += 1;
    }
  } else if (mapped.positionSide === 'Long') {
    state.stageCounters.openLongAttempt += 1;
  } else {
    state.stageCounters.openShortAttempt += 1;
  }

  const closePrice = getClosePrice(timing.bar);
  if (closePrice <= 0) {
    state.stageCounters.failPriceInvalid += 1;
    return { success: false, kind: mapped.isClose ? 'close' : 'open', side: mapped.positionSide, reason: 'invalid_price' };
  }

  const execPrice = applySlippage(closePrice, mapped.orderSide, risk.slippageBps);
  if (mapped.isClose) {
    if (!state.position) {
      state.stageCounters.failCloseWithoutPosition += 1;
      return { success: false, kind: 'close', side: mapped.positionSide, reason: 'close_without_position' };
    }
    if (state.position.side !== mapped.positionSide) {
      state.stageCounters.failCloseSideMismatch += 1;
      return { success: false, kind: 'close', side: mapped.positionSide, reason: 'close_side_mismatch' };
    }
    const success = closePosition(state, timing, execPrice, risk.feeRate, 'Signal', risk.slippageBps);
    if (success) {
      state.signalCount += 1;
      if (mapped.positionSide === 'Long') {
        state.stageCounters.closeLongSuccess += 1;
      } else {
        state.stageCounters.closeShortSuccess += 1;
      }
      return { success: true, kind: 'close', side: mapped.positionSide, reason: 'ok' };
    }
    return { success: false, kind: 'close', side: mapped.positionSide, reason: 'close_without_position' };
  }

  if (state.position) {
    if (state.position.side === mapped.positionSide) {
      state.stageCounters.failSameSidePosition += 1;
      return { success: false, kind: 'open', side: mapped.positionSide, reason: 'same_side_position' };
    }
    if (!risk.autoReverse) {
      state.stageCounters.failNeedAutoReverse += 1;
      return { success: false, kind: 'open', side: mapped.positionSide, reason: 'need_auto_reverse' };
    }
    closePosition(state, timing, execPrice, risk.feeRate, 'Reverse', risk.slippageBps);
  }

  if (risk.orderQty <= 0) {
    state.stageCounters.failOrderQtyInvalid += 1;
    return { success: false, kind: 'open', side: mapped.positionSide, reason: 'invalid_order_qty' };
  }

  const entryFee = calculateFee(execPrice, risk.orderQty, risk.feeRate);
  state.position = {
    side: mapped.positionSide,
    entryPrice: execPrice,
    entryTime: timing.timestamp,
    qty: risk.orderQty,
    entryFee,
    stopLossPrice: buildStopLossPrice(execPrice, risk.stopLossPct, risk.leverage, mapped.positionSide),
    takeProfitPrice: buildTakeProfitPrice(execPrice, risk.takeProfitPct, risk.leverage, mapped.positionSide),
    fundingAccrued: 0,
  };
  pushBacktestEvent(state, {
    timestamp: timing.timestamp,
    type: 'Open',
    message: `开仓 ${mapped.positionSide} 价格=${execPrice.toFixed(4)} 数量=${risk.orderQty}`,
  });
  state.signalCount += 1;
  if (mapped.positionSide === 'Long') {
    state.stageCounters.openLongSuccess += 1;
  } else {
    state.stageCounters.openShortSuccess += 1;
  }
  return { success: true, kind: 'open', side: mapped.positionSide, reason: 'ok' };
};

const executeActions = (
  actions: RuntimeActionSet,
  state: LocalBacktestState,
  timing: LocalBacktestTiming,
  risk: LocalBacktestRiskConfig,
) => {
  if (!actions.enabled) {
    return;
  }

  let optionalSuccessCount = 0;
  let hasEnabled = false;

  actions.actions.forEach((action) => {
    if (!action.enabled) return;
    hasEnabled = true;
    let success = false;
    if (action.method === 'MakeTrade') {
      const result = executeMakeTrade(state, timing, action.params[0] || '', risk);
      success = result.success;
    }
    if (action.required && !success) {
      optionalSuccessCount = -1;
      return;
    }
    if (!action.required && success) {
      optionalSuccessCount += 1;
    }
  });

  if (!hasEnabled) return;
  if (optionalSuccessCount < actions.minPassConditions) return;
};

const evaluateEntryFilters = (
  branch: RuntimeBranch,
  resolver: LocalBacktestResolver,
  index: number,
  cache: Map<string, boolean>,
) => {
  if (!branch.enabled) return true;
  if (!branch.filters || !branch.filters.enabled) return true;
  if (branch.filters.groups.length === 0) return true;
  precomputeRequiredConditions(branch.filters, resolver, index, cache);
  return evaluateChecks(branch.filters, resolver, index, cache);
};

const executeBranch = (
  branch: RuntimeBranch,
  resolver: LocalBacktestResolver,
  index: number,
  cache: Map<string, boolean>,
  state: LocalBacktestState,
  timing: LocalBacktestTiming,
  risk: LocalBacktestRiskConfig,
  branchTag: 'entry_long' | 'entry_short' | 'exit_long' | 'exit_short',
) => {
  if (!branch.enabled) {
    return false;
  }
  let passCount = 0;
  branch.containers.forEach((container) => {
    if (!container?.checks) return;
    precomputeRequiredConditions(container.checks, resolver, index, cache);
    if (evaluateChecks(container.checks, resolver, index, cache)) {
      passCount += 1;
    }
  });
  if (passCount < branch.minPassConditionContainer) {
    return false;
  }
  if (branchTag === 'entry_long') {
    state.stageCounters.entryLongBranchPass += 1;
  } else if (branchTag === 'entry_short') {
    state.stageCounters.entryShortBranchPass += 1;
  } else if (branchTag === 'exit_long') {
    state.stageCounters.exitLongBranchPass += 1;
  } else {
    state.stageCounters.exitShortBranchPass += 1;
  }
  executeActions(branch.onPass, state, timing, risk);
  return true;
};

const processRisk = (
  state: LocalBacktestState,
  timing: LocalBacktestTiming,
  risk: LocalBacktestRiskConfig,
) => {
  if (!state.position) return;

  const close = getClosePrice(timing.bar);
  const high = getHighPrice(timing.bar);
  const low = getLowPrice(timing.bar);
  if (close <= 0 || high <= 0 || low <= 0) return;

  const stopLossHit = checkStopLoss(state.position, high, low);
  const takeProfitHit = checkTakeProfit(state.position, high, low);
  if (!stopLossHit && !takeProfitHit) return;

  const reason = takeProfitHit ? 'TakeProfit' : 'StopLoss';
  const orderSide = state.position.side === 'Long' ? 'sell' : 'buy';
  const execPrice = applySlippage(close, orderSide, risk.slippageBps);
  const closed = closePosition(state, timing, execPrice, risk.feeRate, reason, risk.slippageBps);
  if (!closed) {
    return;
  }
  if (takeProfitHit) {
    state.stageCounters.riskCloseTakeProfit += 1;
  } else {
    state.stageCounters.riskCloseStopLoss += 1;
  }
};

const applyFundingRate = (
  state: LocalBacktestState,
  timing: LocalBacktestTiming,
  risk: LocalBacktestRiskConfig,
  timeframeMs: number,
) => {
  if (!state.position || risk.fundingRate === 0 || timeframeMs <= 0) {
    return;
  }

  const close = getClosePrice(timing.bar);
  if (close <= 0) {
    return;
  }

  // 与后端一致：按 8 小时结算周期，按当前 K 线时长比例分摊资金费率。
  const ratio = timeframeMs / FUNDING_INTERVAL_MS;
  if (ratio <= 0) {
    return;
  }
  const notional = close * state.position.qty;
  const funding = notional * risk.fundingRate * ratio;
  if (funding === 0) {
    return;
  }

  if (state.position.side === 'Long') {
    state.position.fundingAccrued += funding;
    state.accumulatedFunding += funding;
    state.realizedPnl -= funding;
  } else {
    state.position.fundingAccrued -= funding;
    state.accumulatedFunding -= funding;
    state.realizedPnl += funding;
  }
};

const updateMetrics = (
  metrics: LocalBacktestMetrics,
  state: LocalBacktestState,
  bar: KLineData,
  initialCapital: number,
  timeframeMs: number,
) => {
  const timestamp = toTimestamp(bar);
  let unrealized = 0;
  if (state.position) {
    const close = getClosePrice(bar);
    if (close > 0) {
      unrealized = calculatePositionPnl(state.position, close);
    }
  }

  const equity = initialCapital + state.realizedPnl + unrealized;
  const hasLastEquity = Number.isFinite(metrics.lastEquity);
  const periodPnl = hasLastEquity ? equity - metrics.lastEquity : 0;

  if (metrics.firstTimestamp <= 0 && timestamp > 0) {
    metrics.firstTimestamp = timestamp;
  }
  if (timestamp > 0) {
    metrics.lastTimestamp = timestamp;
  }

  if (metrics.peakEquity < 0 || equity >= metrics.peakEquity) {
    metrics.peakEquity = equity;
    metrics.peakEquityAt = timestamp;
    metrics.drawdownStartAt = 0;
  }

  let drawdown = 0;
  if (metrics.peakEquity > 0) {
    drawdown = (metrics.peakEquity - equity) / metrics.peakEquity;
    if (drawdown > metrics.maxDrawdown) {
      metrics.maxDrawdown = drawdown;
    }
    if (drawdown > EPS_ZERO) {
      if (metrics.drawdownStartAt <= 0) {
        metrics.drawdownStartAt = metrics.peakEquityAt > 0 ? metrics.peakEquityAt : timestamp;
      }
      const drawdownDurationMs = timestamp > metrics.drawdownStartAt
        ? timestamp - metrics.drawdownStartAt
        : 0;
      if (drawdownDurationMs > metrics.maxDrawdownDurationMs) {
        metrics.maxDrawdownDurationMs = drawdownDurationMs;
      }
    } else {
      metrics.drawdownStartAt = 0;
    }
  }

  if (metrics.maxEquity < 0 || equity > metrics.maxEquity) {
    metrics.maxEquity = equity;
    metrics.maxEquityAt = timestamp;
  }
  if (metrics.minEquity < 0 || equity < metrics.minEquity) {
    metrics.minEquity = equity;
    metrics.minEquityAt = timestamp;
  }

  if (hasLastEquity) {
    if (periodPnl > metrics.maxPeriodProfit) {
      metrics.maxPeriodProfit = periodPnl;
      metrics.maxPeriodProfitAt = timestamp;
    }
    if (periodPnl < metrics.maxPeriodLoss) {
      metrics.maxPeriodLoss = periodPnl;
      metrics.maxPeriodLossAt = timestamp;
    }

    if (Math.abs(metrics.lastEquity) > EPS_ZERO && timeframeMs > 0) {
      const periodReturn = periodPnl / metrics.lastEquity;
      if (Number.isFinite(periodReturn)) {
        metrics.returnCount += 1;
        metrics.returnSum += periodReturn;
        metrics.returnSquareSum += periodReturn * periodReturn;
        if (periodReturn < 0) {
          metrics.downsideCount += 1;
          metrics.downsideSquareSum += periodReturn * periodReturn;
        }
        metrics.timeframeMsTotal += timeframeMs;
        metrics.timeframeMsCount += 1;
      }
    }
  }

  metrics.equityPointCount += 1;
  metrics.equityPreview.push({
    timestamp,
    equity,
    realizedPnl: state.realizedPnl,
    unrealizedPnl: unrealized,
    periodPnl,
    drawdown,
  });
  if (metrics.equityPreview.length > MAX_EQUITY_PREVIEW_POINTS + 48) {
    metrics.equityPreview.splice(0, metrics.equityPreview.length - MAX_EQUITY_PREVIEW_POINTS);
  }

  metrics.lastEquity = equity;
  metrics.lastUnrealizedPnl = unrealized;
};

export type LocalBacktestSummary = {
  status: 'blocked' | 'waiting_data' | 'running';
  message: string;
  branchMode: 'long_short';
  bars: number;
  signalCount: number;
  tradeCount: number;
  winCount: number;
  lossCount: number;
  winRate: number;
  totalProfit: number;
  totalReturn: number;
  maxDrawdown: number;
  realizedPnl: number;
  unrealizedPnl: number;
  lastEvent: string;
  hasOpenPosition: boolean;
  openPositionSide: 'Long' | 'Short' | '';
  openPositionEntryPrice: number;
  warningCount: number;
  warnings: string[];
  diagnostics: string[];
  trades: LocalBacktestTrade[];
  events: LocalBacktestEvent[];
  equityPreview: LocalBacktestEquityPoint[];
  stats: LocalBacktestStats;
  tradeSummary: LocalBacktestTradeSummary;
  equitySummary: LocalBacktestEquitySummary;
  eventSummary: LocalBacktestEventSummary;
  totalFee: number;
  totalFunding: number;
  processedAt: number;
};

export type LocalBacktestInput = {
  bars: KLineData[];
  selectedIndicators: GeneratedIndicatorPayload[];
  indicatorOutputGroups: IndicatorOutputGroup[];
  logicContainers: ConditionContainer[];
  filterContainers: ConditionContainer[];
  methodOptions: MethodOption[];
  takeProfitPct: number;
  stopLossPct: number;
  leverage: number;
  orderQty: number;
  feeRate?: number;
  fundingRate?: number;
  slippageBps?: number;
  autoReverse?: boolean;
  initialCapital?: number;
  executionMode?: 'batch_open_close' | 'timeline';
  useStrategyRuntime?: boolean;
};

export const buildEmptyBacktestSummary = (
  status: LocalBacktestSummary['status'],
  message: string,
): LocalBacktestSummary => ({
  status,
  message,
  branchMode: 'long_short',
  bars: 0,
  signalCount: 0,
  tradeCount: 0,
  winCount: 0,
  lossCount: 0,
  winRate: 0,
  totalProfit: 0,
  totalReturn: 0,
  maxDrawdown: 0,
  realizedPnl: 0,
  unrealizedPnl: 0,
  lastEvent: '-',
  hasOpenPosition: false,
  openPositionSide: '',
  openPositionEntryPrice: 0,
  warningCount: 0,
  warnings: [],
  diagnostics: [],
  trades: [],
  events: [],
  equityPreview: [],
  stats: {
    totalProfit: 0,
    totalReturn: 0,
    maxDrawdown: 0,
    winRate: 0,
    tradeCount: 0,
    avgProfit: 0,
    profitFactor: 0,
    avgWin: 0,
    avgLoss: 0,
    sharpeRatio: 0,
    sortinoRatio: 0,
    annualizedReturn: 0,
    maxConsecutiveLosses: 0,
    maxConsecutiveWins: 0,
    avgHoldingMs: 0,
    maxDrawdownDurationMs: 0,
    calmarRatio: 0,
  },
  tradeSummary: {
    totalCount: 0,
    winCount: 0,
    lossCount: 0,
    maxProfit: 0,
    maxLoss: 0,
    totalFee: 0,
    totalFunding: 0,
    avgPnl: 0,
    avgHoldingMs: 0,
  },
  equitySummary: {
    pointCount: 0,
    maxEquity: 0,
    maxEquityAt: 0,
    minEquity: 0,
    minEquityAt: 0,
    maxPeriodProfit: 0,
    maxPeriodProfitAt: 0,
    maxPeriodLoss: 0,
    maxPeriodLossAt: 0,
  },
  eventSummary: {
    totalCount: 0,
    firstTimestamp: 0,
    lastTimestamp: 0,
    typeCounts: {},
  },
  totalFee: 0,
  totalFunding: 0,
  processedAt: Date.now(),
});

type LocalBacktestRuntimeContext = {
  bars: KLineData[];
  warnings: string[];
  branchConfigDiagnostics: BranchConfigDiagnostic[];
  executionMode: 'batch_open_close' | 'timeline';
  useStrategyRuntime: boolean;
  resolver: LocalBacktestResolver;
  runtime: ReturnType<typeof buildRuntimeBundle>;
  cache: Map<string, boolean>;
  state: LocalBacktestState;
  risk: LocalBacktestRiskConfig;
  initialCapital: number;
  metrics: LocalBacktestMetrics;
};

type LocalBacktestPrepareResult =
  | { summary: LocalBacktestSummary; context?: undefined }
  | { summary?: undefined; context: LocalBacktestRuntimeContext };

const prepareBacktestContext = (input: LocalBacktestInput): LocalBacktestPrepareResult => {
  const validRiskConfig =
    input.takeProfitPct > 0
    && input.stopLossPct > 0
    && input.leverage > 0
    && input.orderQty > 0;
  if (!validRiskConfig) {
    return {
      summary: buildEmptyBacktestSummary('blocked', '参数未就绪：请填写止盈、止损、杠杆、开仓数量'),
    };
  }

  const bars = input.bars || [];
  if (bars.length === 0) {
    return { summary: buildEmptyBacktestSummary('waiting_data', '等待 K 线数据') };
  }

  const { seriesByValueId, warnings } = getIndicatorSeriesWithCache(bars, input.selectedIndicators);
  const valueOptionMap = new Map<string, ValueOption>();
  input.indicatorOutputGroups.forEach((group) => {
    group.options.forEach((option) => {
      valueOptionMap.set(option.id, option);
    });
  });

  const seriesByRefKey = new Map<string, Array<number | undefined>>();
  for (const [valueId, series] of seriesByValueId.entries()) {
    const option = valueOptionMap.get(valueId);
    const key = buildRefKey(option?.ref);
    if (key) {
      seriesByRefKey.set(key, series);
    }
  }

  const resolveValueById = (valueId: string, index: number, offset: number): number | null => {
    const option = valueOptionMap.get(valueId);
    if (!option || !option.ref) {
      return null;
    }
    const ref = option.ref;
    const baseOffset = getBaseOffset(ref.offsetRange);
    const effectiveOffset = baseOffset + Math.max(0, offset);
    const targetIndex = index - effectiveOffset;
    if (targetIndex < 0 || targetIndex >= bars.length) {
      return null;
    }

    const refType = normalizeLower(ref.refType);
    if (refType === 'const' || refType === 'number' || refType === 'constant') {
      return toFiniteNumber(ref.input);
    }
    if (refType === 'indicator' || refType === 'publicindicator') {
      const byValueId = seriesByValueId.get(valueId);
      if (byValueId) {
        const value = byValueId[targetIndex];
        return typeof value === 'number' && Number.isFinite(value) ? value : null;
      }
      const byRef = seriesByRefKey.get(buildRefKey(ref));
      if (byRef) {
        const value = byRef[targetIndex];
        return typeof value === 'number' && Number.isFinite(value) ? value : null;
      }
      return null;
    }
    return resolveFieldValue(bars[targetIndex], ref.input);
  };

  const resolver: LocalBacktestResolver = {
    resolveArgValue: (arg, index, offset) => {
      if (arg.kind === 'const') {
        return arg.value;
      }
      return resolveValueById(arg.valueId, index, offset);
    },
  };

  const runtime = getRuntimeBundleWithCache(input.logicContainers, input.filterContainers, input.methodOptions);
  const knownValueIds = new Set<string>(valueOptionMap.keys());
  const branchConfigDiagnostics: BranchConfigDiagnostic[] = [
    collectBranchConfigDiagnostic('开多', runtime.entryLong, knownValueIds),
    collectBranchConfigDiagnostic('平多', runtime.exitLong, knownValueIds),
    collectBranchConfigDiagnostic('开空', runtime.entryShort, knownValueIds),
    collectBranchConfigDiagnostic('平空', runtime.exitShort, knownValueIds),
  ];
  const executionMode = input.executionMode === 'timeline' ? 'timeline' : 'batch_open_close';
  const useStrategyRuntime = input.useStrategyRuntime !== undefined ? Boolean(input.useStrategyRuntime) : true;
  const cache = new Map<string, boolean>();
  const state: LocalBacktestState = {
    position: null,
    realizedPnl: 0,
    trades: [],
    events: [],
    eventTypeCounts: {},
    signalCount: 0,
    accumulatedFunding: 0,
    tradeAggregates: createTradeAggregates(),
    stageCounters: createStageCounters(bars.length),
  };
  const risk: LocalBacktestRiskConfig = {
    orderQty: input.orderQty,
    leverage: Math.max(1, Math.trunc(input.leverage)),
    stopLossPct: input.stopLossPct,
    takeProfitPct: input.takeProfitPct,
    feeRate: input.feeRate ?? DEFAULT_FEE_RATE,
    fundingRate: input.fundingRate ?? DEFAULT_FUNDING_RATE,
    slippageBps: input.slippageBps !== undefined ? Math.max(0, Math.trunc(input.slippageBps)) : DEFAULT_SLIPPAGE_BPS,
    autoReverse: input.autoReverse ?? false,
  };
  const initialCapital = input.initialCapital ?? DEFAULT_INITIAL_CAPITAL;
  const metrics: LocalBacktestMetrics = {
    maxDrawdown: 0,
    peakEquity: -1,
    peakEquityAt: 0,
    drawdownStartAt: 0,
    maxDrawdownDurationMs: 0,
    maxEquity: -1,
    maxEquityAt: 0,
    minEquity: -1,
    minEquityAt: 0,
    maxPeriodProfit: Number.NEGATIVE_INFINITY,
    maxPeriodProfitAt: 0,
    maxPeriodLoss: Number.POSITIVE_INFINITY,
    maxPeriodLossAt: 0,
    equityPointCount: 0,
    equityPreview: [],
    firstTimestamp: 0,
    lastTimestamp: 0,
    lastEquity: Number.NaN,
    returnCount: 0,
    returnSum: 0,
    returnSquareSum: 0,
    downsideCount: 0,
    downsideSquareSum: 0,
    timeframeMsTotal: 0,
    timeframeMsCount: 0,
    lastUnrealizedPnl: 0,
  };

  return {
    context: {
      bars,
      warnings,
      branchConfigDiagnostics,
      executionMode,
      useStrategyRuntime,
      resolver,
      runtime,
      cache,
      state,
      risk,
      initialCapital,
      metrics,
    },
  };
};

const processBacktestBar = (context: LocalBacktestRuntimeContext, index: number) => {
  const { bars, state, risk, resolver, runtime, cache, initialCapital, metrics } = context;
  const timing: LocalBacktestTiming = {
    timestamp: toTimestamp(bars[index]),
    bar: bars[index],
  };
  state.stageCounters.barsProcessed += 1;
  if (getClosePrice(timing.bar) <= 0) {
    state.stageCounters.barsInvalidPrice += 1;
  }
  const previousTimestamp = index > 0 ? toTimestamp(bars[index - 1]) : 0;
  const timeframeMs = previousTimestamp > 0 && timing.timestamp > previousTimestamp
    ? timing.timestamp - previousTimestamp
    : 0;
  applyFundingRate(state, timing, risk, timeframeMs);
  processRisk(state, timing, risk);
  // 与后端执行顺序保持一致：先处理风控，再处理平仓分支，最后处理开仓分支。
  executeBranch(runtime.exitLong, resolver, index, cache, state, timing, risk, 'exit_long');
  executeBranch(runtime.exitShort, resolver, index, cache, state, timing, risk, 'exit_short');

  const passLongEntry = evaluateEntryFilters(runtime.entryLong, resolver, index, cache);
  const passShortEntry = evaluateEntryFilters(runtime.entryShort, resolver, index, cache);
  if (passLongEntry) {
    state.stageCounters.filterLongPass += 1;
  } else {
    state.stageCounters.filterLongBlock += 1;
  }
  if (passShortEntry) {
    state.stageCounters.filterShortPass += 1;
  } else {
    state.stageCounters.filterShortBlock += 1;
  }
  if (passLongEntry) {
    executeBranch(runtime.entryLong, resolver, index, cache, state, timing, risk, 'entry_long');
  }
  if (passShortEntry) {
    executeBranch(runtime.entryShort, resolver, index, cache, state, timing, risk, 'entry_short');
  }
  updateMetrics(metrics, state, bars[index], initialCapital, timeframeMs);
};

const buildOpenPositionSnapshot = (
  state: LocalBacktestState,
  bars: KLineData[],
  processedBars: number,
  risk: LocalBacktestRiskConfig,
): LocalBacktestTrade | null => {
  if (!state.position || processedBars <= 0) {
    return null;
  }
  const lastBar = bars[processedBars - 1];
  const closePrice = getClosePrice(lastBar);
  const snapshotPrice = closePrice > 0 ? closePrice : state.position.entryPrice;
  const snapshotPnl = calculatePositionPnl(state.position, snapshotPrice) - state.position.entryFee;
  return {
    side: state.position.side,
    entryTime: state.position.entryTime,
    exitTime: toTimestamp(lastBar),
    entryPrice: state.position.entryPrice,
    exitPrice: snapshotPrice,
    stopLossPrice: state.position.stopLossPrice,
    takeProfitPrice: state.position.takeProfitPrice,
    qty: state.position.qty,
    fee: state.position.entryFee,
    funding: state.position.fundingAccrued,
    pnl: snapshotPnl,
    exitReason: 'Open',
    isOpen: true,
    slippageBps: risk.slippageBps,
  };
};

const buildBacktestSummaryFromContext = (
  context: LocalBacktestRuntimeContext,
  processedBars: number,
  done: boolean,
): LocalBacktestSummary => {
  const safeProcessedBars = Math.max(0, Math.min(processedBars, context.bars.length));
  const snapshot = buildOpenPositionSnapshot(context.state, context.bars, safeProcessedBars, context.risk);
  const trades = snapshot ? [...context.state.trades, snapshot] : [...context.state.trades];
  const aggregated = context.state.tradeAggregates;
  const tradeCount = aggregated.closedTradeCount;
  const winCount = aggregated.winCount;
  const lossCount = aggregated.lossCount;
  const totalProfit = context.state.realizedPnl;
  const winRate = tradeCount > 0 ? winCount / tradeCount : 0;
  const totalReturn = context.initialCapital > 0 ? totalProfit / context.initialCapital : 0;
  const lastEvent = context.state.events.length > 0
    ? context.state.events[context.state.events.length - 1].message
    : '-';
  const totalFee = aggregated.totalFee;
  const totalFunding = context.state.accumulatedFunding;
  const avgProfit = tradeCount > 0 ? aggregated.sumPnl / tradeCount : 0;
  const avgWin = winCount > 0 ? aggregated.sumWinPnl / winCount : 0;
  const avgLoss = lossCount > 0 ? -(aggregated.sumLossAbsPnl / lossCount) : 0;
  const profitFactor = aggregated.grossLossAbs > EPS_ZERO
    ? aggregated.grossProfit / aggregated.grossLossAbs
    : (aggregated.grossProfit > EPS_ZERO ? Number.POSITIVE_INFINITY : 0);
  const avgHoldingMs = tradeCount > 0 ? aggregated.totalHoldingMs / tradeCount : 0;
  const currentEquity = context.initialCapital + context.state.realizedPnl + context.metrics.lastUnrealizedPnl;
  const elapsedMs = context.metrics.firstTimestamp > 0 && context.metrics.lastTimestamp > context.metrics.firstTimestamp
    ? context.metrics.lastTimestamp - context.metrics.firstTimestamp
    : 0;
  const annualizedReturn =
    context.initialCapital > 0
    && currentEquity > 0
    && elapsedMs > 0
    ? Math.pow(currentEquity / context.initialCapital, YEAR_MS / elapsedMs) - 1
    : 0;
  const avgTimeframeMs = context.metrics.timeframeMsCount > 0
    ? context.metrics.timeframeMsTotal / context.metrics.timeframeMsCount
    : 0;
  const periodsPerYear = avgTimeframeMs > 0 ? YEAR_MS / avgTimeframeMs : 0;
  let sharpeRatio = 0;
  let sortinoRatio = 0;
  if (context.metrics.returnCount > 1 && periodsPerYear > 0) {
    const mean = context.metrics.returnSum / context.metrics.returnCount;
    const variance = Math.max(
      0,
      context.metrics.returnSquareSum / context.metrics.returnCount - mean * mean,
    );
    const stdDev = Math.sqrt(variance);
    if (stdDev > EPS_ZERO) {
      sharpeRatio = (mean / stdDev) * Math.sqrt(periodsPerYear);
    }
    if (context.metrics.downsideCount > 0) {
      const downsideDev = Math.sqrt(context.metrics.downsideSquareSum / context.metrics.downsideCount);
      if (downsideDev > EPS_ZERO) {
        sortinoRatio = (mean / downsideDev) * Math.sqrt(periodsPerYear);
      }
    }
  }
  const calmarRatio = context.metrics.maxDrawdown > EPS_ZERO
    ? annualizedReturn / context.metrics.maxDrawdown
    : (annualizedReturn > EPS_ZERO ? Number.POSITIVE_INFINITY : 0);
  const stats: LocalBacktestStats = {
    totalProfit,
    totalReturn,
    maxDrawdown: context.metrics.maxDrawdown,
    winRate,
    tradeCount,
    avgProfit,
    profitFactor,
    avgWin,
    avgLoss,
    sharpeRatio,
    sortinoRatio,
    annualizedReturn,
    maxConsecutiveLosses: aggregated.maxConsecutiveLosses,
    maxConsecutiveWins: aggregated.maxConsecutiveWins,
    avgHoldingMs,
    maxDrawdownDurationMs: context.metrics.maxDrawdownDurationMs,
    calmarRatio,
  };
  const tradeSummary: LocalBacktestTradeSummary = {
    totalCount: tradeCount,
    winCount,
    lossCount,
    maxProfit: Number.isFinite(aggregated.maxProfit) ? aggregated.maxProfit : 0,
    maxLoss: Number.isFinite(aggregated.maxLoss) ? aggregated.maxLoss : 0,
    totalFee,
    totalFunding,
    avgPnl: avgProfit,
    avgHoldingMs,
  };
  const equitySummary: LocalBacktestEquitySummary = {
    pointCount: context.metrics.equityPointCount,
    maxEquity: context.metrics.maxEquity >= 0 ? context.metrics.maxEquity : currentEquity,
    maxEquityAt: context.metrics.maxEquityAt,
    minEquity: context.metrics.minEquity >= 0 ? context.metrics.minEquity : currentEquity,
    minEquityAt: context.metrics.minEquityAt,
    maxPeriodProfit: Number.isFinite(context.metrics.maxPeriodProfit) ? context.metrics.maxPeriodProfit : 0,
    maxPeriodProfitAt: context.metrics.maxPeriodProfitAt,
    maxPeriodLoss: Number.isFinite(context.metrics.maxPeriodLoss) ? context.metrics.maxPeriodLoss : 0,
    maxPeriodLossAt: context.metrics.maxPeriodLossAt,
  };
  const events = [...context.state.events];
  const eventSummary: LocalBacktestEventSummary = {
    totalCount: events.length,
    firstTimestamp: events.length > 0 ? events[0].timestamp : 0,
    lastTimestamp: events.length > 0 ? events[events.length - 1].timestamp : 0,
    typeCounts: { ...context.state.eventTypeCounts },
  };
  const stage = context.state.stageCounters;
  const diagnostics = [
    ...context.branchConfigDiagnostics.map((item) => item.line),
    `配置-执行: mode=${context.executionMode} runtimeGate=${context.useStrategyRuntime ? 'on' : 'off'}（本地单标的预览）`,
    `阶段1-数据: 总样本=${stage.barsTotal} 已处理=${stage.barsProcessed} 无效收盘价=${stage.barsInvalidPrice}`,
    `阶段2-筛选: 开多放行=${stage.filterLongPass} 拦截=${stage.filterLongBlock} | 开空放行=${stage.filterShortPass} 拦截=${stage.filterShortBlock}`,
    `阶段3-条件命中: 开多分支通过=${stage.entryLongBranchPass} 开空分支通过=${stage.entryShortBranchPass} | 平多分支通过=${stage.exitLongBranchPass} 平空分支通过=${stage.exitShortBranchPass}`,
    `阶段4-动作执行: MakeTrade调用=${stage.actionMakeTradeCalls} 开多尝试/成功=${stage.openLongAttempt}/${stage.openLongSuccess} 开空尝试/成功=${stage.openShortAttempt}/${stage.openShortSuccess}`,
    `阶段4-平仓信号: 平多尝试/成功=${stage.closeLongAttempt}/${stage.closeLongSuccess} 平空尝试/成功=${stage.closeShortAttempt}/${stage.closeShortSuccess}`,
    `阶段4-失败原因: 同向持仓=${stage.failSameSidePosition} 未开自动反向=${stage.failNeedAutoReverse} 无持仓可平=${stage.failCloseWithoutPosition} 平仓方向不匹配=${stage.failCloseSideMismatch} 无效价格=${stage.failPriceInvalid} 无效下单量=${stage.failOrderQtyInvalid}`,
    `阶段5-风控平仓: 止损触发=${stage.riskCloseStopLoss} 止盈触发=${stage.riskCloseTakeProfit}`,
    `阶段6-统计: 胜率=${(winRate * 100).toFixed(2)}% 平均盈亏=${avgProfit.toFixed(4)} 盈亏比=${Number.isFinite(profitFactor) ? profitFactor.toFixed(4) : '∞'}`,
    `阶段7-结果: 信号数=${context.state.signalCount} 平仓笔数=${tradeCount} 持仓中=${snapshot ? 1 : 0} 净收益=${totalProfit.toFixed(4)} 事件数=${events.length}`,
  ];
  if (stage.filterLongPass > 0 && stage.entryLongBranchPass === 0) {
    const longBranch = context.branchConfigDiagnostics.find((item) => item.label === '开多');
    if (longBranch) {
      if (longBranch.enabledConditionCount <= 0) {
        diagnostics.push('结论提示: 开多筛选已放行，但开多条件数为0。请把交叉条件放在“开多条件”容器，而不是仅放在“开多筛选器”。');
      } else if (longBranch.unresolvedValueCount > 0) {
        diagnostics.push('结论提示: 开多条件存在未解析值ID。请重新拖拽左右值（EMA/SMA）后再回测。');
      } else if (longBranch.invalidConditionCount > 0) {
        diagnostics.push('结论提示: 开多条件存在无效参数。请检查右值类型、参数数值、必填项。');
      } else {
        diagnostics.push('结论提示: 开多条件已配置但在当前样本区间未命中。可切换更长时间范围或降低条件约束。');
      }
    }
  }
  const progressMessage = done
    ? (context.warnings.length > 0
      ? `本地回测已执行（${context.warnings.length} 条指标警告）`
      : '本地回测已执行（与后端一致：执行多空分支）')
    : `本地回测进行中：已处理 ${safeProcessedBars}/${context.bars.length} 根K线，累计仓位 ${trades.length}。`;

  return {
    status: 'running',
    message: progressMessage,
    branchMode: 'long_short',
    bars: safeProcessedBars,
    signalCount: context.state.signalCount,
    tradeCount,
    winCount,
    lossCount,
    winRate,
    totalProfit,
    totalReturn,
    maxDrawdown: context.metrics.maxDrawdown,
    realizedPnl: context.state.realizedPnl,
    unrealizedPnl: context.metrics.lastUnrealizedPnl,
    lastEvent,
    hasOpenPosition: Boolean(context.state.position),
    openPositionSide: context.state.position?.side || '',
    openPositionEntryPrice: context.state.position?.entryPrice || 0,
    warningCount: context.warnings.length,
    warnings: context.warnings,
    diagnostics,
    trades,
    events,
    equityPreview: [...context.metrics.equityPreview],
    stats,
    tradeSummary,
    equitySummary,
    eventSummary,
    totalFee,
    totalFunding,
    processedAt: Date.now(),
  };
};

export type LocalBacktestRealtimeProgress = {
  summary: LocalBacktestSummary;
  processedBars: number;
  totalBars: number;
  progress: number;
  done: boolean;
  elapsedMs: number;
};

export type LocalBacktestRealtimeOptions = {
  chunkSize?: number;
  tickMs?: number;
  onProgress?: (progress: LocalBacktestRealtimeProgress) => void;
};

const logBacktestDiagnostics = (summary: LocalBacktestSummary, scene: string) => {
  if (typeof console === 'undefined') {
    return;
  }
  const diagnostics = Array.isArray(summary.diagnostics) ? summary.diagnostics : [];
  if (diagnostics.length <= 0) {
    return;
  }
  console.groupCollapsed(`[本地回测诊断][${scene}] bars=${summary.bars} trades=${summary.tradeCount}`);
  diagnostics.forEach((line) => console.log(line));
  if (summary.warningCount > 0) {
    console.log(`[指标警告] ${summary.warningCount} 条`);
  }
  console.groupEnd();
};

export const runLocalBacktest = (input: LocalBacktestInput): LocalBacktestSummary => {
  const prepared = prepareBacktestContext(input);
  if (prepared.summary) {
    logBacktestDiagnostics(prepared.summary, 'sync');
    return prepared.summary;
  }
  const { context } = prepared;
  for (let index = 0; index < context.bars.length; index += 1) {
    processBacktestBar(context, index);
  }
  const finalSummary = buildBacktestSummaryFromContext(context, context.bars.length, true);
  logBacktestDiagnostics(finalSummary, 'sync');
  return finalSummary;
};

export const runLocalBacktestRealtime = (
  input: LocalBacktestInput,
  options: LocalBacktestRealtimeOptions = {},
) => {
  const prepared = prepareBacktestContext(input);
  const onProgress = options.onProgress;
  const startedAt = Date.now();
  const emitFinal = (summary: LocalBacktestSummary) => {
    logBacktestDiagnostics(summary, 'realtime');
    onProgress?.({
      summary,
      processedBars: summary.bars,
      totalBars: summary.bars,
      progress: 1,
      done: true,
      elapsedMs: Date.now() - startedAt,
    });
  };

  if (prepared.summary) {
    emitFinal(prepared.summary);
    return { cancel: () => {} };
  }

  const { context } = prepared;
  const totalBars = context.bars.length;
  const chunkSize = Math.max(50, Math.trunc(options.chunkSize ?? 320));
  const tickMs = Math.max(0, Math.trunc(options.tickMs ?? 16));
  let cursor = 0;
  let disposed = false;
  let timer: ReturnType<typeof setTimeout> | null = null;
  let diagnosticsLogged = false;

  const emit = (done: boolean) => {
    const summary = buildBacktestSummaryFromContext(context, cursor, done);
    if (done && !diagnosticsLogged) {
      diagnosticsLogged = true;
      logBacktestDiagnostics(summary, 'realtime');
    }
    onProgress?.({
      summary,
      processedBars: cursor,
      totalBars,
      progress: totalBars > 0 ? cursor / totalBars : 1,
      done,
      elapsedMs: Date.now() - startedAt,
    });
  };

  const clearTimer = () => {
    if (timer !== null) {
      clearTimeout(timer);
      timer = null;
    }
  };

  const step = () => {
    if (disposed) {
      return;
    }
    const end = Math.min(totalBars, cursor + chunkSize);
    while (cursor < end) {
      processBacktestBar(context, cursor);
      cursor += 1;
    }
    const done = cursor >= totalBars;
    emit(done);
    if (!done) {
      timer = setTimeout(step, tickMs);
    }
  };

  emit(false);
  timer = setTimeout(step, 0);

  return {
    cancel: () => {
      disposed = true;
      clearTimer();
    },
  };
};
