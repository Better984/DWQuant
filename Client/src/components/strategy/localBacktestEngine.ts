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

type BacktestPosition = {
  side: 'Long' | 'Short';
  entryPrice: number;
  entryTime: number;
  qty: number;
  entryFee: number;
  stopLossPrice: number | null;
  takeProfitPrice: number | null;
};

type BacktestTrade = {
  side: 'Long' | 'Short';
  entryTime: number;
  exitTime: number;
  entryPrice: number;
  exitPrice: number;
  qty: number;
  fee: number;
  pnl: number;
  exitReason: string;
  isOpen: boolean;
};

type BacktestEvent = {
  timestamp: number;
  type: string;
  message: string;
};

type MethodRuntimeMeta = {
  argsCount: number;
  paramDefaults: string[];
};

type LocalBacktestState = {
  position: BacktestPosition | null;
  realizedPnl: number;
  trades: BacktestTrade[];
  events: BacktestEvent[];
  signalCount: number;
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
  slippageBps: number;
  autoReverse: boolean;
};

type LocalBacktestMetrics = {
  maxDrawdown: number;
  peakEquity: number;
  lastUnrealizedPnl: number;
};

const CROSS_METHODS = new Set(['CrossUp', 'CrossDown', 'CrossOver', 'CrossUnder', 'CrossAny']);

const EPS_COMPARE = 1e-10;
const EPS_ZERO = 1e-12;
const DEFAULT_FEE_RATE = 0.0004;
const DEFAULT_SLIPPAGE_BPS = 0;
const DEFAULT_INITIAL_CAPITAL = 10_000;

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
  const minPassConditions =
    enabledConditions.length === 0 ? 1 : optionalConditions.length === 0 ? 0 : 1;

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
) => {
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

const closePosition = (
  state: LocalBacktestState,
  timing: LocalBacktestTiming,
  execPrice: number,
  feeRate: number,
  reason: string,
) => {
  if (!state.position) {
    return false;
  }
  const position = state.position;
  const exitFee = calculateFee(execPrice, position.qty, feeRate);
  const pnl = calculatePositionPnl(position, execPrice) - position.entryFee - exitFee;
  state.realizedPnl += pnl;
  state.trades.push({
    side: position.side,
    entryTime: position.entryTime,
    exitTime: timing.timestamp,
    entryPrice: position.entryPrice,
    exitPrice: execPrice,
    qty: position.qty,
    fee: position.entryFee + exitFee,
    pnl,
    exitReason: reason,
    isOpen: false,
  });
  state.events.push({
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
) => {
  const mapped = tryMapAction(action);
  if (!mapped) {
    return false;
  }
  const closePrice = getClosePrice(timing.bar);
  if (closePrice <= 0) {
    return false;
  }

  const execPrice = applySlippage(closePrice, mapped.orderSide, risk.slippageBps);
  if (mapped.isClose) {
    const success = closePosition(state, timing, execPrice, risk.feeRate, 'Signal');
    if (success) {
      state.signalCount += 1;
    }
    return success;
  }

  if (state.position) {
    if (state.position.side === mapped.positionSide) {
      return false;
    }
    if (!risk.autoReverse) {
      return false;
    }
    closePosition(state, timing, execPrice, risk.feeRate, 'Reverse');
  }

  if (risk.orderQty <= 0) {
    return false;
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
  };
  state.events.push({
    timestamp: timing.timestamp,
    type: 'Open',
    message: `开仓 ${mapped.positionSide} 价格=${execPrice.toFixed(4)} 数量=${risk.orderQty}`,
  });
  state.signalCount += 1;
  return true;
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
      success = executeMakeTrade(state, timing, action.params[0] || '', risk);
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
) => {
  if (!branch.enabled) {
    return;
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
    return;
  }
  executeActions(branch.onPass, state, timing, risk);
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
  closePosition(state, timing, execPrice, risk.feeRate, reason);
};

const updateMetrics = (
  metrics: LocalBacktestMetrics,
  state: LocalBacktestState,
  bar: KLineData,
  initialCapital: number,
) => {
  let unrealized = 0;
  if (state.position) {
    const close = getClosePrice(bar);
    if (close > 0) {
      unrealized = calculatePositionPnl(state.position, close);
    }
  }
  const equity = initialCapital + state.realizedPnl + unrealized;
  if (metrics.peakEquity < 0 || equity > metrics.peakEquity) {
    metrics.peakEquity = equity;
  }
  if (metrics.peakEquity > 0) {
    const drawdown = (metrics.peakEquity - equity) / metrics.peakEquity;
    if (drawdown > metrics.maxDrawdown) {
      metrics.maxDrawdown = drawdown;
    }
  }
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
  slippageBps?: number;
  autoReverse?: boolean;
  initialCapital?: number;
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
  processedAt: Date.now(),
});

export const runLocalBacktest = (input: LocalBacktestInput): LocalBacktestSummary => {
  const validRiskConfig =
    input.takeProfitPct > 0
    && input.stopLossPct > 0
    && input.leverage > 0
    && input.orderQty > 0;
  if (!validRiskConfig) {
    return buildEmptyBacktestSummary('blocked', '参数未就绪：请填写止盈、止损、杠杆、开仓数量');
  }

  const bars = input.bars || [];
  if (bars.length === 0) {
    return buildEmptyBacktestSummary('waiting_data', '等待 K 线数据');
  }

  const { seriesByValueId, warnings } = buildIndicatorSeries(bars, input.selectedIndicators);
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

  const runtime = buildRuntimeBundle(input.logicContainers, input.filterContainers, input.methodOptions);
  const cache = new Map<string, boolean>();
  const state: LocalBacktestState = {
    position: null,
    realizedPnl: 0,
    trades: [],
    events: [],
    signalCount: 0,
  };
  const risk: LocalBacktestRiskConfig = {
    orderQty: input.orderQty,
    leverage: Math.max(1, Math.trunc(input.leverage)),
    stopLossPct: input.stopLossPct,
    takeProfitPct: input.takeProfitPct,
    feeRate: input.feeRate ?? DEFAULT_FEE_RATE,
    slippageBps: input.slippageBps !== undefined ? Math.max(0, Math.trunc(input.slippageBps)) : DEFAULT_SLIPPAGE_BPS,
    autoReverse: input.autoReverse ?? false,
  };
  const initialCapital = input.initialCapital ?? DEFAULT_INITIAL_CAPITAL;
  const metrics: LocalBacktestMetrics = {
    maxDrawdown: 0,
    peakEquity: -1,
    lastUnrealizedPnl: 0,
  };

  for (let index = 0; index < bars.length; index += 1) {
    const timing: LocalBacktestTiming = {
      timestamp: toTimestamp(bars[index]),
      bar: bars[index],
    };
    processRisk(state, timing, risk);
    // 与后端执行顺序保持一致：先处理风控，再处理平仓分支，最后处理开仓分支。
    executeBranch(runtime.exitLong, resolver, index, cache, state, timing, risk);
    executeBranch(runtime.exitShort, resolver, index, cache, state, timing, risk);

    const passLongEntry = evaluateEntryFilters(runtime.entryLong, resolver, index, cache);
    const passShortEntry = evaluateEntryFilters(runtime.entryShort, resolver, index, cache);
    if (passLongEntry) {
      executeBranch(runtime.entryLong, resolver, index, cache, state, timing, risk);
    }
    if (passShortEntry) {
      executeBranch(runtime.entryShort, resolver, index, cache, state, timing, risk);
    }
    updateMetrics(metrics, state, bars[index], initialCapital);
  }

  const closedTrades = state.trades.filter((trade) => !trade.isOpen);
  const tradeCount = closedTrades.length;
  const winCount = closedTrades.filter((trade) => trade.pnl > 0).length;
  const lossCount = closedTrades.filter((trade) => trade.pnl < 0).length;
  const totalProfit = closedTrades.reduce((sum, trade) => sum + trade.pnl, 0);
  const winRate = tradeCount > 0 ? winCount / tradeCount : 0;
  const totalReturn = initialCapital > 0 ? totalProfit / initialCapital : 0;
  const lastEvent = state.events.length > 0 ? state.events[state.events.length - 1].message : '-';

  return {
    status: 'running',
    message: warnings.length > 0
      ? `本地回测已执行（${warnings.length} 条指标警告）`
      : '本地回测已执行（与后端一致：执行多空分支）',
    branchMode: 'long_short',
    bars: bars.length,
    signalCount: state.signalCount,
    tradeCount,
    winCount,
    lossCount,
    winRate,
    totalProfit,
    totalReturn,
    maxDrawdown: metrics.maxDrawdown,
    realizedPnl: state.realizedPnl,
    unrealizedPnl: metrics.lastUnrealizedPnl,
    lastEvent,
    hasOpenPosition: Boolean(state.position),
    openPositionSide: state.position?.side || '',
    openPositionEntryPrice: state.position?.entryPrice || 0,
    warningCount: warnings.length,
    warnings,
    processedAt: Date.now(),
  };
};
