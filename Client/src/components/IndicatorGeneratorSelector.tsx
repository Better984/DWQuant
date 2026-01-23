import React, { useEffect, useMemo, useState } from 'react';
import { Button, Dialog, SearchInput, Select, useNotification } from './ui';
import './IndicatorGeneratorSelector.css';

interface TalibEnumOption {
  label: string;
  value: number;
  name?: string;
}

interface TalibCommonOption {
  key: string;
  desc?: string;
  type?: string;
  enum?: TalibEnumOption[];
  default?: number;
}

interface TalibOption {
  key?: string;
  desc?: string;
  $ref?: string;
}

interface TalibOutput {
  key: string;
  hint?: string;
}

interface TalibIndicator {
  code: string;
  abbr_en?: string;
  name_en?: string;
  name_cn?: string;
  method?: string;
  group?: string;
  indicator_type?: string;
  inputs?: {
    shape?: string;
    series?: string[];
  };
  options?: TalibOption[];
  outputs?: TalibOutput[];
}

interface TalibRoot {
  version?: string;
  common?: Record<string, TalibCommonOption>;
  indicators: TalibIndicator[];
}

interface ParamDefinition {
  id: string;
  label: string;
  description?: string;
  type: 'number' | 'enum';
  defaultValue?: number;
  enumOptions?: TalibEnumOption[];
}

interface IndicatorGeneratorSelectorProps {
  open: boolean;
  onClose: () => void;
}

const INPUT_OPTIONS = [
  { value: 'Close', label: 'Close' },
  { value: 'Open', label: 'Open' },
  { value: 'High', label: 'High' },
  { value: 'Low', label: 'Low' },
  { value: 'Volume', label: 'Volume' },
  { value: 'HL2', label: 'HL2' },
  { value: 'HLC3', label: 'HLC3' },
  { value: 'OHLC4', label: 'OHLC4' },
  { value: 'OC2', label: 'OC2' },
  { value: 'HLCC4', label: 'HLCC4' },
];

const CALC_MODE_OPTIONS = [
  { value: 'OnBarClose', label: 'OnBarClose (收盘)' },
  { value: 'OnBarUpdate', label: 'OnBarUpdate (实时)' },
];

const OFFSET_DEFAULT = { min: '1', max: '1' };

const getIndicatorName = (indicator: TalibIndicator) =>
  indicator.name_en || indicator.abbr_en || indicator.code;

const getIndicatorCategory = (indicator: TalibIndicator) =>
  indicator.indicator_type || indicator.group || 'Other';

const IndicatorGeneratorSelector: React.FC<IndicatorGeneratorSelectorProps> = ({ open, onClose }) => {
  const { success, error } = useNotification();
  const [catalog, setCatalog] = useState<TalibRoot | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [step, setStep] = useState<'select' | 'config'>('select');
  const [searchTerm, setSearchTerm] = useState('');
  const [activeIndicator, setActiveIndicator] = useState<TalibIndicator | null>(null);
  const [timeframe, setTimeframe] = useState('m1');
  const [calcMode, setCalcMode] = useState('OnBarClose');
  const [inputSelections, setInputSelections] = useState<string[]>(['Close']);
  const [outputSelection, setOutputSelection] = useState('Value');
  const [offsetMin, setOffsetMin] = useState(OFFSET_DEFAULT.min);
  const [offsetMax, setOffsetMax] = useState(OFFSET_DEFAULT.max);
  const [paramValues, setParamValues] = useState<string[]>([]);
  const [generatedConfig, setGeneratedConfig] = useState<string | null>(null);

  useEffect(() => {
    if (!open) {
      setStep('select');
      setSearchTerm('');
      setActiveIndicator(null);
      setGeneratedConfig(null);
      return;
    }
    if (catalog || isLoading) {
      return;
    }
    setIsLoading(true);
    fetch('/talib_indicators_config.json')
      .then((response) => {
        if (!response.ok) {
          throw new Error(`HTTP ${response.status}`);
        }
        return response.json();
      })
      .then((data) => {
        setCatalog(data as TalibRoot);
        setLoadError(null);
      })
      .catch((err: Error) => {
        setLoadError(err.message || 'Load failed');
        error('指标配置加载失败');
      })
      .finally(() => {
        setIsLoading(false);
      });
  }, [catalog, error, isLoading, open]);

  const filteredIndicators = useMemo(() => {
    if (!catalog?.indicators) {
      return [];
    }
    const term = searchTerm.trim().toLowerCase();
    if (!term) {
      return catalog.indicators;
    }
    return catalog.indicators.filter((indicator) => {
      const haystack = [
        indicator.code,
        indicator.abbr_en,
        indicator.name_en,
        indicator.name_cn,
        indicator.group,
        indicator.indicator_type,
      ]
        .filter(Boolean)
        .join(' ')
        .toLowerCase();
      return haystack.includes(term);
    });
  }, [catalog, searchTerm]);

  const groupedIndicators = useMemo(() => {
    const map = new Map<string, TalibIndicator[]>();
    filteredIndicators.forEach((indicator) => {
      const key = getIndicatorCategory(indicator);
      if (!map.has(key)) {
        map.set(key, []);
      }
      map.get(key)?.push(indicator);
    });
    const entries = Array.from(map.entries());
    entries.sort(([a], [b]) => a.localeCompare(b));
    entries.forEach(([, list]) => {
      list.sort((a, b) => getIndicatorName(a).localeCompare(getIndicatorName(b)));
    });
    return entries;
  }, [filteredIndicators]);

  const paramDefinitions = useMemo<ParamDefinition[]>(() => {
    if (!catalog || !activeIndicator) {
      return [];
    }
    const common = catalog.common || {};
    const options = activeIndicator.options || [];
    return options
      .map((option, index) => {
        if (option.$ref) {
          const refKey = option.$ref.split('/').pop() || '';
          const ref = common[refKey];
          if (!ref) {
            return null;
          }
          return {
            id: `${refKey}-${index}`,
            label: ref.key,
            description: ref.desc,
            type: ref.type === 'enum' ? 'enum' : 'number',
            defaultValue: ref.default,
            enumOptions: ref.enum || [],
          } as ParamDefinition;
        }
        if (!option.key) {
          return null;
        }
        return {
          id: `${option.key}-${index}`,
          label: option.key,
          description: option.desc,
          type: 'number',
        } as ParamDefinition;
      })
      .filter(Boolean) as ParamDefinition[];
  }, [activeIndicator, catalog]);

  const realInputCount = useMemo(() => {
    const series = activeIndicator?.inputs?.series || [];
    return series.filter((item) => item.toLowerCase() === 'real').length;
  }, [activeIndicator]);

  useEffect(() => {
    if (!activeIndicator) {
      return;
    }
    setGeneratedConfig(null);
    setTimeframe('m1');
    setCalcMode('OnBarClose');
    setOffsetMin(OFFSET_DEFAULT.min);
    setOffsetMax(OFFSET_DEFAULT.max);

    const outputs = activeIndicator.outputs || [];
    setOutputSelection(outputs.length > 0 ? outputs[0].key : 'Value');

    const inputSlots = Math.max(1, realInputCount);
    setInputSelections(Array.from({ length: inputSlots }, () => 'Close'));

    const defaults = paramDefinitions.map((param) => {
      if (param.type === 'enum') {
        const fallback = param.defaultValue ?? param.enumOptions?.[0]?.value ?? 0;
        return String(fallback);
      }
      return '';
    });
    setParamValues(defaults);
  }, [activeIndicator, paramDefinitions, realInputCount]);

  const handleSelectIndicator = (indicator: TalibIndicator) => {
    setActiveIndicator(indicator);
    setStep('config');
  };

  const handleBackToSelect = () => {
    setStep('select');
    setActiveIndicator(null);
    setGeneratedConfig(null);
  };

  const handleInputChange = (index: number, value: string) => {
    setInputSelections((prev) => {
      const next = [...prev];
      next[index] = value;
      return next;
    });
  };

  const handleParamChange = (index: number, value: string) => {
    setParamValues((prev) => {
      const next = [...prev];
      next[index] = value;
      return next;
    });
  };

  const offsetMinValue = Number(offsetMin);
  const offsetMaxValue = Number(offsetMax);
  const hasInvalidOffsets =
    Number.isNaN(offsetMinValue) ||
    Number.isNaN(offsetMaxValue) ||
    offsetMinValue < 0 ||
    offsetMaxValue < 0 ||
    offsetMaxValue < offsetMinValue;

  const hasInvalidParams = paramDefinitions.some((param, index) => {
    const raw = paramValues[index];
    if (raw === undefined || raw === '') {
      return true;
    }
    return Number.isNaN(Number(raw));
  });

  const canGenerate = Boolean(
    activeIndicator &&
      !hasInvalidOffsets &&
      !hasInvalidParams &&
      timeframe.trim() &&
      outputSelection.trim(),
  );

  const handleGenerate = () => {
    if (!activeIndicator || !canGenerate) {
      return;
    }
    const params = paramDefinitions.map((param, index) => {
      const raw = paramValues[index];
      if (param.type === 'enum') {
        return Number(raw ?? param.defaultValue ?? 0);
      }
      return Number(raw);
    });
    const input =
      realInputCount > 1
        ? inputSelections.map((selection) => selection || 'Close').join(',')
        : inputSelections[0] || 'Close';

    const config = {
      refType: 'Indicator',
      indicator: activeIndicator.code,
      timeframe: timeframe.trim() || 'm1',
      input,
      params,
      output: outputSelection.trim() || 'Value',
      offsetRange: [offsetMinValue, offsetMaxValue],
      calcMode,
    };
    const formatted = JSON.stringify(config, null, 2);
    setGeneratedConfig(formatted);
    success('已生成指标配置');
  };

  const handleCopy = () => {
    if (!generatedConfig) {
      return;
    }
    if (!navigator.clipboard?.writeText) {
      error('当前环境不支持复制');
      return;
    }
    navigator.clipboard.writeText(generatedConfig).then(
      () => success('已复制到剪贴板'),
      () => error('复制失败'),
    );
  };

  const outputOptions = (activeIndicator?.outputs || []).map((output) => ({
    value: output.key,
    label: output.hint ? `${output.key} (${output.hint})` : output.key,
  }));

  return (
    <>
      <Dialog
        open={open && step === 'select'}
        onClose={onClose}
        title="指标生成选择器"
        cancelText="关闭"
        className="indicator-generator__dialog indicator-generator__dialog--list"
      >
        <div className="indicator-generator">
          <div className="indicator-generator__toolbar">
            <SearchInput
              value={searchTerm}
              onChange={(event) => setSearchTerm(event.target.value)}
              onClear={() => setSearchTerm('')}
              placeholder="搜索指标 / 代码"
              showShortcut={false}
              type="gray"
              className="indicator-generator__search"
            />
            <div className="indicator-generator__stats">
              {isLoading ? '加载中...' : `共 ${filteredIndicators.length} 项`}
            </div>
          </div>

          {loadError && (
            <div className="indicator-generator__empty">配置加载失败：{loadError}</div>
          )}

          {!loadError && (
            <div className="indicator-generator__list">
              {isLoading && (
                <div className="indicator-generator__empty">正在加载指标配置...</div>
              )}
              {!isLoading && groupedIndicators.length === 0 && (
                <div className="indicator-generator__empty">未找到匹配指标</div>
              )}
              {!isLoading &&
                groupedIndicators.map(([category, indicators]) => (
                  <div key={category} className="indicator-generator__group">
                    <div className="indicator-generator__group-header">
                      <span className="indicator-generator__group-title">{category}</span>
                      <span className="indicator-generator__group-count">
                        {indicators.length}
                      </span>
                    </div>
                    <div className="indicator-generator__grid">
                      {indicators.map((indicator) => (
                        <button
                          key={indicator.code}
                          className="indicator-generator__card"
                          type="button"
                          onClick={() => handleSelectIndicator(indicator)}
                        >
                          <div className="indicator-generator__card-code">{indicator.code}</div>
                          <div className="indicator-generator__card-name">
                            {getIndicatorName(indicator)}
                          </div>
                          {indicator.inputs?.shape && (
                            <div className="indicator-generator__card-meta">
                              输入: {indicator.inputs.shape}
                            </div>
                          )}
                        </button>
                      ))}
                    </div>
                  </div>
                ))}
            </div>
          )}
        </div>
      </Dialog>

      <Dialog
        open={open && step === 'config'}
        onClose={onClose}
        title="指标参数配置"
        cancelText="返回"
        confirmText="生成配置"
        onCancel={handleBackToSelect}
        onConfirm={handleGenerate}
        className="indicator-generator__dialog indicator-generator__dialog--config"
      >
        {activeIndicator && (
          <div className="indicator-generator__config">
            <div className="indicator-generator__summary">
              <div>
                <div className="indicator-generator__summary-title">
                  {activeIndicator.code} · {getIndicatorName(activeIndicator)}
                </div>
                <div className="indicator-generator__summary-meta">
                  分类：{getIndicatorCategory(activeIndicator)}
                </div>
              </div>
              <div className="indicator-generator__summary-block">
                <div className="indicator-generator__summary-label">输入</div>
                <div className="indicator-generator__summary-value">
                  {activeIndicator.inputs?.series?.length
                    ? activeIndicator.inputs.series.join(', ')
                    : 'Real'}
                </div>
              </div>
              <div className="indicator-generator__summary-block">
                <div className="indicator-generator__summary-label">输出</div>
                <div className="indicator-generator__summary-value">
                  {(activeIndicator.outputs || []).map((item) => item.key).join(', ') || 'Value'}
                </div>
              </div>
            </div>

            <div className="indicator-generator__form">
              <div className="indicator-generator__field">
                <label className="indicator-generator__label">时间周期</label>
                <input
                  className="indicator-generator__input"
                  value={timeframe}
                  onChange={(event) => setTimeframe(event.target.value)}
                  placeholder="m1"
                />
              </div>

              <div className="indicator-generator__field">
                <label className="indicator-generator__label">计算模式</label>
                <Select
                  options={CALC_MODE_OPTIONS}
                  value={calcMode}
                  onChange={setCalcMode}
                  className="indicator-generator__select"
                />
              </div>

              {realInputCount > 0 && (
                <div className="indicator-generator__field">
                  <label className="indicator-generator__label">
                    输入来源 {realInputCount > 1 ? '(多输入)' : ''}
                  </label>
                  <div className="indicator-generator__input-list">
                    {inputSelections.map((selection, index) => (
                      <Select
                        key={`input-${index}`}
                        options={INPUT_OPTIONS}
                        value={selection}
                        onChange={(value) => handleInputChange(index, value)}
                        className="indicator-generator__select"
                      />
                    ))}
                  </div>
                </div>
              )}

              <div className="indicator-generator__field">
                <label className="indicator-generator__label">输出结果</label>
                <Select
                  options={outputOptions.length ? outputOptions : [{ value: 'Value', label: 'Value' }]}
                  value={outputSelection}
                  onChange={setOutputSelection}
                  className="indicator-generator__select"
                />
              </div>

              <div className="indicator-generator__field">
                <label className="indicator-generator__label">偏移范围</label>
                <div className="indicator-generator__offset">
                  <input
                    className="indicator-generator__input indicator-generator__input--small"
                    value={offsetMin}
                    onChange={(event) => setOffsetMin(event.target.value)}
                    placeholder="0"
                    type="number"
                    min={0}
                  />
                  <span className="indicator-generator__offset-separator">至</span>
                  <input
                    className="indicator-generator__input indicator-generator__input--small"
                    value={offsetMax}
                    onChange={(event) => setOffsetMax(event.target.value)}
                    placeholder="0"
                    type="number"
                    min={0}
                  />
                </div>
                {hasInvalidOffsets && (
                  <div className="indicator-generator__hint">偏移范围需要为非负且最大值不小于最小值。</div>
                )}
              </div>

              <div className="indicator-generator__field">
                <label className="indicator-generator__label">参数设置</label>
                {paramDefinitions.length === 0 && (
                  <div className="indicator-generator__empty">该指标无需参数。</div>
                )}
                {paramDefinitions.length > 0 && (
                  <div className="indicator-generator__params">
                    {paramDefinitions.map((param, index) => (
                      <div key={param.id} className="indicator-generator__param-row">
                        <div className="indicator-generator__param-info">
                          <div className="indicator-generator__param-label">{param.label}</div>
                          {param.description && (
                            <div className="indicator-generator__param-desc">{param.description}</div>
                          )}
                        </div>
                        {param.type === 'enum' ? (
                          <Select
                            options={(param.enumOptions || []).map((option) => ({
                              value: String(option.value),
                              label: option.name ? `${option.label} - ${option.name}` : option.label,
                            }))}
                            value={paramValues[index]}
                            onChange={(value) => handleParamChange(index, value)}
                            className="indicator-generator__select"
                          />
                        ) : (
                          <input
                            className="indicator-generator__input indicator-generator__input--small"
                            value={paramValues[index] ?? ''}
                            onChange={(event) => handleParamChange(index, event.target.value)}
                            type="number"
                            placeholder="请输入"
                          />
                        )}
                      </div>
                    ))}
                  </div>
                )}
              </div>

              {!canGenerate && (
                <div className="indicator-generator__hint">
                  请完善参数与偏移范围后再生成配置。
                </div>
              )}
            </div>

            {generatedConfig && (
              <div className="indicator-generator__result">
                <div className="indicator-generator__result-header">
                  <div className="indicator-generator__result-title">生成结果</div>
                  <Button size="small" style="outline" onClick={handleCopy}>
                    复制
                  </Button>
                </div>
                <pre className="indicator-generator__preview">{generatedConfig}</pre>
              </div>
            )}
          </div>
        )}
      </Dialog>
    </>
  );
};

export default IndicatorGeneratorSelector;
