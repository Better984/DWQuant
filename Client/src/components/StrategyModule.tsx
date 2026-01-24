import React, { useMemo, useState } from 'react';
import './StrategyModule.css';

// 导入图标
import StarIcon from '../assets/SnowUI/icon/Star.svg';
import StorefrontIcon from '../assets/SnowUI/icon/Storefront.svg';
import GridFourIcon from '../assets/SnowUI/icon/GridFour.svg';
import PlusIcon from '../assets/SnowUI/icon/Plus.svg';
import FilePlusIcon from '../assets/SnowUI/icon/FilePlus.svg';
import FileTextIcon from '../assets/SnowUI/icon/FileText.svg';
import ShareNetworkIcon from '../assets/SnowUI/icon/ShareNetwork.svg';
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
  const { error, success } = useNotification();
  const [conditionContainers, setConditionContainers] = useState<ConditionContainer[]>([
    { id: 'open-long', title: '开多条件', enabled: true, required: false, groups: [] },
    { id: 'open-short', title: '开空条件', enabled: true, required: false, groups: [] },
    { id: 'close-long', title: '平多条件', enabled: true, required: false, groups: [] },
    { id: 'close-short', title: '平空条件', enabled: true, required: false, groups: [] },
  ]);
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

  const openStrategyEditor = () => {
    setIsStrategyEditorOpen(true);
  };

  const closeStrategyEditor = () => {
    setIsStrategyEditorOpen(false);
  };

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
    const value = raw.trim().toLowerCase();
    if (!value) {
      return null;
    }
    const match = value.match(/^(\d+)([smhdw])$/);
    const swappedMatch = value.match(/^([smhdw])(\d+)$/);
    const size = match ? Number(match[1]) : swappedMatch ? Number(swappedMatch[2]) : NaN;
    const unit = match ? match[2] : swappedMatch ? swappedMatch[1] : '';
    if (!unit || Number.isNaN(size)) {
      return null;
    }
    const multiplier = {
      s: 1,
      m: 60,
      h: 60 * 60,
      d: 60 * 60 * 24,
      w: 60 * 60 * 24 * 7,
    }[unit];
    return multiplier ? size * multiplier : null;
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
      output?: string;
      input?: string;
    };
    const output = config.output || '-';
    const input = config.input || '-';
    return `输入 ${input} · 输出 ${output}`;
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
      exchange: 'Bitget',
      symbol: 'BTC/USDT',
      timeframeSec: resolveTradeTimeframeSec(),
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

  const handleGenerateConfig = () => {
    const config = buildStrategyConfig();
    const payload = JSON.stringify(config, null, 2);
    if (!navigator.clipboard?.writeText) {
      error('当前环境不支持复制到剪贴板');
      return;
    }
    navigator.clipboard.writeText(payload).then(
      () => success('配置已复制到剪贴板'),
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
                <button className="strategy-generate-button" onClick={handleGenerateConfig}>
                  生成配置并复制
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
    </div>
  );
};

export default StrategyModule;
