import React, { useMemo, useState } from 'react';
import { TAFuncs } from 'talib-web';
import { Dialog, useNotification } from '../ui/index.ts';
import { HttpClient } from '../../network/httpClient';
import { getToken } from '../../network/index.ts';
import { ensureTalibReady } from '../../lib/talibInit';
import {
  normalizeTalibInputSource,
  roundAwayFromZero,
  type TalibInputSource,
} from '../../lib/talibCalcRules';
import './TalibRandomParityTestDialog.css';

interface TalibRandomParityTestDialogProps {
  open: boolean;
  onClose: () => void;
}

interface TaRandomCompareResponse {
  sample: TaSample;
  klines: MarketKline[];
  indicatorCount: number;
  successCount: number;
  failedCount: number;
  indicators: BackendIndicatorResult[];
}

interface TaSample {
  exchange: string;
  symbol: string;
  timeframe: string;
  startTime: string;
  endTime: string;
  bars: number;
  windowStartIndex: number;
  totalCachedBars: number;
  seed?: number | null;
}

interface MarketKline {
  timestamp: number;
  open: number | null;
  high: number | null;
  low: number | null;
  close: number | null;
  volume: number | null;
}

interface BackendIndicatorResult {
  indicatorCode: string;
  talibCode: string;
  displayName: string;
  inputs: InputBinding[];
  options: OptionBinding[];
  outputNames: string[];
  outputs: Array<Array<number | null>>;
  error?: string | null;
}

interface InputBinding {
  name: string;
  source: string;
}

interface OptionBinding {
  name: string;
  type: string;
  value: number;
}

interface OutputCompareRow {
  id: string;
  indicatorCode: string;
  talibCode: string;
  displayName: string;
  outputName: string;
  inputSummary: string;
  optionSummary: string;
  backendValues: Array<number | null>;
  frontendValues: Array<number | null>;
  backendNonNullCount: number;
  frontendNonNullCount: number;
  mismatchCount: number;
  firstMismatchIndex: number | null;
  maxAbsDiff: number;
  pass: boolean;
  backendError?: string | null;
  frontendError?: string | null;
}

type TalibFunction = (params: Record<string, unknown>) => Record<string, unknown>;
const TALIB_FUNCTIONS = TAFuncs as unknown as Record<string, TalibFunction>;
const DIFF_TOLERANCE = 1e-10;
const PARITY_IMPL_VERSION = '2026-02-22.typedarray-fix';
type NumericArrayLike = { length: number; [index: number]: unknown };

const isNumericArrayLike = (value: unknown): value is NumericArrayLike => {
  if (Array.isArray(value)) {
    return true;
  }
  if (ArrayBuffer.isView(value)) {
    const maybeLength = (value as { length?: unknown }).length;
    return typeof maybeLength === 'number';
  }
  return false;
};

const toFiniteOrNull = (value: unknown): number | null => {
  if (value === null || value === undefined) {
    return null;
  }

  if (typeof value === 'number') {
    return Number.isFinite(value) ? value : null;
  }

  const numberValue = Number(value);
  return Number.isFinite(numberValue) ? numberValue : null;
};

const normalizeOptionValue = (value: number, type: string): number => {
  const normalizedType = (type ?? '').trim().toLowerCase();
  if (normalizedType === 'integer' || normalizedType === 'matype') {
    return roundAwayFromZero(value);
  }
  return value;
};

const alignSeries = (values: unknown, expectedLength: number): Array<number | null> => {
  const result: Array<number | null> = new Array(expectedLength).fill(null);
  if (!isNumericArrayLike(values)) {
    return result;
  }

  // 与后端桥接脚本保持一致：数组不足时前补 null，尾部对齐。
  const copyCount = Math.min(expectedLength, values.length);
  const startIndex = Math.max(0, expectedLength - copyCount);
  for (let i = 0; i < copyCount; i += 1) {
    result[startIndex + i] = toFiniteOrNull(values[i]);
  }
  return result;
};

const normalizeOutputKey = (value: string): string => value.trim().toUpperCase().replace(/[^A-Z0-9]/g, '');

const resolveOutputValues = (rawOutputs: Record<string, unknown>, outputName: string): unknown => {
  if (Object.prototype.hasOwnProperty.call(rawOutputs, outputName)) {
    return rawOutputs[outputName];
  }

  const normalizedTarget = normalizeOutputKey(outputName);
  for (const [key, value] of Object.entries(rawOutputs)) {
    if (normalizeOutputKey(key) === normalizedTarget) {
      return value;
    }
  }
  return undefined;
};

const resolveSourceValue = (kline: MarketKline, source: TalibInputSource): number => {
  const open = Number.isFinite(kline.open) ? Number(kline.open) : Number.NaN;
  const high = Number.isFinite(kline.high) ? Number(kline.high) : Number.NaN;
  const low = Number.isFinite(kline.low) ? Number(kline.low) : Number.NaN;
  const close = Number.isFinite(kline.close) ? Number(kline.close) : Number.NaN;
  const volume = Number.isFinite(kline.volume) ? Number(kline.volume) : Number.NaN;

  switch (source) {
    case 'OPEN':
      return open;
    case 'HIGH':
      return high;
    case 'LOW':
      return low;
    case 'CLOSE':
      return close;
    case 'VOLUME':
      return volume;
    case 'HL2':
      return (high + low) / 2;
    case 'HLC3':
      return (high + low + close) / 3;
    case 'OHLC4':
      return (open + high + low + close) / 4;
    case 'OC2':
      return (open + close) / 2;
    case 'HLCC4':
      return (high + low + close + close) / 4;
    default:
      return close;
  }
};

const formatNumber = (value: number | null): string => {
  if (value === null || !Number.isFinite(value)) {
    return '-';
  }

  const abs = Math.abs(value);
  if (abs >= 1000000 || (abs > 0 && abs < 0.000001)) {
    return value.toExponential(6);
  }

  return value.toFixed(8);
};

const formatDateTime = (timeText: string): string => {
  const date = new Date(timeText);
  if (Number.isNaN(date.getTime())) {
    return timeText;
  }
  return date.toLocaleString('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  });
};

const buildInputSummary = (inputs: InputBinding[]): string => {
  if (!inputs.length) {
    return '-';
  }
  return inputs.map((input) => `${input.name}:${input.source}`).join(', ');
};

const buildOptionSummary = (options: OptionBinding[]): string => {
  if (!options.length) {
    return '默认';
  }
  return options.map((option) => `${option.name}=${option.value}`).join(', ');
};

const TalibRandomParityTestDialog: React.FC<TalibRandomParityTestDialogProps> = ({ open, onClose }) => {
  const { error: showError, success: showSuccess } = useNotification();
  const client = useMemo(() => new HttpClient({ tokenProvider: getToken }), []);

  const [running, setRunning] = useState(false);
  const [phaseText, setPhaseText] = useState('');
  const [response, setResponse] = useState<TaRandomCompareResponse | null>(null);
  const [rows, setRows] = useState<OutputCompareRow[]>([]);
  const [selectedRowId, setSelectedRowId] = useState<string | null>(null);
  const [elapsedMs, setElapsedMs] = useState(0);

  const selectedRow = useMemo(
    () => rows.find((row) => row.id === selectedRowId) ?? null,
    [rows, selectedRowId],
  );

  const summary = useMemo(() => {
    const outputCount = rows.length;
    const passCount = rows.filter((row) => row.pass).length;
    const failCount = outputCount - passCount;
    const indicatorPassMap = new Map<string, boolean>();

    for (const row of rows) {
      const key = `${row.indicatorCode}@${row.talibCode}`;
      const current = indicatorPassMap.get(key);
      const pass = row.pass && !row.backendError && !row.frontendError;
      indicatorPassMap.set(key, (current ?? true) && pass);
    }

    const indicatorPassCount = Array.from(indicatorPassMap.values()).filter(Boolean).length;

    return {
      outputCount,
      passCount,
      failCount,
      indicatorCount: indicatorPassMap.size,
      indicatorPassCount,
    };
  }, [rows]);

  const runRandomTest = async () => {
    setRunning(true);
    setPhaseText('请求后端随机样本中...');
    setSelectedRowId(null);
    const startTime = Date.now();

    try {
      const backendResult = await client.postProtocol<TaRandomCompareResponse>(
        '/api/MarketData/ta-random-compare',
        'marketdata.ta.random.compare',
        { bars: 2000 },
        { timeoutMs: 120000 },
      );

      setPhaseText('初始化前端 talib.wasm ...');
      await ensureTalibReady();

      const nextRows: OutputCompareRow[] = [];
      const seriesCache = new Map<string, number[]>();
      const candleLength = backendResult.klines.length;

      const getSeries = (sourceText: string): number[] => {
        const source = normalizeTalibInputSource(sourceText);
        const cacheKey = source;
        const cached = seriesCache.get(cacheKey);
        if (cached) {
          return cached;
        }

        const built = backendResult.klines.map((kline) => resolveSourceValue(kline, source));
        seriesCache.set(cacheKey, built);
        return built;
      };

      for (let i = 0; i < backendResult.indicators.length; i += 1) {
        const indicator = backendResult.indicators[i];
        if (i % 4 === 0) {
          setPhaseText(`前端重算并对比中... ${i + 1}/${backendResult.indicators.length}`);
          await new Promise((resolve) => {
            window.setTimeout(resolve, 0);
          });
        }

        const frontendOutputs: unknown[] = [];
        let frontendError: string | null = null;

        if (!indicator.error) {
          const fn = TALIB_FUNCTIONS[indicator.talibCode];
          if (!fn) {
            frontendError = `前端缺少函数: ${indicator.talibCode}`;
          } else {
            try {
              const params: Record<string, unknown> = {};

              for (const input of indicator.inputs) {
                params[input.name] = getSeries(input.source);
              }

              for (const option of indicator.options) {
                params[option.name] = normalizeOptionValue(option.value, option.type);
              }

              const rawOutputs = fn(params);
              const outputNames = indicator.outputNames.length
                ? indicator.outputNames
                : Object.keys(rawOutputs);

              for (const outputName of outputNames) {
                frontendOutputs.push(resolveOutputValues(rawOutputs, outputName));
              }
            } catch (error) {
              frontendError = error instanceof Error ? error.message : '前端计算异常';
            }
          }
        }

        const outputCount = Math.max(
          indicator.outputNames.length,
          indicator.outputs.length,
          frontendOutputs.length,
          1,
        );

        for (let outputIndex = 0; outputIndex < outputCount; outputIndex += 1) {
          const outputName = indicator.outputNames[outputIndex] ?? `output_${outputIndex + 1}`;
          const backendValues = alignSeries(indicator.outputs[outputIndex], candleLength);
          const frontendValues = alignSeries(frontendOutputs[outputIndex], candleLength);

          let mismatchCount = 0;
          let firstMismatchIndex: number | null = null;
          let maxAbsDiff = 0;
          let backendNonNullCount = 0;
          let frontendNonNullCount = 0;

          for (let barIndex = 0; barIndex < candleLength; barIndex += 1) {
            const backendValue = backendValues[barIndex];
            const frontendValue = frontendValues[barIndex];

            if (backendValue !== null) {
              backendNonNullCount += 1;
            }
            if (frontendValue !== null) {
              frontendNonNullCount += 1;
            }

            if (backendValue === null && frontendValue === null) {
              continue;
            }

            if (backendValue === null || frontendValue === null) {
              mismatchCount += 1;
              if (firstMismatchIndex === null) {
                firstMismatchIndex = barIndex;
              }
              continue;
            }

            const diff = Math.abs(backendValue - frontendValue);
            if (diff > maxAbsDiff) {
              maxAbsDiff = diff;
            }
            if (diff > DIFF_TOLERANCE) {
              mismatchCount += 1;
              if (firstMismatchIndex === null) {
                firstMismatchIndex = barIndex;
              }
            }
          }

          const row: OutputCompareRow = {
            id: `${indicator.indicatorCode}_${indicator.talibCode}_${outputName}_${outputIndex}`,
            indicatorCode: indicator.indicatorCode,
            talibCode: indicator.talibCode,
            displayName: indicator.displayName,
            outputName,
            inputSummary: buildInputSummary(indicator.inputs),
            optionSummary: buildOptionSummary(indicator.options),
            backendValues,
            frontendValues,
            backendNonNullCount,
            frontendNonNullCount,
            mismatchCount,
            firstMismatchIndex,
            maxAbsDiff,
            pass: !indicator.error && !frontendError && mismatchCount === 0,
            backendError: indicator.error,
            frontendError,
          };

          nextRows.push(row);
        }
      }

      setResponse(backendResult);
      setRows(nextRows);
      setElapsedMs(Date.now() - startTime);
      setPhaseText('随机一致性测试完成');
      showSuccess('随机一致性测试完成');
    } catch (error) {
      const message = error instanceof Error ? error.message : '随机测试失败';
      setPhaseText(`测试失败: ${message}`);
      showError(message);
    } finally {
      setRunning(false);
    }
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="TA 指标随机一致性测试"
      cancelText="关闭"
      className="talib-random-parity-dialog"
    >
      <div className="talib-random-parity-content ui-scrollable">
        <div className="talib-random-parity-toolbar">
          <button
            type="button"
            className="talib-random-parity-run-btn"
            onClick={runRandomTest}
            disabled={running}
          >
            {running ? '执行中...' : '随机测试'}
          </button>
          <span className="talib-random-parity-phase">{`版本: ${PARITY_IMPL_VERSION}`}</span>
          <span className="talib-random-parity-phase">{phaseText || '点击“随机测试”开始执行'}</span>
        </div>

        {response && (
          <>
            <div className="talib-random-parity-meta-grid">
              <div className="talib-random-parity-meta-item">
                <span>样本</span>
                <strong>{`${response.sample.exchange} / ${response.sample.symbol} / ${response.sample.timeframe}`}</strong>
              </div>
              <div className="talib-random-parity-meta-item">
                <span>时间范围</span>
                <strong>{`${formatDateTime(response.sample.startTime)} ~ ${formatDateTime(response.sample.endTime)}`}</strong>
              </div>
              <div className="talib-random-parity-meta-item">
                <span>窗口位置</span>
                <strong>{`${response.sample.windowStartIndex + 1} / ${response.sample.totalCachedBars}`}</strong>
              </div>
              <div className="talib-random-parity-meta-item">
                <span>耗时</span>
                <strong>{`${elapsedMs} ms`}</strong>
              </div>
            </div>

            <div className="talib-random-parity-summary-grid">
              <div className="talib-random-parity-summary-card">
                <div className="talib-random-parity-summary-label">指标通过</div>
                <div className="talib-random-parity-summary-value">{`${summary.indicatorPassCount} / ${summary.indicatorCount}`}</div>
              </div>
              <div className="talib-random-parity-summary-card">
                <div className="talib-random-parity-summary-label">输出通过</div>
                <div className="talib-random-parity-summary-value">{`${summary.passCount} / ${summary.outputCount}`}</div>
              </div>
              <div className="talib-random-parity-summary-card">
                <div className="talib-random-parity-summary-label">后端计算成功</div>
                <div className="talib-random-parity-summary-value">{`${response.successCount} / ${response.indicatorCount}`}</div>
              </div>
              <div className="talib-random-parity-summary-card talib-random-parity-summary-card-fail">
                <div className="talib-random-parity-summary-label">不一致输出</div>
                <div className="talib-random-parity-summary-value">{summary.failCount}</div>
              </div>
            </div>

            <div className="talib-random-parity-table-wrap ui-scrollable">
              <table className="talib-random-parity-table">
                <thead>
                  <tr>
                    <th>指标</th>
                    <th>输出</th>
                    <th>输入源</th>
                    <th>参数</th>
                    <th>后端有效</th>
                    <th>前端有效</th>
                    <th>最大绝对误差</th>
                    <th>首差异索引</th>
                    <th>状态</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((row) => (
                    <tr
                      key={row.id}
                      className={selectedRowId === row.id ? 'is-selected' : ''}
                      onClick={() => setSelectedRowId(row.id)}
                    >
                      <td>{`${row.indicatorCode} (${row.talibCode})`}</td>
                      <td>{row.outputName}</td>
                      <td title={row.inputSummary}>{row.inputSummary}</td>
                      <td title={row.optionSummary}>{row.optionSummary}</td>
                      <td>{row.backendNonNullCount}</td>
                      <td>{row.frontendNonNullCount}</td>
                      <td>{row.maxAbsDiff.toExponential(6)}</td>
                      <td>{row.firstMismatchIndex ?? '-'}</td>
                      <td>
                        {row.pass ? (
                          <span className="talib-random-parity-tag tag-pass">通过</span>
                        ) : (
                          <span className="talib-random-parity-tag tag-fail">不一致</span>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {selectedRow && (
              <div className="talib-random-parity-detail">
                <div className="talib-random-parity-detail-header">
                  <h4>{`${selectedRow.displayName} - ${selectedRow.outputName}`}</h4>
                  <div className="talib-random-parity-detail-errors">
                    {selectedRow.backendError && <span>{`后端错误: ${selectedRow.backendError}`}</span>}
                    {selectedRow.frontendError && <span>{`前端错误: ${selectedRow.frontendError}`}</span>}
                  </div>
                </div>
                <div className="talib-random-parity-detail-table-wrap ui-scrollable">
                  <table className="talib-random-parity-detail-table">
                    <thead>
                      <tr>
                        <th>索引</th>
                        <th>时间</th>
                        <th>后端值</th>
                        <th>前端值</th>
                        <th>绝对误差</th>
                      </tr>
                    </thead>
                    <tbody>
                      {selectedRow.backendValues.map((backendValue, index) => {
                        const frontendValue = selectedRow.frontendValues[index];
                        const diff =
                          backendValue === null || frontendValue === null
                            ? null
                            : Math.abs(backendValue - frontendValue);
                        const isMismatch =
                          backendValue === null || frontendValue === null
                            ? backendValue !== frontendValue
                            : diff !== null && diff > DIFF_TOLERANCE;

                        return (
                          <tr key={`${selectedRow.id}_${index}`} className={isMismatch ? 'is-mismatch' : ''}>
                            <td>{index}</td>
                            <td>{new Date(response.klines[index]?.timestamp ?? 0).toLocaleString('zh-CN')}</td>
                            <td>{formatNumber(backendValue)}</td>
                            <td>{formatNumber(frontendValue)}</td>
                            <td>{diff === null ? '-' : diff.toExponential(6)}</td>
                          </tr>
                        );
                      })}
                    </tbody>
                  </table>
                </div>
              </div>
            )}
          </>
        )}
      </div>
    </Dialog>
  );
};

export default TalibRandomParityTestDialog;
