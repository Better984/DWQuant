import type { KLineData } from 'klinecharts';

import type { GeneratedIndicatorPayload } from '../../indicator/IndicatorGeneratorSelector';
import {
  buildLocalIndicatorSeriesMap,
  type LocalBacktestTrade,
} from '../localBacktestEngine';
import {
  getTalibIndicatorMetaList,
  getTalibRuntimeCalcSpec,
} from '../../../lib/registerTalibIndicators';
import type {
  StrategySignalAnalysis,
  StrategySignalFeatureOption,
  StrategySignalInsight,
  StrategySignalInsightBucket,
  StrategySignalRuleInsight,
} from './strategyTuningTypes';

type FeatureEntry = {
  id: string;
  label: string;
  description: string;
  enabledByDefault: boolean;
  series: Array<number | undefined>;
};

type TradePoint = {
  trade: LocalBacktestTrade;
  entryIndex: number;
  returnPct: number;
};

type BucketMetric = {
  key: string;
  label: string;
  min: number;
  max: number;
  count: number;
  winCount: number;
  lossCount: number;
  pnlSum: number;
  returnPctSum: number;
};

const CLASSIC_BASE_ID = '__tuning_classic__';

const normalizeLookupKey = (value: string) => value.trim().toLowerCase().replace(/[^a-z0-9]/g, '');

const toPercent = (value: number, base: number) => {
  if (!Number.isFinite(value) || !Number.isFinite(base) || Math.abs(base) <= 1e-9) {
    return 0;
  }
  return (value / base) * 100;
};

const formatPercent = (value: number) => `${value.toFixed(1)}%`;

const formatNumber = (value: number) => {
  if (!Number.isFinite(value)) {
    return '0';
  }
  if (Math.abs(value) >= 100) {
    return value.toFixed(1);
  }
  if (Math.abs(value) >= 1) {
    return value.toFixed(2);
  }
  return value.toFixed(4);
};

const findBarIndexAtOrBefore = (bars: KLineData[], timestamp: number) => {
  let left = 0;
  let right = bars.length - 1;
  let answer = -1;
  while (left <= right) {
    const middle = Math.floor((left + right) / 2);
    const current = Number(bars[middle]?.timestamp || 0);
    if (current <= timestamp) {
      answer = middle;
      left = middle + 1;
    } else {
      right = middle - 1;
    }
  }
  return answer;
};

const resolveMetaByCode = (code: string) => {
  const normalized = normalizeLookupKey(code);
  return getTalibIndicatorMetaList().find((item) => normalizeLookupKey(item.code) === normalized) || null;
};

const buildClassicIndicator = (
  id: string,
  code: string,
  name: string,
  params: number[],
  input = 'Close',
) => {
  const meta = resolveMetaByCode(code);
  const spec = meta ? getTalibRuntimeCalcSpec(meta.name) : null;
  return {
    indicator: {
      id,
      code,
      name,
      category: '调优分析',
      outputs: (spec?.outputs || [{ key: 'real' }]).map((output) => ({ key: output.key })),
      config: {
        indicator: code,
        input,
        params,
      },
      configText: JSON.stringify({
        indicator: code,
        input,
        params,
      }),
    } satisfies GeneratedIndicatorPayload,
    outputKeys: (spec?.outputs || [{ key: 'real' }]).map((item) => item.key),
  };
};

const buildFeatureOptions = (
  strategyIndicators: GeneratedIndicatorPayload[],
  analysisIndicators: GeneratedIndicatorPayload[],
  formatIndicatorName: (indicator: GeneratedIndicatorPayload) => string,
): StrategySignalFeatureOption[] => {
  const options: StrategySignalFeatureOption[] = [
    {
      id: 'classic:rsi',
      label: 'RSI(14)',
      description: '超买超卖区间与胜率的对应关系',
      enabledByDefault: true,
    },
    {
      id: 'classic:adx',
      label: 'ADX(14)',
      description: '趋势强度对盈亏分布的影响',
      enabledByDefault: true,
    },
    {
      id: 'classic:macd_line',
      label: 'MACD 主线',
      description: 'MACD 主线在开仓时的区间表现',
      enabledByDefault: true,
    },
    {
      id: 'classic:macd_signal',
      label: 'MACD 信号线',
      description: 'MACD 信号线在开仓时的区间表现',
      enabledByDefault: false,
    },
    {
      id: 'classic:macd_hist',
      label: 'MACD 柱体',
      description: '动量柱体与开仓胜率的关系',
      enabledByDefault: false,
    },
  ];

  strategyIndicators.forEach((indicator) => {
    const outputs = indicator.outputs.length > 0 ? indicator.outputs : [{ key: 'Value' }];
    outputs.forEach((output, index) => {
      options.push({
        id: `custom:${indicator.id}:${output.key}`,
        label: `${formatIndicatorName(indicator)} · ${output.key}`,
        description: index === 0 ? '来自当前策略的自定义指标输出' : '来自当前策略的附加输出',
        enabledByDefault: false,
      });
    });
  });

  analysisIndicators.forEach((indicator) => {
    const outputs = indicator.outputs.length > 0 ? indicator.outputs : [{ key: 'Value' }];
    outputs.forEach((output, index) => {
      options.push({
        id: `analysis:${indicator.id}:${output.key}`,
        label: `${formatIndicatorName(indicator)} · ${output.key}`,
        description: index === 0 ? '来自调优模块新增的分析指标输出' : '来自调优模块新增的附加输出',
        enabledByDefault: false,
      });
    });
  });

  return options;
};

const buildFeatureEntries = (
  bars: KLineData[],
  strategyIndicators: GeneratedIndicatorPayload[],
  analysisIndicators: GeneratedIndicatorPayload[],
  formatIndicatorName: (indicator: GeneratedIndicatorPayload) => string,
) => {
  const options = buildFeatureOptions(strategyIndicators, analysisIndicators, formatIndicatorName);
  const features: FeatureEntry[] = [];
  const warnings: string[] = [];

  const customResult = buildLocalIndicatorSeriesMap(bars, strategyIndicators);
  warnings.push(...customResult.warnings);
  const analysisResult = buildLocalIndicatorSeriesMap(bars, analysisIndicators);
  warnings.push(...analysisResult.warnings);

  const classicRsi = buildClassicIndicator(`${CLASSIC_BASE_ID}:rsi`, 'RSI', 'RSI(14)', [14]);
  const classicAdx = buildClassicIndicator(`${CLASSIC_BASE_ID}:adx`, 'ADX', 'ADX(14)', [14]);
  const classicMacd = buildClassicIndicator(`${CLASSIC_BASE_ID}:macd`, 'MACD', 'MACD(12,26,9)', [12, 26, 9]);
  const classicResult = buildLocalIndicatorSeriesMap(bars, [
    classicRsi.indicator,
    classicAdx.indicator,
    classicMacd.indicator,
  ]);
  warnings.push(...classicResult.warnings);

  const pushSeries = (
    id: string,
    label: string,
    description: string,
    enabledByDefault: boolean,
    series: Array<number | undefined> | undefined,
  ) => {
    if (!series || series.length <= 0) {
      return;
    }
    features.push({
      id,
      label,
      description,
      enabledByDefault,
      series,
    });
  };

  const getOption = (id: string) => options.find((item) => item.id === id);
  const rsiKey = `${classicRsi.indicator.id}:${classicRsi.outputKeys[0] || 'real'}`;
  const adxKey = `${classicAdx.indicator.id}:${classicAdx.outputKeys[0] || 'real'}`;
  const macdKeys = classicMacd.outputKeys;
  const macdLineKey = macdKeys[0] ? `${classicMacd.indicator.id}:${macdKeys[0]}` : '';
  const macdSignalKey = macdKeys[1] ? `${classicMacd.indicator.id}:${macdKeys[1]}` : '';
  const macdHistKey = macdKeys[2] ? `${classicMacd.indicator.id}:${macdKeys[2]}` : '';

  const rsiOption = getOption('classic:rsi');
  const adxOption = getOption('classic:adx');
  const macdLineOption = getOption('classic:macd_line');
  const macdSignalOption = getOption('classic:macd_signal');
  const macdHistOption = getOption('classic:macd_hist');

  pushSeries(
    'classic:rsi',
    rsiOption?.label || 'RSI(14)',
    rsiOption?.description || '超买超卖区间与胜率的对应关系',
    Boolean(rsiOption?.enabledByDefault),
    classicResult.seriesByValueId.get(rsiKey),
  );
  pushSeries(
    'classic:adx',
    adxOption?.label || 'ADX(14)',
    adxOption?.description || '趋势强度对盈亏分布的影响',
    Boolean(adxOption?.enabledByDefault),
    classicResult.seriesByValueId.get(adxKey),
  );
  pushSeries(
    'classic:macd_line',
    macdLineOption?.label || 'MACD 主线',
    macdLineOption?.description || 'MACD 主线在开仓时的区间表现',
    Boolean(macdLineOption?.enabledByDefault),
    classicResult.seriesByValueId.get(macdLineKey),
  );
  pushSeries(
    'classic:macd_signal',
    macdSignalOption?.label || 'MACD 信号线',
    macdSignalOption?.description || 'MACD 信号线在开仓时的区间表现',
    Boolean(macdSignalOption?.enabledByDefault),
    classicResult.seriesByValueId.get(macdSignalKey),
  );
  pushSeries(
    'classic:macd_hist',
    macdHistOption?.label || 'MACD 柱体',
    macdHistOption?.description || '动量柱体与开仓胜率的关系',
    Boolean(macdHistOption?.enabledByDefault),
    classicResult.seriesByValueId.get(macdHistKey),
  );

  strategyIndicators.forEach((indicator) => {
    const outputs = indicator.outputs.length > 0 ? indicator.outputs : [{ key: 'Value' }];
    outputs.forEach((output) => {
      const optionId = `custom:${indicator.id}:${output.key}`;
      const option = getOption(optionId);
      const valueId = `${indicator.id}:${output.key}`;
      pushSeries(
        optionId,
        option?.label || `${formatIndicatorName(indicator)} · ${output.key}`,
        option?.description || '来自当前策略的自定义指标输出',
        Boolean(option?.enabledByDefault),
        customResult.seriesByValueId.get(valueId),
      );
    });
  });

  analysisIndicators.forEach((indicator) => {
    const outputs = indicator.outputs.length > 0 ? indicator.outputs : [{ key: 'Value' }];
    outputs.forEach((output) => {
      const optionId = `analysis:${indicator.id}:${output.key}`;
      const option = getOption(optionId);
      const valueId = `${indicator.id}:${output.key}`;
      pushSeries(
        optionId,
        option?.label || `${formatIndicatorName(indicator)} · ${output.key}`,
        option?.description || '来自调优模块新增的分析指标输出',
        Boolean(option?.enabledByDefault),
        analysisResult.seriesByValueId.get(valueId),
      );
    });
  });

  return {
    features,
    featureOptions: options,
    warnings: warnings.filter((item, index, array) => array.indexOf(item) === index),
    macdPair: {
      line: classicResult.seriesByValueId.get(macdLineKey) || [],
      signal: classicResult.seriesByValueId.get(macdSignalKey) || [],
    },
    rsiSeries: classicResult.seriesByValueId.get(rsiKey) || [],
  };
};

const buildEqualBuckets = (values: number[], count = 6) => {
  const min = Math.min(...values);
  const max = Math.max(...values);
  if (!Number.isFinite(min) || !Number.isFinite(max)) {
    return [];
  }
  if (Math.abs(max - min) <= 1e-9) {
    return [
      { min, max, label: formatNumber(min) },
    ];
  }
  const step = (max - min) / count;
  return new Array(count).fill(null).map((_, index) => {
    const bucketMin = min + step * index;
    const bucketMax = index === count - 1 ? max : min + step * (index + 1);
    return {
      min: bucketMin,
      max: bucketMax,
      label: `${formatNumber(bucketMin)} ~ ${formatNumber(bucketMax)}`,
    };
  });
};

const buildRsiBuckets = () => ([
  { min: 0, max: 10, label: '0 ~ 10' },
  { min: 10, max: 30, label: '10 ~ 30' },
  { min: 30, max: 50, label: '30 ~ 50' },
  { min: 50, max: 70, label: '50 ~ 70' },
  { min: 70, max: 90, label: '70 ~ 90' },
  { min: 90, max: 100, label: '90 ~ 100' },
]);

const buildAdxBuckets = () => ([
  { min: 0, max: 20, label: '0 ~ 20' },
  { min: 20, max: 30, label: '20 ~ 30' },
  { min: 30, max: 45, label: '30 ~ 45' },
  { min: 45, max: 60, label: '45 ~ 60' },
  { min: 60, max: 100, label: '60 ~ 100' },
]);

const selectBucketsForFeature = (featureId: string, values: number[]) => {
  if (featureId === 'classic:rsi') {
    return buildRsiBuckets();
  }
  if (featureId === 'classic:adx') {
    return buildAdxBuckets();
  }
  return buildEqualBuckets(values, 6);
};

const finalizeBucket = (bucket: BucketMetric): StrategySignalInsightBucket => {
  const count = bucket.count;
  const winRate = count > 0 ? bucket.winCount / count : 0;
  const lossRate = count > 0 ? bucket.lossCount / count : 0;
  return {
    key: bucket.key,
    label: bucket.label,
    min: bucket.min,
    max: bucket.max,
    count,
    winCount: bucket.winCount,
    lossCount: bucket.lossCount,
    winRate,
    lossRate,
    avgPnl: count > 0 ? bucket.pnlSum / count : 0,
    avgReturnPct: count > 0 ? bucket.returnPctSum / count : 0,
  };
};

const buildFeatureInsight = (
  feature: FeatureEntry,
  tradePoints: TradePoint[],
  side: 'Long' | 'Short',
): StrategySignalInsight | null => {
  const sideTrades = tradePoints.filter((item) => item.trade.side === side);
  if (sideTrades.length <= 0) {
    return null;
  }

  const values = sideTrades
    .map((item) => feature.series[item.entryIndex])
    .filter((value): value is number => typeof value === 'number' && Number.isFinite(value));
  if (values.length <= 0) {
    return null;
  }

  const buckets = selectBucketsForFeature(feature.id, values);
  if (buckets.length <= 0) {
    return null;
  }

  const metrics = buckets.map((bucket, index) => ({
    key: `${feature.id}:${side}:${index}`,
    label: bucket.label,
    min: bucket.min,
    max: bucket.max,
    count: 0,
    winCount: 0,
    lossCount: 0,
    pnlSum: 0,
    returnPctSum: 0,
  } satisfies BucketMetric));

  sideTrades.forEach((item) => {
    const value = feature.series[item.entryIndex];
    if (typeof value !== 'number' || !Number.isFinite(value)) {
      return;
    }
    const bucket = metrics.find((candidate) => (
      value >= candidate.min && (value < candidate.max || candidate === metrics[metrics.length - 1])
    ));
    if (!bucket) {
      return;
    }
    bucket.count += 1;
    if (item.trade.pnl >= 0) {
      bucket.winCount += 1;
    } else {
      bucket.lossCount += 1;
    }
    bucket.pnlSum += item.trade.pnl;
    bucket.returnPctSum += item.returnPct;
  });

  const finalized = metrics
    .filter((item) => item.count > 0)
    .map((item) => finalizeBucket(item));
  if (finalized.length <= 0) {
    return null;
  }

  const minSamples = Math.max(3, Math.floor(sideTrades.length / 8));
  const candidates = finalized.filter((item) => item.count >= minSamples);
  const scoped = candidates.length > 0 ? candidates : finalized;
  const bestBucket = [...scoped].sort((left, right) => {
    if (right.winRate !== left.winRate) {
      return right.winRate - left.winRate;
    }
    return right.avgPnl - left.avgPnl;
  })[0] || null;
  const riskBucket = [...scoped].sort((left, right) => {
    if (right.lossRate !== left.lossRate) {
      return right.lossRate - left.lossRate;
    }
    return left.avgPnl - right.avgPnl;
  })[0] || null;

  if (!bestBucket || !riskBucket) {
    return null;
  }

  return {
    id: `${feature.id}:${side}`,
    label: feature.label,
    side,
    totalSamples: sideTrades.length,
    bestBucket,
    riskBucket,
    buckets: finalized,
    summary:
      `做${side === 'Long' ? '多' : '空'}时，${feature.label} 落在 ${bestBucket.label} 的胜率最高 ` +
      `(${formatPercent(bestBucket.winRate * 100)}，${bestBucket.count} 笔)，` +
      `而 ${riskBucket.label} 的亏损概率最高 (${formatPercent(riskBucket.lossRate * 100)})。`,
  };
};

const buildMacdRuleInsights = (
  tradePoints: TradePoint[],
  macdLine: Array<number | undefined>,
  macdSignal: Array<number | undefined>,
) => {
  const states = [
    {
      key: 'below_zero',
      label: '快线与慢线都在零轴下方',
      match: (line: number, signal: number) => line < 0 && signal < 0,
    },
    {
      key: 'above_zero',
      label: '快线与慢线都在零轴上方',
      match: (line: number, signal: number) => line > 0 && signal > 0,
    },
    {
      key: 'line_above_signal',
      label: '快线位于慢线之上',
      match: (line: number, signal: number) => line > signal,
    },
    {
      key: 'line_below_signal',
      label: '快线位于慢线之下',
      match: (line: number, signal: number) => line < signal,
    },
  ];

  const rules: StrategySignalRuleInsight[] = [];
  (['Long', 'Short'] as const).forEach((side) => {
    const sideTrades = tradePoints.filter((item) => item.trade.side === side);
    const minSamples = Math.max(3, Math.floor(sideTrades.length / 10));
    states.forEach((state) => {
      const matched = sideTrades.filter((item) => {
        const line = macdLine[item.entryIndex];
        const signal = macdSignal[item.entryIndex];
        return typeof line === 'number'
          && Number.isFinite(line)
          && typeof signal === 'number'
          && Number.isFinite(signal)
          && state.match(line, signal);
      });
      if (matched.length < minSamples) {
        return;
      }
      const winCount = matched.filter((item) => item.trade.pnl >= 0).length;
      const avgPnl = matched.reduce((sum, item) => sum + item.trade.pnl, 0) / matched.length;
      const winRate = matched.length > 0 ? winCount / matched.length : 0;
      rules.push({
        id: `macd:${side}:${state.key}`,
        title: `MACD ${state.label}`,
        side,
        sampleCount: matched.length,
        winRate,
        avgPnl,
        mood: winRate >= 0.55 ? 'positive' : 'risk',
        summary:
          `做${side === 'Long' ? '多' : '空'}时，若 ${state.label}，样本 ${matched.length} 笔，` +
          `胜率 ${formatPercent(winRate * 100)}，平均盈亏 ${formatNumber(avgPnl)}。`,
      });
    });
  });

  return rules
    .sort((left, right) => {
      const leftScore = (left.mood === 'positive' ? left.winRate : 1 - left.winRate) * left.sampleCount;
      const rightScore = (right.mood === 'positive' ? right.winRate : 1 - right.winRate) * right.sampleCount;
      return rightScore - leftScore;
    })
    .slice(0, 6);
};

const buildRsiRuleInsights = (
  tradePoints: TradePoint[],
  rsiSeries: Array<number | undefined>,
) => {
  const zones = [
    { key: 'lt10', label: 'RSI < 10', match: (value: number) => value < 10 },
    { key: 'lt30', label: 'RSI 10 ~ 30', match: (value: number) => value >= 10 && value < 30 },
    { key: 'mid', label: 'RSI 30 ~ 70', match: (value: number) => value >= 30 && value < 70 },
    { key: 'gt70', label: 'RSI 70 ~ 90', match: (value: number) => value >= 70 && value < 90 },
    { key: 'gt90', label: 'RSI > 90', match: (value: number) => value >= 90 },
  ];

  const rules: StrategySignalRuleInsight[] = [];
  (['Long', 'Short'] as const).forEach((side) => {
    const sideTrades = tradePoints.filter((item) => item.trade.side === side);
    const minSamples = Math.max(3, Math.floor(sideTrades.length / 10));
    zones.forEach((zone) => {
      const matched = sideTrades.filter((item) => {
        const value = rsiSeries[item.entryIndex];
        return typeof value === 'number' && Number.isFinite(value) && zone.match(value);
      });
      if (matched.length < minSamples) {
        return;
      }
      const winCount = matched.filter((item) => item.trade.pnl >= 0).length;
      const avgPnl = matched.reduce((sum, item) => sum + item.trade.pnl, 0) / matched.length;
      const winRate = matched.length > 0 ? winCount / matched.length : 0;
      rules.push({
        id: `rsi:${side}:${zone.key}`,
        title: zone.label,
        side,
        sampleCount: matched.length,
        winRate,
        avgPnl,
        mood: winRate >= 0.55 ? 'positive' : 'risk',
        summary:
          `做${side === 'Long' ? '多' : '空'}时，${zone.label} 的样本 ${matched.length} 笔，` +
          `胜率 ${formatPercent(winRate * 100)}，平均盈亏 ${formatNumber(avgPnl)}。`,
      });
    });
  });

  return rules
    .sort((left, right) => {
      const leftScore = (left.mood === 'positive' ? left.winRate : 1 - left.winRate) * left.sampleCount;
      const rightScore = (right.mood === 'positive' ? right.winRate : 1 - right.winRate) * right.sampleCount;
      return rightScore - leftScore;
    })
    .slice(0, 6);
};

export const buildStrategySignalAnalysis = ({
  bars,
  selectedIndicators,
  analysisIndicators,
  trades,
  enabledFeatureIds,
  talibReady,
  formatIndicatorName,
}: {
  bars: KLineData[];
  selectedIndicators: GeneratedIndicatorPayload[];
  analysisIndicators?: GeneratedIndicatorPayload[];
  trades: LocalBacktestTrade[];
  enabledFeatureIds: string[];
  talibReady: boolean;
  formatIndicatorName: (indicator: GeneratedIndicatorPayload) => string;
}): StrategySignalAnalysis => {
  const extraIndicators = analysisIndicators || [];
  const featureOptions = buildFeatureOptions(selectedIndicators, extraIndicators, formatIndicatorName);
  if (!talibReady) {
    return {
      featureOptions,
      insights: [],
      ruleInsights: [],
      warnings: ['指标内核尚未完成初始化，暂时无法生成指标共振分析。'],
    };
  }
  if (bars.length <= 0) {
    return {
      featureOptions,
      insights: [],
      ruleInsights: [],
      warnings: ['当前没有可用K线，无法生成指标共振分析。'],
    };
  }

  const closedTrades = trades.filter((item) => !item.isOpen && item.exitTime > item.entryTime);
  if (closedTrades.length <= 0) {
    return {
      featureOptions,
      insights: [],
      ruleInsights: [],
      warnings: ['至少需要一批已平仓样本后，才能识别指标区间与胜率分布。'],
    };
  }

  const tradePoints: TradePoint[] = closedTrades
    .map((trade) => {
      const entryIndex = findBarIndexAtOrBefore(bars, Number(trade.entryTime) || 0);
      if (entryIndex < 0) {
        return null;
      }
      return {
        trade,
        entryIndex,
        returnPct: toPercent(trade.pnl, trade.entryPrice * trade.qty),
      } satisfies TradePoint;
    })
    .filter((item): item is TradePoint => item !== null);

  const resolved = buildFeatureEntries(bars, selectedIndicators, extraIndicators, formatIndicatorName);
  const enabled = new Set(enabledFeatureIds.length > 0 ? enabledFeatureIds : featureOptions
    .filter((item) => item.enabledByDefault)
    .map((item) => item.id));
  const features = resolved.features.filter((item) => enabled.has(item.id));
  const insights: StrategySignalInsight[] = [];

  features.forEach((feature) => {
    const longInsight = buildFeatureInsight(feature, tradePoints, 'Long');
    const shortInsight = buildFeatureInsight(feature, tradePoints, 'Short');
    if (longInsight) {
      insights.push(longInsight);
    }
    if (shortInsight) {
      insights.push(shortInsight);
    }
  });

  const ruleInsights = [
    ...buildMacdRuleInsights(tradePoints, resolved.macdPair.line, resolved.macdPair.signal),
    ...buildRsiRuleInsights(tradePoints, resolved.rsiSeries),
  ]
    .sort((left, right) => right.sampleCount - left.sampleCount)
    .slice(0, 8);

  return {
    featureOptions,
    insights: insights.sort((left, right) => right.totalSamples - left.totalSamples),
    ruleInsights,
    warnings: resolved.warnings,
  };
};
