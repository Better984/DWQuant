import React, { useMemo, useState, useRef, useEffect } from 'react';
import './StrategyModule.css';

// 导入图标
import StarIcon from '../assets/SnowUI/icon/Star.svg';
import StorefrontIcon from '../assets/SnowUI/icon/Storefront.svg';
import GridFourIcon from '../assets/SnowUI/icon/GridFour.svg';
import PlusIcon from '../assets/SnowUI/icon/Plus.svg';
import FilePlusIcon from '../assets/SnowUI/icon/FilePlus.svg';
import FileTextIcon from '../assets/SnowUI/icon/FileText.svg';
import ShareNetworkIcon from '../assets/SnowUI/icon/ShareNetwork.svg';
import BinanceLogo from '../assets/SnowUI/cexlogo/Binance.svg';
import BitgetLogo from '../assets/SnowUI/cexlogo/bitget.svg';
import BTCIcon from '../assets/SnowUI/cryptoicon/BTC.svg';
import ETHIcon from '../assets/SnowUI/cryptoicon/ETH.svg';
import XRPIcon from '../assets/SnowUI/cryptoicon/XRP.svg';
import SOLIcon from '../assets/SnowUI/cryptoicon/SOL.svg';
import DOGEIcon from '../assets/SnowUI/cryptoicon/DOGE.svg';
import BNBIcon from '../assets/SnowUI/cryptoicon/BNB.svg';
import IndicatorGeneratorSelector, { type GeneratedIndicatorPayload } from './IndicatorGeneratorSelector';
import { Dialog, useNotification } from './ui';

interface MenuItem {
  id: string;
  label: string;
  icon: string;
}

interface StrategyValueRef {
  refType: string;
  indicator: string;
  timeframe: string;
  input: string;
  params: number[];
  output: string;
  offsetRange: number[];
  calcMode: string;
}

interface ConditionItem {
  id: string;
  enabled: boolean;
  required: boolean;
  method: string;
  leftValueId: string;
  rightValueType: 'field' | 'number';
  rightValueId?: string;
  rightNumber?: string;
}

interface ConditionGroup {
  id: string;
  name: string;
  enabled: boolean;
  required: boolean;
  conditions: ConditionItem[];
}

interface ConditionContainer {
  id: string;
  title: string;
  enabled: boolean;
  required: boolean;
  groups: ConditionGroup[];
}

interface ConditionEditTarget {
  containerId: string;
  groupId: string;
  conditionId?: string;
}

interface ValueOption {
  id: string;
  label: string;
  fullLabel: string;
  ref: StrategyValueRef;
}

interface IndicatorOutputGroup {
  id: string;
  label: string;
  options: ValueOption[];
}

interface StrategyMethodConfig {
  enabled: boolean;
  required: boolean;
  method: string;
  args?: Array<StrategyValueRef | string>;
}

interface ConditionGroupConfig {
  enabled: boolean;
  minPassConditions: number;
  conditions: StrategyMethodConfig[];
}

interface ConditionGroupSetConfig {
  enabled: boolean;
  minPassGroups: number;
  groups: ConditionGroupConfig[];
}

interface ConditionContainerConfig {
  checks: ConditionGroupSetConfig;
}

interface ActionSetConfig {
  enabled: boolean;
  minPassConditions: number;
  conditions: StrategyMethodConfig[];
}

interface StrategyLogicBranchConfig {
  enabled: boolean;
  minPassConditionContainer: number;
  containers: ConditionContainerConfig[];
  onPass: ActionSetConfig;
}

interface StrategyLogicConfig {
  entry: {
    long: StrategyLogicBranchConfig;
    short: StrategyLogicBranchConfig;
  };
  exit: {
    long: StrategyLogicBranchConfig;
    short: StrategyLogicBranchConfig;
  };
}

interface StrategyTradeConfig {
  exchange: string;
  symbol: string;
  timeframeSec: number;
  positionMode: string;
  openConflictPolicy: string;
  sizing: {
    orderQty: number;
    maxPositionQty: number;
    leverage: number;
  };
  risk: {
    takeProfitPct: number;
    stopLossPct: number;
    trailing: {
      enabled: boolean;
      activationProfitPct: number;
      closeOnDrawdownPct: number;
    };
  };
}

interface StrategyConfig {
  trade: StrategyTradeConfig;
  logic: StrategyLogicConfig;
}

const StrategyModule: React.FC = () => {
  const [activeMenuId, setActiveMenuId] = useState<string>('create');
  const [isStrategyEditorOpen, setIsStrategyEditorOpen] = useState(false);
  const [isIndicatorGeneratorOpen, setIsIndicatorGeneratorOpen] = useState(false);
  const [selectedIndicators, setSelectedIndicators] = useState<GeneratedIndicatorPayload[]>([]);
  const [isConfigReviewOpen, setIsConfigReviewOpen] = useState(false);
  const [isLogicPreviewVisible, setIsLogicPreviewVisible] = useState(false);
  const [configStep, setConfigStep] = useState(0); // 0: 基本信息, 1: 详细配置
  const [strategyName, setStrategyName] = useState('');
  const [strategyDescription, setStrategyDescription] = useState('');
  const { error, success } = useNotification();
  
  // 滚动条 refs
  const summaryListRef = useRef<HTMLDivElement>(null);
  const summaryTrackRef = useRef<HTMLDivElement>(null);
  const summaryThumbRef = useRef<HTMLDivElement>(null);
  const codeListRef = useRef<HTMLPreElement>(null);
  const codeTrackRef = useRef<HTMLDivElement>(null);
  const codeThumbRef = useRef<HTMLDivElement>(null);
  const tradeConfigRef = useRef<HTMLDivElement>(null);
  const [conditionContainers, setConditionContainers] = useState<ConditionContainer[]>([
    { id: 'open-long', title: '开多条件', enabled: true, required: false, groups: [] },
    { id: 'open-short', title: '开空条件', enabled: true, required: false, groups: [] },
    { id: 'close-long', title: '平多条件', enabled: true, required: false, groups: [] },
    { id: 'close-short', title: '平空条件', enabled: true, required: false, groups: [] },
  ]);
  const [tradeConfig, setTradeConfig] = useState<StrategyTradeConfig>({
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
      takeProfitPct: 0.1,
      stopLossPct: 0.05,
      trailing: {
        enabled: false,
        activationProfitPct: 0.05,
        closeOnDrawdownPct: 0.01,
      },
    },
  });
  const [isConditionModalOpen, setIsConditionModalOpen] = useState(false);
  const [conditionDraft, setConditionDraft] = useState<ConditionItem | null>(null);
  const [conditionEditTarget, setConditionEditTarget] = useState<ConditionEditTarget | null>(null);
  const [conditionError, setConditionError] = useState('');
  const MAX_GROUPS_PER_CONTAINER = 3;
  const MAX_CONDITIONS_PER_GROUP = 6;

  const menuItems: MenuItem[] = [
    { id: 'official', label: '官方策略', icon: StarIcon },
    { id: 'market', label: '策略市场', icon: StorefrontIcon },
    { id: 'template', label: '策略模板', icon: GridFourIcon },
    { id: 'create', label: '创建策略', icon: PlusIcon },
  ];

  const exchangeOptions = [
    { value: 'binance', label: '币安', icon: BinanceLogo },
    { value: 'okx', label: 'OKX' },
    { value: 'bitget', label: 'Bitget', icon: BitgetLogo },
  ];

  const symbolOptions = [
    { value: 'BTC/USDT', label: 'BTC', icon: BTCIcon },
    { value: 'ETH/USDT', label: 'ETH', icon: ETHIcon },
    { value: 'XRP/USDT', label: 'XRP', icon: XRPIcon },
    { value: 'SOL/USDT', label: 'SOL', icon: SOLIcon },
    { value: 'DOGE/USDT', label: 'DOGE', icon: DOGEIcon },
    { value: 'BNB/USDT', label: 'BNB', icon: BNBIcon },
  ];

  const timeframeOptions = [
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

  const openStrategyEditor = () => {
    setIsStrategyEditorOpen(true);
  };

  const closeStrategyEditor = () => {
    setIsStrategyEditorOpen(false);
  };

  const openConfigReview = () => {
    setIsConfigReviewOpen(true);
    setIsLogicPreviewVisible(false);
    setConfigStep(0);
    setStrategyName('');
    setStrategyDescription('');
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
      const isAtBottom = scrollTop + clientHeight >= scrollHeight - 10; // 10px 容差
      const isAtTop = scrollTop <= 10; // 10px 容差

      // 向下滚动且到达底部，切换到下一步
      if (e.deltaY > 0 && isAtBottom && configStep === 0) {
        e.preventDefault();
        isScrolling = true;
        setConfigStep(1);
        if (scrollTimeout) clearTimeout(scrollTimeout);
        scrollTimeout = setTimeout(() => {
          isScrolling = false;
        }, 500);
      }
      // 向上滚动且到达顶部，切换到上一步
      else if (e.deltaY < 0 && isAtTop && configStep === 1) {
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

  const methodOptions = [
    { value: 'GreaterThanOrEqual', label: '大于等于 (>=)' },
    { value: 'LessThan', label: '小于 (<)' },
    { value: 'LessThanOrEqual', label: '小于等于 (<=)' },
    { value: 'Equal', label: '等于 (=)' },
    { value: 'CrossOver', label: '上穿 (CrossOver)' },
  ];

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

  const conditionSummarySections = useMemo(() => {
    const sections = [
      { id: 'open-long', label: '开多' },
      { id: 'open-short', label: '开空' },
      { id: 'close-long', label: '平多' },
      { id: 'close-short', label: '平空' },
    ];
    return sections.map((section) => {
      const container = conditionContainers.find((item) => item.id === section.id);
      const groups = container?.groups ?? [];
      const groupSummaries = groups.map((group, groupIndex) => {
        const conditions = group.conditions.map((condition, conditionIndex) => {
          const leftOption = indicatorValueMap.get(condition.leftValueId);
          const leftLabel = formatValueRefLabel(leftOption?.ref);
          const rightLabel =
            condition.rightValueType === 'number'
              ? condition.rightNumber?.trim() || '0'
              : formatValueRefLabel(indicatorValueMap.get(condition.rightValueId || '')?.ref);
          const methodLabel = getMethodLabel(condition.method);
          const statusLabel = !condition.enabled ? '（未启用）' : condition.required ? '（必须）' : '';
          return `条件${conditionIndex + 1}：${leftLabel} ${methodLabel} ${rightLabel}${statusLabel}`;
        });
        return {
          title: `第${groupIndex + 1}个条件组${group.required ? '（必须满足）' : ''}${
            group.enabled ? '' : '（未启用）'
          }`,
          conditions: conditions.length > 0 ? conditions : ['暂无条件'],
        };
      });
      return {
        title: `${section.label} 共${groups.length}个条件组${container?.enabled ? '' : '（未启用）'}`,
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

  const handleConfirmGenerate = () => {
    const payload = JSON.stringify(configPreview, null, 2);
    if (!navigator.clipboard?.writeText) {
      error('当前环境不支持复制到剪贴板');
      return;
    }
    navigator.clipboard.writeText(payload).then(
      () => {
        success('配置已复制到剪贴板');
        closeConfigReview();
      },
      () => error('复制失败，请稍后重试'),
    );
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
          name: `条件组 ${nextIndex}`,
          enabled: true,
          required: false,
          conditions: [],
        };
        return { ...container, groups: [...container.groups, newGroup] };
      }),
    );
  };

  const toggleContainerFlag = (containerId: string, key: 'enabled' | 'required') => {
    setConditionContainers((prev) => {
      const index = prev.findIndex((container) => container.id === containerId);
      if (index < 0) {
        return prev;
      }
      const container = prev[index];
      const nextValue = !container[key];
      const nextContainer = { ...container, [key]: nextValue };
      const nextList = prev.map((item, idx) => (idx === index ? nextContainer : item));
      if (key === 'required' && nextValue) {
        return promoteToTop(nextList, index);
      }
      return nextList;
    });
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

  const removeContainerGroups = (containerId: string) => {
    setConditionContainers((prev) =>
      prev.map((container) =>
        container.id === containerId ? { ...container, groups: [] } : container,
      ),
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
    <div className="strategy-module-container">
      {/* 左侧导航栏 */}
      <aside className="strategy-sidebar">
        <div className="strategy-menu-group">
          {menuItems.map((item) => (
            <button
              key={item.id}
              className={`strategy-menu-item ${activeMenuId === item.id ? 'active' : ''}`}
              onClick={() => setActiveMenuId(item.id)}
            >
              <div className="strategy-menu-icon">
                <img src={item.icon} alt={item.label} />
              </div>
              <span className="strategy-menu-text">{item.label}</span>
            </button>
          ))}
        </div>
      </aside>

      {/* 右侧内容区域 */}
      <main className="strategy-content">
        <div className="strategy-content-inner">
          {/* 创建策略选中时显示三个按钮 */}
          {activeMenuId === 'create' && !isStrategyEditorOpen && (
            <div className="strategy-template-options">
              <button className="strategy-option-button" onClick={openStrategyEditor}>
                <div className="strategy-option-icon">
                  <img src={FilePlusIcon} alt="自定义创建" />
                </div>
                <div className="strategy-option-content">
                  <div className="strategy-option-title">自定义创建</div>
                  <div className="strategy-option-desc">从零开始创建您的专属策略</div>
                </div>
              </button>
              <button className="strategy-option-button">
                <div className="strategy-option-icon">
                  <img src={FileTextIcon} alt="模板创建" />
                </div>
                <div className="strategy-option-content">
                  <div className="strategy-option-title">模板创建</div>
                  <div className="strategy-option-desc">基于预设模板快速创建策略</div>
                </div>
              </button>
              <button className="strategy-option-button">
                <div className="strategy-option-icon">
                  <img src={ShareNetworkIcon} alt="分享码导入" />
                </div>
                <div className="strategy-option-content">
                  <div className="strategy-option-title">分享码导入</div>
                  <div className="strategy-option-desc">通过分享码快速导入策略配置</div>
                </div>
              </button>
            </div>
          )}
          {activeMenuId === 'create' && isStrategyEditorOpen && (
            <div className="strategy-editor-shell">
              <div className="strategy-editor-header">
                <div className="strategy-editor-title">策略编辑器</div>
                <button className="strategy-editor-close" onClick={closeStrategyEditor}>
                  返回
                </button>
              </div>
              <div className="strategy-editor-body">
                <div className="strategy-indicator-panel">
                  <div className="strategy-indicator-header">
                    <div className="strategy-indicator-title">已选指标</div>
                    <button
                      className="strategy-indicator-add"
                      onClick={() => setIsIndicatorGeneratorOpen(true)}
                    >
                      新建指标
                    </button>
                  </div>
                  {selectedIndicators.length === 0 ? (
                    <div className="strategy-indicator-empty">
                      还没有添加指标，点击“新建指标”开始创建。
                    </div>
                  ) : (
                    <div className="strategy-indicator-list">
                      {selectedIndicators.map((indicator) => (
                        <div key={indicator.id} className="strategy-indicator-item">
                          <div className="strategy-indicator-name">
                            {formatIndicatorName(indicator)}
                          </div>
                          <div className="strategy-indicator-meta">
                            {formatIndicatorMeta(indicator)}
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
                <div className="strategy-condition-section">
                  <div className="strategy-condition-title">条件容器</div>
                  <div className="strategy-condition-grid">
                    {conditionContainers.map((container) => (
                      <div key={container.id} className="condition-container-card">
                        <div className="condition-container-header">
                          <div>
                            <div className="condition-container-title">{container.title}</div>
                            <div className="condition-container-meta">
                              条件组 {container.groups.length}/{MAX_GROUPS_PER_CONTAINER}
                            </div>
                          </div>
                          <div className="condition-container-actions">
                            <button
                              className="condition-add-group"
                              onClick={() => addConditionGroup(container.id)}
                            >
                              添加条件组
                            </button>
                          </div>
                        </div>
                        {container.groups.length === 0 ? (
                          <div className="condition-container-empty">暂无条件组</div>
                        ) : (
                          <div className="condition-group-list">
                            {container.groups.map((group) => (
                              <div key={group.id} className="condition-group-card">
                                <div className="condition-group-header">
                                  <div className="condition-group-title">{group.name}</div>
                                  <div className="condition-group-actions">
                                    {renderToggle(
                                      group.enabled,
                                      () => toggleGroupFlag(container.id, group.id, 'enabled'),
                                      '启用',
                                    )}
                                    {renderToggle(
                                      group.required,
                                      () => toggleGroupFlag(container.id, group.id, 'required'),
                                      '必须满足',
                                    )}
                                    <button
                                      className="condition-add-button"
                                      onClick={() => openConditionModal(container.id, group.id)}
                                    >
                                      添加条件
                                    </button>
                                    <button
                                      className="condition-delete-button"
                                      onClick={() => removeGroup(container.id, group.id)}
                                    >
                                      删除条件组
                                    </button>
                                  </div>
                                </div>
                                {group.conditions.length === 0 ? (
                                  <div className="condition-group-empty">暂无条件</div>
                                ) : (
                                  <div className="condition-item-list">
                                    {group.conditions.map((condition) => (
                                      <div key={condition.id} className="condition-item-card">
                                        <div className="condition-item-info">
                                          <div className="condition-item-title">
                                            {buildConditionPreview(condition)}
                                          </div>
                                          <div className="condition-item-method">
                                            方法: {condition.method}
                                          </div>
                                        </div>
                                        <div className="condition-item-actions">
                                          {renderToggle(
                                            condition.enabled,
                                            () =>
                                              toggleConditionFlag(
                                                container.id,
                                                group.id,
                                                condition.id,
                                                'enabled',
                                              ),
                                            '启用',
                                          )}
                                          {renderToggle(
                                            condition.required,
                                            () =>
                                              toggleConditionFlag(
                                                container.id,
                                                group.id,
                                                condition.id,
                                                'required',
                                              ),
                                            '必须满足',
                                          )}
                                          <button
                                            className="condition-edit-button"
                                            onClick={() =>
                                              openConditionModal(container.id, group.id, condition.id)
                                            }
                                          >
                                            编辑
                                          </button>
                                          <button
                                            className="condition-delete-button"
                                            onClick={() => removeCondition(container.id, group.id, condition.id)}
                                          >
                                            删除
                                          </button>
                                        </div>
                                      </div>
                                    ))}
                                  </div>
                                )}
                              </div>
                            ))}
                          </div>
                        )}
                      </div>
                    ))}
                  </div>
                </div>
              </div>
              <div className="strategy-editor-footer">
                <button className="strategy-generate-button" onClick={openConfigReview}>
                  生成配置
                </button>
              </div>
            </div>
          )}
        </div>
      </main>
      <IndicatorGeneratorSelector
        open={isIndicatorGeneratorOpen}
        onClose={() => setIsIndicatorGeneratorOpen(false)}
        onGenerated={handleAddIndicator}
        autoCloseOnGenerate={true}
      />
      <Dialog
        open={isConditionModalOpen}
        onClose={closeConditionModal}
        title={conditionEditTarget?.conditionId ? '编辑条件' : '配置触发条件'}
        cancelText="取消"
        confirmText="保存条件"
        onConfirm={handleSaveCondition}
        className="condition-dialog"
      >
        <div className="condition-dialog-body">
          <div className="condition-form-section">
            <div className="condition-form-title">参数配置</div>
            <div className="condition-form-field">
              <label className="condition-form-label">选择字段</label>
              <select
                className="condition-form-select"
                value={conditionDraft?.leftValueId || ''}
                onChange={(event) =>
                  setConditionDraft((prev) =>
                    prev ? { ...prev, leftValueId: event.target.value } : prev,
                  )
                }
              >
                <option value="">请选择字段</option>
                {indicatorOutputGroups.map((group) => (
                  <optgroup key={group.id} label={group.label}>
                    {group.options.map((option) => (
                      <option key={option.id} value={option.id}>
                        {option.label}
                      </option>
                    ))}
                  </optgroup>
                ))}
              </select>
            </div>
            <div className="condition-form-field">
              <label className="condition-form-label">选择操作符</label>
              <select
                className="condition-form-select"
                value={conditionDraft?.method || methodOptions[0].value}
                onChange={(event) =>
                  setConditionDraft((prev) =>
                    prev ? { ...prev, method: event.target.value } : prev,
                  )
                }
              >
                {methodOptions.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </select>
            </div>
            <div className="condition-form-field">
              <label className="condition-form-label">比较类型</label>
              <div className="condition-form-radio">
                <label>
                  <input
                    type="radio"
                    name="compareType"
                    checked={conditionDraft?.rightValueType === 'number'}
                    onChange={() =>
                      setConditionDraft((prev) =>
                        prev ? { ...prev, rightValueType: 'number' } : prev,
                      )
                    }
                  />
                  数值
                </label>
                <label>
                  <input
                    type="radio"
                    name="compareType"
                    checked={conditionDraft?.rightValueType === 'field'}
                    onChange={() =>
                      setConditionDraft((prev) =>
                        prev ? { ...prev, rightValueType: 'field' } : prev,
                      )
                    }
                  />
                  字段
                </label>
              </div>
            </div>
            <div className="condition-form-field">
              <label className="condition-form-label">选择比较字段</label>
              {conditionDraft?.rightValueType === 'number' ? (
                <input
                  className="condition-form-input"
                  type="number"
                  placeholder="请输入数值"
                  value={conditionDraft?.rightNumber || ''}
                  onChange={(event) =>
                    setConditionDraft((prev) =>
                      prev ? { ...prev, rightNumber: event.target.value } : prev,
                    )
                  }
                />
              ) : (
                <select
                  className="condition-form-select"
                  value={conditionDraft?.rightValueId || ''}
                  onChange={(event) =>
                    setConditionDraft((prev) =>
                      prev ? { ...prev, rightValueId: event.target.value } : prev,
                    )
                  }
                >
                  <option value="">请选择字段</option>
                  {indicatorOutputGroups.map((group) => (
                    <optgroup key={group.id} label={group.label}>
                      {group.options.map((option) => (
                        <option key={option.id} value={option.id}>
                          {option.label}
                        </option>
                      ))}
                    </optgroup>
                  ))}
                </select>
              )}
            </div>
          </div>
          <div className="condition-form-section">
            <div className="condition-form-title">条件预览</div>
            <div className="condition-preview-box">{buildConditionPreview(conditionDraft)}</div>
            <div className="condition-toggle-row">
              {renderToggle(
                conditionDraft?.enabled ?? true,
                () =>
                  setConditionDraft((prev) =>
                    prev ? { ...prev, enabled: !prev.enabled } : prev,
                  ),
                '启用此条件',
              )}
              {renderToggle(
                conditionDraft?.required ?? false,
                () =>
                  setConditionDraft((prev) =>
                    prev ? { ...prev, required: !prev.required } : prev,
                  ),
                '必须满足此条件',
              )}
            </div>
            {conditionError && <div className="condition-form-error">{conditionError}</div>}
          </div>
        </div>
      </Dialog>
      <Dialog
        open={isConfigReviewOpen}
        onClose={closeConfigReview}
        title="生成策略配置"
        cancelText={configStep === 1 ? undefined : "取消"}
        confirmText={configStep === 1 ? "复制配置" : undefined}
        onConfirm={configStep === 1 ? handleConfirmGenerate : undefined}
        onCancel={configStep === 0 ? closeConfigReview : undefined}
        className="strategy-config-dialog"
        footer={
          <div className="strategy-config-footer">
            {configStep === 0 ? (
              <>
                <button
                  className="snowui-dialog__button snowui-dialog__button--cancel"
                  onClick={closeConfigReview}
                >
                  取消
                </button>
                <button
                  className="snowui-dialog__button snowui-dialog__button--confirm"
                  onClick={handleNextStep}
                >
                  下一步
                </button>
              </>
            ) : (
              <>
                <button
                  className="snowui-dialog__button snowui-dialog__button--cancel"
                  onClick={handlePrevStep}
                >
                  上一步
                </button>
                <button
                  className="snowui-dialog__button snowui-dialog__button--confirm"
                  onClick={handleConfirmGenerate}
                >
                  复制配置
                </button>
              </>
            )}
          </div>
        }
      >
        <div className={`strategy-config-dialog-body strategy-config-step-${configStep}`}>
          <div className="strategy-config-progress">
            <div className="strategy-config-progress-item">
              <div className={`strategy-config-progress-dot ${configStep >= 0 ? 'active' : ''}`}></div>
              <div className={`strategy-config-progress-line ${configStep >= 1 ? 'active' : ''}`}></div>
            </div>
            <div className="strategy-config-progress-item">
              <div className={`strategy-config-progress-dot ${configStep >= 1 ? 'active' : ''}`}></div>
            </div>
          </div>
          <div className="strategy-config-preview">
            <div className="strategy-config-preview-header">
              <div className="strategy-config-preview-title">
                {isLogicPreviewVisible ? '逻辑配置' : '条件概览'}
              </div>
              <button
                type="button"
                className="strategy-config-toggle"
                onClick={() => setIsLogicPreviewVisible((prev) => !prev)}
              >
                {isLogicPreviewVisible ? '查看概览' : '查看 JSON'}
              </button>
            </div>
            {isLogicPreviewVisible ? (
              <div className="strategy-config-code-wrapper">
                <pre className="strategy-config-code" ref={codeListRef}>{logicPreview}</pre>
                <div className="strategy-config-scrollbar" ref={codeTrackRef}>
                  <div className="strategy-config-scrollbar-thumb" ref={codeThumbRef}></div>
                </div>
              </div>
            ) : (
              <>
                <div className="strategy-config-indicator-summary">
                  <div className="strategy-config-indicator-title">
                    当前参与指标数量：{usedIndicatorOutputs.length}
                  </div>
                  {usedIndicatorOutputs.length === 0 ? (
                    <div className="strategy-config-indicator-empty">暂无参与指标</div>
                  ) : (
                    <div className="strategy-config-indicator-list">
                      {usedIndicatorOutputs.map((label) => (
                        <div key={label} className="strategy-config-indicator-item">
                          {label}
                        </div>
                      ))}
                    </div>
                  )}
                </div>
                <div className="strategy-config-summary-wrapper">
                  <div className="strategy-config-summary" ref={summaryListRef}>
                    {conditionSummarySections.map((section) => (
                      <div key={section.title} className="strategy-config-summary-section">
                        <div className="strategy-config-summary-title">{section.title}</div>
                        {section.groups.map((group) => (
                          <div key={`${section.title}-${group.title}`} className="strategy-config-summary-group">
                            <div className="strategy-config-summary-group-title">{group.title}</div>
                            {group.conditions.length > 0 && (
                              <div className="strategy-config-summary-list">
                                {group.conditions.map((line, index) => (
                                  <div
                                    key={`${group.title}-${index}`}
                                    className="strategy-config-summary-item"
                                  >
                                    {line}
                                  </div>
                                ))}
                              </div>
                            )}
                          </div>
                        ))}
                      </div>
                    ))}
                  </div>
                  <div className="strategy-config-scrollbar" ref={summaryTrackRef}>
                    <div className="strategy-config-scrollbar-thumb" ref={summaryThumbRef}></div>
                  </div>
                </div>
              </>
            )}
          </div>
          <div className={`strategy-config-trade strategy-config-trade-step-${configStep}`} ref={tradeConfigRef}>
            <div className="strategy-config-trade-title">交易规则</div>
            {configStep === 0 ? (
              <>
                <div className="trade-form-section">
                  <div className="trade-form-label">策略名称</div>
                  <input
                    className="trade-input trade-input-full"
                    type="text"
                    placeholder="请输入策略名称"
                    value={strategyName}
                    onChange={(e) => setStrategyName(e.target.value)}
                  />
                </div>
                <div className="trade-form-section">
                  <div className="trade-form-label">策略描述</div>
                  <textarea
                    className="trade-textarea"
                    placeholder="请输入策略描述"
                    value={strategyDescription}
                    onChange={(e) => setStrategyDescription(e.target.value)}
                    rows={4}
                  />
                </div>
                <div className="trade-form-section">
                  <div className="trade-form-label">目标交易所</div>
                  <div className="trade-option-grid">
                    {exchangeOptions.map((option) => (
                      <button
                        key={option.value}
                        type="button"
                        className={`trade-option-card ${
                          tradeConfig.exchange === option.value ? 'active' : ''
                        }`}
                        onClick={() =>
                          setTradeConfig((prev) => ({
                            ...prev,
                            exchange: option.value,
                          }))
                        }
                      >
                        {option.icon ? (
                          <img className="trade-option-icon" src={option.icon} alt={option.label} />
                        ) : (
                          <div className="trade-option-icon-text">{option.label}</div>
                        )}
                        <span>{option.label}</span>
                      </button>
                    ))}
                  </div>
                </div>
                <div className="trade-form-section">
                  <div className="trade-form-label">交易对</div>
                  <div className="trade-option-grid">
                    {symbolOptions.map((option) => (
                      <button
                        key={option.value}
                        type="button"
                        className={`trade-option-card ${
                          tradeConfig.symbol === option.value ? 'active' : ''
                        }`}
                        onClick={() =>
                          setTradeConfig((prev) => ({
                            ...prev,
                            symbol: option.value,
                          }))
                        }
                      >
                        <img className="trade-option-icon" src={option.icon} alt={option.label} />
                        <span>{option.label}</span>
                      </button>
                    ))}
                  </div>
                </div>
              </>
            ) : (
              <>
                <div className="trade-form-section">
                  <div className="trade-form-label">交易周期</div>
                  <div className="trade-chip-group">
                    {timeframeOptions.map((option) => (
                      <button
                        key={option.label}
                        type="button"
                        className={`trade-chip ${
                          tradeConfig.timeframeSec === option.value ? 'active' : ''
                        }`}
                        onClick={() =>
                          setTradeConfig((prev) => ({
                            ...prev,
                            timeframeSec: option.value,
                          }))
                        }
                      >
                        {option.label}
                      </button>
                    ))}
                  </div>
                </div>
            <div className="trade-form-row">
              <div className="trade-form-field">
                <div className="trade-form-label">单次开仓数量</div>
                <div className="trade-input-group">
                  <input
                    className="trade-input"
                    type="number"
                    min={0}
                    step="0.0001"
                    value={tradeConfig.sizing.orderQty}
                    onChange={(event) =>
                      updateTradeSizing('orderQty', Number(event.target.value) || 0)
                    }
                  />
                  <span className="trade-input-suffix">张</span>
                </div>
              </div>
              <div className="trade-form-field">
                <div className="trade-form-label">最大持仓数量</div>
                <div className="trade-input-group">
                  <input
                    className="trade-input"
                    type="number"
                    min={0}
                    step="0.0001"
                    value={tradeConfig.sizing.maxPositionQty}
                    onChange={(event) =>
                      updateTradeSizing('maxPositionQty', Number(event.target.value) || 0)
                    }
                  />
                  <span className="trade-input-suffix">张</span>
                </div>
              </div>
            </div>
            <div className="trade-form-section">
              <div className="trade-form-label">杠杆</div>
              <div className="trade-chip-group">
                {leverageOptions.map((value) => (
                  <button
                    key={value}
                    type="button"
                    className={`trade-chip ${
                      tradeConfig.sizing.leverage === value ? 'active' : ''
                    }`}
                    onClick={() => updateTradeSizing('leverage', value)}
                  >
                    {value}x
                  </button>
                ))}
              </div>
            </div>
            <div className="trade-form-row">
              <div className="trade-form-field">
                <div className="trade-form-label">止盈比例</div>
                <div className="trade-input-group">
                  <input
                    className="trade-input"
                    type="number"
                    min={0}
                    step="0.1"
                    value={tradeConfig.risk.takeProfitPct * 100}
                    onChange={(event) =>
                      updateTradeRisk('takeProfitPct', (Number(event.target.value) || 0) / 100)
                    }
                  />
                  <span className="trade-input-suffix">%</span>
                </div>
              </div>
              <div className="trade-form-field">
                <div className="trade-form-label">止损比例</div>
                <div className="trade-input-group">
                  <input
                    className="trade-input"
                    type="number"
                    min={0}
                    step="0.1"
                    value={tradeConfig.risk.stopLossPct * 100}
                    onChange={(event) =>
                      updateTradeRisk('stopLossPct', (Number(event.target.value) || 0) / 100)
                    }
                  />
                  <span className="trade-input-suffix">%</span>
                </div>
              </div>
            </div>
            <div className="trade-form-section">
              <label className="trade-toggle">
                <input
                  type="checkbox"
                  checked={tradeConfig.risk.trailing.enabled}
                  onChange={() =>
                    updateTrailingRisk('enabled', !tradeConfig.risk.trailing.enabled)
                  }
                />
                <span className="trade-toggle-indicator" />
                <span className="trade-toggle-label">启用移动止盈止损</span>
              </label>
              <div
                className={`trade-trailing-panel ${
                  tradeConfig.risk.trailing.enabled ? '' : 'is-disabled'
                }`}
              >
                <div className="trade-form-row">
                  <div className="trade-form-field">
                    <div className="trade-form-label">触发收益阈值</div>
                    <div className="trade-input-group">
                      <input
                        className="trade-input"
                        type="number"
                        min={0}
                        step="0.1"
                        value={tradeConfig.risk.trailing.activationProfitPct * 100}
                        onChange={(event) =>
                          updateTrailingRisk(
                            'activationProfitPct',
                            (Number(event.target.value) || 0) / 100,
                          )
                        }
                        disabled={!tradeConfig.risk.trailing.enabled}
                      />
                      <span className="trade-input-suffix">%</span>
                    </div>
                  </div>
                  <div className="trade-form-field">
                    <div className="trade-form-label">回撤触发比例</div>
                    <div className="trade-input-group">
                      <input
                        className="trade-input"
                        type="number"
                        min={0}
                        step="0.1"
                        value={tradeConfig.risk.trailing.closeOnDrawdownPct * 100}
                        onChange={(event) =>
                          updateTrailingRisk(
                            'closeOnDrawdownPct',
                            (Number(event.target.value) || 0) / 100,
                          )
                        }
                        disabled={!tradeConfig.risk.trailing.enabled}
                      />
                      <span className="trade-input-suffix">%</span>
                    </div>
                  </div>
                </div>
                <div className="trade-form-hint">
                  触发后按回撤比例自动止盈。
                </div>
              </div>
            </div>
              </>
            )}
          </div>
        </div>
      </Dialog>
    </div>
  );
};

export default StrategyModule;
