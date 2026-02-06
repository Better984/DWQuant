export interface MenuItem {
  id: string;
  label: string;
  icon: string;
}

export interface StrategyValueRef {
  refType: string;
  indicator: string;
  timeframe: string;
  input: string;
  params: number[];
  output: string;
  offsetRange: number[];
  calcMode: string;
}

export interface ConditionItem {
  id: string;
  enabled: boolean;
  required: boolean;
  method: string;
  leftValueId: string;
  rightValueType: 'field' | 'number';
  rightValueId?: string;
  rightNumber?: string;
  extraValueType?: 'field' | 'number';
  extraValueId?: string;
  extraNumber?: string;
  paramValues?: string[];
}

export interface ConditionGroup {
  id: string;
  name: string;
  enabled: boolean;
  required: boolean;
  conditions: ConditionItem[];
}

export interface ConditionContainer {
  id: string;
  title: string;
  enabled: boolean;
  required: boolean;
  groups: ConditionGroup[];
}

export interface ConditionEditTarget {
  containerId: string;
  groupId: string;
  conditionId?: string;
}

export interface ValueOption {
  id: string;
  label: string;
  fullLabel: string;
  ref: StrategyValueRef;
}

export interface IndicatorOutputGroup {
  id: string;
  label: string;
  options: ValueOption[];
}

export interface StrategyMethodConfig {
  enabled: boolean;
  required: boolean;
  method: string;
  args?: Array<StrategyValueRef | string>;
  param?: string[];
}

export interface ConditionGroupConfig {
  enabled: boolean;
  minPassConditions: number;
  conditions: StrategyMethodConfig[];
}

export interface ConditionGroupSetConfig {
  enabled: boolean;
  minPassGroups: number;
  groups: ConditionGroupConfig[];
}

export interface ConditionContainerConfig {
  checks: ConditionGroupSetConfig;
}

export interface ActionSetConfig {
  enabled: boolean;
  minPassConditions: number;
  conditions: StrategyMethodConfig[];
}

export interface StrategyLogicBranchConfig {
  enabled: boolean;
  minPassConditionContainer: number;
  containers: ConditionContainerConfig[];
  filters?: ConditionGroupSetConfig;
  onPass: ActionSetConfig;
}

export interface StrategyLogicConfig {
  entry: {
    long: StrategyLogicBranchConfig;
    short: StrategyLogicBranchConfig;
  };
  exit: {
    long: StrategyLogicBranchConfig;
    short: StrategyLogicBranchConfig;
  };
}

export interface StrategyTradeConfig {
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

export interface StrategyRuntimeTimeRange {
  start: string;
  end: string;
}

export interface StrategyRuntimeCustomConfig {
  mode: 'Allow' | 'Deny';
  timezone: string;
  days: string[];
  timeRanges: StrategyRuntimeTimeRange[];
}

export interface StrategyRuntimeCalendarException {
  date: string;
  type: string;
  name?: string;
  timeRanges: StrategyRuntimeTimeRange[];
}

export interface StrategyRuntimeTemplateConfig {
  id: string;
  name: string;
  timezone: string;
  days: string[];
  timeRanges: StrategyRuntimeTimeRange[];
  calendar?: StrategyRuntimeCalendarException[];
}

export interface StrategyRuntimeConfig {
  scheduleType: 'Always' | 'Template' | 'Custom';
  outOfSessionPolicy: 'BlockEntryAllowExit' | 'BlockAll';
  templateIds: string[];
  templates?: StrategyRuntimeTemplateConfig[];
  custom: StrategyRuntimeCustomConfig;
}

export interface StrategyConfig {
  trade: StrategyTradeConfig;
  logic: StrategyLogicConfig;
  runtime?: StrategyRuntimeConfig;
}

export interface MethodOption {
  value: string;
  label: string;
  category?: string;
  categoryLabel?: string;
  argsCount?: number;
  argLabels?: string[];
  argValueTypes?: Array<'field' | 'number' | 'both'>;
  params?: Array<{
    key: string;
    label: string;
    placeholder?: string;
    required?: boolean;
    defaultValue?: string;
  }>;
}

export interface TradeOption {
  value: string;
  label: string;
  icon?: string;
}

export interface TimeframeOption {
  value: number;
  label: string;
}

export interface ConditionSummaryGroup {
  title: string;
  conditions: string[];
}

export interface ConditionSummarySection {
  title: string;
  groups: ConditionSummaryGroup[];
}
