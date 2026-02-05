import React, { useEffect, useMemo } from 'react';

import type {
  ConditionEditTarget,
  ConditionItem,
  IndicatorOutputGroup,
  MethodOption,
} from './StrategyModule.types';
import { Dialog } from './ui';

interface ConditionEditorDialogProps {
  open: boolean;
  onClose: () => void;
  onConfirm: () => void;
  conditionEditTarget: ConditionEditTarget | null;
  conditionDraft: ConditionItem | null;
  conditionError: string;
  indicatorOutputGroups: IndicatorOutputGroup[];
  methodOptions: MethodOption[];
  setConditionDraft: React.Dispatch<React.SetStateAction<ConditionItem | null>>;
  buildConditionPreview: (draft: ConditionItem | null) => string;
  renderToggle: (checked: boolean, onChange: () => void, label: string) => React.ReactNode;
}

const CATEGORY_LABELS: Record<string, string> = {
  compare: '基础比较',
  cross: '交叉',
  range: '区间',
  trend: '持续/趋势',
  change: '变化率/斜率',
  stats: '统计',
  channel: '通道/边界',
  bandwidth: '带宽',
};

const ConditionEditorDialog: React.FC<ConditionEditorDialogProps> = ({
  open,
  onClose,
  onConfirm,
  conditionEditTarget,
  conditionDraft,
  conditionError,
  indicatorOutputGroups,
  methodOptions,
  setConditionDraft,
  buildConditionPreview,
  renderToggle,
}) => {
  const methodMeta = methodOptions.find((option) => option.value === conditionDraft?.method) || methodOptions[0];
  const argsCount = methodMeta?.argsCount ?? 2;
  const argLabels = methodMeta?.argLabels || [];
  const paramDefs = methodMeta?.params || [];
  const rightValueType = conditionDraft?.rightValueType ?? 'number';
  const extraValueType = conditionDraft?.extraValueType ?? 'number';

  const categoryOptions = useMemo(() => {
    const map = new Map<string, string>();
    methodOptions.forEach((option) => {
      const category = option.category || 'compare';
      const label = option.categoryLabel || CATEGORY_LABELS[category] || category;
      if (!map.has(category)) {
        map.set(category, label);
      }
    });
    return Array.from(map.entries()).map(([value, label]) => ({ value, label }));
  }, [methodOptions]);

  const selectedCategory = methodMeta?.category || categoryOptions[0]?.value || 'compare';
  const methodsInCategory = methodOptions.filter(
    (option) => (option.category || 'compare') === selectedCategory,
  );

  const buildFieldLabel = (index: number, fallback: string) => {
    const label = argLabels[index];
    return label ? `选择${label}` : fallback;
  };

  const buildValueLabel = (index: number, fallback: string, mode: 'field' | 'number') => {
    const label = argLabels[index] || fallback;
    return `${mode === 'number' ? '输入' : '选择'}${label}`;
  };

  const updateParamValues = (nextMethod: string, previous?: string[]) => {
    const nextMeta = methodOptions.find((option) => option.value === nextMethod) || methodOptions[0];
    const nextParams = nextMeta?.params || [];
    return nextParams.map((param, index) => previous?.[index] ?? param.defaultValue ?? '');
  };

  const resolveArgMode = (meta: MethodOption | undefined, index: number) => {
    const mode = meta?.argValueTypes?.[index];
    if (mode === 'field' || mode === 'number' || mode === 'both') {
      return mode;
    }
    return 'both';
  };

  const coerceValueType = (
    current: 'field' | 'number' | undefined,
    mode: 'field' | 'number' | 'both',
    fallback: 'field' | 'number',
  ) => {
    if (mode === 'field' || mode === 'number') {
      return mode;
    }
    if (current === 'field' || current === 'number') {
      return current;
    }
    return fallback;
  };

  useEffect(() => {
    if (!conditionDraft) {
      return;
    }
    const nextRightMode = resolveArgMode(methodMeta, 1);
    const nextExtraMode = resolveArgMode(methodMeta, 2);
    const nextRightType = coerceValueType(conditionDraft.rightValueType, nextRightMode, 'field');
    const nextExtraType = coerceValueType(conditionDraft.extraValueType, nextExtraMode, 'field');
    if (nextRightType === conditionDraft.rightValueType && nextExtraType === conditionDraft.extraValueType) {
      return;
    }
    setConditionDraft((prev) =>
      prev
        ? {
            ...prev,
            rightValueType: nextRightType,
            extraValueType: nextExtraType,
          }
        : prev,
    );
  }, [conditionDraft, methodMeta, setConditionDraft]);

  const handleCategoryChange = (category: string) => {
    const nextMethod = methodOptions.find((option) => (option.category || 'compare') === category) || methodOptions[0];
    if (!nextMethod) {
      return;
    }
    setConditionDraft((prev) => {
      if (!prev) {
        return prev;
      }
      const nextDraft = {
        ...prev,
        method: nextMethod.value,
        paramValues: updateParamValues(nextMethod.value, prev.paramValues),
      };
      const nextRightMode = resolveArgMode(nextMethod, 1);
      const nextExtraMode = resolveArgMode(nextMethod, 2);
      return {
        ...nextDraft,
        rightValueType: coerceValueType(nextDraft.rightValueType, nextRightMode, 'field'),
        extraValueType: coerceValueType(nextDraft.extraValueType, nextExtraMode, 'field'),
      };
    });
  };

  const handleMethodChange = (nextMethodValue: string) => {
    const nextMethod = methodOptions.find((option) => option.value === nextMethodValue) || methodOptions[0];
    setConditionDraft((prev) => {
      if (!prev) {
        return prev;
      }
      const nextDraft = {
        ...prev,
        method: nextMethod.value,
        paramValues: updateParamValues(nextMethod.value, prev.paramValues),
      };
      const nextRightMode = resolveArgMode(nextMethod, 1);
      const nextExtraMode = resolveArgMode(nextMethod, 2);
      return {
        ...nextDraft,
        rightValueType: coerceValueType(nextDraft.rightValueType, nextRightMode, 'field'),
        extraValueType: coerceValueType(nextDraft.extraValueType, nextExtraMode, 'field'),
      };
    });
  };

  const rightArgMode = resolveArgMode(methodMeta, 1);
  const extraArgMode = resolveArgMode(methodMeta, 2);
  const resolvedRightType = rightArgMode === 'both' ? rightValueType : rightArgMode;
  const resolvedExtraType = extraArgMode === 'both' ? extraValueType : extraArgMode;

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title={conditionEditTarget?.conditionId ? '编辑条件' : '配置触发条件'}
      cancelText="取消"
      confirmText="保存条件"
      onConfirm={onConfirm}
      className="condition-dialog"
    >
      <div className="condition-dialog-body">
        <div className="condition-form-section">
          <div className="condition-form-title">条件类型</div>
          <div className="condition-form-field">
            <label className="condition-form-label">选择类型</label>
            <select
              className="condition-form-select"
              value={selectedCategory}
              onChange={(event) => handleCategoryChange(event.target.value)}
            >
              {categoryOptions.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </div>
          <div className="condition-form-field">
            <label className="condition-form-label">选择条件</label>
            <select
              className="condition-form-select"
              value={conditionDraft?.method || methodOptions[0].value}
              onChange={(event) => handleMethodChange(event.target.value)}
            >
              {methodsInCategory.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </div>
        </div>
        <div className="condition-form-section">
          <div className="condition-form-title">参数配置</div>
          {argsCount >= 1 && (
            <div className="condition-form-field">
              <label className="condition-form-label">{buildFieldLabel(0, '选择字段')}</label>
              <select
                className="condition-form-select"
                value={conditionDraft?.leftValueId || ''}
                onChange={(event) =>
                  setConditionDraft((prev) => (prev ? { ...prev, leftValueId: event.target.value } : prev))
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
          )}
          {argsCount >= 2 && (
            <>
              {rightArgMode === 'both' && (
                <div className="condition-form-field">
                  <label className="condition-form-label">
                    {argLabels[1] ? `${argLabels[1]}类型` : '第二参数类型'}
                  </label>
                  <div className="condition-form-radio">
                    <label>
                      <input
                        type="radio"
                        name="compareType"
                        checked={rightValueType === 'number'}
                        onChange={() =>
                          setConditionDraft((prev) => (prev ? { ...prev, rightValueType: 'number' } : prev))
                        }
                      />
                      数值
                    </label>
                    <label>
                      <input
                        type="radio"
                        name="compareType"
                        checked={rightValueType === 'field'}
                        onChange={() =>
                          setConditionDraft((prev) => (prev ? { ...prev, rightValueType: 'field' } : prev))
                        }
                      />
                      字段
                    </label>
                  </div>
                </div>
              )}
              <div className="condition-form-field">
                <label className="condition-form-label">
                  {buildValueLabel(1, resolvedRightType === 'number' ? '数值' : '比较字段', resolvedRightType)}
                </label>
                {resolvedRightType === 'number' ? (
                  <input
                    className="condition-form-input"
                    type="number"
                    placeholder="请输入数值"
                    value={conditionDraft?.rightNumber || ''}
                    onChange={(event) =>
                      setConditionDraft((prev) => (prev ? { ...prev, rightNumber: event.target.value } : prev))
                    }
                  />
                ) : (
                  <select
                    className="condition-form-select"
                    value={conditionDraft?.rightValueId || ''}
                    onChange={(event) =>
                      setConditionDraft((prev) => (prev ? { ...prev, rightValueId: event.target.value } : prev))
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
            </>
          )}
          {argsCount >= 3 && (
            <>
              {extraArgMode === 'both' && (
                <div className="condition-form-field">
                  <label className="condition-form-label">
                    {argLabels[2] ? `${argLabels[2]}类型` : '第三参数类型'}
                  </label>
                  <div className="condition-form-radio">
                    <label>
                      <input
                        type="radio"
                        name="extraType"
                        checked={extraValueType === 'number'}
                        onChange={() =>
                          setConditionDraft((prev) => (prev ? { ...prev, extraValueType: 'number' } : prev))
                        }
                      />
                      数值
                    </label>
                    <label>
                      <input
                        type="radio"
                        name="extraType"
                        checked={extraValueType === 'field'}
                        onChange={() =>
                          setConditionDraft((prev) => (prev ? { ...prev, extraValueType: 'field' } : prev))
                        }
                      />
                      字段
                    </label>
                  </div>
                </div>
              )}
              <div className="condition-form-field">
                <label className="condition-form-label">
                  {buildValueLabel(2, resolvedExtraType === 'number' ? '数值' : '第三参数', resolvedExtraType)}
                </label>
                {resolvedExtraType === 'number' ? (
                  <input
                    className="condition-form-input"
                    type="number"
                    placeholder="请输入数值"
                    value={conditionDraft?.extraNumber || ''}
                    onChange={(event) =>
                      setConditionDraft((prev) => (prev ? { ...prev, extraNumber: event.target.value } : prev))
                    }
                  />
                ) : (
                  <select
                    className="condition-form-select"
                    value={conditionDraft?.extraValueId || ''}
                    onChange={(event) =>
                      setConditionDraft((prev) => (prev ? { ...prev, extraValueId: event.target.value } : prev))
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
            </>
          )}
          {paramDefs.length > 0 && (
            <div className="condition-form-field">
              <label className="condition-form-label">附加参数</label>
              {paramDefs.map((param, index) => (
                <input
                  key={`${param.key}-${index}`}
                  className="condition-form-input"
                  type="number"
                  placeholder={param.placeholder || param.label}
                  value={conditionDraft?.paramValues?.[index] ?? param.defaultValue ?? ''}
                  onChange={(event) =>
                    setConditionDraft((prev) => {
                      if (!prev) {
                        return prev;
                      }
                      const nextValues = [...(prev.paramValues || [])];
                      nextValues[index] = event.target.value;
                      return { ...prev, paramValues: nextValues };
                    })
                  }
                />
              ))}
            </div>
          )}
        </div>
        <div className="condition-form-section">
          <div className="condition-form-title">条件预览</div>
          <div className="condition-preview-box">{buildConditionPreview(conditionDraft)}</div>
          <div className="condition-toggle-row">
            {renderToggle(
              conditionDraft?.enabled ?? true,
              () =>
                setConditionDraft((prev) => (prev ? { ...prev, enabled: !prev.enabled } : prev)),
              '启用此条件',
            )}
            {renderToggle(
              conditionDraft?.required ?? false,
              () =>
                setConditionDraft((prev) => (prev ? { ...prev, required: !prev.required } : prev)),
              '必须满足此条件',
            )}
          </div>
          {conditionError && <div className="condition-form-error">{conditionError}</div>}
        </div>
      </div>
    </Dialog>
  );
};

export default ConditionEditorDialog;
