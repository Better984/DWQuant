import React, { useEffect, useMemo, useState } from 'react';
import { Button, Dialog, SearchInput, useNotification } from '../ui/index.ts';
import { resolveTalibGroupCn, resolveTalibIndicatorDisplayName } from '../../lib/talibLocale';
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
  group_cn?: string;
  indicator_type?: string;
  indicator_type_cn?: string;
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

interface TalibMetaOption {
  defaultValue?: number;
}

interface TalibMetaIndicator {
  options?: TalibMetaOption[];
}

type TalibMetaRoot = Record<string, TalibMetaIndicator>;

interface ParamDefinition {
  id: string;
  label: string;
  description?: string;
  type: 'number' | 'enum';
  defaultValue?: number;
  enumOptions?: TalibEnumOption[];
}

export interface GeneratedIndicatorPayload {
  id: string;
  code: string;
  name: string;
  category: string;
  outputs: Array<{ key: string; hint?: string }>;
  config: Record<string, unknown>;
  configText: string;
}

type IndicatorDialogMode = 'create' | 'edit';
type IndicatorFilterPane = 'all' | 'main' | 'sub';
type IndicatorPane = 'main' | 'sub';
type ConfigurableInputKind = 'real' | 'periods';

interface ConfigurableInputSlot {
  id: string;
  kind: ConfigurableInputKind;
  label: string;
  keyName: string;
  fallback: string;
}

interface IndicatorGeneratorSelectorProps {
  open: boolean;
  onClose: () => void;
  onGenerated?: (indicator: GeneratedIndicatorPayload) => void;
  onUpdated?: (indicator: GeneratedIndicatorPayload) => void;
  autoCloseOnGenerate?: boolean;
  mode?: IndicatorDialogMode;
  initialIndicator?: GeneratedIndicatorPayload | null;
  validateIndicator?: (indicator: GeneratedIndicatorPayload, mode: IndicatorDialogMode) => string | null;
  fixedTimeframe?: string;
  hideTimeframeSelector?: boolean;
  preferParamFocus?: boolean;
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

const TIMEFRAME_OPTIONS = [
  { value: 'm1', label: '1m' },
  { value: 'm3', label: '3m' },
  { value: 'm5', label: '5m' },
  { value: 'm15', label: '15m' },
  { value: 'm30', label: '30m' },
  { value: 'h1', label: '1h' },
  { value: 'h2', label: '2h' },
  { value: 'h4', label: '4h' },
  { value: 'h6', label: '6h' },
  { value: 'h8', label: '8h' },
  { value: 'h12', label: '12h' },
  { value: 'd1', label: '1d' },
  { value: 'd3', label: '3d' },
  { value: 'w1', label: '1w' },
  { value: 'mo1', label: '1mo' },
];

const OFFSET_DEFAULT = { min: '1', max: '1' };

const TALIB_CODE_ALIAS: Record<string, string> = {
  CONCEALINGBABYSWALLOW: 'CDLCONCEALBABYSWALL',
  GAPSIDEBYSIDEWHITELINES: 'CDLGAPSIDESIDEWHITE',
  HIKKAKEMODIFIED: 'CDLHIKKAKEMOD',
  IDENTICALTHREECROWS: 'CDLIDENTICAL3CROWS',
  PIERCINGLINE: 'CDLPIERCING',
  RISINGFALLINGTHREEMETHODS: 'CDLRISEFALL3METHODS',
  TAKURILINE: 'CDLTAKURI',
  UNIQUETHREERIVER: 'CDLUNIQUE3RIVER',
  UPDOWNSIDEGAPTHREEMETHODS: 'CDLXSIDEGAP3METHODS',
};

const normalizeTalibLookupKey = (value: string): string =>
  value.trim().toLowerCase().replace(/[^a-z0-9]/g, '');

const getIndicatorName = (indicator: TalibIndicator) =>
  resolveTalibIndicatorDisplayName(
    indicator.code,
    indicator.name_cn,
    indicator.name_en,
    indicator.abbr_en
  );

const getIndicatorDisplayName = (indicator: TalibIndicator) =>
  resolveTalibIndicatorDisplayName(
    indicator.code,
    indicator.name_cn,
    indicator.name_en,
    indicator.abbr_en
  );

const getIndicatorCategory = (indicator: TalibIndicator) =>
  resolveTalibGroupCn(
    indicator.indicator_type || indicator.group || 'Other',
    indicator.indicator_type_cn || indicator.group_cn
  );

function isMainIndicatorGroupText(value: string | null | undefined): boolean {
  const text = (value ?? '').trim().toLowerCase();
  if (!text) {
    return false;
  }

  return (
    text.includes('overlap studies') ||
    text.includes('price transform') ||
    text.includes('叠加') ||
    text.includes('价格变换')
  );
}

function getIndicatorPane(indicator: TalibIndicator): IndicatorPane {
  const rawGroup = indicator.indicator_type || indicator.group || '';
  const rawGroupCn = indicator.indicator_type_cn || indicator.group_cn || '';
  return isMainIndicatorGroupText(rawGroup) || isMainIndicatorGroupText(rawGroupCn)
    ? 'main'
    : 'sub';
}

const normalizeTimeframe = (raw: string) => {
  const value = raw.trim().toLowerCase().replace(/\s+/g, '');
  if (!value) {
    return '';
  }
  if (/^mo\d+$/.test(value)) {
    return value;
  }
  if (/^\d+mo$/.test(value)) {
    return `mo${value.slice(0, -2)}`;
  }
  if (/^[mhdw]\d+$/.test(value)) {
    return value;
  }
  if (/^\d+[mhdw]$/.test(value)) {
    return `${value.slice(-1)}${value.slice(0, -1)}`;
  }
  const match = value.match(/^([a-z]+)(\d+)$/);
  if (match) {
    return `${match[1]}${match[2]}`;
  }
  return value;
};

function resolveTalibMetaCode(
  indicator: TalibIndicator,
  metaCodeIndex: Map<string, string>,
): string | null {
  const candidates: string[] = [];
  const upperCode = (indicator.code || '').toUpperCase();
  if (upperCode && TALIB_CODE_ALIAS[upperCode]) {
    candidates.push(TALIB_CODE_ALIAS[upperCode]);
  }
  if (indicator.code) {
    candidates.push(indicator.code);
    candidates.push(`CDL${indicator.code}`);
  }
  if (indicator.method) {
    candidates.push(indicator.method);
    candidates.push(`cdl${indicator.method}`);
  }
  if (indicator.name_en) {
    candidates.push(indicator.name_en);
  }

  for (const candidate of candidates) {
    const key = normalizeTalibLookupKey(candidate);
    if (!key) {
      continue;
    }
    const resolved = metaCodeIndex.get(key);
    if (resolved) {
      return resolved;
    }
  }
  return null;
}

function parseNamedInputMap(rawInput: string): Map<string, string> {
  const map = new Map<string, string>();
  if (!rawInput.includes('=')) {
    return map;
  }

  const separator = rawInput.includes(';') ? ';' : ',';
  const pairs = rawInput.split(separator).map((item) => item.trim()).filter(Boolean);
  for (const pair of pairs) {
    const index = pair.indexOf('=');
    if (index <= 0 || index >= pair.length - 1) {
      continue;
    }
    const key = pair.slice(0, index).trim().toUpperCase();
    const value = pair.slice(index + 1).trim();
    if (!key || !value) {
      continue;
    }
    map.set(key, value);
  }

  return map;
}

function resolveInputSelectionsFromConfig(
  rawInput: string,
  slots: ConfigurableInputSlot[],
): string[] {
  if (slots.length === 0) {
    return [];
  }

  const trimmed = rawInput.trim();
  if (!trimmed) {
    return slots.map((slot) => slot.fallback);
  }

  const namedMap = parseNamedInputMap(trimmed);
  if (namedMap.size > 0) {
    return slots.map((slot) => {
      const exact = namedMap.get(slot.keyName.toUpperCase());
      if (exact) {
        return exact;
      }

      // 兼容早期命名：Real1 / Periods1
      if (slot.keyName === 'Real' && namedMap.has('REAL1')) {
        return namedMap.get('REAL1') || slot.fallback;
      }
      if (slot.keyName === 'Periods' && namedMap.has('PERIODS1')) {
        return namedMap.get('PERIODS1') || slot.fallback;
      }

      return slot.fallback;
    });
  }

  const parts = trimmed.split(',').map((part) => part.trim()).filter(Boolean);
  if (parts.length === 0) {
    return slots.map((slot) => slot.fallback);
  }

  return slots.map((slot, index) => parts[index] || parts[0] || slot.fallback);
}

function buildConfigInputValue(
  slots: ConfigurableInputSlot[],
  selections: string[],
): string {
  if (slots.length === 0) {
    return 'Close';
  }

  const values = slots.map(
    (slot, index) => selections[index]?.trim() || slot.fallback || 'Close'
  );
  const allReal = slots.every((slot) => slot.kind === 'real');
  if (allReal) {
    return values.length > 1 ? values.join(',') : values[0] || 'Close';
  }

  // 含 Periods 时使用命名编码，避免与多 Real 的逗号格式冲突。
  return slots
    .map((slot, index) => `${slot.keyName}=${values[index]}`)
    .join(';');
}

const IndicatorGeneratorSelector: React.FC<IndicatorGeneratorSelectorProps> = ({
  open,
  onClose,
  onGenerated,
  onUpdated,
  autoCloseOnGenerate = false,
  mode = 'create',
  initialIndicator,
  validateIndicator,
  fixedTimeframe,
  hideTimeframeSelector = false,
  preferParamFocus = false,
}) => {
  const { success, error } = useNotification();
  const [catalog, setCatalog] = useState<TalibRoot | null>(null);
  const [metaRoot, setMetaRoot] = useState<TalibMetaRoot | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [step, setStep] = useState<'select' | 'config'>('select');
  const [searchTerm, setSearchTerm] = useState('');
  const [indicatorPaneFilter, setIndicatorPaneFilter] = useState<IndicatorFilterPane>('all');
  const [indicatorGroupFilter, setIndicatorGroupFilter] = useState<string>('ALL');
  const [activeIndicator, setActiveIndicator] = useState<TalibIndicator | null>(null);
  const [timeframe, setTimeframe] = useState('m1');
  const [calcMode, setCalcMode] = useState('OnBarClose');
  const [inputSelections, setInputSelections] = useState<string[]>(['Close']);
  const [outputSelection, setOutputSelection] = useState('Value');
  const [offsetMin, setOffsetMin] = useState(OFFSET_DEFAULT.min);
  const [offsetMax, setOffsetMax] = useState(OFFSET_DEFAULT.max);
  const [paramValues, setParamValues] = useState<string[]>([]);
  const [generatedConfig, setGeneratedConfig] = useState<string | null>(null);
  const isEditMode = mode === 'edit';
  const normalizedFixedTimeframe = useMemo(() => {
    if (!fixedTimeframe) {
      return '';
    }
    const normalized = normalizeTimeframe(fixedTimeframe);
    if (!normalized) {
      return '';
    }
    const matched = TIMEFRAME_OPTIONS.find((option) => option.value === normalized);
    return matched?.value || normalized;
  }, [fixedTimeframe]);

  useEffect(() => {
    if (!open) {
      return;
    }
    setStep(isEditMode ? 'config' : 'select');
  }, [open, isEditMode]);

  useEffect(() => {
    if (!open || !normalizedFixedTimeframe) {
      return;
    }
    setTimeframe(normalizedFixedTimeframe);
  }, [normalizedFixedTimeframe, open]);

  useEffect(() => {
    if (!open) {
      setStep('select');
      setSearchTerm('');
      setIndicatorPaneFilter('all');
      setIndicatorGroupFilter('ALL');
      setActiveIndicator(null);
      setGeneratedConfig(null);
      return;
    }
    if (isLoading || (catalog && metaRoot)) {
      return;
    }
    setIsLoading(true);
    const loadCatalog = catalog
      ? Promise.resolve(catalog)
      : fetch('/talib_indicators_config.json').then((response) => {
          if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
          }
          return response.json() as Promise<TalibRoot>;
        });

    const loadMeta = metaRoot
      ? Promise.resolve(metaRoot)
      : fetch('/talib_web_api_meta.json').then((response) => {
          if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
          }
          return response.json() as Promise<TalibMetaRoot>;
        });

    Promise.all([loadCatalog, loadMeta])
      .then(([catalogData, metaData]) => {
        setCatalog(catalogData);
        setMetaRoot(metaData);
        setLoadError(null);
      })
      .catch((err: Error) => {
        setLoadError(err.message || 'Load failed');
        error('指标配置加载失败');
      })
      .finally(() => {
        setIsLoading(false);
      });
  }, [catalog, error, isLoading, metaRoot, open]);

  useEffect(() => {
    if (!open || !isEditMode || !catalog || !initialIndicator) {
      return;
    }
    const config = initialIndicator.config as { indicator?: string } | undefined;
    const indicatorCode =
      initialIndicator.code ||
      (config && typeof config.indicator === 'string' ? config.indicator : '');
    if (!indicatorCode) {
      return;
    }
    const matched = catalog.indicators.find((item) => item.code === indicatorCode);
    if (!matched) {
      error(`未找到指标：${indicatorCode}`);
      return;
    }
    setActiveIndicator(matched);
  }, [catalog, error, initialIndicator, isEditMode, open]);

  const searchedIndicators = useMemo(() => {
    if (!catalog?.indicators) {
      return [];
    }
    const term = searchTerm.trim().toLowerCase();
    if (!term) {
      return catalog.indicators;
    }
    return catalog.indicators.filter((indicator) => {
      const category = getIndicatorCategory(indicator);
      const haystack = [
        indicator.code,
        indicator.abbr_en,
        indicator.name_en,
        indicator.name_cn,
        indicator.group,
        indicator.group_cn,
        indicator.indicator_type,
        indicator.indicator_type_cn,
        category,
      ]
        .filter(Boolean)
        .join(' ')
        .toLowerCase();
      return haystack.includes(term);
    });
  }, [catalog, searchTerm]);

  const indicatorGroupOptions = useMemo(() => {
    const groups = new Set<string>();
    for (const indicator of searchedIndicators) {
      const pane = getIndicatorPane(indicator);
      if (indicatorPaneFilter === 'main' && pane !== 'main') {
        continue;
      }
      if (indicatorPaneFilter === 'sub' && pane !== 'sub') {
        continue;
      }
      groups.add(getIndicatorCategory(indicator));
    }
    return Array.from(groups).sort((a, b) => a.localeCompare(b));
  }, [indicatorPaneFilter, searchedIndicators]);

  useEffect(() => {
    if (indicatorGroupFilter === 'ALL') {
      return;
    }
    if (!indicatorGroupOptions.includes(indicatorGroupFilter)) {
      setIndicatorGroupFilter('ALL');
    }
  }, [indicatorGroupFilter, indicatorGroupOptions]);

  const groupedIndicators = useMemo(() => {
    const map = new Map<string, TalibIndicator[]>();
    searchedIndicators.forEach((indicator) => {
      const pane = getIndicatorPane(indicator);
      if (indicatorPaneFilter === 'main' && pane !== 'main') {
        return;
      }
      if (indicatorPaneFilter === 'sub' && pane !== 'sub') {
        return;
      }

      const key = getIndicatorCategory(indicator);
      if (indicatorGroupFilter !== 'ALL' && key !== indicatorGroupFilter) {
        return;
      }

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
  }, [indicatorGroupFilter, indicatorPaneFilter, searchedIndicators]);

  const visibleIndicatorCount = useMemo(
    () => groupedIndicators.reduce((sum, [, indicators]) => sum + indicators.length, 0),
    [groupedIndicators]
  );

  const metaCodeIndex = useMemo(() => {
    const index = new Map<string, string>();
    if (!metaRoot) {
      return index;
    }
    for (const code of Object.keys(metaRoot)) {
      index.set(normalizeTalibLookupKey(code), code);
    }
    return index;
  }, [metaRoot]);

  const activeIndicatorMetaDefaults = useMemo<Array<number | undefined>>(() => {
    if (!activeIndicator || !metaRoot) {
      return [];
    }
    const talibCode = resolveTalibMetaCode(activeIndicator, metaCodeIndex);
    if (!talibCode) {
      return [];
    }
    const options = metaRoot[talibCode]?.options || [];
    return options.map((option) =>
      typeof option.defaultValue === 'number' && Number.isFinite(option.defaultValue)
        ? option.defaultValue
        : undefined
    );
  }, [activeIndicator, metaCodeIndex, metaRoot]);

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
            defaultValue:
              typeof ref.default === 'number' && Number.isFinite(ref.default)
                ? ref.default
                : activeIndicatorMetaDefaults[index],
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
          defaultValue: activeIndicatorMetaDefaults[index],
        } as ParamDefinition;
      })
      .filter(Boolean) as ParamDefinition[];
  }, [activeIndicator, activeIndicatorMetaDefaults, catalog]);

  const configurableInputSlots = useMemo<ConfigurableInputSlot[]>(() => {
    const series = activeIndicator?.inputs?.series || [];
    const realCount = series.filter((item) => item.toLowerCase() === 'real').length;
    const periodsCount = series.filter((item) => item.toLowerCase() === 'periods').length;
    let realIndex = 0;
    let periodsIndex = 0;

    const slots: ConfigurableInputSlot[] = [];
    for (const rawSeries of series) {
      const seriesName = rawSeries.toLowerCase();
      if (seriesName === 'real') {
        realIndex += 1;
        const keyName = realCount > 1 ? `Real${realIndex}` : 'Real';
        slots.push({
          id: `real-${realIndex}`,
          kind: 'real',
          label: realCount > 1 ? `输入 ${realIndex}（Real）` : '输入来源（Real）',
          keyName,
          fallback: 'Close',
        });
        continue;
      }

      if (seriesName === 'periods') {
        periodsIndex += 1;
        const keyName = periodsCount > 1 ? `Periods${periodsIndex}` : 'Periods';
        slots.push({
          id: `periods-${periodsIndex}`,
          kind: 'periods',
          label: periodsCount > 1 ? `周期序列 ${periodsIndex}（Periods）` : '周期序列（Periods）',
          keyName,
          fallback: 'Close',
        });
      }
    }

    return slots;
  }, [activeIndicator]);

  const outputOptions = useMemo(() => activeIndicator?.outputs || [], [activeIndicator]);

  useEffect(() => {
    if (!activeIndicator) {
      return;
    }
    setGeneratedConfig(null);
    const defaultOutput = outputOptions.length > 0 ? outputOptions[0].key : 'Value';
    if (isEditMode && initialIndicator) {
      const config = initialIndicator.config as {
        timeframe?: string;
        calcMode?: string;
        input?: string;
        params?: number[];
        output?: string;
        offsetRange?: number[];
      };
      const rawTimeframe = (config?.timeframe || 'm1').trim() || 'm1';
      const normalizedTimeframe = normalizeTimeframe(rawTimeframe);
      const knownTimeframe =
        TIMEFRAME_OPTIONS.find((option) => option.value === normalizedTimeframe)?.value || rawTimeframe;
      const nextCalcMode = (config?.calcMode || 'OnBarClose').trim() || 'OnBarClose';
      const nextOutput = (config?.output || defaultOutput).trim() || defaultOutput;
      const offsetRange = Array.isArray(config?.offsetRange) ? config.offsetRange.map(Number) : [];
      const offsetMinValue = Number(offsetRange[0] ?? OFFSET_DEFAULT.min);
      const offsetMaxValue = Number(
        offsetRange.length > 1 ? offsetRange[1] : offsetRange[0] ?? OFFSET_DEFAULT.max,
      );
      setTimeframe(normalizedFixedTimeframe || knownTimeframe);
      setCalcMode(nextCalcMode);
      setOffsetMin(Number.isFinite(offsetMinValue) ? String(offsetMinValue) : OFFSET_DEFAULT.min);
      setOffsetMax(Number.isFinite(offsetMaxValue) ? String(offsetMaxValue) : OFFSET_DEFAULT.max);
      setOutputSelection(nextOutput);

      const rawInput = (config?.input || '').trim();
      const nextInputs = resolveInputSelectionsFromConfig(rawInput, configurableInputSlots);
      setInputSelections(nextInputs);

      const configParams = Array.isArray(config?.params) ? config.params.map(Number) : [];
      const nextParams = paramDefinitions.map((param, index) => {
        const raw = configParams[index];
        if (raw === undefined || Number.isNaN(Number(raw))) {
          if (param.type === 'enum') {
            const fallback = param.defaultValue ?? param.enumOptions?.[0]?.value ?? 0;
            return String(fallback);
          }
          const fallback = param.defaultValue ?? 0;
          return String(fallback);
        }
        return String(raw);
      });
      setParamValues(nextParams);
      return;
    }

    setTimeframe(normalizedFixedTimeframe || 'm1');
    setCalcMode('OnBarClose');
    setOffsetMin(OFFSET_DEFAULT.min);
    setOffsetMax(OFFSET_DEFAULT.max);
    setOutputSelection(defaultOutput);
    setInputSelections(configurableInputSlots.map((slot) => slot.fallback));

    const defaults = paramDefinitions.map((param) => {
      if (param.type === 'enum') {
        const fallback = param.defaultValue ?? param.enumOptions?.[0]?.value ?? 0;
        return String(fallback);
      }
      const fallback = param.defaultValue ?? 0;
      return String(fallback);
    });
    setParamValues(defaults);
  }, [
    activeIndicator,
    configurableInputSlots,
    initialIndicator,
    isEditMode,
    normalizedFixedTimeframe,
    outputOptions,
    paramDefinitions,
  ]);

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

  const hasInvalidParams = paramDefinitions.some((_, index) => {
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
      (normalizedFixedTimeframe || timeframe).trim() &&
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
    const input = buildConfigInputValue(configurableInputSlots, inputSelections);

    const resolvedTimeframe = (normalizedFixedTimeframe || timeframe).trim() || 'm1';
    const config = {
      refType: 'Indicator',
      indicator: activeIndicator.code,
      timeframe: resolvedTimeframe,
      input,
      params,
      output: outputSelection.trim() || 'Value',
      offsetRange: [offsetMinValue, offsetMaxValue],
      calcMode,
    };
    const formatted = JSON.stringify(config, null, 2);
    const payload: GeneratedIndicatorPayload = {
      id: isEditMode && initialIndicator ? initialIndicator.id : `${activeIndicator.code}-${Date.now()}`,
      code: activeIndicator.code,
      name: getIndicatorName(activeIndicator),
      category: getIndicatorCategory(activeIndicator),
      outputs: activeIndicator.outputs || [],
      config: config as Record<string, unknown>,
      configText: formatted,
    };
    const validationMessage = validateIndicator?.(payload, isEditMode ? 'edit' : 'create');
    if (validationMessage) {
      error(validationMessage);
      return;
    }
    setGeneratedConfig(formatted);
    success(isEditMode ? '已更新指标' : '已生成指标');

    if (isEditMode) {
      onUpdated?.(payload);
    } else {
      onGenerated?.(payload);
    }
    if (autoCloseOnGenerate) {
      onClose();
    }
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
              placeholder="搜索指标名称 / 中文 / 代码（如 MA, RSI, CDL）"
              showShortcut={false}
              type="gray"
              className="indicator-generator__search"
            />
            <div className="indicator-generator__stats">
              {isLoading ? '加载中...' : `共 ${visibleIndicatorCount} 项`}
            </div>
          </div>

          <div className="indicator-generator__filter-row">
            <button
              type="button"
              className={`indicator-generator__filter-button ${indicatorPaneFilter === 'all' ? 'is-active' : ''}`}
              onClick={() => setIndicatorPaneFilter('all')}
            >
              全部
            </button>
            <button
              type="button"
              className={`indicator-generator__filter-button ${indicatorPaneFilter === 'main' ? 'is-active' : ''}`}
              onClick={() => setIndicatorPaneFilter('main')}
            >
              主图
            </button>
            <button
              type="button"
              className={`indicator-generator__filter-button ${indicatorPaneFilter === 'sub' ? 'is-active' : ''}`}
              onClick={() => setIndicatorPaneFilter('sub')}
            >
              副图
            </button>
          </div>

          <div className="indicator-generator__filter-row indicator-generator__filter-row-groups ui-scrollable">
            <button
              type="button"
              className={`indicator-generator__filter-button ${indicatorGroupFilter === 'ALL' ? 'is-active' : ''}`}
              onClick={() => setIndicatorGroupFilter('ALL')}
            >
              全部分类
            </button>
            {indicatorGroupOptions.map((group) => (
              <button
                key={group}
                type="button"
                className={`indicator-generator__filter-button ${indicatorGroupFilter === group ? 'is-active' : ''}`}
                onClick={() => setIndicatorGroupFilter(group)}
              >
                {group}
              </button>
            ))}
          </div>

          {loadError && (
            <div className="indicator-generator__empty">配置加载失败：{loadError}</div>
          )}

          {!loadError && (
            <div className="indicator-generator__list ui-scrollable">
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
                            {getIndicatorDisplayName(indicator)}
                          </div>
                          <div className="indicator-generator__card-meta">
                            {getIndicatorPane(indicator) === 'main' ? '主图' : '副图'}
                          </div>
                          {indicator.outputs && indicator.outputs.length > 0 && (
                            <div className="indicator-generator__card-meta">
                              输出: {indicator.outputs.map((output) => output.key).join(', ')}
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
        cancelText={isEditMode ? '取消' : '返回'}
        confirmText={isEditMode ? '确认修改' : '生成指标'}
        onCancel={isEditMode ? onClose : handleBackToSelect}
        onConfirm={handleGenerate}
        className="indicator-generator__dialog indicator-generator__dialog--config"
      >
        {activeIndicator ? (
          <div className="indicator-generator__config">
            <div className="indicator-generator__headline">
              <div className="indicator-generator__headline-title">
                {activeIndicator.code} · {getIndicatorName(activeIndicator)}
              </div>
              <div className="indicator-generator__headline-category">
                分类：{getIndicatorCategory(activeIndicator)}
              </div>
            </div>

            <div className="indicator-generator__form">
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
                          <div className="indicator-generator__option-list">
                            {(param.enumOptions || []).map((option) => {
                              const value = String(option.value);
                              const label = option.name ? `${option.label} - ${option.name}` : option.label;
                              return (
                                <button
                                  key={`${param.id}-${value}`}
                                  type="button"
                                  className={`indicator-generator__option-button ${
                                    paramValues[index] === value ? 'is-active' : ''
                                  }`}
                                  onClick={() => handleParamChange(index, value)}
                                  autoFocus={preferParamFocus && index === 0 && option.value === (param.enumOptions?.[0]?.value ?? option.value)}
                                >
                                  {label}
                                </button>
                              );
                            })}
                          </div>
                        ) : (
                          <input
                            className="indicator-generator__input indicator-generator__input--small"
                            value={paramValues[index] ?? ''}
                            onChange={(event) => handleParamChange(index, event.target.value)}
                            type="number"
                            placeholder="请输入"
                            autoFocus={preferParamFocus && index === 0}
                          />
                        )}
                      </div>
                    ))}
                  </div>
                )}
              </div>

              {configurableInputSlots.length > 0 && (
                <div className="indicator-generator__field">
                  <label className="indicator-generator__label">
                    输入来源 {configurableInputSlots.length > 1 ? '(多输入)' : ''}
                  </label>
                  <div className="indicator-generator__input-list">
                    {configurableInputSlots.map((slot, index) => {
                      const selection = inputSelections[index] || slot.fallback;
                      return (
                        <div key={slot.id} className="indicator-generator__input-row">
                          <div className="indicator-generator__input-label">
                            {slot.label}
                          </div>
                          <div className="indicator-generator__option-list">
                            {INPUT_OPTIONS.map((option) => (
                              <button
                                key={`${option.value}-${index}`}
                                type="button"
                                className={`indicator-generator__option-button ${
                                  selection === option.value ? 'is-active' : ''
                                }`}
                                onClick={() => handleInputChange(index, option.value)}
                              >
                                {option.label}
                              </button>
                            ))}
                          </div>
                        </div>
                      );
                    })}
                  </div>
                </div>
              )}

              {outputOptions.length > 0 && (
                <div className="indicator-generator__field">
                  <label className="indicator-generator__label">输出项</label>
                  <div className="indicator-generator__option-list">
                    {outputOptions.map((output, index) => {
                      const outputKey = (output.key || '').trim() || `Output${index + 1}`;
                      const outputLabel = output.hint ? `${output.hint} (${outputKey})` : outputKey;
                      return (
                        <button
                          key={`${outputKey}-${index}`}
                          type="button"
                          className={`indicator-generator__option-button ${
                            outputSelection === outputKey ? 'is-active' : ''
                          }`}
                          onClick={() => setOutputSelection(outputKey)}
                        >
                          {outputLabel}
                        </button>
                      );
                    })}
                    {!outputOptions.some(
                      (output) => (output.key || '').trim() === outputSelection.trim()
                    ) &&
                      outputSelection.trim() && (
                        <button
                          type="button"
                          className="indicator-generator__option-button is-active"
                          onClick={() => setOutputSelection(outputSelection)}
                        >
                          {outputSelection}
                        </button>
                      )}
                  </div>
                </div>
              )}

              <div className="indicator-generator__field">
                <label className="indicator-generator__label">计算模式</label>
                <div className="indicator-generator__option-list">
                  {CALC_MODE_OPTIONS.map((option) => (
                    <button
                      key={option.value}
                      type="button"
                      className={`indicator-generator__option-button ${
                        calcMode === option.value ? 'is-active' : ''
                      }`}
                      onClick={() => setCalcMode(option.value)}
                    >
                      {option.label}
                    </button>
                  ))}
                </div>
              </div>

              {!hideTimeframeSelector && (
                <div className="indicator-generator__field">
                  <label className="indicator-generator__label">时间周期</label>
                  <div className="indicator-generator__option-list">
                    {TIMEFRAME_OPTIONS.map((option) => (
                      <button
                        key={option.value}
                        type="button"
                        className={`indicator-generator__option-button ${
                          timeframe === option.value ? 'is-active' : ''
                        }`}
                        onClick={() => setTimeframe(option.value)}
                      >
                        {option.label}
                      </button>
                    ))}
                    {!TIMEFRAME_OPTIONS.some((option) => option.value === timeframe) && timeframe && (
                      <button
                        type="button"
                        className="indicator-generator__option-button is-active"
                        onClick={() => setTimeframe(timeframe)}
                      >
                        {timeframe}
                      </button>
                    )}
                  </div>
                </div>
              )}

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
        ) : (
          <div className="indicator-generator__empty">
            {isLoading ? '正在加载指标配置...' : '未找到指标配置'}
          </div>
        )}
      </Dialog>
    </>
  );
};

export default IndicatorGeneratorSelector;
