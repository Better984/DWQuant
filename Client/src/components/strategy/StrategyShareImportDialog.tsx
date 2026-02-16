import React, { useMemo, useState } from 'react';
import { useNotification } from '../ui/index.ts';
import './StrategyShareImportDialog.css';

type ImportPayload = {
  shareCode: string;
  aliasName?: string;
};

type StrategyShareImportDialogProps = {
  onImportShare: (payload: ImportPayload) => Promise<void>;
  onClose: () => void;
};

const sanitizeShareCode = (value: string) => value.toUpperCase().replace(/[^A-Z0-9]/g, '');

const formatShareCode = (value: string) => {
  const cleaned = sanitizeShareCode(value).slice(0, 8);
  if (cleaned.length <= 4) {
    return cleaned;
  }
  return `${cleaned.slice(0, 4)}-${cleaned.slice(4)}`;
};

const StrategyShareImportDialog: React.FC<StrategyShareImportDialogProps> = ({
  onImportShare,
  onClose,
}) => {
  const [shareCode, setShareCode] = useState('');
  const [aliasName, setAliasName] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const { success, error } = useNotification();

  const normalizedShareCode = useMemo(() => formatShareCode(shareCode), [shareCode]);
  const isShareCodeReady = sanitizeShareCode(shareCode).length === 8;

  const handleSubmit = async () => {
    if (!isShareCodeReady) {
      error('请输入正确的分享码');
      return;
    }
    if (isSubmitting) {
      return;
    }

    setIsSubmitting(true);
    try {
      await onImportShare({
        shareCode: normalizedShareCode,
        aliasName: aliasName.trim() || undefined,
      });
      success('导入成功');
      onClose();
    } catch (err) {
      const message = err instanceof Error ? err.message : '导入失败';
      error(message);
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="strategy-import-dialog">
      <div className="strategy-import-title">分享码导入</div>
      <div className="strategy-import-form">
        <label className="strategy-import-field">
          <span className="strategy-import-label">分享码</span>
          <input
            className="strategy-import-input"
            value={normalizedShareCode}
            onChange={(event) => setShareCode(event.target.value)}
            placeholder="例如 QS9F-45QS"
          />
        </label>
        <label className="strategy-import-field">
          <span className="strategy-import-label">策略名称</span>
          <input
            className="strategy-import-input"
            value={aliasName}
            onChange={(event) => setAliasName(event.target.value)}
            placeholder="可选，留空使用默认名称"
          />
        </label>
      </div>
      <div className="strategy-import-actions">
        <button className="strategy-import-btn ghost" type="button" onClick={onClose}>
          取消
        </button>
        <button
          className="strategy-import-btn primary"
          type="button"
          onClick={handleSubmit}
          disabled={isSubmitting}
        >
          {isSubmitting ? '导入中...' : '导入策略'}
        </button>
      </div>
    </div>
  );
};

export default StrategyShareImportDialog;


