import React, { useEffect, useMemo, useState } from 'react';
import StrategyHistoryDialog, { type StrategyHistoryVersion } from './StrategyHistoryDialog';
import { useNotification } from './ui';
import './OfficialStrategyDetailDialog.css';

export type OfficialStrategyDetailRecord = {
  defId: number;
  name: string;
  description: string;
  versionNo: number;
};

type OfficialStrategyDetailDialogProps = {
  strategy: OfficialStrategyDetailRecord | null;
  onClose: () => void;
  onViewHistory: (defId: number) => Promise<StrategyHistoryVersion[]>;
};

const OfficialStrategyDetailDialog: React.FC<OfficialStrategyDetailDialogProps> = ({
  strategy,
  onClose,
  onViewHistory,
}) => {
  const { error } = useNotification();
  const [historyVersions, setHistoryVersions] = useState<StrategyHistoryVersion[]>([]);
  const [selectedHistoryVersionId, setSelectedHistoryVersionId] = useState<number | null>(null);
  const [isHistoryLoading, setIsHistoryLoading] = useState(false);

  useEffect(() => {
    if (!strategy) {
      return;
    }

    const loadHistory = async () => {
      setIsHistoryLoading(true);
      try {
        const versions = await onViewHistory(strategy.defId);
        setHistoryVersions(versions);
        const pinnedVersion = versions.find((item) => item.isPinned);
        const fallbackVersion = pinnedVersion ?? versions[versions.length - 1];
        setSelectedHistoryVersionId(fallbackVersion ? fallbackVersion.versionId : null);
      } catch (err) {
        const message = err instanceof Error ? err.message : '加载历史版本失败';
        error(message);
      } finally {
        setIsHistoryLoading(false);
      }
    };

    loadHistory();
  }, [strategy, onViewHistory, error]);

  const selectedVersion = useMemo(() => {
    if (!selectedHistoryVersionId) {
      return historyVersions[historyVersions.length - 1];
    }
    return historyVersions.find((item) => item.versionId === selectedHistoryVersionId)
      ?? historyVersions[historyVersions.length - 1];
  }, [historyVersions, selectedHistoryVersionId]);

  if (!strategy) {
    return null;
  }

  const versionLabel = selectedVersion ? `v${selectedVersion.versionNo}` : '';

  return (
    <div className="official-strategy-detail-dialog">
      <StrategyHistoryDialog
        versions={historyVersions}
        selectedVersionId={selectedHistoryVersionId}
        onSelectVersion={setSelectedHistoryVersionId}
        onClose={onClose}
        isLoading={isHistoryLoading}
      />
      <div className="official-strategy-detail-footer">
        <div className="official-strategy-detail-meta">
          当前选择版本：{versionLabel || '未选择'}
        </div>
        <button type="button" className="official-strategy-detail-action">
          {versionLabel ? `运用${versionLabel}版本` : '运用版本'}
        </button>
      </div>
    </div>
  );
};

export default OfficialStrategyDetailDialog;
