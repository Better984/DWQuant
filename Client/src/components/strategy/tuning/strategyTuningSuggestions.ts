import type {
  StrategyOptimizationResult,
  StrategyRobustnessInsight,
  StrategySignalAnalysis,
  StrategyTimeAnalysis,
  StrategyTuningSuggestion,
} from './strategyTuningTypes';

const formatPercent = (value: number) => `${value.toFixed(1)}%`;

export const buildStrategyRobustnessInsight = ({
  tradeCount,
  signalAnalysis,
  timeAnalysis,
  optimizationResult,
}: {
  tradeCount: number;
  signalAnalysis: StrategySignalAnalysis;
  timeAnalysis: StrategyTimeAnalysis;
  optimizationResult: StrategyOptimizationResult | null;
}): StrategyRobustnessInsight => {
  const topTimeBucket = timeAnalysis.groups
    .flatMap((group) => group.buckets)
    .sort((left, right) => right.count - left.count)[0] || null;
  const dominantRatio = topTimeBucket && tradeCount > 0 ? topTimeBucket.count / tradeCount : 0;
  const positiveRules = signalAnalysis.ruleInsights.filter((item) => item.mood === 'positive').length;
  const riskRules = signalAnalysis.ruleInsights.filter((item) => item.mood === 'risk').length;
  const improvementRatio = optimizationResult?.bestCandidate && Math.abs(optimizationResult.baselineSummary.totalProfit) > 1e-9
    ? (optimizationResult.bestCandidate.summary.totalProfit - optimizationResult.baselineSummary.totalProfit)
      / Math.abs(optimizationResult.baselineSummary.totalProfit)
    : 0;

  let overfitRisk: StrategyRobustnessInsight['overfitRisk'] = 'low';
  if (tradeCount < 20 || (tradeCount < 35 && improvementRatio > 0.25)) {
    overfitRisk = 'high';
  } else if (tradeCount < 45 || improvementRatio > 0.15) {
    overfitRisk = 'medium';
  }

  const sampleAdequacy =
    tradeCount < 20
      ? `当前仅 ${tradeCount} 笔已平仓样本，调优结果很容易被偶然波动放大。`
      : tradeCount < 45
        ? `当前共有 ${tradeCount} 笔已平仓样本，建议把调优结果当作候选过滤器，而不是直接上线。`
        : `当前共有 ${tradeCount} 笔已平仓样本，已经可以支持一轮初步调优，但仍建议做滚动验证。`;

  const distributionRisk =
    dominantRatio >= 0.5 && topTimeBucket
      ? `样本过度集中在“${topTimeBucket.label}”，占比 ${formatPercent(dominantRatio * 100)}，需警惕时间分布偏差。`
      : '时间分布相对分散，没有出现单一时段独占大部分样本的情况。';

  const summary =
    `当前共有 ${positiveRules} 条正向线索、${riskRules} 条风险线索。` +
    (optimizationResult?.bestCandidate
      ? ` 参数扫描最优解相对基线的收益变化约 ${formatPercent(improvementRatio * 100)}。`
      : ' 暂未执行参数扫描。');

  return {
    overfitRisk,
    sampleAdequacy,
    distributionRisk,
    summary,
  };
};

export const buildStrategyTuningSuggestions = ({
  optimizationResult,
  signalAnalysis,
  timeAnalysis,
  tradeCount,
}: {
  optimizationResult: StrategyOptimizationResult | null;
  signalAnalysis: StrategySignalAnalysis;
  timeAnalysis: StrategyTimeAnalysis;
  tradeCount: number;
}): StrategyTuningSuggestion[] => {
  const suggestions: StrategyTuningSuggestion[] = [];

  if (optimizationResult?.bestCandidate) {
    const baseline = optimizationResult.baselineSummary.totalProfit;
    const optimized = optimizationResult.bestCandidate.summary.totalProfit;
    const delta = optimized - baseline;
    suggestions.push({
      id: 'optimizer-best',
      title: '参数扫描最优组合',
      category: '参数',
      level: delta >= 0 ? 'positive' : 'warning',
      description:
        `${optimizationResult.targetIndicatorName} 建议切到输入 ${optimizationResult.bestCandidate.input}，` +
        `参数 ${optimizationResult.bestCandidate.params.join(', ')}。` +
        `累计收益从 ${baseline.toFixed(2)} 变化到 ${optimized.toFixed(2)}。`,
    });
  }

  const positiveSignal = signalAnalysis.ruleInsights.find((item) => item.mood === 'positive');
  if (positiveSignal) {
    suggestions.push({
      id: `signal-positive:${positiveSignal.id}`,
      title: positiveSignal.title,
      category: '指标',
      level: 'positive',
      description: positiveSignal.summary,
    });
  }

  const riskSignal = signalAnalysis.ruleInsights.find((item) => item.mood === 'risk');
  if (riskSignal) {
    suggestions.push({
      id: `signal-risk:${riskSignal.id}`,
      title: `规避 ${riskSignal.title}`,
      category: '指标',
      level: 'warning',
      description: riskSignal.summary,
    });
  }

  const riskTimeGroup = timeAnalysis.groups.find((group) => group.riskBucket && group.bestBucket);
  if (riskTimeGroup?.riskBucket) {
    suggestions.push({
      id: `time-risk:${riskTimeGroup.id}`,
      title: `规避 ${riskTimeGroup.title}`,
      category: '时段',
      level: 'warning',
      description:
        `${riskTimeGroup.title} 中，${riskTimeGroup.riskBucket.label} 的亏损概率最高 ` +
        `(${formatPercent(riskTimeGroup.riskBucket.lossRate * 100)})，建议先作为过滤条件候选。`,
    });
  }

  const robustness = buildStrategyRobustnessInsight({
    tradeCount,
    signalAnalysis,
    timeAnalysis,
    optimizationResult,
  });
  suggestions.push({
    id: 'robustness-summary',
    title: '样本稳健性',
    category: '稳健性',
    level: robustness.overfitRisk === 'high' ? 'warning' : 'positive',
    description: `${robustness.sampleAdequacy} ${robustness.distributionRisk}`,
  });

  return suggestions.slice(0, 6);
};
