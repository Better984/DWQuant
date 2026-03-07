import type { KLineData } from 'klinecharts';

import type { GeneratedIndicatorPayload } from '../../indicator/IndicatorGeneratorSelector';
import type {
  ConditionContainer,
  IndicatorOutputGroup,
  MethodOption,
} from '../StrategyModule.types';
import type {
  LocalBacktestStats,
  LocalBacktestSummary,
  LocalBacktestTrade,
} from '../localBacktestEngine';

export type StrategyOptimizationObjective =
  | 'composite'
  | 'total_profit'
  | 'win_rate'
  | 'sharpe_ratio'
  | 'calmar_ratio'
  | 'profit_factor';

export type StrategyOptimizationScheduleMode = 'timeout' | 'idle';

export type StrategyOptimizationParamRange = {
  index: number;
  label: string;
  currentValue: number;
  min: number;
  max: number;
  step: number;
  enabled: boolean;
};

export type StrategyOptimizationVariant = {
  input: string;
  params: number[];
};

export type StrategyOptimizationSummarySnapshot = {
  totalProfit: number;
  winRate: number;
  maxDrawdown: number;
  tradeCount: number;
  warningCount: number;
  stats: Pick<LocalBacktestStats, 'sharpeRatio' | 'calmarRatio' | 'profitFactor'>;
};

export type StrategyOptimizationCandidate = {
  key: string;
  rank: number;
  score: number;
  input: string;
  params: number[];
  summary: StrategyOptimizationSummarySnapshot;
  scoreBreakdown: Array<{ label: string; value: number }>;
};

export type StrategyOptimizationProgress = {
  processed: number;
  total: number;
  progress: number;
  elapsedMs: number;
  bestScore: number;
  bestLabel: string;
  done: boolean;
  phase: string;
};

export type StrategyOptimizationResult = {
  targetIndicatorId: string;
  targetIndicatorName: string;
  objective: StrategyOptimizationObjective;
  processed: number;
  total: number;
  elapsedMs: number;
  bestCandidate: StrategyOptimizationCandidate | null;
  candidates: StrategyOptimizationCandidate[];
  baselineSummary: StrategyOptimizationSummarySnapshot;
};

export type StrategyOptimizationRequest = {
  bars: KLineData[];
  selectedIndicators: GeneratedIndicatorPayload[];
  indicatorOutputGroups: IndicatorOutputGroup[];
  logicContainers: ConditionContainer[];
  filterContainers: ConditionContainer[];
  methodOptions: MethodOption[];
  targetIndicatorId: string;
  targetIndicatorName: string;
  baseSummary: LocalBacktestSummary;
  objective: StrategyOptimizationObjective;
  inputCandidates: string[];
  paramRanges: StrategyOptimizationParamRange[];
  topN?: number;
  chunkSize?: number;
  tickMs?: number;
  progressMinIntervalMs?: number;
  maxChunkWorkMs?: number;
  scheduleMode?: StrategyOptimizationScheduleMode;
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
};

export type StrategyOptimizationCallbacks = {
  onProgress?: (progress: StrategyOptimizationProgress) => void;
  onComplete?: (result: StrategyOptimizationResult) => void;
  onError?: (message: string) => void;
};

export type StrategyOptimizationHandle = {
  cancel: () => void;
};

export type StrategySignalFeatureOption = {
  id: string;
  label: string;
  description: string;
  enabledByDefault: boolean;
};

export type StrategySignalInsightBucket = {
  key: string;
  label: string;
  min: number;
  max: number;
  count: number;
  winCount: number;
  lossCount: number;
  winRate: number;
  lossRate: number;
  avgPnl: number;
  avgReturnPct: number;
};

export type StrategySignalInsight = {
  id: string;
  label: string;
  side: 'Long' | 'Short';
  totalSamples: number;
  bestBucket: StrategySignalInsightBucket | null;
  riskBucket: StrategySignalInsightBucket | null;
  buckets: StrategySignalInsightBucket[];
  summary: string;
};

export type StrategySignalRuleInsight = {
  id: string;
  title: string;
  side: 'Long' | 'Short';
  sampleCount: number;
  winRate: number;
  avgPnl: number;
  summary: string;
  mood: 'positive' | 'risk';
};

export type StrategySignalAnalysis = {
  featureOptions: StrategySignalFeatureOption[];
  insights: StrategySignalInsight[];
  ruleInsights: StrategySignalRuleInsight[];
  warnings: string[];
};

export type StrategyTimeBucketInsight = {
  key: string;
  label: string;
  count: number;
  winCount: number;
  lossCount: number;
  winRate: number;
  lossRate: number;
  avgPnl: number;
  avgReturnPct: number;
  avgHoldingHours: number;
};

export type StrategyTimeInsightGroup = {
  id: string;
  title: string;
  description: string;
  bestBucket: StrategyTimeBucketInsight | null;
  riskBucket: StrategyTimeBucketInsight | null;
  buckets: StrategyTimeBucketInsight[];
  summary: string;
};

export type StrategyTimeAnalysis = {
  totalClosedTrades: number;
  groups: StrategyTimeInsightGroup[];
};

export type StrategyTuningSuggestion = {
  id: string;
  title: string;
  category: '参数' | '指标' | '时段' | '稳健性';
  level: 'positive' | 'warning';
  description: string;
};

export type StrategyRobustnessInsight = {
  overfitRisk: 'low' | 'medium' | 'high';
  sampleAdequacy: string;
  distributionRisk: string;
  summary: string;
};

export type StrategyTuningPanelProps = {
  talibReady: boolean;
  analysisTimeframe?: string;
  bars: KLineData[];
  selectedIndicators: GeneratedIndicatorPayload[];
  indicatorOutputGroups: IndicatorOutputGroup[];
  logicContainers: ConditionContainer[];
  filterContainers: ConditionContainer[];
  methodOptions: MethodOption[];
  formatIndicatorName: (indicator: GeneratedIndicatorPayload) => string;
  backtestSummary: LocalBacktestSummary;
  backtestStats: LocalBacktestStats;
  previewTrades: LocalBacktestTrade[];
  backtestParams: {
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
  };
  onApplyOptimization: (targetIndicatorId: string, input: string, params: number[]) => boolean;
};
