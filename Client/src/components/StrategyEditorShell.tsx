import React from 'react';

import type { GeneratedIndicatorPayload } from './IndicatorGeneratorSelector';
import type { ConditionContainer, ConditionItem } from './StrategyModule.types';
import ConditionContainerList from './ConditionContainerList';
import StrategyIndicatorPanel from './StrategyIndicatorPanel';

interface StrategyEditorShellProps {
  selectedIndicators: GeneratedIndicatorPayload[];
  formatIndicatorName: (indicator: GeneratedIndicatorPayload) => string;
  formatIndicatorMeta: (indicator: GeneratedIndicatorPayload) => string;
  onOpenIndicatorGenerator: () => void;
  onEditIndicator: (indicatorId: string) => void;
  onRemoveIndicator: (indicatorId: string) => void;
  conditionContainers: ConditionContainer[];
  maxGroupsPerContainer: number;
  buildConditionPreview: (condition: ConditionItem | null) => string;
  onAddConditionGroup: (containerId: string) => void;
  onToggleGroupFlag: (containerId: string, groupId: string, key: 'enabled' | 'required') => void;
  onOpenConditionModal: (containerId: string, groupId: string, conditionId?: string) => void;
  onRemoveGroup: (containerId: string, groupId: string) => void;
  onToggleConditionFlag: (
    containerId: string,
    groupId: string,
    conditionId: string,
    key: 'enabled' | 'required',
  ) => void;
  onRemoveCondition: (containerId: string, groupId: string, conditionId: string) => void;
  renderToggle: (checked: boolean, onChange: () => void, label: string) => React.ReactNode;
  onClose: () => void;
  onGenerateConfig: () => void;
}

const StrategyEditorShell: React.FC<StrategyEditorShellProps> = ({
  selectedIndicators,
  formatIndicatorName,
  formatIndicatorMeta,
  onOpenIndicatorGenerator,
  onEditIndicator,
  onRemoveIndicator,
  conditionContainers,
  maxGroupsPerContainer,
  buildConditionPreview,
  onAddConditionGroup,
  onToggleGroupFlag,
  onOpenConditionModal,
  onRemoveGroup,
  onToggleConditionFlag,
  onRemoveCondition,
  renderToggle,
  onClose,
  onGenerateConfig,
}) => {
  return (
    <div className="strategy-editor-shell">
      <div className="strategy-editor-header">
        <div className="strategy-editor-title">策略编辑器</div>
        <button className="strategy-editor-close" onClick={onClose}>返回</button>
      </div>
      <div className="strategy-editor-body">
        <StrategyIndicatorPanel
          selectedIndicators={selectedIndicators}
          onOpenIndicatorGenerator={onOpenIndicatorGenerator}
          onEditIndicator={onEditIndicator}
          onRemoveIndicator={onRemoveIndicator}
          formatIndicatorName={formatIndicatorName}
          formatIndicatorMeta={formatIndicatorMeta}
        />
        <ConditionContainerList
          conditionContainers={conditionContainers}
          maxGroupsPerContainer={maxGroupsPerContainer}
          buildConditionPreview={buildConditionPreview}
          onAddConditionGroup={onAddConditionGroup}
          onToggleGroupFlag={onToggleGroupFlag}
          onOpenConditionModal={onOpenConditionModal}
          onRemoveGroup={onRemoveGroup}
          onToggleConditionFlag={onToggleConditionFlag}
          onRemoveCondition={onRemoveCondition}
          renderToggle={renderToggle}
        />
      </div>
      <div className="strategy-editor-footer">
        <button className="strategy-generate-button" onClick={onGenerateConfig}>生成配置</button>
      </div>
    </div>
  );
};

export default StrategyEditorShell;
