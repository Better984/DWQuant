import React, { useState, useMemo, useEffect } from 'react';
import { useNotification } from './ui';
import StrategyShareDialog, { type SharePolicyPayload } from './StrategyShareDialog';
import StrategyHistoryDialog, { type StrategyHistoryVersion } from './StrategyHistoryDialog';
import AlertDialog from './AlertDialog';
import { getAuthProfile } from '../auth/profileStore';
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
  officialDefId?: number | null;
  officialVersionNo?: number | null;
  templateDefId?: number | null;
  templateVersionNo?: number | null;
  marketId?: number | null;
  marketVersionNo?: number | null;
};

type StrategyDetailDialogProps = {
  strategy: StrategyDetailRecord | null;
  onClose: () => void;
  onCreateVersion: (usId: number) => void;
  onViewHistory: (usId: number) => Promise<StrategyHistoryVersion[]>;
  onCreateShare: (usId: number, payload: SharePolicyPayload) => Promise<string>;
  onUpdateStatus: (usId: number, status: 'running' | 'paused' | 'paused_open_position' | 'completed') => Promise<void>;
  onDelete: (usId: number) => void;
  onPublishOfficial: (usId: number) => Promise<void>;
  onPublishTemplate: (usId: number) => Promise<void>;
  onPublishMarket: (usId: number) => Promise<void>;
  onSyncOfficial: (usId: number) => Promise<void>;
  onSyncTemplate: (usId: number) => Promise<void>;
  onSyncMarket: (usId: number) => Promise<void>;
  onRemoveOfficial: (usId: number) => Promise<void>;
  onRemoveTemplate: (usId: number) => Promise<void>;
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
  onPublishOfficial,
  onPublishTemplate,
  onPublishMarket,
  onSyncOfficial,
  onSyncTemplate,
  onSyncMarket,
  onRemoveOfficial,
  onRemoveTemplate,
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
  const [publishTarget, setPublishTarget] = useState<'official' | 'template' | null>(null);
  const [isPublishing, setIsPublishing] = useState(false);
  const [isMarketPublishing, setIsMarketPublishing] = useState(false);
  const [isMarketConfirmOpen, setIsMarketConfirmOpen] = useState(false);
  const [syncTarget, setSyncTarget] = useState<'official' | 'template' | 'market' | null>(null);
  const [isSyncing, setIsSyncing] = useState(false);
  const [removeTarget, setRemoveTarget] = useState<'official' | 'template' | null>(null);
  const [isRemoving, setIsRemoving] = useState(false);

  const profile = useMemo(() => getAuthProfile(), []);
  const canPublish = profile?.role === 255;

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

  const handlePublish = async (target: 'official' | 'template') => {
    if (!strategy || isPublishing) {
      return;
    }
    setIsPublishing(true);
    try {
      if (target === 'official') {
        await onPublishOfficial(strategy.usId);
        success('已发布到官方策略库');
      } else {
        await onPublishTemplate(strategy.usId);
        success('已发布到策略模板库');
      }
    } catch (err) {
      const message = err instanceof Error ? err.message : '发布失败';
      error(message);
    } finally {
      setIsPublishing(false);
      setPublishTarget(null);
    }
  };

  const handlePublishMarket = async () => {
    if (!strategy || isMarketPublishing) {
      return;
    }
    setIsMarketPublishing(true);
    try {
      await onPublishMarket(strategy.usId);
      success('已公开到策略广场');
    } catch (err) {
      const message = err instanceof Error ? err.message : '公开失败，请稍后重试';
      error(message);
    } finally {
      setIsMarketPublishing(false);
      setIsMarketConfirmOpen(false);
    }
  };

  const handleSync = async (target: 'official' | 'template' | 'market') => {
    if (!strategy || isSyncing) {
      return;
    }
    setIsSyncing(true);
    try {
      if (target === 'official') {
        await onSyncOfficial(strategy.usId);
        success('已发布最新版本到官方策略库');
      } else if (target === 'template') {
        await onSyncTemplate(strategy.usId);
        success('已发布最新版本到策略模板库');
      } else {
        await onSyncMarket(strategy.usId);
        success('已发布最新版本到策略广场');
      }
    } catch (err) {
      const message = err instanceof Error ? err.message : '发布最新版本失败';
      error(message);
    } finally {
      setIsSyncing(false);
      setSyncTarget(null);
    }
  };

  const handleRemove = async (target: 'official' | 'template') => {
    if (!strategy || isRemoving) {
      return;
    }
    setIsRemoving(true);
    try {
      if (target === 'official') {
        await onRemoveOfficial(strategy.usId);
        success('已从官方策略库移除');
      } else {
        await onRemoveTemplate(strategy.usId);
        success('已从策略模板库移除');
      }
    } catch (err) {
      const message = err instanceof Error ? err.message : '移除失败';
      error(message);
    } finally {
      setIsRemoving(false);
      setRemoveTarget(null);
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

  const officialPublished = Boolean(strategy.officialDefId);
  const templatePublished = Boolean(strategy.templateDefId);
  const marketPublished = Boolean(strategy.marketId);
  const officialVersionNo = strategy.officialVersionNo ?? 0;
  const templateVersionNo = strategy.templateVersionNo ?? 0;
  const marketVersionNo = strategy.marketVersionNo ?? 0;
  const officialOutdated = officialPublished && strategy.versionNo > officialVersionNo;
  const templateOutdated = templatePublished && strategy.versionNo > templateVersionNo;
  const marketOutdated = marketPublished && strategy.versionNo > marketVersionNo;

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
                {!marketPublished && (
                  <button
                    type="button"
                    className="strategy-detail-action-btn"
                    onClick={() => setIsMarketConfirmOpen(true)}
                  >
                    公开到策略广场
                  </button>
                )}
                {marketPublished && marketOutdated && (
                  <button
                    type="button"
                    className="strategy-detail-action-btn"
                    onClick={() => setSyncTarget('market')}
                  >
                    发布最新版本到广场
                  </button>
                )}
                {canPublish && (
                  <>
                    {!officialPublished && (
                      <button
                        type="button"
                        className="strategy-detail-action-btn"
                        onClick={() => setPublishTarget('official')}
                      >
                        发布到官方
                      </button>
                    )}
                    {officialPublished && officialOutdated && (
                      <button
                        type="button"
                        className="strategy-detail-action-btn"
                        onClick={() => setSyncTarget('official')}
                      >
                        发布最新版本到官方
                      </button>
                    )}
                    {officialPublished && (
                      <button
                        type="button"
                        className="strategy-detail-action-btn strategy-detail-action-btn--danger"
                        onClick={() => setRemoveTarget('official')}
                      >
                        从官方策略中移除
                      </button>
                    )}
                    {!templatePublished && (
                      <button
                        type="button"
                        className="strategy-detail-action-btn"
                        onClick={() => setPublishTarget('template')}
                      >
                        发布到模板
                      </button>
                    )}
                    {templatePublished && templateOutdated && (
                      <button
                        type="button"
                        className="strategy-detail-action-btn"
                        onClick={() => setSyncTarget('template')}
                      >
                        发布最新版本到模板
                      </button>
                    )}
                    {templatePublished && (
                      <button
                        type="button"
                        className="strategy-detail-action-btn strategy-detail-action-btn--danger"
                        onClick={() => setRemoveTarget('template')}
                      >
                        从策略模板中移除
                      </button>
                    )}
                  </>
                )}
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
      <AlertDialog
        open={publishTarget !== null}
        title={publishTarget === 'official' ? '发布到官方策略库' : '发布到策略模板库'}
        description={publishTarget === 'official' ? '确定要发布此策略到官方策略库吗？发布后其他用户可以使用此策略。' : '确定要发布此策略到策略模板库吗？发布后其他用户可以使用此策略模板。'}
        helperText="发布后无法撤销，请谨慎操作"
        cancelText="取消"
        confirmText={isPublishing ? '发布中...' : '确认发布'}
        onCancel={() => setPublishTarget(null)}
        onClose={() => setPublishTarget(null)}
        onConfirm={() => {
          if (publishTarget) {
            handlePublish(publishTarget);
          }
        }}
      />
      <AlertDialog
        open={syncTarget !== null}
        title={
          syncTarget === 'official'
            ? '发布最新版本到官方策略库'
            : syncTarget === 'template'
              ? '发布最新版本到策略模板库'
              : '发布最新版本到策略广场'
        }
        description={
          syncTarget === 'official'
            ? '确认将最新版本同步到官方策略库吗？'
            : syncTarget === 'template'
              ? '确认将最新版本同步到策略模板库吗？'
              : '确认将最新版本同步到策略广场吗？'
        }
        helperText="同步后会覆盖公开版本，请谨慎操作"
        cancelText="取消"
        confirmText={isSyncing ? '发布中...' : '确认发布'}
        onCancel={() => setSyncTarget(null)}
        onClose={() => setSyncTarget(null)}
        onConfirm={() => {
          if (syncTarget) {
            handleSync(syncTarget);
          }
        }}
      />
      <AlertDialog
        open={removeTarget !== null}
        title={removeTarget === 'official' ? '从官方策略中移除' : '从策略模板中移除'}
        description={
          removeTarget === 'official'
            ? '确认将该策略从官方策略库移除吗？'
            : '确认将该策略从策略模板库移除吗？'
        }
        helperText="移除后其他用户将无法继续使用该发布记录。"
        cancelText="取消"
        confirmText={isRemoving ? '移除中...' : '确认移除'}
        danger={true}
        onCancel={() => setRemoveTarget(null)}
        onClose={() => setRemoveTarget(null)}
        onConfirm={() => {
          if (removeTarget) {
            handleRemove(removeTarget);
          }
        }}
      />
      <AlertDialog
        open={isMarketConfirmOpen}
        title="公开到策略广场"
        description="确认将该策略公开到策略广场吗？公开后所有用户都可查看。"
        helperText="公开后可继续更新版本。"
        cancelText="取消"
        confirmText={isMarketPublishing ? '公开中...' : '确认公开'}
        onCancel={() => setIsMarketConfirmOpen(false)}
        onClose={() => setIsMarketConfirmOpen(false)}
        onConfirm={handlePublishMarket}
      />
    </div>
  );
};

export default StrategyDetailDialog;



