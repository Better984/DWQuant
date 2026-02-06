import React from 'react';

import type { ConditionContainer, ConditionItem } from './StrategyModule.types';

interface ConditionContainerListProps {
  sectionTitle?: string;
  sectionHint?: string;
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
}

const ConditionContainerList: React.FC<ConditionContainerListProps> = ({
  sectionTitle,
  sectionHint,
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
}) => {
  return (
    <div className="strategy-condition-section">
      <div className="strategy-condition-title">{sectionTitle || '条件容器'}</div>
      {sectionHint ? <div className="strategy-condition-hint">{sectionHint}</div> : null}
      <div className="strategy-condition-grid">
        {conditionContainers.map((container) => (
          <div key={container.id} className="condition-container-card">
            <div className="condition-container-header">
              <div>
                <div className="condition-container-title">{container.title}</div>
                <div className="condition-container-meta">
                  条件组 {container.groups.length}/{maxGroupsPerContainer}
                </div>
              </div>
              <div className="condition-container-actions">
                <button
                  className="condition-add-group"
                  onClick={() => onAddConditionGroup(container.id)}
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
                          () => onToggleGroupFlag(container.id, group.id, 'enabled'),
                          '启用',
                        )}
                        {renderToggle(
                          group.required,
                          () => onToggleGroupFlag(container.id, group.id, 'required'),
                          '必须满足',
                        )}
                        <button
                          className="condition-add-button"
                          onClick={() => onOpenConditionModal(container.id, group.id)}
                        >
                          添加条件
                        </button>
                        <button
                          className="condition-delete-button"
                          onClick={() => onRemoveGroup(container.id, group.id)}
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
                              <div className="condition-item-method">方法: {condition.method}</div>
                            </div>
                            <div className="condition-item-actions">
                              {renderToggle(
                                condition.enabled,
                                () =>
                                  onToggleConditionFlag(
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
                                  onToggleConditionFlag(
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
                                  onOpenConditionModal(container.id, group.id, condition.id)
                                }
                              >
                                编辑
                              </button>
                              <button
                                className="condition-delete-button"
                                onClick={() => onRemoveCondition(container.id, group.id, condition.id)}
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
  );
};

export default ConditionContainerList;
