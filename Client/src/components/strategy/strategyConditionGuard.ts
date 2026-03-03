import type { StrategyValueRef, ValueOption } from './StrategyModule.types';

const MAIN_PANE_INDICATOR_CODES = new Set<string>([
  'MA',
  'MAVP',
  'SMA',
  'EMA',
  'WMA',
  'DEMA',
  'TEMA',
  'TRIMA',
  'KAMA',
  'MAMA',
  'FAMA',
  'T3',
  'BBANDS',
  'MIDPOINT',
  'MIDPRICE',
  'SAR',
  'SAREXT',
  'HT_TRENDLINE',
  'AVGPRICE',
  'MEDPRICE',
  'TYPPRICE',
  'WCLPRICE',
]);

const CONST_REF_TYPES = new Set(['const', 'number']);
const FIELD_REF_TYPES = new Set(['field']);
const INDICATOR_REF_TYPES = new Set(['indicator', 'publicindicator']);

const normalizeText = (value?: string) => (value || '').trim();
const normalizeUpperText = (value?: string) => normalizeText(value).toUpperCase();
const normalizeLowerText = (value?: string) => normalizeText(value).toLowerCase();

const normalizeRefType = (ref?: StrategyValueRef | null) =>
  normalizeText(ref?.refType).toLowerCase();

const normalizeCalcMode = (value?: string) => {
  const mode = normalizeUpperText(value);
  return mode || 'ONBARCLOSE';
};

const normalizeTimeframeText = (value?: string) => {
  const raw = normalizeLowerText(value).replace(/\s+/g, '');
  if (!raw) {
    return '';
  }
  if (/^\d+mo$/.test(raw)) {
    return raw;
  }
  if (/^mo\d+$/.test(raw)) {
    return `${raw.slice(2)}mo`;
  }
  if (/^\d+[mhdw]$/.test(raw)) {
    return raw;
  }
  if (/^[mhdw]\d+$/.test(raw)) {
    return `${raw.slice(1)}${raw[0]}`;
  }
  if (raw.startsWith('mo')) {
    return raw.length > 2 ? `${raw.slice(2)}mo` : '';
  }
  if (raw.startsWith('m')) {
    return raw.length > 1 ? `${raw.slice(1)}m` : '';
  }
  if (raw.startsWith('h')) {
    return raw.length > 1 ? `${raw.slice(1)}h` : '';
  }
  if (raw.startsWith('d')) {
    return raw.length > 1 ? `${raw.slice(1)}d` : '';
  }
  if (raw.startsWith('w')) {
    return raw.length > 1 ? `${raw.slice(1)}w` : '';
  }
  return raw;
};

const normalizeParams = (values?: number[]) => {
  if (!Array.isArray(values) || values.length === 0) {
    return '';
  }
  return values
    .map((item) => {
      const numeric = Number(item);
      return Number.isFinite(numeric) ? numeric.toString() : '';
    })
    .join(',');
};

const normalizeNumberText = (value?: string) => {
  const raw = normalizeText(value);
  if (!raw) {
    return '';
  }
  const numeric = Number(raw);
  if (!Number.isFinite(numeric)) {
    return raw;
  }
  return numeric.toString();
};

export const isConstRef = (ref?: StrategyValueRef | null) =>
  CONST_REF_TYPES.has(normalizeRefType(ref));

export const isFieldRef = (ref?: StrategyValueRef | null) =>
  FIELD_REF_TYPES.has(normalizeRefType(ref));

export const isIndicatorRef = (ref?: StrategyValueRef | null) =>
  INDICATOR_REF_TYPES.has(normalizeRefType(ref));

const resolveRefTimeframe = (
  ref?: StrategyValueRef | null,
  defaultTimeframe?: string,
) => {
  const resolved = normalizeTimeframeText(ref?.timeframe);
  if (resolved) {
    return resolved;
  }
  return normalizeTimeframeText(defaultTimeframe);
};

export const resolveRefPaneKind = (ref?: StrategyValueRef | null): 'main' | 'sub' | 'none' => {
  if (!ref) {
    return 'none';
  }
  if (isConstRef(ref)) {
    return 'none';
  }
  if (isFieldRef(ref)) {
    return 'main';
  }
  if (isIndicatorRef(ref)) {
    const indicatorCode = normalizeUpperText(ref.indicator);
    return MAIN_PANE_INDICATOR_CODES.has(indicatorCode) ? 'main' : 'sub';
  }
  return 'none';
};

const validateRefTimeframeByStrategy = (
  ref?: StrategyValueRef | null,
  defaultTimeframe?: string,
): string | null => {
  if (!ref || !isIndicatorRef(ref)) {
    return null;
  }
  const strategyTimeframe = normalizeTimeframeText(defaultTimeframe);
  if (!strategyTimeframe) {
    return null;
  }
  const referenceTimeframe = normalizeTimeframeText(ref.timeframe);
  if (referenceTimeframe && referenceTimeframe !== strategyTimeframe) {
    return `不允许使用与策略周期不同的指标（策略周期 ${strategyTimeframe}）`;
  }
  return null;
};

export const buildRefSemanticKey = (
  ref?: StrategyValueRef | null,
  defaultTimeframe?: string,
) => {
  if (!ref) {
    return '';
  }
  if (isConstRef(ref)) {
    return `const|${normalizeNumberText(ref.input)}`;
  }
  if (isFieldRef(ref)) {
    return `field|${normalizeUpperText(ref.input)}|${normalizeCalcMode(ref.calcMode)}`;
  }
  if (isIndicatorRef(ref)) {
    return [
      'indicator',
      normalizeUpperText(ref.indicator),
      normalizeUpperText(resolveRefTimeframe(ref, defaultTimeframe)),
      normalizeUpperText(ref.input),
      normalizeUpperText(ref.output),
      normalizeParams(ref.params),
      normalizeCalcMode(ref.calcMode),
    ].join('|');
  }
  return [
    normalizeRefType(ref),
    normalizeUpperText(ref.indicator),
    normalizeUpperText(resolveRefTimeframe(ref, defaultTimeframe)),
    normalizeUpperText(ref.input),
    normalizeUpperText(ref.output),
    normalizeParams(ref.params),
    normalizeCalcMode(ref.calcMode),
  ].join('|');
};

export const validateReferencePairSemantics = (
  left?: StrategyValueRef | null,
  right?: StrategyValueRef | null,
  defaultTimeframe?: string,
): string | null => {
  if (!left || !right) {
    return null;
  }

  if (isConstRef(left) && isConstRef(right)) {
    return '不允许使用数字与数字直接比较';
  }

  if (buildRefSemanticKey(left, defaultTimeframe) === buildRefSemanticKey(right, defaultTimeframe)) {
    return '不允许同一指标或字段与自身比较';
  }

  if (isFieldRef(left) && isFieldRef(right)) {
    return '不允许使用K线字段互相比较';
  }

  if (isIndicatorRef(left) && isIndicatorRef(right)) {
    const leftTimeframe = resolveRefTimeframe(left, defaultTimeframe);
    const rightTimeframe = resolveRefTimeframe(right, defaultTimeframe);
    if (leftTimeframe && rightTimeframe && leftTimeframe !== rightTimeframe) {
      return '不允许跨周期指标直接比较';
    }
  }

  if (!isConstRef(left) && !isConstRef(right)) {
    const leftPane = resolveRefPaneKind(left);
    const rightPane = resolveRefPaneKind(right);
    if (leftPane !== 'none' && rightPane !== 'none' && leftPane !== rightPane) {
      return '不允许主图与副图指标直接比较';
    }
  }

  return null;
};

export const validateConditionArgsSemantics = (
  args: StrategyValueRef[],
  expectedArgsCount: number,
  defaultTimeframe?: string,
): string | null => {
  if (expectedArgsCount < 2) {
    return null;
  }
  if (args.length < expectedArgsCount) {
    return '参数数量不足';
  }
  for (let index = 0; index < expectedArgsCount; index += 1) {
    const refError = validateRefTimeframeByStrategy(args[index], defaultTimeframe);
    if (refError) {
      return refError;
    }
  }

  const pairError = validateReferencePairSemantics(args[0], args[1], defaultTimeframe);
  if (pairError) {
    return pairError;
  }

  if (expectedArgsCount >= 3) {
    const thirdPairError = validateReferencePairSemantics(args[0], args[2], defaultTimeframe);
    if (thirdPairError) {
      return `第三参数不合法：${thirdPairError}`;
    }

    const secondKey = buildRefSemanticKey(args[1], defaultTimeframe);
    const thirdKey = buildRefSemanticKey(args[2], defaultTimeframe);
    if (secondKey && thirdKey && secondKey === thirdKey) {
      return '第二参数与第三参数不能相同';
    }
  }

  return null;
};

export const buildConditionFingerprint = (
  method: string,
  args: StrategyValueRef[],
  params: string[] | undefined,
  defaultTimeframe?: string,
): string => {
  const methodKey = normalizeUpperText(method);
  const argsKey = args.map((arg) => buildRefSemanticKey(arg, defaultTimeframe)).join('#');
  const paramsKey = (params || [])
    .map((item) => normalizeNumberText(item))
    .join(',');
  return `${methodKey}|${argsKey}|${paramsKey}`;
};

export const filterComparableOptionsByLeftRef = (
  leftRef: StrategyValueRef | null | undefined,
  options: ValueOption[],
  extraExcludedRefs?: Array<StrategyValueRef | null | undefined>,
  defaultTimeframe?: string,
) => {
  const excludedKeys = new Set(
    (extraExcludedRefs || [])
      .filter((item): item is StrategyValueRef => Boolean(item))
      .map((item) => buildRefSemanticKey(item, defaultTimeframe)),
  );

  if (!leftRef) {
    return options.filter((option) => {
      const candidateKey = buildRefSemanticKey(option.ref, defaultTimeframe);
      if (excludedKeys.has(candidateKey)) {
        return false;
      }
      return !validateRefTimeframeByStrategy(option.ref, defaultTimeframe);
    });
  }

  return options.filter((option) => {
    const candidateKey = buildRefSemanticKey(option.ref, defaultTimeframe);
    if (excludedKeys.has(candidateKey)) {
      return false;
    }
    if (validateRefTimeframeByStrategy(option.ref, defaultTimeframe)) {
      return false;
    }
    return !validateReferencePairSemantics(leftRef, option.ref, defaultTimeframe);
  });
};
