import React, { startTransition, useEffect, useMemo, useRef, useState } from 'react';

import IndicatorGeneratorSelector, {
  type GeneratedIndicatorPayload,
} from '../../indicator/IndicatorGeneratorSelector';
import {
  getTalibIndicatorEditorSchema,
  getTalibIndicatorMetaList,
} from '../../../lib/registerTalibIndicators';
import {
  estimateOptimizationVariantCount,
  runStrategyParameterOptimization,
  TUNING_INPUT_SOURCE_OPTIONS,
} from './strategyParameterOptimizer';
import { buildStrategySignalAnalysis } from './strategySignalInsights';
import { buildStrategyTimeAnalysis } from './strategyTimeInsights';
import {
  buildStrategyRobustnessInsight,
  buildStrategyTuningSuggestions,
} from './strategyTuningSuggestions';
import type {
  StrategyOptimizationHandle,
  StrategyOptimizationObjective,
  StrategyOptimizationParamRange,
  StrategyOptimizationProgress,
  StrategyOptimizationResult,
  StrategyTuningPanelProps,
} from './strategyTuningTypes';
import './StrategyTuningPanel.css';

type TuningTab = 'optimizer' | 'signal' | 'time' | 'robustness';

const OBJECTIVE_OPTIONS: Array<{ value: StrategyOptimizationObjective; label: string }> = [
  { value: 'composite', label: '综合评分' },
  { value: 'total_profit', label: '累计收益' },
  { value: 'win_rate', label: '胜率' },
  { value: 'sharpe_ratio', label: '夏普比率' },
  { value: 'calmar_ratio', label: 'Calmar' },
  { value: 'profit_factor', label: 'Profit Factor' },
];

const MAX_VARIANT_LIMIT = 2400;

const normalizeLookupKey = (value: string) => value.trim().toLowerCase().replace(/[^a-z0-9]/g, '');

const areStringArraysEqual = (left: string[], right: string[]) => (
  left.length === right.length && left.every((item, index) => item === right[index])
);

const buildIndicatorFeatureSignature = (indicator: GeneratedIndicatorPayload) => {
  const config = (indicator.config || {}) as {
    indicator?: unknown;
    input?: unknown;
    params?: unknown[];
    output?: unknown;
    calcMode?: unknown;
    offsetRange?: unknown[];
  };
  return [
    String(config.indicator || indicator.code || '').trim(),
    String(config.input || '').trim(),
    Array.isArray(config.params) ? config.params.map((item) => String(item)).join(',') : '',
    String(config.output || '').trim(),
    String(config.calcMode || '').trim(),
    Array.isArray(config.offsetRange) ? config.offsetRange.map((item) => String(item)).join(',') : '',
  ].join('|');
};

const formatSignedNumber = (value: number, digits = 2) => {
  if (!Number.isFinite(value)) {
    return '0';
  }
  const text = value.toFixed(digits);
  return value > 0 ? `+${text}` : text;
};

const formatPercent = (value: number, digits = 1) => `${value.toFixed(digits)}%`;

const formatDuration = (value: number) => {
  if (!Number.isFinite(value) || value <= 0) {
    return '0秒';
  }
  const seconds = Math.round(value / 1000);
  if (seconds < 60) {
    return `${seconds}秒`;
  }
  const minutes = Math.floor(seconds / 60);
  const remainSeconds = seconds % 60;
  if (minutes < 60) {
    return `${minutes}分${remainSeconds}秒`;
  }
  const hours = Math.floor(minutes / 60);
  const remainMinutes = minutes % 60;
  return `${hours}小时${remainMinutes}分`;
};

const extractPrimaryInputSource = (rawInput: unknown) => {
  const text = String(rawInput || '').trim();
  if (!text) {
    return 'Close';
  }
  if (!text.includes('=')) {
    return text;
  }
  const segments = text
    .split(';')
    .map((segment) => segment.trim())
    .filter((segment) => segment.length > 0);
  for (const segment of segments) {
    const [rawKey, rawValue] = segment.split('=');
    const key = String(rawKey || '').trim().toLowerCase();
    const value = String(rawValue || '').trim();
    if (!value) {
      continue;
    }
    if (key === 'real' || key === 'inreal') {
      return value;
    }
  }
  const firstValue = segments[0]?.split('=')?.[1];
  return String(firstValue || 'Close').trim() || 'Close';
};

const resolveIndicatorSchema = (indicatorCode: string) => {
  const normalized = normalizeLookupKey(indicatorCode);
  const meta = getTalibIndicatorMetaList().find((item) => {
    const code = normalizeLookupKey(item.code);
    const name = normalizeLookupKey(item.name);
    return code === normalized || name === normalized || normalizeLookupKey(`ta_${item.code}`) === normalized;
  });
  return meta ? getTalibIndicatorEditorSchema(meta.name) : null;
};

const buildParamRanges = (
  currentParams: number[],
  indicatorCode: string,
): StrategyOptimizationParamRange[] => {
  const schema = resolveIndicatorSchema(indicatorCode);
  const paramCount = Math.max(currentParams.length, schema?.paramDefinitions.length || 0);
  return new Array(paramCount).fill(null).map((_, index) => {
    const definition = schema?.paramDefinitions[index];
    const currentValue = Number(currentParams[index] ?? definition?.defaultValue ?? 0);
    const integerLike = definition?.valueType === 'integer'
      || definition?.valueType === 'matype'
      || Number.isInteger(currentValue);
    const step = integerLike
      ? 1
      : Math.max(0.1, Math.abs(currentValue) >= 1 ? Math.abs(currentValue) * 0.1 : 0.1);
    const span = integerLike ? 4 : step * 4;
    return {
      index,
      label: definition?.label || `参数 ${index + 1}`,
      currentValue,
      min: integerLike ? Math.max(1, currentValue - span) : Math.max(0, currentValue - span),
      max: currentValue + span,
      step,
      enabled: index === 0,
    };
  });
};

const buildEmptyProgress = (): StrategyOptimizationProgress => ({
  processed: 0,
  total: 0,
  progress: 0,
  elapsedMs: 0,
  bestScore: Number.NEGATIVE_INFINITY,
  bestLabel: '等待开始',
  done: false,
  phase: 'idle',
});

const StrategyTuningPanel: React.FC<StrategyTuningPanelProps> = ({
  talibReady,
  analysisTimeframe,
  bars,
  selectedIndicators,
  indicatorOutputGroups,
  logicContainers,
  filterContainers,
  methodOptions,
  formatIndicatorName,
  backtestSummary,
  backtestStats,
  previewTrades,
  backtestParams,
  onApplyOptimization,
}) => {
  const [activeTab, setActiveTab] = useState<TuningTab>('optimizer');
  const [selectedIndicatorId, setSelectedIndicatorId] = useState('');
  const [selectedInputs, setSelectedInputs] = useState<string[]>(['Close']);
  const [paramRanges, setParamRanges] = useState<StrategyOptimizationParamRange[]>([]);
  const [objective, setObjective] = useState<StrategyOptimizationObjective>('composite');
  const [topN, setTopN] = useState(8);
  const [optimizationProgress, setOptimizationProgress] = useState<StrategyOptimizationProgress>(buildEmptyProgress);
  const [optimizationResult, setOptimizationResult] = useState<StrategyOptimizationResult | null>(null);
  const [optimizationError, setOptimizationError] = useState('');
  const [selectedFeatureIds, setSelectedFeatureIds] = useState<string[]>([]);
  const [analysisIndicators, setAnalysisIndicators] = useState<GeneratedIndicatorPayload[]>([]);
  const [analysisIndicatorDialogOpen, setAnalysisIndicatorDialogOpen] = useState(false);
  const optimizationHandleRef = useRef<StrategyOptimizationHandle | null>(null);

  const selectedIndicator = useMemo(
    () => selectedIndicators.find((item) => item.id === selectedIndicatorId) || null,
    [selectedIndicatorId, selectedIndicators],
  );

  useEffect(() => {
    setSelectedIndicatorId((prev) => {
      if (prev && selectedIndicators.some((item) => item.id === prev)) {
        return prev;
      }
      return selectedIndicators[0]?.id || '';
    });
  }, [selectedIndicators]);

  useEffect(() => {
    if (!selectedIndicator) {
      setSelectedInputs(['Close']);
      setParamRanges([]);
      return;
    }
    const config = (selectedIndicator.config || {}) as { input?: unknown; indicator?: unknown; params?: unknown[] };
    const currentInput = extractPrimaryInputSource(config.input);
    const currentParams = Array.isArray(config.params)
      ? config.params.map((item) => Number(item)).filter((item) => Number.isFinite(item))
      : [];
    setSelectedInputs([currentInput]);
    setParamRanges(buildParamRanges(currentParams, String(config.indicator || selectedIndicator.code || '')));
  }, [selectedIndicator]);

  useEffect(() => () => {
    optimizationHandleRef.current?.cancel();
    optimizationHandleRef.current = null;
  }, []);

  const variantCount = useMemo(
    () => estimateOptimizationVariantCount(selectedInputs, paramRanges),
    [paramRanges, selectedInputs],
  );

  const signalAnalysis = useMemo(
    () => buildStrategySignalAnalysis({
      bars,
      selectedIndicators,
      analysisIndicators,
      trades: previewTrades,
      enabledFeatureIds: selectedFeatureIds,
      talibReady,
      formatIndicatorName,
    }),
    [analysisIndicators, bars, formatIndicatorName, previewTrades, selectedFeatureIds, selectedIndicators, talibReady],
  );

  useEffect(() => {
    setSelectedFeatureIds((prev) => {
      const available = new Set(signalAnalysis.featureOptions.map((item) => item.id));
      const filtered = prev.filter((item) => available.has(item));
      if (filtered.length > 0) {
        return areStringArraysEqual(prev, filtered) ? prev : filtered;
      }
      const defaults = signalAnalysis.featureOptions
        .filter((item) => item.enabledByDefault)
        .map((item) => item.id);
      return areStringArraysEqual(prev, defaults) ? prev : defaults;
    });
  }, [signalAnalysis.featureOptions]);

  const timeAnalysis = useMemo(
    () => buildStrategyTimeAnalysis(previewTrades),
    [previewTrades],
  );

  const robustness = useMemo(
    () => buildStrategyRobustnessInsight({
      tradeCount: timeAnalysis.totalClosedTrades,
      signalAnalysis,
      timeAnalysis,
      optimizationResult,
    }),
    [optimizationResult, signalAnalysis, timeAnalysis],
  );

  const suggestions = useMemo(
    () => buildStrategyTuningSuggestions({
      optimizationResult,
      signalAnalysis,
      timeAnalysis,
      tradeCount: timeAnalysis.totalClosedTrades,
    }),
    [optimizationResult, signalAnalysis, timeAnalysis],
  );

  const isOptimizationRunning = optimizationProgress.phase === 'running' && !optimizationProgress.done;
  const isOptimizationDisabled =
    !talibReady
    || !selectedIndicator
    || bars.length <= 0
    || selectedInputs.length <= 0
    || variantCount <= 0
    || variantCount > MAX_VARIANT_LIMIT;

  const handleToggleInput = (value: string) => {
    setSelectedInputs((prev) => (
      prev.includes(value)
        ? prev.filter((item) => item !== value)
        : [...prev, value]
    ));
  };

  const handleUpdateParamRange = (
    index: number,
    key: 'min' | 'max' | 'step' | 'enabled',
    value: string | boolean,
  ) => {
    setParamRanges((prev) => prev.map((item) => {
      if (item.index !== index) {
        return item;
      }
      if (key === 'enabled') {
        return { ...item, enabled: Boolean(value) };
      }
      const parsed = Number(value);
      return {
        ...item,
        [key]: Number.isFinite(parsed) ? parsed : item[key],
      };
    }));
  };

  const handleStartOptimization = () => {
    if (!selectedIndicator) {
      return;
    }
    optimizationHandleRef.current?.cancel();
    setOptimizationError('');
    startTransition(() => {
      setOptimizationProgress({
        processed: 0,
        total: variantCount,
        progress: 0,
        elapsedMs: 0,
        bestScore: Number.NEGATIVE_INFINITY,
        bestLabel: '准备扫描',
        done: false,
        phase: 'running',
      });
      setOptimizationResult(null);
    });
    optimizationHandleRef.current = runStrategyParameterOptimization(
      {
        bars,
        selectedIndicators,
        indicatorOutputGroups,
        logicContainers,
        filterContainers,
        methodOptions,
        targetIndicatorId: selectedIndicator.id,
        targetIndicatorName: formatIndicatorName(selectedIndicator),
        baseSummary: backtestSummary,
        objective,
        inputCandidates: selectedInputs,
        paramRanges,
        topN,
        chunkSize: 1,
        tickMs: 24,
        progressMinIntervalMs: 500,
        maxChunkWorkMs: 18,
        scheduleMode: 'idle',
        takeProfitPct: backtestParams.takeProfitPct,
        stopLossPct: backtestParams.stopLossPct,
        leverage: backtestParams.leverage,
        orderQty: backtestParams.orderQty,
        initialCapital: backtestParams.initialCapital,
        feeRate: backtestParams.feeRate,
        fundingRate: backtestParams.fundingRate,
        slippageBps: backtestParams.slippageBps,
        autoReverse: backtestParams.autoReverse,
        useStrategyRuntime: backtestParams.useStrategyRuntime,
        executionMode: backtestParams.executionMode,
      },
      {
        onProgress: (progress) => {
          startTransition(() => {
            setOptimizationProgress(progress);
          });
        },
        onComplete: (result) => {
          optimizationHandleRef.current = null;
          startTransition(() => {
            setOptimizationProgress((prev) => ({
              ...prev,
              done: true,
              phase: 'done',
            }));
            setOptimizationResult(result);
          });
        },
        onError: (message) => {
          optimizationHandleRef.current = null;
          startTransition(() => {
            setOptimizationError(message);
            setOptimizationProgress({
              ...buildEmptyProgress(),
              phase: 'error',
            });
          });
        },
      },
    );
  };

  const handleCancelOptimization = () => {
    optimizationHandleRef.current?.cancel();
    optimizationHandleRef.current = null;
    startTransition(() => {
      setOptimizationProgress((prev) => ({
        ...prev,
        done: false,
        phase: 'cancelled',
      }));
    });
  };

  const handleCreateAnalysisIndicator = (indicator: GeneratedIndicatorPayload) => {
    const nextSignature = buildIndicatorFeatureSignature(indicator);
    const allIndicators = [...selectedIndicators, ...analysisIndicators];
    const duplicate = allIndicators.some((item) => buildIndicatorFeatureSignature(item) === nextSignature);
    if (duplicate) {
      return;
    }
    setAnalysisIndicators((prev) => [indicator, ...prev]);
    const selectedOutput = String((indicator.config as { output?: unknown })?.output || indicator.outputs[0]?.key || 'Value').trim();
    if (selectedOutput) {
      setSelectedFeatureIds((prev) => (
        prev.includes(`analysis:${indicator.id}:${selectedOutput}`)
          ? prev
          : [...prev, `analysis:${indicator.id}:${selectedOutput}`]
      ));
    }
  };

  const handleRemoveAnalysisIndicator = (indicatorId: string) => {
    setAnalysisIndicators((prev) => prev.filter((item) => item.id !== indicatorId));
    setSelectedFeatureIds((prev) => prev.filter((item) => !item.startsWith(`analysis:${indicatorId}:`)));
  };

  const validateAnalysisIndicator = (indicator: GeneratedIndicatorPayload) => {
    const nextSignature = buildIndicatorFeatureSignature(indicator);
    const allIndicators = [...selectedIndicators, ...analysisIndicators];
    const duplicate = allIndicators.some((item) => buildIndicatorFeatureSignature(item) === nextSignature);
    return duplicate ? '该分析指标已经存在，请直接勾选对应输出值查看统计。' : null;
  };

  const renderBucketTable = (
    rows: Array<{
      key: string;
      label: string;
      count: number;
      winRate: number;
      lossRate: number;
      avgPnl: number;
      avgReturnPct: number;
    }>,
  ) => (
    <div className="strategy-tuning-table-wrap">
      <table className="strategy-tuning-table">
        <thead>
          <tr>
            <th>区间</th>
            <th>样本</th>
            <th>胜率</th>
            <th>亏损率</th>
            <th>平均盈亏</th>
            <th>平均收益率</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.key}>
              <td>{row.label}</td>
              <td>{row.count}</td>
              <td>{formatPercent(row.winRate * 100)}</td>
              <td>{formatPercent(row.lossRate * 100)}</td>
              <td className={row.avgPnl >= 0 ? 'is-positive' : 'is-negative'}>{formatSignedNumber(row.avgPnl)}</td>
              <td className={row.avgReturnPct >= 0 ? 'is-positive' : 'is-negative'}>{formatPercent(row.avgReturnPct)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );

  return (
    <div className="strategy-tuning-panel">
      <div className="strategy-tuning-panel-header">
        <div>
          <div className="strategy-tuning-panel-title">调优模式</div>
          <div className="strategy-tuning-panel-subtitle">
            参数扫描、指标共振、时间分布和样本稳健性集中在同一视图处理。
          </div>
        </div>
        <div className="strategy-tuning-metric-grid">
          <div className="strategy-tuning-metric-card">
            <span>已平仓样本</span>
            <strong>{timeAnalysis.totalClosedTrades}</strong>
          </div>
          <div className="strategy-tuning-metric-card">
            <span>当前累计收益</span>
            <strong className={backtestSummary.totalProfit >= 0 ? 'is-positive' : 'is-negative'}>
              {formatSignedNumber(backtestSummary.totalProfit)}
            </strong>
          </div>
          <div className="strategy-tuning-metric-card">
            <span>当前最大回撤</span>
            <strong className="is-negative">{formatPercent(backtestStats.maxDrawdown * 100)}</strong>
          </div>
          <div className="strategy-tuning-metric-card">
            <span>待扫描组合</span>
            <strong>{variantCount}</strong>
          </div>
        </div>
      </div>

      <div className="strategy-tuning-suggestion-grid">
        {suggestions.map((item) => (
          <div
            key={item.id}
            className={`strategy-tuning-suggestion-card ${item.level === 'positive' ? 'is-positive' : 'is-warning'}`}
          >
            <div className="strategy-tuning-suggestion-head">
              <span>{item.category}</span>
              <strong>{item.title}</strong>
            </div>
            <div className="strategy-tuning-suggestion-body">{item.description}</div>
          </div>
        ))}
      </div>

      <div className="strategy-tuning-tablist" role="tablist" aria-label="调优模式分组">
        <button
          type="button"
          className={`strategy-tuning-tab ${activeTab === 'optimizer' ? 'is-active' : ''}`}
          onClick={() => setActiveTab('optimizer')}
        >
          参数调优
        </button>
        <button
          type="button"
          className={`strategy-tuning-tab ${activeTab === 'signal' ? 'is-active' : ''}`}
          onClick={() => setActiveTab('signal')}
        >
          指标共振
        </button>
        <button
          type="button"
          className={`strategy-tuning-tab ${activeTab === 'time' ? 'is-active' : ''}`}
          onClick={() => setActiveTab('time')}
        >
          时段分布
        </button>
        <button
          type="button"
          className={`strategy-tuning-tab ${activeTab === 'robustness' ? 'is-active' : ''}`}
          onClick={() => setActiveTab('robustness')}
        >
          稳健性
        </button>
      </div>

      {activeTab === 'optimizer' ? (
        <div className="strategy-tuning-content-grid">
          <div className="strategy-tuning-card">
            <div className="strategy-tuning-card-header">
              <div>
                <div className="strategy-tuning-card-title">参数扫描设置</div>
                <div className="strategy-tuning-card-description">
                  以当前回测参数为基准，协程式扫描目标指标的输入源和参数组合。
                </div>
              </div>
            </div>

            <div className="strategy-tuning-form-grid">
              <label className="strategy-tuning-field">
                <span>目标指标</span>
                <select
                  value={selectedIndicatorId}
                  onChange={(event) => setSelectedIndicatorId(event.target.value)}
                >
                  {selectedIndicators.map((indicator) => (
                    <option key={indicator.id} value={indicator.id}>
                      {formatIndicatorName(indicator)}
                    </option>
                  ))}
                </select>
              </label>

              <label className="strategy-tuning-field">
                <span>优化目标</span>
                <select
                  value={objective}
                  onChange={(event) => setObjective(event.target.value as StrategyOptimizationObjective)}
                >
                  {OBJECTIVE_OPTIONS.map((option) => (
                    <option key={option.value} value={option.value}>
                      {option.label}
                    </option>
                  ))}
                </select>
              </label>

              <label className="strategy-tuning-field">
                <span>保留 TopN</span>
                <input
                  type="number"
                  min={3}
                  max={20}
                  value={topN}
                  onChange={(event) => setTopN(Math.max(3, Math.min(20, Number(event.target.value) || 8)))}
                />
              </label>
            </div>

            <div className="strategy-tuning-section">
              <div className="strategy-tuning-section-title">扫描输入源</div>
              <div className="strategy-tuning-chip-list">
                {TUNING_INPUT_SOURCE_OPTIONS.map((input) => (
                  <button
                    type="button"
                    key={input}
                    className={`strategy-tuning-chip ${selectedInputs.includes(input) ? 'is-active' : ''}`}
                    onClick={() => handleToggleInput(input)}
                  >
                    {input}
                  </button>
                ))}
              </div>
            </div>

            <div className="strategy-tuning-section">
              <div className="strategy-tuning-section-title">参数范围</div>
              {paramRanges.length <= 0 ? (
                <div className="strategy-tuning-empty">当前指标没有可扫描参数，可以仅扫描输入源。</div>
              ) : (
                <div className="strategy-tuning-param-list">
                  {paramRanges.map((range) => (
                    <div className="strategy-tuning-param-card" key={range.index}>
                      <label className="strategy-tuning-param-toggle">
                        <input
                          type="checkbox"
                          checked={range.enabled}
                          onChange={(event) => handleUpdateParamRange(range.index, 'enabled', event.target.checked)}
                        />
                        <span>{range.label}</span>
                        <strong>当前 {range.currentValue}</strong>
                      </label>
                      <div className="strategy-tuning-param-grid">
                        <label className="strategy-tuning-field">
                          <span>最小值</span>
                          <input
                            type="number"
                            value={range.min}
                            onChange={(event) => handleUpdateParamRange(range.index, 'min', event.target.value)}
                          />
                        </label>
                        <label className="strategy-tuning-field">
                          <span>最大值</span>
                          <input
                            type="number"
                            value={range.max}
                            onChange={(event) => handleUpdateParamRange(range.index, 'max', event.target.value)}
                          />
                        </label>
                        <label className="strategy-tuning-field">
                          <span>步长</span>
                          <input
                            type="number"
                            min={0.0001}
                            step={0.1}
                            value={range.step}
                            onChange={(event) => handleUpdateParamRange(range.index, 'step', event.target.value)}
                          />
                        </label>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>

            <div className="strategy-tuning-hint-row">
              <span>组合总数 {variantCount}</span>
              <span>调度方式 空闲切片</span>
              <span>刷新频率 0.5 秒</span>
              {variantCount > MAX_VARIANT_LIMIT ? (
                <span className="is-warning">组合过多，请收窄范围到 {MAX_VARIANT_LIMIT} 组以内。</span>
              ) : null}
            </div>

            <div className="strategy-tuning-action-row">
              <button
                type="button"
                className="strategy-tuning-primary-button"
                onClick={handleStartOptimization}
                disabled={isOptimizationDisabled || isOptimizationRunning}
              >
                开始调优
              </button>
              <button
                type="button"
                className="strategy-tuning-secondary-button"
                onClick={handleCancelOptimization}
                disabled={!isOptimizationRunning}
              >
                停止
              </button>
            </div>

            {optimizationError ? (
              <div className="strategy-tuning-error">{optimizationError}</div>
            ) : null}

            <div className="strategy-tuning-progress-card">
              <div className="strategy-tuning-progress-head">
                <span>扫描进度</span>
                <strong>{formatPercent(optimizationProgress.progress * 100)}</strong>
              </div>
              <div className="strategy-tuning-progress-bar">
                <div
                  className="strategy-tuning-progress-fill"
                  style={{ width: `${Math.max(0, Math.min(100, optimizationProgress.progress * 100))}%` }}
                />
              </div>
              <div className="strategy-tuning-progress-meta">
                <span>{optimizationProgress.processed}/{optimizationProgress.total}</span>
                <span>耗时 {formatDuration(optimizationProgress.elapsedMs)}</span>
                <span>当前最优 {optimizationProgress.bestLabel}</span>
              </div>
            </div>
          </div>

          <div className="strategy-tuning-card">
            <div className="strategy-tuning-card-header">
              <div>
                <div className="strategy-tuning-card-title">调优结果</div>
                <div className="strategy-tuning-card-description">
                  优先展示得分最高的组合，并支持一键回写到当前指标。
                </div>
              </div>
            </div>

            {!optimizationResult?.bestCandidate ? (
              <div className="strategy-tuning-empty">
                {isOptimizationRunning ? '扫描中，请等待第一批结果。' : '尚未执行参数调优。'}
              </div>
            ) : (
              <>
                <div className="strategy-tuning-highlight-card">
                  <div className="strategy-tuning-highlight-title">最优建议</div>
                  <div className="strategy-tuning-highlight-main">
                    <strong>{optimizationResult.targetIndicatorName}</strong>
                    <span>输入 {optimizationResult.bestCandidate.input}</span>
                    <span>参数 {optimizationResult.bestCandidate.params.join(', ')}</span>
                  </div>
                  <div className="strategy-tuning-highlight-meta">
                    <span>评分 {optimizationResult.bestCandidate.score.toFixed(2)}</span>
                    <span>收益 {formatSignedNumber(optimizationResult.bestCandidate.summary.totalProfit)}</span>
                    <span>胜率 {formatPercent(optimizationResult.bestCandidate.summary.winRate * 100)}</span>
                    <span>夏普 {optimizationResult.bestCandidate.summary.stats.sharpeRatio.toFixed(2)}</span>
                  </div>
                  <button
                    type="button"
                    className="strategy-tuning-primary-button"
                    onClick={() => {
                      onApplyOptimization(
                        optimizationResult.targetIndicatorId,
                        optimizationResult.bestCandidate?.input || 'Close',
                        optimizationResult.bestCandidate?.params || [],
                      );
                    }}
                  >
                    应用到指标
                  </button>
                </div>

                <div className="strategy-tuning-result-list">
                  {optimizationResult.candidates.map((candidate) => (
                    <div className="strategy-tuning-result-row" key={candidate.key}>
                      <div className="strategy-tuning-result-rank">#{candidate.rank}</div>
                      <div className="strategy-tuning-result-body">
                        <div className="strategy-tuning-result-title">
                          输入 {candidate.input} · 参数 {candidate.params.join(', ')}
                        </div>
                        <div className="strategy-tuning-result-meta">
                          <span>评分 {candidate.score.toFixed(2)}</span>
                          <span>收益 {formatSignedNumber(candidate.summary.totalProfit)}</span>
                          <span>胜率 {formatPercent(candidate.summary.winRate * 100)}</span>
                          <span>回撤 {formatPercent(candidate.summary.maxDrawdown * 100)}</span>
                        </div>
                      </div>
                      <button
                        type="button"
                        className="strategy-tuning-inline-button"
                        onClick={() => {
                          onApplyOptimization(
                            optimizationResult.targetIndicatorId,
                            candidate.input,
                            candidate.params,
                          );
                        }}
                      >
                        应用
                      </button>
                    </div>
                  ))}
                </div>
              </>
            )}
          </div>
        </div>
      ) : null}

      {activeTab === 'signal' ? (
        <div className="strategy-tuning-content-grid strategy-tuning-content-grid--single">
          <div className="strategy-tuning-card">
            <div className="strategy-tuning-card-header">
              <div>
                <div className="strategy-tuning-card-title">共振指标选择</div>
                <div className="strategy-tuning-card-description">
                  默认提供 RSI / MACD / ADX，也支持把当前策略里的自定义指标一起纳入统计。
                </div>
              </div>
              <button
                type="button"
                className="strategy-tuning-inline-button"
                onClick={() => setAnalysisIndicatorDialogOpen(true)}
              >
                添加分析指标
              </button>
            </div>

            <div className="strategy-tuning-chip-list">
              {signalAnalysis.featureOptions.map((item) => (
                <button
                  type="button"
                  key={item.id}
                  className={`strategy-tuning-chip ${selectedFeatureIds.includes(item.id) ? 'is-active' : ''}`}
                  onClick={() => setSelectedFeatureIds((prev) => (
                    prev.includes(item.id)
                      ? prev.filter((value) => value !== item.id)
                      : [...prev, item.id]
                  ))}
                >
                  {item.label}
                </button>
              ))}
            </div>

            {signalAnalysis.warnings.length > 0 ? (
              <div className="strategy-tuning-warning-list">
                {signalAnalysis.warnings.map((item) => (
                  <div key={item} className="strategy-tuning-warning-item">{item}</div>
                ))}
              </div>
            ) : null}

            <div className="strategy-tuning-section">
              <div className="strategy-tuning-section-title">已添加分析指标</div>
              {analysisIndicators.length <= 0 ? (
                <div className="strategy-tuning-empty">
                  还没有新增分析指标。点击“添加分析指标”后，可在上方输出值列表中勾选对应输出做统计。
                </div>
              ) : (
                <div className="strategy-tuning-analysis-indicator-list">
                  {analysisIndicators.map((indicator) => (
                    <div className="strategy-tuning-analysis-indicator-row" key={indicator.id}>
                      <div className="strategy-tuning-analysis-indicator-main">
                        <strong>{formatIndicatorName(indicator)}</strong>
                        <span>
                          输出 {indicator.outputs.length > 0 ? indicator.outputs.map((output) => output.key).join(', ') : 'Value'}
                        </span>
                      </div>
                      <button
                        type="button"
                        className="strategy-tuning-inline-button"
                        onClick={() => handleRemoveAnalysisIndicator(indicator.id)}
                      >
                        移除
                      </button>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>

          <div className="strategy-tuning-card">
            <div className="strategy-tuning-card-header">
              <div>
                <div className="strategy-tuning-card-title">经典规则提示</div>
                <div className="strategy-tuning-card-description">
                  聚焦 RSI / MACD 的极值和零轴分布，优先给出可以直接转成过滤条件的提示。
                </div>
              </div>
            </div>
            <div className="strategy-tuning-rule-grid">
              {signalAnalysis.ruleInsights.length <= 0 ? (
                <div className="strategy-tuning-empty">暂无足够样本生成规则提示。</div>
              ) : (
                signalAnalysis.ruleInsights.map((item) => (
                  <div
                    key={item.id}
                    className={`strategy-tuning-rule-card ${item.mood === 'positive' ? 'is-positive' : 'is-risk'}`}
                  >
                    <div className="strategy-tuning-rule-title">
                      {item.title}
                      <span>{item.side === 'Long' ? '做多' : '做空'}</span>
                    </div>
                    <div className="strategy-tuning-rule-body">{item.summary}</div>
                  </div>
                ))
              )}
            </div>
          </div>

          <div className="strategy-tuning-card">
            <div className="strategy-tuning-card-header">
              <div>
                <div className="strategy-tuning-card-title">指标区间统计</div>
                <div className="strategy-tuning-card-description">
                  用已平仓样本回看开仓点数值，分别找出做多和做空时的高胜率区间与高风险区间。
                </div>
              </div>
            </div>

            {signalAnalysis.insights.length <= 0 ? (
              <div className="strategy-tuning-empty">暂无可展示的指标区间分布。</div>
            ) : (
              <div className="strategy-tuning-insight-list">
                {signalAnalysis.insights.map((item) => (
                  <div className="strategy-tuning-insight-card" key={item.id}>
                    <div className="strategy-tuning-insight-head">
                      <div>
                        <div className="strategy-tuning-insight-title">
                          {item.label} · {item.side === 'Long' ? '做多' : '做空'}
                        </div>
                        <div className="strategy-tuning-insight-summary">{item.summary}</div>
                      </div>
                      <div className="strategy-tuning-insight-badges">
                        <span>样本 {item.totalSamples}</span>
                        {item.bestBucket ? <span>最佳 {item.bestBucket.label}</span> : null}
                        {item.riskBucket ? <span>风险 {item.riskBucket.label}</span> : null}
                      </div>
                    </div>
                    {renderBucketTable(item.buckets)}
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>
      ) : null}

      {activeTab === 'time' ? (
        <div className="strategy-tuning-content-grid strategy-tuning-content-grid--single">
          <div className="strategy-tuning-card">
            <div className="strategy-tuning-card-header">
              <div>
                <div className="strategy-tuning-card-title">时段与持仓统计</div>
                <div className="strategy-tuning-card-description">
                  围绕持仓时长、开仓星期、开仓时段和美股活跃窗口，识别稳定性更好的过滤维度。
                </div>
              </div>
            </div>
            <div className="strategy-tuning-rule-grid">
              {timeAnalysis.groups.map((group) => (
                <div className="strategy-tuning-rule-card is-neutral" key={group.id}>
                  <div className="strategy-tuning-rule-title">{group.title}</div>
                  <div className="strategy-tuning-rule-body">{group.summary}</div>
                </div>
              ))}
            </div>
          </div>

          {timeAnalysis.groups.map((group) => (
            <div className="strategy-tuning-card" key={group.id}>
              <div className="strategy-tuning-card-header">
                <div>
                  <div className="strategy-tuning-card-title">{group.title}</div>
                  <div className="strategy-tuning-card-description">{group.description}</div>
                </div>
              </div>
              <div className="strategy-tuning-insight-summary">{group.summary}</div>
              {renderBucketTable(group.buckets)}
            </div>
          ))}
        </div>
      ) : null}

      {activeTab === 'robustness' ? (
        <div className="strategy-tuning-content-grid strategy-tuning-content-grid--single">
          <div className="strategy-tuning-card">
            <div className="strategy-tuning-card-header">
              <div>
                <div className="strategy-tuning-card-title">样本稳健性</div>
                <div className="strategy-tuning-card-description">
                  除了找最优组合，还需要关注样本数量、时间分布是否过于集中，避免过拟合。
                </div>
              </div>
            </div>

            <div className="strategy-tuning-robust-grid">
              <div className={`strategy-tuning-robust-card is-${robustness.overfitRisk}`}>
                <span>过拟合风险</span>
                <strong>
                  {robustness.overfitRisk === 'high' ? '高' : robustness.overfitRisk === 'medium' ? '中' : '低'}
                </strong>
              </div>
              <div className="strategy-tuning-robust-card">
                <span>样本建议</span>
                <strong>{timeAnalysis.totalClosedTrades} 笔</strong>
              </div>
              <div className="strategy-tuning-robust-card">
                <span>信号线索</span>
                <strong>{signalAnalysis.ruleInsights.length} 条</strong>
              </div>
            </div>

            <div className="strategy-tuning-robust-copy">
              <p>{robustness.sampleAdequacy}</p>
              <p>{robustness.distributionRisk}</p>
              <p>{robustness.summary}</p>
            </div>
          </div>

          <div className="strategy-tuning-card">
            <div className="strategy-tuning-card-header">
              <div>
                <div className="strategy-tuning-card-title">落地建议</div>
                <div className="strategy-tuning-card-description">
                  推荐先把高胜率信号、低胜率时段和参数最优解拆成独立过滤层，再做滚动回测验证。
                </div>
              </div>
            </div>

            <div className="strategy-tuning-steps">
              <div className="strategy-tuning-step">
                <strong>1. 先做过滤，不直接替换逻辑</strong>
                <span>优先把风险区间和低胜率时段转成禁入条件，观察收益曲线是否更平滑。</span>
              </div>
              <div className="strategy-tuning-step">
                <strong>2. 参数应用后重新验证样本分布</strong>
                <span>如果最优组合让样本数量明显下降，需要回到调优模式继续看是否出现过拟合。</span>
              </div>
              <div className="strategy-tuning-step">
                <strong>3. 对正向规则做组合共振</strong>
                <span>把 RSI / MACD 的高胜率区间与时段过滤叠加，构建更稳定的二次筛选层。</span>
              </div>
            </div>
          </div>
        </div>
      ) : null}

      <IndicatorGeneratorSelector
        open={analysisIndicatorDialogOpen}
        onClose={() => setAnalysisIndicatorDialogOpen(false)}
        onGenerated={handleCreateAnalysisIndicator}
        autoCloseOnGenerate={true}
        validateIndicator={(indicator) => validateAnalysisIndicator(indicator)}
        fixedTimeframe={analysisTimeframe}
        hideTimeframeSelector={Boolean(analysisTimeframe)}
      />
    </div>
  );
};

export default StrategyTuningPanel;
