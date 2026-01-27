import React, { useState, useMemo, useEffect } from 'react';
import { useNotification } from './ui';
import StrategyShareDialog, { type SharePolicyPayload } from './StrategyShareDialog';
import StrategyHistoryDialog, { type StrategyHistoryVersion } from './StrategyHistoryDialog';
import './StrategyDetailDialog.css';

export type StrategyDetailRecord = {
  usId: number;
  defId: number;
  defName: string;
  aliasName: string;
  description: string;
  state: string;
  versionNo: number;
  configJson?: any;
  updatedAt?: string;
};

type StrategyDetailDialogProps = {
  strategy: StrategyDetailRecord | null;
  onClose: () => void;
  onCreateVersion: (usId: number) => void;
  onViewHistory: (usId: number) => Promise<StrategyHistoryVersion[]>;
  onCreateShare: (usId: number, payload: SharePolicyPayload) => Promise<string>;
  onUpdateStatus: (usId: number, status: 'running' | 'paused' | 'paused_open_position' | 'completed') => Promise<void>;
  onDelete: (usId: number) => void;
};

type TabType = 'info' | 'share' | 'history';

const StrategyDetailDialog: React.FC<StrategyDetailDialogProps> = ({
  strategy,
  onClose,
  onCreateVersion,
  onViewHistory,
  onCreateShare,
  onUpdateStatus,
  onDelete,
}) => {
  const { success, error } = useNotification();
  const [activeTab, setActiveTab] = useState<TabType>('info');
  const [currentStatus, setCurrentStatus] = useState<'running' | 'paused' | 'paused_open_position' | 'completed'>('completed');
  const [isUpdatingStatus, setIsUpdatingStatus] = useState(false);
  const [historyVersions, setHistoryVersions] = useState<StrategyHistoryVersion[]>([]);
  const [selectedHistoryVersionId, setSelectedHistoryVersionId] = useState<number | null>(null);
  const [isHistoryLoading, setIsHistoryLoading] = useState(false);
  const [shareCode, setShareCode] = useState<string | null>(null);
  const [isShareLoading, setIsShareLoading] = useState(false);

  useEffect(() => {
    if (strategy) {
      const status = strategy.state?.trim().toLowerCase();
      if (status === 'running') {
        setCurrentStatus('running');
      } else if (status === 'paused') {
        setCurrentStatus('paused');
      } else if (status === 'paused_open_position') {
        setCurrentStatus('paused_open_position');
      } else {
        setCurrentStatus('completed');
      }
    }
  }, [strategy]);

  const handleUpdateStatus = async (newStatus: 'running' | 'paused' | 'paused_open_position' | 'completed') => {
    if (!strategy || isUpdatingStatus) {
      return;
    }
    setIsUpdatingStatus(true);
    try {
      await onUpdateStatus(strategy.usId, newStatus);
      setCurrentStatus(newStatus);
      success('策略状态已更新');
    } catch (err) {
      const message = err instanceof Error ? err.message : '更新策略状态失败';
      error(message);
    } finally {
      setIsUpdatingStatus(false);
    }
  };

  const handleLoadHistory = async () => {
    if (!strategy || isHistoryLoading) {
      return;
    }
    setIsHistoryLoading(true);
    try {
      const versions = await onViewHistory(strategy.usId);
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

  const handleCreateShare = async (payload: SharePolicyPayload) => {
    if (!strategy || isShareLoading) {
      throw new Error('策略未选择');
    }
    setIsShareLoading(true);
    try {
      const code = await onCreateShare(strategy.usId, payload);
      setShareCode(code);
      return code;
    } finally {
      setIsShareLoading(false);
    }
  };

  const handleTabChange = (tab: TabType) => {
    setActiveTab(tab);
    if (tab === 'history' && historyVersions.length === 0) {
      handleLoadHistory();
    }
  };

  const handleCreateVersion = () => {
    if (strategy) {
      onCreateVersion(strategy.usId);
      onClose();
    }
  };

  const getStatusText = (status: string) => {
    switch (status) {
      case 'running':
        return '运行中';
      case 'paused':
        return '已暂停';
      case 'paused_open_position':
        return '暂停开新仓';
      case 'completed':
        return '完成';
      case 'error':
        return '错误';
      default:
        return '完成';
    }
  };

  const getStatusColor = (status: string) => {
    switch (status) {
      case 'running':
        return 'status-running';
      case 'paused':
        return 'status-paused';
      case 'paused_open_position':
        return 'status-paused-open-position';
      case 'completed':
        return 'status-completed';
      case 'error':
        return 'status-error';
      default:
        return 'status-completed';
    }
  };

  if (!strategy) {
    return null;
  }

  return (
    <div className="strategy-detail-dialog">
      <div className="strategy-detail-header">
        <div className="strategy-detail-title-section">
          <h2 className="strategy-detail-title">
            {strategy.aliasName || strategy.defName}
            {strategy.versionNo && <span className="strategy-detail-version">v{strategy.versionNo}</span>}
          </h2>
          <div className={`strategy-detail-status ${getStatusColor(currentStatus)}`}>
            <div className="status-dot"></div>
            <span>{getStatusText(currentStatus)}</span>
          </div>
        </div>
        <button className="strategy-detail-close" type="button" onClick={onClose} aria-label="关闭">
          <svg width={20} height={20} viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
            <path
              d="M18 6L6 18M6 6L18 18"
              stroke="currentColor"
              strokeWidth="1.5"
              strokeLinecap="round"
              strokeLinejoin="round"
            />
          </svg>
        </button>
      </div>

      <div className="strategy-detail-tabs">
        <button
          type="button"
          className={`strategy-detail-tab ${activeTab === 'info' ? 'is-active' : ''}`}
          onClick={() => handleTabChange('info')}
        >
          基本信息
        </button>
        <button
          type="button"
          className={`strategy-detail-tab ${activeTab === 'share' ? 'is-active' : ''}`}
          onClick={() => handleTabChange('share')}
        >
          分享码
        </button>
        <button
          type="button"
          className={`strategy-detail-tab ${activeTab === 'history' ? 'is-active' : ''}`}
          onClick={() => handleTabChange('history')}
        >
          历史版本
        </button>
      </div>

      <div className="strategy-detail-content">
        {activeTab === 'info' && (
          <div className="strategy-detail-info">
            <div className="strategy-detail-section">
              <h3 className="strategy-detail-section-title">策略状态</h3>
              <div className="strategy-detail-status-controls">
                <button
                  type="button"
                  className={`strategy-status-btn ${currentStatus === 'running' ? 'is-active' : ''}`}
                  onClick={() => handleUpdateStatus('running')}
                  disabled={isUpdatingStatus || currentStatus === 'running'}
                >
                  {isUpdatingStatus && currentStatus !== 'running' ? '更新中...' : '运行中'}
                </button>
                <button
                  type="button"
                  className={`strategy-status-btn ${currentStatus === 'paused' ? 'is-active' : ''}`}
                  onClick={() => handleUpdateStatus('paused')}
                  disabled={isUpdatingStatus || currentStatus === 'paused'}
                >
                  {isUpdatingStatus && currentStatus !== 'paused' ? '更新中...' : '已暂停'}
                </button>
                <button
                  type="button"
                  className={`strategy-status-btn ${currentStatus === 'paused_open_position' ? 'is-active' : ''}`}
                  onClick={() => handleUpdateStatus('paused_open_position')}
                  disabled={isUpdatingStatus || currentStatus === 'paused_open_position'}
                >
                  {isUpdatingStatus && currentStatus !== 'paused_open_position' ? '更新中...' : '暂停开新仓'}
                </button>
                <button
                  type="button"
                  className={`strategy-status-btn ${currentStatus === 'completed' ? 'is-active' : ''}`}
                  onClick={() => handleUpdateStatus('completed')}
                  disabled={isUpdatingStatus || currentStatus === 'completed'}
                >
                  {isUpdatingStatus && currentStatus !== 'completed' ? '更新中...' : '完成'}
                </button>
              </div>
            </div>

            <div className="strategy-detail-section">
              <h3 className="strategy-detail-section-title">策略信息</h3>
              <div className="strategy-detail-info-grid">
                <div className="strategy-detail-info-item">
                  <span className="strategy-detail-info-label">策略名称</span>
                  <span className="strategy-detail-info-value">{strategy.aliasName || strategy.defName}</span>
                </div>
                <div className="strategy-detail-info-item">
                  <span className="strategy-detail-info-label">版本号</span>
                  <span className="strategy-detail-info-value">v{strategy.versionNo}</span>
                </div>
                {strategy.description && (
                  <div className="strategy-detail-info-item strategy-detail-info-item--full">
                    <span className="strategy-detail-info-label">描述</span>
                    <span className="strategy-detail-info-value">{strategy.description}</span>
                  </div>
                )}
              </div>
            </div>

            <div className="strategy-detail-section">
              <h3 className="strategy-detail-section-title">操作</h3>
              <div className="strategy-detail-actions">
                <button
                  type="button"
                  className="strategy-detail-action-btn strategy-detail-action-btn--primary"
                  onClick={handleCreateVersion}
                >
                  创建新版本
                </button>
                <button
                  type="button"
                  className="strategy-detail-action-btn strategy-detail-action-btn--danger"
                  onClick={() => onDelete(strategy.usId)}
                >
                  删除策略
                </button>
              </div>
            </div>
          </div>
        )}

        {activeTab === 'share' && (
          <div className="strategy-detail-share">
            <div className="strategy-detail-share-wrapper">
              <StrategyShareDialog
                strategyName={strategy.aliasName || strategy.defName}
                onCreateShare={handleCreateShare}
                onClose={() => {}}
              />
            </div>
          </div>
        )}

        {activeTab === 'history' && (
          <div className="strategy-detail-history">
            <StrategyHistoryDialog
              versions={historyVersions}
              selectedVersionId={selectedHistoryVersionId}
              onSelectVersion={setSelectedHistoryVersionId}
              onClose={() => {}}
              isLoading={isHistoryLoading}
            />
          </div>
        )}
      </div>
    </div>
  );
};

export default StrategyDetailDialog;



