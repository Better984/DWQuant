import React from 'react';

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
          <div className="condition-form-title">参数配置</div>
          <div className="condition-form-field">
            <label className="condition-form-label">选择字段</label>
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
          <div className="condition-form-field">
            <label className="condition-form-label">选择操作符</label>
            <select
              className="condition-form-select"
              value={conditionDraft?.method || methodOptions[0].value}
              onChange={(event) =>
                setConditionDraft((prev) => (prev ? { ...prev, method: event.target.value } : prev))
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
                    setConditionDraft((prev) => (prev ? { ...prev, rightValueType: 'number' } : prev))
                  }
                />
                数值</label>
              <label>
                <input
                  type="radio"
                  name="compareType"
                  checked={conditionDraft?.rightValueType === 'field'}
                  onChange={() =>
                    setConditionDraft((prev) => (prev ? { ...prev, rightValueType: 'field' } : prev))
                  }
                />
                字段</label>
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
