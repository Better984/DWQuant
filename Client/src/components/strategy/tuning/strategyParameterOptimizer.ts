import type { GeneratedIndicatorPayload } from '../../indicator/IndicatorGeneratorSelector';
import { runLocalBacktest, type LocalBacktestSummary } from '../localBacktestEngine';
import type {
  StrategyOptimizationCallbacks,
  StrategyOptimizationCandidate,
  StrategyOptimizationHandle,
  StrategyOptimizationObjective,
  StrategyOptimizationProgress,
  StrategyOptimizationRequest,
  StrategyOptimizationSummarySnapshot,
  StrategyOptimizationVariant,
  StrategyOptimizationParamRange,
} from './strategyTuningTypes';

export const TUNING_INPUT_SOURCE_OPTIONS = [
  'Close',
  'Open',
  'High',
  'Low',
  'Volume',
  'HL2',
  'HLC3',
  'OHLC4',
  'OC2',
  'HLCC4',
] as const;

const DEFAULT_PROGRESS_INTERVAL_MS = 500;
const DEFAULT_TICK_MS = 24;
const DEFAULT_MAX_CHUNK_WORK_MS = 18;
const DEFAULT_CHUNK_SIZE = 1;

const clampNumber = (value: number, min: number, max: number) => Math.min(max, Math.max(min, value));

const normalizeStep = (value: number) => {
  if (!Number.isFinite(value) || value <= 0) {
    return 1;
  }
  return value;
};

const countDecimalPlaces = (value: number) => {
  const text = String(value);
  const dotIndex = text.indexOf('.');
  if (dotIndex < 0) {
    return 0;
  }
  return text.length - dotIndex - 1;
};

const roundToStep = (value: number, step: number) => {
  const decimals = Math.min(6, countDecimalPlaces(step));
  return Number(value.toFixed(decimals));
};

const normalizeInputSource = (value: string) => {
  const matched = TUNING_INPUT_SOURCE_OPTIONS.find((item) => item.toLowerCase() === value.trim().toLowerCase());
  return matched || 'Close';
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

const buildOptimizationScoreBreakdown = (
  objective: StrategyOptimizationObjective,
  summary: StrategyOptimizationCandidate['summary'],
) => {
  const stats = summary.stats;
  const normalizedProfit = summary.totalProfit;
  const normalizedWinRate = summary.winRate * 100;
  const normalizedSharpe = stats.sharpeRatio * 100;
  const normalizedCalmar = stats.calmarRatio * 100;
  const normalizedFactor = stats.profitFactor * 40;
  const stabilityPenalty = summary.maxDrawdown * 40;

  const compositeBreakdown = [
    { label: '收益', value: normalizedProfit },
    { label: '胜率', value: normalizedWinRate },
    { label: '夏普', value: normalizedSharpe },
    { label: 'Calmar', value: normalizedCalmar },
    { label: 'ProfitFactor', value: normalizedFactor },
    { label: '回撤惩罚', value: -stabilityPenalty },
  ];

  if (objective === 'total_profit') {
    return [{ label: '收益', value: normalizedProfit }];
  }
  if (objective === 'win_rate') {
    return [{ label: '胜率', value: normalizedWinRate }];
  }
  if (objective === 'sharpe_ratio') {
    return [{ label: '夏普', value: normalizedSharpe }];
  }
  if (objective === 'calmar_ratio') {
    return [{ label: 'Calmar', value: normalizedCalmar }];
  }
  if (objective === 'profit_factor') {
    return [{ label: 'ProfitFactor', value: normalizedFactor }];
  }
  return compositeBreakdown;
};

export const scoreBacktestSummary = (
  objective: StrategyOptimizationObjective,
  summary: StrategyOptimizationCandidate['summary'],
) => {
  const breakdown = buildOptimizationScoreBreakdown(objective, summary);
  return {
    score: breakdown.reduce((sum, item) => sum + item.value, 0),
    breakdown,
  };
};

const buildVariantKey = (variant: StrategyOptimizationVariant) => (
  `${variant.input}|${variant.params.map((item) => roundToStep(item, 0.000001)).join(',')}`
);

const expandRangeValues = (range: StrategyOptimizationParamRange) => {
  const step = normalizeStep(range.step);
  const minValue = Math.min(range.min, range.max);
  const maxValue = Math.max(range.min, range.max);
  const values: number[] = [];
  let cursor = minValue;
  const limit = 512;
  while (cursor <= maxValue + step * 0.5 && values.length < limit) {
    values.push(roundToStep(cursor, step));
    cursor += step;
  }
  if (values.length === 0) {
    values.push(roundToStep(clampNumber(range.currentValue, minValue, maxValue), step));
  }
  return values;
};

export const estimateOptimizationVariantCount = (
  inputCandidates: string[],
  paramRanges: StrategyOptimizationParamRange[],
) => {
  const safeInputs = inputCandidates.length > 0 ? inputCandidates.length : 1;
  return paramRanges.reduce((product, range) => {
    if (!range.enabled) {
      return product;
    }
    return product * expandRangeValues(range).length;
  }, safeInputs);
};

export const buildOptimizationVariants = (
  inputCandidates: string[],
  paramRanges: StrategyOptimizationParamRange[],
) => {
  const safeInputs = inputCandidates.length > 0 ? inputCandidates : ['Close'];
  const enabledRanges = paramRanges.filter((item) => item.enabled);
  const baseParams = paramRanges.map((item) => item.currentValue);
  const variants: StrategyOptimizationVariant[] = [];

  const walk = (rangeIndex: number, currentParams: number[]) => {
    if (rangeIndex >= enabledRanges.length) {
      safeInputs.forEach((input) => {
        variants.push({
          input: normalizeInputSource(input),
          params: [...currentParams],
        });
      });
      return;
    }

    const range = enabledRanges[rangeIndex];
    const values = expandRangeValues(range);
    values.forEach((value) => {
      const nextParams = [...currentParams];
      nextParams[range.index] = value;
      walk(rangeIndex + 1, nextParams);
    });
  };

  walk(0, baseParams);
  return variants;
};

export const patchIndicatorWithVariant = (
  indicator: GeneratedIndicatorPayload,
  variant: StrategyOptimizationVariant,
) => {
  const currentConfig = (indicator.config || {}) as Record<string, unknown>;
  const rawInput = String(currentConfig.input || '').trim();
  const nextConfig = {
    ...currentConfig,
    input: rewriteIndicatorInputSource(rawInput, variant.input),
    params: [...variant.params],
  };
  return {
    ...indicator,
    config: nextConfig,
    configText: JSON.stringify(nextConfig),
  } satisfies GeneratedIndicatorPayload;
};

const insertCandidate = (
  list: StrategyOptimizationCandidate[],
  candidate: StrategyOptimizationCandidate,
  topN: number,
) => {
  list.push(candidate);
  list.sort((left, right) => {
    if (right.score !== left.score) {
      return right.score - left.score;
    }
    return right.summary.totalProfit - left.summary.totalProfit;
  });
  if (list.length > topN) {
    list.splice(topN, list.length - topN);
  }
};

const scheduleIdle = (
  callback: (deadline?: { didTimeout: boolean; timeRemaining: () => number }) => void,
  tickMs: number,
  scheduleMode: StrategyOptimizationRequest['scheduleMode'],
) => {
  const idleScheduler = globalThis as typeof globalThis & {
    requestIdleCallback?: (
      cb: (deadline: { didTimeout: boolean; timeRemaining: () => number }) => void,
      options?: { timeout?: number },
    ) => number;
    cancelIdleCallback?: (handle: number) => void;
  };

  if (scheduleMode === 'idle' && typeof idleScheduler.requestIdleCallback === 'function') {
    const handle = idleScheduler.requestIdleCallback(
      (deadline) => callback(deadline),
      { timeout: Math.max(32, tickMs) },
    );
    return {
      kind: 'idle' as const,
      handle,
      cancel: () => idleScheduler.cancelIdleCallback?.(handle),
    };
  }

  const handle = setTimeout(() => callback(), tickMs);
  return {
    kind: 'timeout' as const,
    handle,
    cancel: () => clearTimeout(handle),
  };
};

const createCandidateLabel = (candidate: StrategyOptimizationCandidate | null) => {
  if (!candidate) {
    return '等待结果';
  }
  return `${candidate.input} / ${candidate.params.join(', ')}`;
};

export const createOptimizationSummarySnapshot = (
  summary: LocalBacktestSummary,
): StrategyOptimizationSummarySnapshot => ({
  totalProfit: summary.totalProfit,
  winRate: summary.winRate,
  maxDrawdown: summary.maxDrawdown,
  tradeCount: summary.tradeCount,
  warningCount: summary.warningCount,
  stats: {
    sharpeRatio: summary.stats.sharpeRatio,
    calmarRatio: summary.stats.calmarRatio,
    profitFactor: summary.stats.profitFactor,
  },
});

export const runStrategyParameterOptimization = (
  request: StrategyOptimizationRequest,
  callbacks: StrategyOptimizationCallbacks = {},
): StrategyOptimizationHandle => {
  const targetIndicator = request.selectedIndicators.find((item) => item.id === request.targetIndicatorId);
  if (!targetIndicator) {
    callbacks.onError?.('目标指标不存在，无法执行参数调优');
    return { cancel: () => {} };
  }

  const variants = buildOptimizationVariants(request.inputCandidates, request.paramRanges);
  if (variants.length <= 0) {
    callbacks.onError?.('当前没有可扫描的参数组合');
    return { cancel: () => {} };
  }

  const topN = Math.max(1, Math.trunc(request.topN ?? 8));
  const chunkSize = Math.max(1, Math.trunc(request.chunkSize ?? DEFAULT_CHUNK_SIZE));
  const tickMs = Math.max(0, Math.trunc(request.tickMs ?? DEFAULT_TICK_MS));
  const maxChunkWorkMs = Math.max(8, Math.trunc(request.maxChunkWorkMs ?? DEFAULT_MAX_CHUNK_WORK_MS));
  const progressMinIntervalMs = Math.max(0, Math.trunc(request.progressMinIntervalMs ?? DEFAULT_PROGRESS_INTERVAL_MS));
  const startedAt = Date.now();
  const baselineSummary = createOptimizationSummarySnapshot(request.baseSummary);
  const winners: StrategyOptimizationCandidate[] = [];
  let bestCandidate: StrategyOptimizationCandidate | null = null;
  let processed = 0;
  let disposed = false;
  let lastEmitAt = 0;
  let scheduledTask: { cancel: () => void } | null = null;

  const emitProgress = (done: boolean, force = false) => {
    const now = Date.now();
    if (!done && !force && progressMinIntervalMs > 0 && now - lastEmitAt < progressMinIntervalMs) {
      return;
    }
    lastEmitAt = now;
    const payload: StrategyOptimizationProgress = {
      processed,
      total: variants.length,
      progress: variants.length > 0 ? processed / variants.length : 1,
      elapsedMs: now - startedAt,
      bestScore: bestCandidate?.score ?? Number.NEGATIVE_INFINITY,
      bestLabel: createCandidateLabel(bestCandidate),
      done,
      phase: done ? 'done' : 'running',
    };
    callbacks.onProgress?.(payload);
  };

  const scheduleNext = () => {
    if (disposed) {
      return;
    }
    scheduledTask = scheduleIdle(step, tickMs, request.scheduleMode);
  };

  const step = (deadline?: { didTimeout: boolean; timeRemaining: () => number }) => {
    if (disposed) {
      return;
    }
    const chunkStartedAt = Date.now();
    let chunkProcessed = 0;

    while (processed < variants.length && chunkProcessed < chunkSize) {
      if (chunkProcessed > 0) {
        if (deadline) {
          if (!deadline.didTimeout && deadline.timeRemaining() <= 1) {
            break;
          }
        } else if (Date.now() - chunkStartedAt >= maxChunkWorkMs) {
          break;
        }
      }

      const variant = variants[processed];
      const patchedIndicators = request.selectedIndicators.map((indicator) => (
        indicator.id === targetIndicator.id ? patchIndicatorWithVariant(indicator, variant) : indicator
      ));
      const summary = runLocalBacktest({
        bars: request.bars,
        selectedIndicators: patchedIndicators,
        indicatorOutputGroups: request.indicatorOutputGroups,
        logicContainers: request.logicContainers,
        filterContainers: request.filterContainers,
        methodOptions: request.methodOptions,
        takeProfitPct: request.takeProfitPct,
        stopLossPct: request.stopLossPct,
        leverage: request.leverage,
        orderQty: request.orderQty,
        initialCapital: request.initialCapital,
        feeRate: request.feeRate,
        fundingRate: request.fundingRate,
        slippageBps: request.slippageBps,
        autoReverse: request.autoReverse,
        useStrategyRuntime: request.useStrategyRuntime,
        executionMode: request.executionMode,
      });
      const scored = scoreBacktestSummary(request.objective, summary);
      const compactSummary = createOptimizationSummarySnapshot(summary);
      const candidate: StrategyOptimizationCandidate = {
        key: buildVariantKey(variant),
        rank: 0,
        score: scored.score,
        input: variant.input,
        params: [...variant.params],
        summary: compactSummary,
        scoreBreakdown: scored.breakdown,
      };
      insertCandidate(winners, candidate, topN);
      winners.forEach((item, index) => {
        item.rank = index + 1;
      });
      if (!bestCandidate || candidate.score > bestCandidate.score) {
        bestCandidate = candidate;
      }

      processed += 1;
      chunkProcessed += 1;
    }

    const done = processed >= variants.length;
    emitProgress(done, done);
    if (done) {
      callbacks.onComplete?.({
        targetIndicatorId: request.targetIndicatorId,
        targetIndicatorName: request.targetIndicatorName,
        objective: request.objective,
        processed,
        total: variants.length,
        elapsedMs: Date.now() - startedAt,
        bestCandidate,
        candidates: winners.map((item, index) => ({
          ...item,
          rank: index + 1,
        })),
        baselineSummary,
      });
      return;
    }

    scheduleNext();
  };

  emitProgress(false, true);
  scheduleNext();

  return {
    cancel: () => {
      disposed = true;
      scheduledTask?.cancel();
      scheduledTask = null;
    },
  };
};
