
import React, { useMemo, useState, useRef, useEffect } from 'react';

import IndicatorGeneratorSelector, { type GeneratedIndicatorPayload } from './IndicatorGeneratorSelector';
import ConditionEditorDialog from './ConditionEditorDialog';
import StrategyConfigDialog from './StrategyConfigDialog';
import StrategyEditorShell from './StrategyEditorShell';
import type {
  ActionSetConfig,
  ConditionContainer,
  ConditionEditTarget,
  ConditionGroup,
  ConditionGroupConfig,
  ConditionGroupSetConfig,
  ConditionItem,
  ConditionSummarySection,
  IndicatorOutputGroup,
  MethodOption,
  StrategyConfig,
  StrategyLogicBranchConfig,
  StrategyMethodConfig,
  StrategyTradeConfig,
  StrategyValueRef,
  TimeframeOption,
  TradeOption,
  ValueOption,
} from './StrategyModule.types';
import { useNotification } from './ui';

export type StrategyEditorSubmitPayload = {
  name: string;
  description: string;
  configJson: StrategyConfig;
};

type StrategyEditorFlowProps = {
  onSubmit: (payload: StrategyEditorSubmitPayload) => Promise<void>;
  onClose?: () => void;
  submitLabel?: string;
  successMessage?: string;
  errorMessage?: string;
  initialName?: string;
  initialDescription?: string;
  initialTradeConfig?: StrategyTradeConfig;
  initialConfig?: StrategyConfig;
  disableMetaFields?: boolean;
};

const createDefaultConditionContainers = (): ConditionContainer[] => ([
  { id: 'open-long', title: '开多条件', enabled: true, required: false, groups: [] },
  { id: 'open-short', title: '开空条件', enabled: true, required: false, groups: [] },
  { id: 'close-long', title: '平多条件', enabled: true, required: false, groups: [] },
  { id: 'close-short', title: '平空条件', enabled: true, required: false, groups: [] },
]);

const createDefaultTradeConfig = (): StrategyTradeConfig => ({
  exchange: 'bitget',
  symbol: 'BTC/USDT',
  timeframeSec: 60,
  positionMode: 'LongShort',
  openConflictPolicy: 'GiveUp',
  sizing: {
    orderQty: 0.001,
    maxPositionQty: 10,
    leverage: 100,
  },
  risk: {
    takeProfitPct: 2.0,
    stopLossPct: 1.0,
    trailing: {
      enabled: false,
      activationProfitPct: 1.0,
      closeOnDrawdownPct: 0.2,
    },
  },
});

const mergeTradeConfig = (initial?: StrategyTradeConfig): StrategyTradeConfig => {
  const defaults = createDefaultTradeConfig();
  if (!initial) {
    return defaults;
  }

  return {
    ...defaults,
    ...initial,
    sizing: {
      ...defaults.sizing,
      ...initial.sizing,
    },
    risk: {
      ...defaults.risk,
      ...initial.risk,
      trailing: {
        ...defaults.risk.trailing,
        ...initial.risk?.trailing,
      },
    },
  };
};

// 从 StrategyValueRef 创建 GeneratedIndicatorPayload
const createIndicatorFromRef = (ref: StrategyValueRef, id: string): GeneratedIndicatorPayload => {
  const params = ref.params || [];
  const config = {
    indicator: ref.indicator,
    timeframe: ref.timeframe,
    input: ref.input,
    params,
    output: ref.output,
    offsetRange: ref.offsetRange || [0, 0],
    calcMode: ref.calcMode || 'OnBarClose',
  };
  return {
    id,
    code: ref.indicator || '',
    name: ref.indicator || '',
    category: 'Loaded',
    outputs: [{ key: ref.output || 'Value' }],
    config,
    configText: JSON.stringify(config),
  };
};

// 从配置中提取所有指标引用
const extractIndicatorsFromConfig = (config: StrategyConfig): GeneratedIndicatorPayload[] => {
  const indicatorMap = new Map<string, GeneratedIndicatorPayload>();
  let indicatorCounter = 0;

  const addIndicator = (ref: StrategyValueRef | string | undefined) => {
    if (!ref || typeof ref === 'string') {
      return;
    }
    if ((ref.refType || '').toLowerCase() !== 'indicator') {
      return;
    }
    const key = `${ref.indicator}|${ref.timeframe}|${ref.input}|${ref.output}|${(ref.params || []).join(',')}`;
    if (!indicatorMap.has(key)) {
      indicatorCounter++;
      indicatorMap.set(key, createIndicatorFromRef(ref, `loaded-${indicatorCounter}`));
    }
  };

  // 遍历所有条件
  const traverseBranch = (branch: StrategyLogicBranchConfig) => {
    branch.containers?.forEach((container) => {
      container.checks?.groups?.forEach((group) => {
        group.conditions?.forEach((condition) => {
          if (condition.args) {
            condition.args.forEach((arg) => {
              if (typeof arg === 'object' && 'refType' in arg) {
                addIndicator(arg as StrategyValueRef);
              }
            });
          }
        });
      });
    });
  };

  if (config.logic) {
    traverseBranch(config.logic.entry.long);
    traverseBranch(config.logic.entry.short);
    traverseBranch(config.logic.exit.long);
    traverseBranch(config.logic.exit.short);
  }

  return Array.from(indicatorMap.values());
};

// 从配置中解析条件容器
const parseConditionContainersFromConfig = (config: StrategyConfig): ConditionContainer[] => {
  const containers: ConditionContainer[] = [
    { id: 'open-long', title: '开多条件', enabled: false, required: false, groups: [] },
    { id: 'open-short', title: '开空条件', enabled: false, required: false, groups: [] },
    { id: 'close-long', title: '平多条件', enabled: false, required: false, groups: [] },
    { id: 'close-short', title: '平空条件', enabled: false, required: false, groups: [] },
  ];

  const parseBranch = (branch: StrategyLogicBranchConfig, containerId: string) => {
    const container = containers.find((c) => c.id === containerId);
    if (!container || !branch.containers || branch.containers.length === 0) {
      return;
    }

    container.enabled = branch.enabled || false;
    const checks = branch.containers[0]?.checks;
    if (!checks || !checks.groups) {
      return;
    }

    let groupCounter = 0;
    container.groups = checks.groups.map((groupConfig, index) => {
      groupCounter++;
      const groupId = `${containerId}-group-${groupCounter}`;
      let conditionCounter = 0;

      const conditions: ConditionItem[] = (groupConfig.conditions || []).map((conditionConfig) => {
        conditionCounter++;
        const conditionId = `${groupId}-condition-${conditionCounter}`;
        const args = conditionConfig.args || [];
        const leftArg = args[0];
        const rightArg = args[1];

        let leftValueId = '';
        let rightValueType: 'field' | 'number' = 'number';
        let rightValueId: string | undefined;
        let rightNumber: string | undefined;

        if (leftArg && typeof leftArg === 'object' && 'refType' in leftArg) {
          const ref = leftArg as StrategyValueRef;
          const paramsKey = (ref.params || []).join(',');
          leftValueId = `${ref.indicator}|${ref.timeframe}|${ref.input}|${ref.output}|${paramsKey}`;
        }

        if (rightArg) {
          if (typeof rightArg === 'string') {
            rightValueType = 'number';
            rightNumber = rightArg;
          } else if (typeof rightArg === 'object' && 'refType' in rightArg) {
            rightValueType = 'field';
            const ref = rightArg as StrategyValueRef;
            const paramsKey = (ref.params || []).join(',');
            rightValueId = `${ref.indicator}|${ref.timeframe}|${ref.input}|${ref.output}|${paramsKey}`;
          }
        }

        return {
          id: conditionId,
          enabled: conditionConfig.enabled !== false,
          required: conditionConfig.required || false,
          method: conditionConfig.method || 'GreaterThanOrEqual',
          leftValueId,
          rightValueType,
          rightValueId,
          rightNumber,
        };
      });

      return {
        id: groupId,
        name: `条件组 ${index + 1}`,
        enabled: groupConfig.enabled !== false,
        required: false,
        conditions,
      };
    });
  };

  if (config.logic) {
    parseBranch(config.logic.entry.long, 'open-long');
    parseBranch(config.logic.entry.short, 'open-short');
    parseBranch(config.logic.exit.long, 'close-long');
    parseBranch(config.logic.exit.short, 'close-short');
  }

  return containers;
};

const StrategyEditorFlow: React.FC<StrategyEditorFlowProps> = ({
  onSubmit,
  onClose,
  submitLabel = '创建策略',
  successMessage = '策略创建成功',
  errorMessage = '操作失败，请稍后重试',
  initialName,
  initialDescription,
  initialTradeConfig,
  initialConfig,
  disableMetaFields = false,
}) => {
  const [isIndicatorGeneratorOpen, setIsIndicatorGeneratorOpen] = useState(false);
  
  // 从配置中加载初始数据
  const loadedIndicators = useMemo(() => {
    if (!initialConfig) {
      return [];
    }
    return extractIndicatorsFromConfig(initialConfig);
  }, [initialConfig]);

  const loadedContainers = useMemo(() => {
    if (!initialConfig) {
      return createDefaultConditionContainers();
    }
    return parseConditionContainersFromConfig(initialConfig);
  }, [initialConfig]);

  const [selectedIndicators, setSelectedIndicators] = useState<GeneratedIndicatorPayload[]>(loadedIndicators);
  const [isConfigReviewOpen, setIsConfigReviewOpen] = useState(false);
  const [isLogicPreviewVisible, setIsLogicPreviewVisible] = useState(false);
  const [configStep, setConfigStep] = useState(0); // 0: 基本信息, 1: 详细配置
  const [strategyName, setStrategyName] = useState(initialName ?? '');
  const [strategyDescription, setStrategyDescription] = useState(initialDescription ?? '');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const { error, success } = useNotification();

  // 滚动条 refs
  const summaryListRef = useRef<HTMLDivElement>(null);
  const summaryTrackRef = useRef<HTMLDivElement>(null);
  const summaryThumbRef = useRef<HTMLDivElement>(null);
  const codeListRef = useRef<HTMLPreElement>(null);
  const codeTrackRef = useRef<HTMLDivElement>(null);
  const codeThumbRef = useRef<HTMLDivElement>(null);
  const tradeConfigRef = useRef<HTMLDivElement>(null);
  const [conditionContainers, setConditionContainers] = useState<ConditionContainer[]>(loadedContainers);
  const [tradeConfig, setTradeConfig] = useState<StrategyTradeConfig>(() => 
    mergeTradeConfig(initialConfig?.trade || initialTradeConfig)
  );
  const [isConditionModalOpen, setIsConditionModalOpen] = useState(false);
  const [conditionDraft, setConditionDraft] = useState<ConditionItem | null>(null);
  const [conditionEditTarget, setConditionEditTarget] = useState<ConditionEditTarget | null>(null);
  const [conditionError, setConditionError] = useState('');
  const MAX_GROUPS_PER_CONTAINER = 3;
  const MAX_CONDITIONS_PER_GROUP = 6;

  const exchangeOptions: TradeOption[] = [
    { value: 'binance', label: '币安' },
    { value: 'okx', label: 'OKX' },
    { value: 'bitget', label: 'Bitget' },
  ];

  const symbolOptions: TradeOption[] = [
    { value: 'BTC/USDT', label: 'BTC' },
    { value: 'ETH/USDT', label: 'ETH' },
    { value: 'XRP/USDT', label: 'XRP' },
    { value: 'SOL/USDT', label: 'SOL' },
    { value: 'DOGE/USDT', label: 'DOGE' },
    { value: 'BNB/USDT', label: 'BNB' },
  ];

  const timeframeOptions: TimeframeOption[] = [
    { value: 60, label: '1m' },
    { value: 180, label: '3m' },
    { value: 300, label: '5m' },
    { value: 900, label: '15m' },
    { value: 1800, label: '30m' },
    { value: 3600, label: '1h' },
    { value: 7200, label: '2h' },
    { value: 14400, label: '4h' },
    { value: 21600, label: '6h' },
    { value: 28800, label: '8h' },
    { value: 43200, label: '12h' },
    { value: 86400, label: '1d' },
    { value: 259200, label: '3d' },
    { value: 604800, label: '1w' },
    { value: 2592000, label: '1mo' },
  ];

  const timeframeSecondsMap = useMemo(() => {
    return new Map(timeframeOptions.map((option) => [option.label, option.value]));
  }, [timeframeOptions]);

  const leverageOptions = [10, 20, 50, 100];

  const openConfigReview = () => {
    setIsConfigReviewOpen(true);
    setIsLogicPreviewVisible(false);
    setConfigStep(0);
    if (!strategyName.trim()) {
      const now = new Date();
      const month = now.getMonth() + 1;
      const day = now.getDate();
      const hour = now.getHours();
      const minute = now.getMinutes();
      const defaultStrategyName = `${month}月${day}日${hour}时${minute}分创建的策略`;
      setStrategyName(defaultStrategyName);
    }
    if (selectedIndicators.length > 0 && tradeConfig.timeframeSec === 60) {
      const derived = resolveTradeTimeframeSec();
      if (derived) {
        setTradeConfig((prev) => ({ ...prev, timeframeSec: derived }));
      }
    }
  };

  const closeConfigReview = () => {
    setIsConfigReviewOpen(false);
    setConfigStep(0);
  };

  const handleNextStep = () => {
    setConfigStep(1);
  };

  const handlePrevStep = () => {
    setConfigStep(0);
  };

  const toggleLogicPreview = () => {
    setIsLogicPreviewVisible((prev) => !prev);
  };

  // 更新条件组滚动条
  useEffect(() => {
    const list = summaryListRef.current;
    const track = summaryTrackRef.current;
    const thumb = summaryThumbRef.current;
    if (!list || !track || !thumb) {
      return;
    }

    const updateScroll = () => {
      const { scrollHeight, clientHeight, scrollTop } = list;
      const trackHeight = track.clientHeight;
      const isScrollable = scrollHeight > clientHeight + 1;
      track.style.opacity = isScrollable ? '1' : '0';

      if (!isScrollable) {
        thumb.style.height = `${trackHeight}px`;
        thumb.style.transform = 'translateY(0px)';
        return;
      }

      const thumbHeight = Math.max(24, (clientHeight / scrollHeight) * trackHeight);
      const maxThumbTop = trackHeight - thumbHeight;
      const thumbTop =
        scrollHeight === clientHeight
          ? 0
          : (scrollTop / (scrollHeight - clientHeight)) * maxThumbTop;

      thumb.style.height = `${thumbHeight}px`;
      thumb.style.transform = `translateY(${thumbTop}px)`;
    };

    updateScroll();
    list.addEventListener('scroll', updateScroll);

    const resizeObserver = new ResizeObserver(updateScroll);
    resizeObserver.observe(list);

    return () => {
      list.removeEventListener('scroll', updateScroll);
      resizeObserver.disconnect();
    };
  }, [isConfigReviewOpen, isLogicPreviewVisible]);

  // 更新代码区域滚动条
  useEffect(() => {
    const list = codeListRef.current;
    const track = codeTrackRef.current;
    const thumb = codeThumbRef.current;
    if (!list || !track || !thumb) {
      return;
    }

    const updateScroll = () => {
      const { scrollHeight, clientHeight, scrollTop } = list;
      const trackHeight = track.clientHeight;
      const isScrollable = scrollHeight > clientHeight + 1;
      track.style.opacity = isScrollable ? '1' : '0';

      if (!isScrollable) {
        thumb.style.height = `${trackHeight}px`;
        thumb.style.transform = 'translateY(0px)';
        return;
      }

      const thumbHeight = Math.max(24, (clientHeight / scrollHeight) * trackHeight);
      const maxThumbTop = trackHeight - thumbHeight;
      const thumbTop =
        scrollHeight === clientHeight
          ? 0
          : (scrollTop / (scrollHeight - clientHeight)) * maxThumbTop;

      thumb.style.height = `${thumbHeight}px`;
      thumb.style.transform = `translateY(${thumbTop}px)`;
    };

    updateScroll();
    list.addEventListener('scroll', updateScroll);

    const resizeObserver = new ResizeObserver(updateScroll);
    resizeObserver.observe(list);

    return () => {
      list.removeEventListener('scroll', updateScroll);
      resizeObserver.disconnect();
    };
  }, [isConfigReviewOpen, isLogicPreviewVisible]);

  // 交易规则区域滚轮切换步骤
  useEffect(() => {
    const tradeElement = tradeConfigRef.current;
    if (!tradeElement || !isConfigReviewOpen) {
      return;
    }

    let scrollTimeout: NodeJS.Timeout | null = null;
    let isScrolling = false;

    const handleWheel = (e: WheelEvent) => {
      if (isScrolling) {
        return;
      }

      const { scrollTop, scrollHeight, clientHeight } = tradeElement;
      const isAtBottom = scrollTop + clientHeight >= scrollHeight - 10;
      const isAtTop = scrollTop <= 10;

      if (e.deltaY > 0 && isAtBottom && configStep === 0) {
        e.preventDefault();
        isScrolling = true;
        setConfigStep(1);
        if (scrollTimeout) clearTimeout(scrollTimeout);
        scrollTimeout = setTimeout(() => {
          isScrolling = false;
        }, 500);
      } else if (e.deltaY < 0 && isAtTop && configStep === 1) {
        e.preventDefault();
        isScrolling = true;
        setConfigStep(0);
        if (scrollTimeout) clearTimeout(scrollTimeout);
        scrollTimeout = setTimeout(() => {
          isScrolling = false;
        }, 500);
      }
    };

    tradeElement.addEventListener('wheel', handleWheel, { passive: false });

    return () => {
      tradeElement.removeEventListener('wheel', handleWheel);
      if (scrollTimeout) clearTimeout(scrollTimeout);
    };
  }, [isConfigReviewOpen, configStep]);

  const generateId = () => `${Date.now().toString(36)}_${Math.random().toString(36).slice(2, 8)}`;

  const promoteToTop = <T,>(items: T[], index: number) => {
    if (index <= 0 || index >= items.length) {
      return items;
    }
    const selected = items[index];
    const rest = items.filter((_, idx) => idx !== index);
    return [selected, ...rest];
  };

  const handleAddIndicator = (indicator: GeneratedIndicatorPayload) => {
    setSelectedIndicators((prev) => [indicator, ...prev]);
  };

  const parseTimeframeSeconds = (raw: string | undefined) => {
    if (!raw) {
      return null;
    }
    const value = raw.trim().toLowerCase().replace(/\s+/g, '');
    if (!value) {
      return null;
    }
    const direct = timeframeSecondsMap.get(value);
    if (direct) {
      return direct;
    }
    const match = value.match(/^([a-z]+)(\d+)$/);
    if (!match) {
      return null;
    }
    const swapped = `${match[2]}${match[1]}`;
    return timeframeSecondsMap.get(swapped) ?? null;
  };

  const resolveTradeTimeframeSec = () => {
    for (const indicator of selectedIndicators) {
      const config = indicator.config as { timeframe?: string };
      const seconds = parseTimeframeSeconds(config.timeframe);
      if (seconds) {
        return seconds;
      }
    }
    return 60;
  };

  const updateTradeSizing = (key: keyof StrategyTradeConfig['sizing'], value: number) => {
    setTradeConfig((prev) => ({
      ...prev,
      sizing: {
        ...prev.sizing,
        [key]: value,
      },
    }));
  };

  const updateTradeRisk = (key: keyof StrategyTradeConfig['risk'], value: number) => {
    setTradeConfig((prev) => ({
      ...prev,
      risk: {
        ...prev.risk,
        [key]: value,
      },
    }));
  };

  const updateTrailingRisk = (
    key: keyof StrategyTradeConfig['risk']['trailing'],
    value: number | boolean,
  ) => {
    setTradeConfig((prev) => ({
      ...prev,
      risk: {
        ...prev.risk,
        trailing: {
          ...prev.risk.trailing,
          [key]: value,
        },
      },
    }));
  };

  const handleStrategyNameChange = (value: string) => {
    setStrategyName(value);
  };

  const handleStrategyDescriptionChange = (value: string) => {
    setStrategyDescription(value);
  };

  const handleExchangeChange = (exchange: string) => {
    setTradeConfig((prev) => ({
      ...prev,
      exchange,
    }));
  };

  const handleSymbolChange = (symbol: string) => {
    setTradeConfig((prev) => ({
      ...prev,
      symbol,
    }));
  };

  const handleTimeframeChange = (timeframeSec: number) => {
    setTradeConfig((prev) => ({
      ...prev,
      timeframeSec,
    }));
  };

  const formatIndicatorName = (indicator: GeneratedIndicatorPayload) => {
    const config = indicator.config as {
      timeframe?: string;
      params?: number[];
    };
    const timeframe = config.timeframe || '';
    const params = Array.isArray(config.params) && config.params.length > 0
      ? config.params.join(',')
      : '';
    const periodAndParams = params ? `${timeframe} ${params}` : timeframe;
    return periodAndParams ? `${indicator.code} ${periodAndParams}` : indicator.code;
  };

  const formatIndicatorMeta = (indicator: GeneratedIndicatorPayload) => {
    const config = indicator.config as {
      input?: string;
    };
    const input = config.input || '-';
    const outputs = indicator.outputs && indicator.outputs.length > 0
      ? indicator.outputs.map((output) => output.key).join(', ')
      : '-';
    return `输入 ${input} · 输出 ${outputs}`;
  };

  const methodOptions: MethodOption[] = [
    { value: 'GreaterThanOrEqual', label: '大于等于 (>=)' },
    { value: 'LessThan', label: '小于 (<)' },
    { value: 'LessThanOrEqual', label: '小于等于 (<=)' },
    { value: 'Equal', label: '等于 (=)' },
    { value: 'CrossOver', label: '上穿 (CrossOver)' },
  ];

  // 创建指标引用到指标值 ID 的映射
  const indicatorRefToValueIdMap = useMemo(() => {
    const map = new Map<string, string>();
    selectedIndicators.forEach((indicator) => {
      const config = indicator.config as {
        indicator?: string;
        timeframe?: string;
        input?: string;
        params?: number[];
        output?: string;
        offsetRange?: number[];
        calcMode?: string;
      };
      const params = Array.isArray(config.params) ? config.params.map(Number) : [];
      const indicatorCode = config.indicator || indicator.code;
      const outputs =
        indicator.outputs && indicator.outputs.length > 0
          ? indicator.outputs
          : [{ key: config.output || 'Value', hint: config.output || 'Value' }];

      outputs.forEach((output) => {
        const refKey = `${indicatorCode}|${config.timeframe || ''}|${config.input || ''}|${output.key}|${params.join(',')}`;
        const valueId = `${indicator.id}:${output.key}`;
        map.set(refKey, valueId);
      });
    });
    return map;
  }, [selectedIndicators]);

  // 当指标加载后，更新条件容器中的 ID 映射
  useEffect(() => {
    if (selectedIndicators.length === 0 || !initialConfig) {
      return;
    }

    setConditionContainers((prevContainers) => {
      return prevContainers.map((container) => {
        const updatedGroups = container.groups.map((group) => {
          const updatedConditions = group.conditions.map((condition) => {
            let updatedLeftValueId = condition.leftValueId;
            let updatedRightValueId = condition.rightValueId;

            // 映射左侧值 ID
            if (condition.leftValueId && indicatorRefToValueIdMap.has(condition.leftValueId)) {
              updatedLeftValueId = indicatorRefToValueIdMap.get(condition.leftValueId) || condition.leftValueId;
            }

            // 映射右侧值 ID
            if (condition.rightValueType === 'field' && condition.rightValueId && indicatorRefToValueIdMap.has(condition.rightValueId)) {
              updatedRightValueId = indicatorRefToValueIdMap.get(condition.rightValueId) || condition.rightValueId;
            }

            return {
              ...condition,
              leftValueId: updatedLeftValueId,
              rightValueId: updatedRightValueId,
            };
          });

          return {
            ...group,
            conditions: updatedConditions,
          };
        });

        return {
          ...container,
          groups: updatedGroups,
        };
      });
    });
  }, [selectedIndicators, indicatorRefToValueIdMap, initialConfig]);

  const indicatorOutputGroups = useMemo<IndicatorOutputGroup[]>(() => {
    return selectedIndicators.map((indicator) => {
      const config = indicator.config as {
        indicator?: string;
        timeframe?: string;
        input?: string;
        params?: number[];
        output?: string;
        offsetRange?: number[];
        calcMode?: string;
      };
      const params = Array.isArray(config.params) ? config.params.map(Number) : [];
      const offsetRange = Array.isArray(config.offsetRange) ? config.offsetRange.map(Number) : [0, 0];
      const timeframe = config.timeframe || '-';
      const input = config.input || '';
      const paramLabel = params.length > 0 ? params.join(',') : '默认参数';
      const indicatorCode = config.indicator || indicator.code;
      const groupLabel = [indicatorCode, timeframe, paramLabel, input].filter(Boolean).join(' ');
      const outputs =
        indicator.outputs && indicator.outputs.length > 0
          ? indicator.outputs
          : [{ key: config.output || 'Value', hint: config.output || 'Value' }];

      const options = outputs.map((output) => {
        const outputLabel = output.hint ? `${output.hint} (${output.key})` : output.key;
        const fullLabel = `${groupLabel} - ${outputLabel}`;
        return {
          id: `${indicator.id}:${output.key}`,
          label: outputLabel,
          fullLabel,
          ref: {
            refType: 'Indicator',
            indicator: indicatorCode,
            timeframe: config.timeframe || '',
            input: config.input || '',
            params,
            output: output.key,
            offsetRange,
            calcMode: config.calcMode || 'OnBarClose',
          },
        };
      });

      return {
        id: indicator.id,
        label: groupLabel,
        options,
      };
    });
  }, [selectedIndicators]);

  const indicatorOutputOptions = useMemo<ValueOption[]>(() => {
    return indicatorOutputGroups.flatMap((group) => group.options);
  }, [indicatorOutputGroups]);

  const indicatorValueMap = useMemo(() => {
    return new Map(indicatorOutputOptions.map((option) => [option.id, option]));
  }, [indicatorOutputOptions]);

  const outputHintMap = useMemo(() => {
    const map = new Map<string, string>();
    selectedIndicators.forEach((indicator) => {
      (indicator.outputs || []).forEach((output) => {
        map.set(`${indicator.code}:${output.key}`, output.hint || output.key);
      });
    });
    return map;
  }, [selectedIndicators]);

  const formatTimeframeLabel = (raw?: string) => {
    if (!raw) {
      return '';
    }
    const value = raw.trim().toLowerCase().replace(/\s+/g, '');
    if (!value) {
      return '';
    }
    if (timeframeSecondsMap.has(value)) {
      return value;
    }
    const match = value.match(/^([a-z]+)(\d+)$/);
    if (!match) {
      return value;
    }
    const swapped = `${match[2]}${match[1]}`;
    return swapped;
  };

  const formatValueRefLabel = (ref?: StrategyValueRef | null) => {
    if (!ref) {
      return '未配置';
    }
    const refType = (ref.refType || '').toLowerCase();
    if (refType === 'const') {
      return ref.input?.trim() || '0';
    }
    if (refType === 'field') {
      const timeframe = formatTimeframeLabel(ref.timeframe);
      const inputLabel = ref.input || 'Field';
      return timeframe ? `${inputLabel} ${timeframe}` : inputLabel;
    }
    const timeframe = formatTimeframeLabel(ref.timeframe);
    const paramsLabel = ref.params && ref.params.length > 0 ? ref.params.join(',') : '默认参数';
    const outputKey = ref.output || 'Value';
    const outputLabel = outputHintMap.get(`${ref.indicator}:${outputKey}`) || outputKey;
    const indicatorLabel = ref.indicator || 'Indicator';
    const timeframeLabel = timeframe ? ` ${timeframe}` : '';
    return `${indicatorLabel}${timeframeLabel} (${paramsLabel}) ${outputLabel}`.trim();
  };

  const getMethodLabel = (method: string) => {
    const rawLabel = methodOptions.find((option) => option.value === method)?.label || method;
    return rawLabel.split(' ')[0];
  };

  const conditionSummarySections = useMemo<ConditionSummarySection[]>(() => {
    const sections = [
      { id: 'open-long', label: '开多' },
      { id: 'open-short', label: '开空' },
      { id: 'close-long', label: '平多' },
      { id: 'close-short', label: '平空' },
    ];

    return sections.map((section) => {
      const container = conditionContainers.find((item) => item.id === section.id);
      const groups = container?.groups ?? [];
      const groupSummaries = groups.map((group) => {
        const lines = group.conditions.map((condition) => {
          const leftLabel =
            indicatorValueMap.get(condition.leftValueId)?.label ||
            indicatorValueMap.get(condition.leftValueId)?.fullLabel ||
            '未选择字段';
          const methodLabel = getMethodLabel(condition.method);
          const rightLabel =
            condition.rightValueType === 'number'
              ? condition.rightNumber || '未填写数值'
              : indicatorValueMap.get(condition.rightValueId || '')?.label ||
                indicatorValueMap.get(condition.rightValueId || '')?.fullLabel ||
                '未选择字段';
          return `${leftLabel} ${methodLabel} ${rightLabel}`;
        });
        return {
          title: `${group.name}${group.enabled ? '' : ' (未启用)'}`,
          conditions: lines,
        };
      });
      return {
        title: `${section.label} 共${groups.length}个条件组${container?.enabled ? '' : ' (未启用)'}`,
        groups: groupSummaries.length > 0 ? groupSummaries : [{ title: '暂无条件组', conditions: [] }],
      };
    });
  }, [conditionContainers, indicatorValueMap, methodOptions, outputHintMap]);

  const usedIndicatorOutputs = useMemo(() => {
    const map = new Map<string, string>();
    const addIndicator = (ref?: StrategyValueRef | null) => {
      if (!ref) {
        return;
      }
      if ((ref.refType || '').toLowerCase() !== 'indicator') {
        return;
      }
      const paramsKey = ref.params && ref.params.length > 0 ? ref.params.join(',') : 'default';
      const key = [ref.indicator, ref.timeframe, ref.input, ref.output, ref.calcMode, paramsKey].join('|');
      if (!map.has(key)) {
        map.set(key, formatValueRefLabel(ref));
      }
    };

    conditionContainers.forEach((container) => {
      container.groups.forEach((group) => {
        group.conditions.forEach((condition) => {
          addIndicator(indicatorValueMap.get(condition.leftValueId)?.ref);
          if (condition.rightValueType === 'field') {
            addIndicator(indicatorValueMap.get(condition.rightValueId || '')?.ref);
          }
        });
      });
    });

    return Array.from(map.values());
  }, [conditionContainers, indicatorValueMap, outputHintMap]);

  const buildConstantValueRef = (
    rawValue: string | undefined,
    fallback?: StrategyValueRef,
  ): StrategyValueRef => {
    return {
      refType: 'Const',
      indicator: '',
      timeframe: fallback?.timeframe || 'm1',
      input: (rawValue || '0').trim(),
      params: [],
      output: 'Value',
      offsetRange: [0, 0],
      calcMode: fallback?.calcMode || 'OnBarClose',
    };
  };

  const buildStrategyMethod = (condition: ConditionItem): StrategyMethodConfig | null => {
    const leftOption = indicatorValueMap.get(condition.leftValueId);
    if (!leftOption) {
      return null;
    }
    let rightRef: StrategyValueRef | null = null;
    if (condition.rightValueType === 'number') {
      rightRef = buildConstantValueRef(condition.rightNumber, leftOption.ref);
    } else {
      const rightOption = indicatorValueMap.get(condition.rightValueId || '');
      if (!rightOption) {
        return null;
      }
      rightRef = rightOption.ref;
    }
    return {
      enabled: condition.enabled,
      required: condition.required,
      method: condition.method,
      args: [leftOption.ref, rightRef],
    };
  };

  const buildConditionGroupConfig = (group: ConditionGroup): ConditionGroupConfig => {
    const conditions = group.conditions
      .map(buildStrategyMethod)
      .filter((item): item is StrategyMethodConfig => Boolean(item));
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

  const buildConditionGroupSetConfig = (container: ConditionContainer): ConditionGroupSetConfig => {
    const groups = container.groups.map(buildConditionGroupConfig);
    const requiredGroupsCount = container.groups.filter((group) => group.enabled && group.required).length;
    const minPassGroups = requiredGroupsCount > 0 ? requiredGroupsCount : 1;
    return {
      enabled: container.enabled,
      minPassGroups,
      groups,
    };
  };

  const buildActionSetConfig = (action: string): ActionSetConfig => ({
    enabled: true,
    minPassConditions: 1,
    conditions: [
      {
        enabled: true,
        required: false,
        method: 'MakeTrade',
        args: [action],
      },
    ],
  });

  const buildBranchConfig = (
    containerId: string,
    action: string,
  ): StrategyLogicBranchConfig => {
    const container = conditionContainers.find((item) => item.id === containerId);
    const checks = container
      ? buildConditionGroupSetConfig(container)
      : { enabled: false, minPassGroups: 1, groups: [] };
    return {
      enabled: container?.enabled ?? false,
      minPassConditionContainer: 1,
      containers: [{ checks }],
      onPass: buildActionSetConfig(action),
    };
  };

  const buildStrategyConfig = (): StrategyConfig => ({
    trade: {
      exchange: tradeConfig.exchange,
      symbol: tradeConfig.symbol,
      timeframeSec: tradeConfig.timeframeSec,
      positionMode: tradeConfig.positionMode,
      openConflictPolicy: tradeConfig.openConflictPolicy,
      sizing: { ...tradeConfig.sizing },
      risk: {
        takeProfitPct: tradeConfig.risk.takeProfitPct,
        stopLossPct: tradeConfig.risk.stopLossPct,
        trailing: { ...tradeConfig.risk.trailing },
      },
    },
    logic: {
      entry: {
        long: buildBranchConfig('open-long', 'Long'),
        short: buildBranchConfig('open-short', 'Short'),
      },
      exit: {
        long: buildBranchConfig('close-long', 'CloseLong'),
        short: buildBranchConfig('close-short', 'CloseShort'),
      },
    },
  });

  const configPreview = useMemo(() => {
    return buildStrategyConfig();
  }, [conditionContainers, tradeConfig, indicatorValueMap]);

  const logicPreview = useMemo(() => {
    return JSON.stringify(configPreview.logic, null, 2);
  }, [configPreview]);

  const handleConfirmGenerate = async () => {
    if (isSubmitting) {
      return;
    }
    const trimmedName = strategyName.trim();
    if (!trimmedName) {
      error('请输入策略名称');
      return;
    }
    setIsSubmitting(true);
    try {
      await onSubmit({
        name: trimmedName,
        description: strategyDescription.trim(),
        configJson: configPreview,
      });
      success(successMessage);
      window.dispatchEvent(new CustomEvent('strategy:changed'));
      closeConfigReview();
      onClose?.();
    } catch (err) {
      const message = err instanceof Error ? err.message : errorMessage;
      error(message || errorMessage);
    } finally {
      setIsSubmitting(false);
    }
  };

  const addConditionGroup = (containerId: string) => {
    setConditionContainers((prev) =>
      prev.map((container) => {
        if (container.id !== containerId) {
          return container;
        }
        if (container.groups.length >= MAX_GROUPS_PER_CONTAINER) {
          error(`${container.title}最多只能创建三个条件组`);
          return container;
        }
        const nextIndex = container.groups.length + 1;
        const newGroup: ConditionGroup = {
          id: generateId(),
          name: `条件组${nextIndex}`,
          enabled: true,
          required: false,
          conditions: [],
        };
        return { ...container, groups: [...container.groups, newGroup] };
      }),
    );
  };

  const toggleGroupFlag = (containerId: string, groupId: string, key: 'enabled' | 'required') => {
    setConditionContainers((prev) =>
      prev.map((container) => {
        if (container.id !== containerId) {
          return container;
        }
        const groupIndex = container.groups.findIndex((group) => group.id === groupId);
        if (groupIndex < 0) {
          return container;
        }
        const group = container.groups[groupIndex];
        const nextValue = !group[key];
        const nextGroup = { ...group, [key]: nextValue };
        const nextGroups = container.groups.map((item, idx) =>
          idx === groupIndex ? nextGroup : item,
        );
        const reordered = key === 'required' && nextValue ? promoteToTop(nextGroups, groupIndex) : nextGroups;
        return { ...container, groups: reordered };
      }),
    );
  };

  const toggleConditionFlag = (
    containerId: string,
    groupId: string,
    conditionId: string,
    key: 'enabled' | 'required',
  ) => {
    setConditionContainers((prev) =>
      prev.map((container) => {
        if (container.id !== containerId) {
          return container;
        }
        const groups = container.groups.map((group) => {
          if (group.id !== groupId) {
            return group;
          }
          const conditionIndex = group.conditions.findIndex((condition) => condition.id === conditionId);
          if (conditionIndex < 0) {
            return group;
          }
          const condition = group.conditions[conditionIndex];
          const nextValue = !condition[key];
          const nextCondition = { ...condition, [key]: nextValue };
          const nextConditions = group.conditions.map((item, idx) =>
            idx === conditionIndex ? nextCondition : item,
          );
          const reordered =
            key === 'required' && nextValue ? promoteToTop(nextConditions, conditionIndex) : nextConditions;
          return { ...group, conditions: reordered };
        });
        return { ...container, groups };
      }),
    );
  };

  const removeGroup = (containerId: string, groupId: string) => {
    setConditionContainers((prev) =>
      prev.map((container) => {
        if (container.id !== containerId) {
          return container;
        }
        return { ...container, groups: container.groups.filter((group) => group.id !== groupId) };
      }),
    );
  };

  const removeCondition = (containerId: string, groupId: string, conditionId: string) => {
    setConditionContainers((prev) =>
      prev.map((container) => {
        if (container.id !== containerId) {
          return container;
        }
        const groups = container.groups.map((group) => {
          if (group.id !== groupId) {
            return group;
          }
          return {
            ...group,
            conditions: group.conditions.filter((condition) => condition.id !== conditionId),
          };
        });
        return { ...container, groups };
      }),
    );
  };

  const openConditionModal = (containerId: string, groupId: string, conditionId?: string) => {
    setConditionError('');
    setConditionEditTarget({ containerId, groupId, conditionId });

    if (conditionId) {
      const container = conditionContainers.find((item) => item.id === containerId);
      const group = container?.groups.find((item) => item.id === groupId);
      const condition = group?.conditions.find((item) => item.id === conditionId);
      if (condition) {
        setConditionDraft({ ...condition });
        setIsConditionModalOpen(true);
        return;
      }
    }

    const groupLimitContainer = conditionContainers.find((container) => container.id === containerId);
    const groupLimit = groupLimitContainer?.groups.find((group) => group.id === groupId);
    if (!conditionId && groupLimit && groupLimit.conditions.length >= MAX_CONDITIONS_PER_GROUP) {
      error('最多只能创建6个条件判断');
      return;
    }

    const defaultLeft = indicatorOutputOptions[0]?.id || '';
    const defaultRight = indicatorOutputOptions[1]?.id || defaultLeft;
    setConditionDraft({
      id: generateId(),
      enabled: true,
      required: false,
      method: methodOptions[0].value,
      leftValueId: defaultLeft,
      rightValueType: 'field',
      rightValueId: defaultRight,
      rightNumber: '',
    });
    setIsConditionModalOpen(true);
  };

  const closeConditionModal = () => {
    setIsConditionModalOpen(false);
    setConditionDraft(null);
    setConditionEditTarget(null);
    setConditionError('');
  };

  const buildConditionPreview = (draft: ConditionItem | null) => {
    if (!draft) {
      return '请先配置字段与操作符';
    }
    const leftLabel =
      indicatorValueMap.get(draft.leftValueId)?.fullLabel ||
      indicatorValueMap.get(draft.leftValueId)?.label ||
      '未选择字段';
    const methodLabel = methodOptions.find((opt) => opt.value === draft.method)?.label || draft.method;
    const rightLabel =
      draft.rightValueType === 'number'
        ? draft.rightNumber || '未填写数值'
        : indicatorValueMap.get(draft.rightValueId || '')?.fullLabel ||
          indicatorValueMap.get(draft.rightValueId || '')?.label ||
          '未选择字段';
    return `${leftLabel} ${methodLabel} ${rightLabel}`;
  };

  const handleSaveCondition = () => {
    if (!conditionDraft || !conditionEditTarget) {
      return;
    }
    if (!conditionDraft.leftValueId) {
      setConditionError('请选择字段');
      return;
    }
    if (conditionDraft.rightValueType === 'field' && !conditionDraft.rightValueId) {
      setConditionError('请选择比较字段');
      return;
    }
    if (conditionDraft.rightValueType === 'number') {
      const numberValue = conditionDraft.rightNumber?.trim() || '';
      if (!numberValue || Number.isNaN(Number(numberValue))) {
        setConditionError('请输入有效数值');
        return;
      }
    }

    setConditionContainers((prev) =>
      prev.map((container) => {
        if (container.id !== conditionEditTarget.containerId) {
          return container;
        }
        const groups = container.groups.map((group) => {
          if (group.id !== conditionEditTarget.groupId) {
            return group;
          }
          const isEditing = Boolean(conditionEditTarget.conditionId);
          const nextConditions = isEditing
            ? group.conditions.map((condition) =>
                condition.id === conditionEditTarget.conditionId ? conditionDraft : condition,
              )
            : [...group.conditions, conditionDraft];
          const currentIndex = nextConditions.findIndex((condition) => condition.id === conditionDraft.id);
          const conditions =
            conditionDraft.required && currentIndex >= 0
              ? promoteToTop(nextConditions, currentIndex)
              : nextConditions;
          return { ...group, conditions };
        });
        return { ...container, groups };
      }),
    );
    closeConditionModal();
  };

  const renderToggle = (checked: boolean, onChange: () => void, label: string) => (
    <div className="condition-toggle-item">
      <span className="condition-toggle-label">{label}</span>
      <label className="condition-toggle">
        <input type="checkbox" checked={checked} onChange={onChange} />
        <span className="condition-toggle-slider" />
      </label>
    </div>
  );

  return (
    <>
      <StrategyEditorShell
        selectedIndicators={selectedIndicators}
        formatIndicatorName={formatIndicatorName}
        formatIndicatorMeta={formatIndicatorMeta}
        onOpenIndicatorGenerator={() => setIsIndicatorGeneratorOpen(true)}
        conditionContainers={conditionContainers}
        maxGroupsPerContainer={MAX_GROUPS_PER_CONTAINER}
        buildConditionPreview={buildConditionPreview}
        onAddConditionGroup={addConditionGroup}
        onToggleGroupFlag={toggleGroupFlag}
        onOpenConditionModal={openConditionModal}
        onRemoveGroup={removeGroup}
        onToggleConditionFlag={toggleConditionFlag}
        onRemoveCondition={removeCondition}
        renderToggle={renderToggle}
        onClose={() => onClose?.()}
        onGenerateConfig={openConfigReview}
      />
      <IndicatorGeneratorSelector
        open={isIndicatorGeneratorOpen}
        onClose={() => setIsIndicatorGeneratorOpen(false)}
        onGenerated={handleAddIndicator}
        autoCloseOnGenerate={true}
      />
      <ConditionEditorDialog
        open={isConditionModalOpen}
        onClose={closeConditionModal}
        onConfirm={handleSaveCondition}
        conditionEditTarget={conditionEditTarget}
        conditionDraft={conditionDraft}
        conditionError={conditionError}
        indicatorOutputGroups={indicatorOutputGroups}
        methodOptions={methodOptions}
        setConditionDraft={setConditionDraft}
        buildConditionPreview={buildConditionPreview}
        renderToggle={renderToggle}
      />
      <StrategyConfigDialog
        open={isConfigReviewOpen}
        onClose={closeConfigReview}
        configStep={configStep}
        onNextStep={handleNextStep}
        onPrevStep={handlePrevStep}
        onConfirmGenerate={handleConfirmGenerate}
        isLogicPreviewVisible={isLogicPreviewVisible}
        onToggleLogicPreview={toggleLogicPreview}
        logicPreview={logicPreview}
        usedIndicatorOutputs={usedIndicatorOutputs}
        conditionSummarySections={conditionSummarySections}
        summaryListRef={summaryListRef}
        summaryTrackRef={summaryTrackRef}
        summaryThumbRef={summaryThumbRef}
        codeListRef={codeListRef}
        codeTrackRef={codeTrackRef}
        codeThumbRef={codeThumbRef}
        tradeConfigRef={tradeConfigRef}
        tradeConfig={tradeConfig}
        strategyName={strategyName}
        strategyDescription={strategyDescription}
        exchangeOptions={exchangeOptions}
        symbolOptions={symbolOptions}
        timeframeOptions={timeframeOptions}
        leverageOptions={leverageOptions}
        onStrategyNameChange={handleStrategyNameChange}
        onStrategyDescriptionChange={handleStrategyDescriptionChange}
        onExchangeChange={handleExchangeChange}
        onSymbolChange={handleSymbolChange}
        onTimeframeChange={handleTimeframeChange}
        updateTradeSizing={updateTradeSizing}
        updateTradeRisk={updateTradeRisk}
        updateTrailingRisk={updateTrailingRisk}
        confirmLabel={submitLabel}
        isSubmitting={isSubmitting}
        disableMetaFields={disableMetaFields}
      />
    </>
  );
};

export default StrategyEditorFlow;
