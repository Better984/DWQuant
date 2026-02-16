import React, { useMemo, useState } from 'react';
import { useNotification } from '../ui/index.ts';
import './StrategyShareDialog.css';

export type SharePolicyPayload = {
  canFork: boolean;
  maxClaims?: number;
  expiredAt?: string;
};

type StrategyShareDialogProps = {
  strategyName?: string;
  onCreateShare: (payload: SharePolicyPayload) => Promise<string>;
  onClose: () => void;
};

const StrategyShareDialog: React.FC<StrategyShareDialogProps> = ({
  strategyName,
  onCreateShare,
  onClose,
}) => {
  const [canFork, setCanFork] = useState(true);
  const [maxClaims, setMaxClaims] = useState('');
  const [expiredAt, setExpiredAt] = useState('');
  const [shareCode, setShareCode] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const { success, error } = useNotification();

  const maxClaimsValue = useMemo(() => {
    const trimmed = maxClaims.trim();
    if (!trimmed) {
      return undefined;
    }
    const parsed = Number(trimmed);
    if (Number.isNaN(parsed) || parsed <= 0) {
      return null;
    }
    return Math.floor(parsed);
  }, [maxClaims]);

  const handleCreateShare = async () => {
    if (isSubmitting) {
      return;
    }
    if (maxClaimsValue === null) {
      error('次数请输入大于 0 的整数，留空表示不限');
      return;
    }

    setIsSubmitting(true);
    try {
      const code = await onCreateShare({
        canFork,
        maxClaims: maxClaimsValue,
        expiredAt: expiredAt || undefined,
      });
      setShareCode(code);
      success('分享码已生成');
    } catch (err) {
      const message = err instanceof Error ? err.message : '创建分享码失败';
      error(message);
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleCopy = async () => {
    if (!shareCode) {
      return;
    }
    try {
      await navigator.clipboard.writeText(shareCode);
      success('分享码已复制');
    } catch {
      error('复制失败，请手动复制');
    }
  };

  return (
    <div className="strategy-share-dialog">
      <div className="strategy-share-title">创建分享码</div>
      {strategyName && (
        <div className="strategy-share-subtitle">策略：{strategyName}</div>
      )}

      <div className="strategy-share-form">
        <label className="strategy-share-field">
          <span className="strategy-share-label">权限</span>
          <select
            className="strategy-share-select"
            value={canFork ? 'allow' : 'deny'}
            onChange={(event) => setCanFork(event.target.value === 'allow')}
          >
            <option value="allow">允许复制</option>
            <option value="deny">禁止复制</option>
          </select>
        </label>

        <label className="strategy-share-field">
          <span className="strategy-share-label">次数</span>
          <input
            className="strategy-share-input"
            type="number"
            min={1}
            placeholder="留空表示不限"
            value={maxClaims}
            onChange={(event) => setMaxClaims(event.target.value)}
          />
        </label>

        <label className="strategy-share-field">
          <span className="strategy-share-label">过期时间</span>
          <input
            className="strategy-share-input"
            type="datetime-local"
            value={expiredAt}
            onChange={(event) => setExpiredAt(event.target.value)}
          />
        </label>
      </div>

      {shareCode && (
        <div className="strategy-share-result">
          <div className="strategy-share-result-label">分享码</div>
          <div className="strategy-share-code">
            <span>{shareCode}</span>
            <button type="button" className="strategy-share-copy" onClick={handleCopy}>
              复制
            </button>
          </div>
        </div>
      )}

      <div className="strategy-share-actions">
        <button className="strategy-share-btn ghost" type="button" onClick={onClose}>
          取消
        </button>
        <button
          className="strategy-share-btn primary"
          type="button"
          onClick={handleCreateShare}
          disabled={isSubmitting}
        >
          {isSubmitting ? '创建中...' : '创建分享码'}
        </button>
      </div>
    </div>
  );
};

export default StrategyShareDialog;


