import React, { useState, useMemo, useEffect } from 'react';
import { useNotification } from './ui';
import StrategyShareDialog, { type SharePolicyPayload } from './StrategyShareDialog';
import StrategyHistoryDialog, { type StrategyHistoryVersion } from './StrategyHistoryDialog';
import AlertDialog from './AlertDialog';
import { getAuthProfile } from '../auth/profileStore';
import { getWsClient, getToken } from '../network';
import { generateReqId } from '../network/requestId';
import { HttpClient } from '../network/httpClient';
import type { StrategyConfig, StrategyRuntimeConfig } from './StrategyModule.types';
import './StrategyDetailDialog.css';

export type StrategyDetailRecord = {
  usId: number;
  defId: number;
  defName: string;
  aliasName: string;
  description: string;
  state: string;
  versionNo: number;
  exchangeApiKeyId?: number | null;
  configJson?: any;
  updatedAt?: string;
  officialDefId?: number | null;
  officialVersionNo?: number | null;
  templateDefId?: number | null;
  templateVersionNo?: number | null;
  marketId?: number | null;
  marketVersionNo?: number | null;
};

export type StrategyPositionRecord = {
  positionId?: number;
  uid?: number;
  usId?: number;
  exchange?: string;
  symbol?: string;
  side?: string;
  status?: string;
  entryPrice?: number;
  qty?: number;
  stopLossPrice?: number;
  takeProfitPrice?: number;
  trailingEnabled?: boolean;
  trailingTriggered?: boolean;
  trailingStopPrice?: number;
  closeReason?: string | null;
  openedAt?: string;
  closedAt?: string | null;
};

export type BacktestOutputOptions = {
  includeTrades: boolean;
  includeEquityCurve: boolean;
  includeEvents: boolean;
  equityCurveGranularity: string;
};

export type BacktestRunPayload = {
  usId?: number;
  configJson?: string;
  executionMode?: string;
  exchange?: string;
  symbols?: string[];
  timeframe?: string;
  startTime?: string;
  endTime?: string;
  barCount?: number;
  initialCapital: number;
  orderQtyOverride?: number;
  leverageOverride?: number;
  takeProfitPctOverride?: number;
  stopLossPctOverride?: number;
  feeRate: number;
  fundingRate: number;
  slippageBps: number;
  autoReverse: boolean;
  runtime?: StrategyRuntimeConfig;
  useStrategyRuntime: boolean;
  output?: BacktestOutputOptions;
};

export type BacktestStats = {
  totalProfit: number;
  totalReturn: number;
  maxDrawdown: number;
  winRate: number;
  tradeCount: number;
  avgProfit: number;
  profitFactor: number;
  avgWin: number;
  avgLoss: number;
  /** 夏普比率（年化，无风险利率=0） */
  sharpeRatio?: number;
  /** Sortino 比率（年化，仅下行波动率） */
  sortinoRatio?: number;
  /** 年化收益率 */
  annualizedReturn?: number;
  /** 最大连续亏损次数 */
  maxConsecutiveLosses?: number;
  /** 最大连续盈利次数 */
  maxConsecutiveWins?: number;
  /** 平均持仓时间（毫秒） */
  avgHoldingMs?: number;
  /** 最大回撤持续时间（毫秒） */
  maxDrawdownDurationMs?: number;
  /** Calmar 比率（年化收益率/最大回撤） */
  calmarRatio?: number;
};

export type BacktestTradeSummary = {
  totalCount: number;
  winCount: number;
  lossCount: number;
  maxProfit: number;
  maxLoss: number;
  totalFee: number;
  firstEntryTime: number;
  lastExitTime: number;
};

export type BacktestEquitySummary = {
  pointCount: number;
  maxEquity: number;
  maxEquityAt: number;
  minEquity: number;
  minEquityAt: number;
  maxPeriodProfit: number;
  maxPeriodProfitAt: number;
  maxPeriodLoss: number;
  maxPeriodLossAt: number;
};

export type BacktestEventSummary = {
  totalCount: number;
  firstTimestamp: number;
  lastTimestamp: number;
  typeCounts: Record<string, number>;
};

export type BacktestTrade = {
  symbol: string;
  side: string;
  entryTime: number;
  exitTime: number;
  entryPrice: number;
  exitPrice: number;
  qty: number;
  contractSize: number;
  fee: number;
  pnL: number;
  exitReason: string;
  slippageBps: number;
};

export type BacktestEquityPoint = {
  timestamp: number;
  equity: number;
  realizedPnl: number;
  unrealizedPnl: number;
  periodRealizedPnl?: number;
  periodUnrealizedPnl?: number;
};

export type BacktestEvent = {
  timestamp: number;
  type: string;
  message: string;
};

export type BacktestSymbolResult = {
  symbol: string;
  bars: number;
  initialCapital: number;
  stats: BacktestStats;
  tradeSummary?: BacktestTradeSummary;
  equitySummary?: BacktestEquitySummary;
  eventSummary?: BacktestEventSummary;
  tradesRaw?: string[];
  equityCurveRaw?: string[];
  eventsRaw?: string[];
};

export type BacktestRunResult = {
  exchange: string;
  timeframe: string;
  equityCurveGranularity?: string;
  startTimestamp: number;
  endTimestamp: number;
  totalBars: number;
  durationMs: number;
  totalStats: BacktestStats;
  symbols: BacktestSymbolResult[];
};

type BacktestProgressPayload = {
  eventKind?: string;
  stage?: string;
  stageName?: string;
  message?: string;
  processedBars?: number;
  totalBars?: number;
  progress?: number;
  elapsedMs?: number;
  foundPositions?: number;
  totalPositions?: number;
  chunkCount?: number;
  winCount?: number;
  lossCount?: number;
  winRate?: number;
  completed?: boolean;
  symbol?: string;
  positions?: BacktestTrade[];
  replacePositions?: boolean;
};

type StrategyDetailDialogProps = {
  strategy: StrategyDetailRecord | null;
  onClose: () => void;
  onCreateVersion: (usId: number) => void;
  onViewHistory: (usId: number) => Promise<StrategyHistoryVersion[]>;
  onCreateShare: (usId: number, payload: SharePolicyPayload) => Promise<string>;
  onUpdateStatus: (usId: number, status: 'running' | 'paused' | 'paused_open_position' | 'completed') => Promise<void>;
  onDelete: (usId: number) => void;
  onEditStrategy: (usId: number) => void;
  onFetchOpenPositionsCount: (usId: number) => Promise<number>;
  onFetchPositions: (usId: number) => Promise<StrategyPositionRecord[]>;
  onClosePositions: (usId: number) => Promise<void>;
  onClosePosition: (positionId: number) => Promise<void>;
  onPublishOfficial: (usId: number) => Promise<void>;
  onPublishTemplate: (usId: number) => Promise<void>;
  onPublishMarket: (usId: number) => Promise<void>;
  onSyncOfficial: (usId: number) => Promise<void>;
  onSyncTemplate: (usId: number) => Promise<void>;
  onSyncMarket: (usId: number) => Promise<void>;
  onRemoveOfficial: (usId: number) => Promise<void>;
  onRemoveTemplate: (usId: number) => Promise<void>;
  onRunBacktest: (payload: BacktestRunPayload, reqId?: string) => Promise<BacktestRunResult>;
};

type TabType = 'info' | 'share' | 'history' | 'positions' | 'backtest';

type BacktestFormState = {
  exchange: string;
  symbols: string;
  timeframe: string;
  rangeMode: 'bars' | 'time';
  startTime: string;
  endTime: string;
  barCount: string;
  initialCapital: string;
  orderQty: string;
  leverage: string;
  takeProfitPct: string;
  stopLossPct: string;
  feeRate: string;
  fundingRate: string;
  slippageBps: string;
  autoReverse: boolean;
  useStrategyRuntime: boolean;
  outputTrades: boolean;
  outputEquity: boolean;
  outputEvents: boolean;
  outputEquityGranularity: string;
};

const equityGranularityOptions = [
  { value: '1m', label: '1分钟' },
  { value: '15m', label: '15分钟' },
  { value: '1h', label: '1小时' },
  { value: '4h', label: '4小时' },
  { value: '1d', label: '1天' },
  { value: '3d', label: '3天' },
  { value: '7d', label: '7天' },
];

type LazyTableProps<T> = {
  rawItems?: string[];
  parseItem: (raw: string) => T | null;
  renderRow: (item: T, index: number) => React.ReactNode;
  columns: React.ReactNode;
  colSpan: number;
  emptyText: string;
  rowHeight?: number;
  overscan?: number;
};

const parseJsonSafe = <T,>(raw: string): T | null => {
  if (!raw) {
    return null;
  }
  try {
    return JSON.parse(raw) as T;
  } catch (err) {
    console.warn('回测数据解析失败', err);
    return null;
  }
};

const LazyTable = <T,>({
  rawItems,
  parseItem,
  renderRow,
  columns,
  colSpan,
  emptyText,
  rowHeight = 28,
  overscan = 20,
}: LazyTableProps<T>) => {
  const items = rawItems ?? [];
  const containerRef = React.useRef<HTMLDivElement | null>(null);
  const cacheRef = React.useRef<Map<number, T>>(new Map());
  const [range, setRange] = useState(() => ({
    start: 0,
    end: Math.min(items.length, overscan * 2),
  }));

  const updateRange = React.useCallback(() => {
    const container = containerRef.current;
    if (!container) {
      return;
    }
    const scrollTop = container.scrollTop;
    const viewportHeight = container.clientHeight;
    const start = Math.max(0, Math.floor(scrollTop / rowHeight) - overscan);
    const end = Math.min(items.length, Math.ceil((scrollTop + viewportHeight) / rowHeight) + overscan);
    setRange((prev) => (prev.start === start && prev.end === end ? prev : { start, end }));
  }, [items.length, overscan, rowHeight]);

  useEffect(() => {
    cacheRef.current.clear();
    setRange({ start: 0, end: Math.min(items.length, overscan * 2) });
  }, [items.length, overscan]);

  useEffect(() => {
    const container = containerRef.current;
    if (!container) {
      return;
    }
    updateRange();
    const onScroll = () => updateRange();
    container.addEventListener('scroll', onScroll, { passive: true });
    window.addEventListener('resize', updateRange);
    return () => {
      container.removeEventListener('scroll', onScroll);
      window.removeEventListener('resize', updateRange);
    };
  }, [updateRange]);

  const visibleItems = useMemo(() => {
    if (items.length === 0) {
      return [];
    }
    const parsed: Array<{ index: number; item: T }> = [];
    for (let i = range.start; i < range.end && i < items.length; i += 1) {
      const cached = cacheRef.current.get(i);
      if (cached) {
        parsed.push({ index: i, item: cached });
        continue;
      }
      const raw = items[i];
      const value = raw ? parseItem(raw) : null;
      if (value) {
        cacheRef.current.set(i, value);
        parsed.push({ index: i, item: value });
      }
    }
    return parsed;
  }, [items, parseItem, range.end, range.start]);

  if (items.length === 0) {
    return <div className="backtest-empty">{emptyText}</div>;
  }

  const topPad = range.start * rowHeight;
  const bottomPad = Math.max(0, items.length - range.end) * rowHeight;
  const startIndex = range.start + 1;
  const endIndex = Math.min(range.end, items.length);

  return (
    <>
      <div className="backtest-table-meta">
        <span>数据量：{items.length}</span>
        <span>
          解析范围：{startIndex}-{endIndex}
        </span>
      </div>
      <div className="backtest-table-wrapper" ref={containerRef}>
        <table className="backtest-table">
          <thead>{columns}</thead>
          <tbody>
            {topPad > 0 && (
              <tr className="backtest-table-spacer">
                <td colSpan={colSpan} style={{ height: topPad }} />
              </tr>
            )}
            {visibleItems.map(({ item, index }) => renderRow(item, index))}
            {bottomPad > 0 && (
              <tr className="backtest-table-spacer">
                <td colSpan={colSpan} style={{ height: bottomPad }} />
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </>
  );
};

const formatTimeframeFromSeconds = (value?: number) => {
  if (!value || value <= 0) {
    return '';
  }
  if (value % 86400 === 0) {
    return `${value / 86400}d`;
  }
  if (value % 3600 === 0) {
    return `${value / 3600}h`;
  }
  if (value % 60 === 0) {
    return `${value / 60}m`;
  }
  return `${value}s`;
};

const buildBacktestDefaults = (config?: StrategyConfig | null): BacktestFormState => {
  const trade = config?.trade;
  return {
    exchange: trade?.exchange ?? '',
    symbols: trade?.symbol ?? '',
    timeframe: formatTimeframeFromSeconds(trade?.timeframeSec),
    rangeMode: 'bars',
    startTime: '',
    endTime: '',
    barCount: '1000',
    initialCapital: '10000',
    orderQty: trade?.sizing?.orderQty !== undefined ? String(trade.sizing.orderQty) : '',
    leverage: trade?.sizing?.leverage !== undefined ? String(trade.sizing.leverage) : '',
    takeProfitPct: trade?.risk?.takeProfitPct !== undefined ? String(trade.risk.takeProfitPct) : '',
    stopLossPct: trade?.risk?.stopLossPct !== undefined ? String(trade.risk.stopLossPct) : '',
    feeRate: '0.0004',
    fundingRate: '0',
    slippageBps: '0',
    autoReverse: false,
    useStrategyRuntime: true,
    outputTrades: true,
    outputEquity: true,
    outputEvents: true,
    outputEquityGranularity: '1m',
  };
};

const StrategyDetailDialog: React.FC<StrategyDetailDialogProps> = ({
  strategy,
  onClose,
  onCreateVersion,
  onViewHistory,
  onCreateShare,
  onUpdateStatus,
  onDelete,
  onEditStrategy,
  onFetchOpenPositionsCount,
  onFetchPositions,
  onClosePositions,
  onClosePosition,
  onPublishOfficial,
  onPublishTemplate,
  onPublishMarket,
  onSyncOfficial,
  onSyncTemplate,
  onSyncMarket,
  onRemoveOfficial,
  onRemoveTemplate,
  onRunBacktest,
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
  const [isEditConfirmOpen, setIsEditConfirmOpen] = useState(false);
  const [openPositionCount, setOpenPositionCount] = useState(0);
  const [isCheckingPositions, setIsCheckingPositions] = useState(false);
  const [syncTarget, setSyncTarget] = useState<'official' | 'template' | 'market' | null>(null);
  const [isSyncing, setIsSyncing] = useState(false);
  const [removeTarget, setRemoveTarget] = useState<'official' | 'template' | null>(null);
  const [isRemoving, setIsRemoving] = useState(false);
  const [positions, setPositions] = useState<StrategyPositionRecord[]>([]);
  const [isPositionsLoading, setIsPositionsLoading] = useState(false);
  const [hasLoadedPositions, setHasLoadedPositions] = useState(false);
  const [isClosePositionsConfirmOpen, setIsClosePositionsConfirmOpen] = useState(false);
  const [isClosingPositions, setIsClosingPositions] = useState(false);
  const [closePositionTarget, setClosePositionTarget] = useState<StrategyPositionRecord | null>(null);
  const [isClosingPosition, setIsClosingPosition] = useState(false);
  const [backtestForm, setBacktestForm] = useState<BacktestFormState>(() => buildBacktestDefaults(null));
  const [backtestResult, setBacktestResult] = useState<BacktestRunResult | null>(null);
  const [backtestError, setBacktestError] = useState<string | null>(null);
  const [isBacktestRunning, setIsBacktestRunning] = useState(false);
  const [backtestProgressStageCode, setBacktestProgressStageCode] = useState<string>('');
  const [backtestProgressStage, setBacktestProgressStage] = useState<string>('');
  const [backtestProgressMessage, setBacktestProgressMessage] = useState<string>('');
  const [backtestProgressPercent, setBacktestProgressPercent] = useState<number | null>(null);
  const [backtestFoundPositions, setBacktestFoundPositions] = useState(0);
  const [backtestTotalPositions, setBacktestTotalPositions] = useState(0);
  const [backtestWinCount, setBacktestWinCount] = useState(0);
  const [backtestLossCount, setBacktestLossCount] = useState(0);
  const [backtestWinRate, setBacktestWinRate] = useState<number | null>(null);
  const [backtestStreamingTrades, setBacktestStreamingTrades] = useState<BacktestTrade[]>([]);
  const [cacheSnapshots, setCacheSnapshots] = useState<Array<{
    exchange: string;
    symbol: string;
    timeframe: string;
    startTime: string;
    endTime: string;
    count: number;
  }>>([]);
  const [isLoadingCache, setIsLoadingCache] = useState(false);
  const httpClient = useMemo(() => new HttpClient({ tokenProvider: getToken }), []);

  const profile = useMemo(() => getAuthProfile(), []);
  const canPublish = profile?.role === 255;
  const resolvedConfig = useMemo<StrategyConfig | null>(() => {
    if (!strategy?.configJson) {
      return null;
    }
    if (typeof strategy.configJson === 'string') {
      try {
        return JSON.parse(strategy.configJson) as StrategyConfig;
      } catch {
        return null;
      }
    }
    return strategy.configJson as StrategyConfig;
  }, [strategy?.configJson]);
  const defaultBacktestForm = useMemo(() => buildBacktestDefaults(resolvedConfig), [resolvedConfig]);

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

  // 加载历史行情缓存数据
  useEffect(() => {
    if (activeTab !== 'backtest') {
      return;
    }
    loadCacheSnapshots();
  }, [activeTab]);

  const loadCacheSnapshots = async () => {
    setIsLoadingCache(true);
    try {
      const response = await httpClient.postProtocol<{ snapshots: Array<{
        exchange: string;
        symbol: string;
        timeframe: string;
        startTime: string;
        endTime: string;
        count: number;
      }> }>(
        '/api/MarketData/cache-snapshots',
        'marketdata.cache.snapshots',
        {}
      );
      setCacheSnapshots(response.snapshots || []);
    } catch (err) {
      console.error('加载缓存数据失败', err);
    } finally {
      setIsLoadingCache(false);
    }
  };

  // 获取可用的交易所列表
  const availableExchanges = useMemo(() => {
    const exchanges = new Set<string>();
    cacheSnapshots.forEach((snapshot) => {
      exchanges.add(snapshot.exchange);
    });
    return Array.from(exchanges).sort();
  }, [cacheSnapshots]);

  // 根据选择的交易所获取可用的币种
  const availableSymbols = useMemo(() => {
    if (!backtestForm.exchange) {
      return [];
    }
    const symbols = new Set<string>();
    cacheSnapshots.forEach((snapshot) => {
      if (snapshot.exchange === backtestForm.exchange) {
        symbols.add(snapshot.symbol);
      }
    });
    return Array.from(symbols).sort();
  }, [cacheSnapshots, backtestForm.exchange]);

  // 根据选择的交易所和币种获取可用的周期
  const availableTimeframes = useMemo(() => {
    if (!backtestForm.exchange || !backtestForm.symbols) {
      return [];
    }
    const symbols = backtestForm.symbols.split(/[,\s]+/).filter(Boolean);
    if (symbols.length === 0) {
      return [];
    }
    const timeframes = new Set<string>();
    cacheSnapshots.forEach((snapshot) => {
      if (snapshot.exchange === backtestForm.exchange && symbols.includes(snapshot.symbol)) {
        timeframes.add(snapshot.timeframe);
      }
    });
    return Array.from(timeframes).sort();
  }, [cacheSnapshots, backtestForm.exchange, backtestForm.symbols]);

  // 获取支持的时间范围（取交集）
  const supportedTimeRange = useMemo(() => {
    if (!backtestForm.exchange || !backtestForm.symbols || !backtestForm.timeframe) {
      return null;
    }
    const symbols = backtestForm.symbols.split(/[,\s]+/).filter(Boolean);
    if (symbols.length === 0) {
      return null;
    }
    let earliestStart: Date | null = null;
    let latestEnd: Date | null = null;
    let foundAny = false;

    cacheSnapshots.forEach((snapshot) => {
      if (
        snapshot.exchange === backtestForm.exchange &&
        symbols.includes(snapshot.symbol) &&
        snapshot.timeframe === backtestForm.timeframe
      ) {
        foundAny = true;
        const start = new Date(snapshot.startTime);
        const end = new Date(snapshot.endTime);
        if (!earliestStart || start > earliestStart) {
          earliestStart = start;
        }
        if (!latestEnd || end < latestEnd) {
          latestEnd = end;
        }
      }
    });

    if (!foundAny || !earliestStart || !latestEnd) {
      return null;
    }

    return {
      start: earliestStart,
      end: latestEnd,
    };
  }, [cacheSnapshots, backtestForm.exchange, backtestForm.symbols, backtestForm.timeframe]);

  // 全量回测功能
  const handleFullRangeBacktest = () => {
    if (!supportedTimeRange) {
      error('请先选择交易所、币种和周期');
      return;
    }
    // 切换到时间范围模式
    updateBacktestField('rangeMode', 'time');
    // 设置时间范围
    const startDate = new Date(supportedTimeRange.start);
    const endDate = new Date(supportedTimeRange.end);
    // 转换为本地时间格式 (YYYY-MM-DDTHH:mm)
    const formatLocalDateTime = (date: Date) => {
      const year = date.getFullYear();
      const month = String(date.getMonth() + 1).padStart(2, '0');
      const day = String(date.getDate()).padStart(2, '0');
      const hours = String(date.getHours()).padStart(2, '0');
      const minutes = String(date.getMinutes()).padStart(2, '0');
      return `${year}-${month}-${day}T${hours}:${minutes}`;
    };
    updateBacktestField('startTime', formatLocalDateTime(startDate));
    updateBacktestField('endTime', formatLocalDateTime(endDate));
    success('已设置为全量回测时间范围');
  };

  useEffect(() => {
    if (!strategy) {
      return;
    }
    setBacktestForm(defaultBacktestForm);
    setBacktestResult(null);
    setBacktestError(null);
    setBacktestProgressStageCode('');
    setBacktestProgressStage('');
    setBacktestProgressMessage('');
    setBacktestProgressPercent(null);
    setBacktestFoundPositions(0);
    setBacktestTotalPositions(0);
    setBacktestWinCount(0);
    setBacktestLossCount(0);
    setBacktestWinRate(null);
    setBacktestStreamingTrades([]);
  }, [defaultBacktestForm, strategy?.usId]);

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
    if (tab === 'positions' && !hasLoadedPositions) {
      handleLoadPositions(false);
    }
  };

  const handleCreateVersion = () => {
    if (strategy) {
      onCreateVersion(strategy.usId);
      onClose();
    }
  };

  const handleEditStrategy = async () => {
    if (!strategy || isCheckingPositions) {
      return;
    }
    setIsCheckingPositions(true);
    try {
      const count = await onFetchOpenPositionsCount(strategy.usId);
      if (count > 0) {
        setOpenPositionCount(count);
        setIsEditConfirmOpen(true);
        return;
      }
      onEditStrategy(strategy.usId);
      onClose();
    } catch (err) {
      const message = err instanceof Error ? err.message : '获取仓位信息失败';
      error(message);
    } finally {
      setIsCheckingPositions(false);
    }
  };

  const handleCloseAllPositions = async () => {
    if (!strategy || isClosingPositions) {
      return;
    }
    setIsClosingPositions(true);
    try {
      await onUpdateStatus(strategy.usId, 'paused');
      await onClosePositions(strategy.usId);
      success('已发起一键平仓');
      await handleLoadPositions(true);
    } catch (err) {
      const message = err instanceof Error ? err.message : '一键平仓失败';
      error(message);
    } finally {
      setIsClosingPositions(false);
      setIsClosePositionsConfirmOpen(false);
    }
  };

  const handleClosePosition = async () => {
    if (!closePositionTarget || isClosingPosition) {
      return;
    }
    if (!closePositionTarget.positionId) {
      error('仓位ID无效');
      setClosePositionTarget(null);
      return;
    }
    setIsClosingPosition(true);
    try {
      await onClosePosition(closePositionTarget.positionId);
      success('已发起平仓');
      await handleLoadPositions(true);
    } catch (err) {
      const message = err instanceof Error ? err.message : '平仓失败';
      error(message);
    } finally {
      setIsClosingPosition(false);
      setClosePositionTarget(null);
    }
  };

  const handleLoadPositions = async (forceReload: boolean) => {
    if (!strategy || isPositionsLoading) {
      return;
    }
    if (!forceReload && hasLoadedPositions) {
      return;
    }
    setIsPositionsLoading(true);
    try {
      const items = await onFetchPositions(strategy.usId);
      setPositions(items);
      setHasLoadedPositions(true);
    } catch (err) {
      const message = err instanceof Error ? err.message : '获取仓位历史失败';
      error(message);
    } finally {
      setIsPositionsLoading(false);
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

  const formatNumber = (value?: number) => {
    if (value === null || value === undefined || Number.isNaN(value)) {
      return '-';
    }
    return value.toFixed(4).replace(/\.?0+$/, '');
  };

  const formatStatus = (status?: string, closeReason?: string | null) => {
    if (!status) {
      return '-';
    }
    const normalized = status.toLowerCase();
    if (normalized === 'open') {
      return '未平仓';
    }
    if (normalized === 'closed') {
      const reasonText = formatCloseReason(closeReason);
      return reasonText !== '-' ? reasonText : '已平仓';
    }
    return status;
  };

  const formatSide = (side?: string) => {
    if (!side) {
      return '-';
    }
    const normalized = side.toLowerCase();
    if (normalized === 'long') {
      return '多';
    }
    if (normalized === 'short') {
      return '空';
    }
    return side;
  };

  const formatBoolean = (value?: boolean) => {
    if (value === null || value === undefined) {
      return '-';
    }
    return value ? '是' : '否';
  };

  const formatCloseReason = (value?: string | null) => {
    if (!value) {
      return '-';
    }
    const normalized = value.toLowerCase();
    if (normalized === 'manualsingle' || normalized === 'manual_single') {
      return '手动平此仓';
    }
    if (normalized === 'manualbatch' || normalized === 'manual_batch') {
      return '批量一键平仓';
    }
    if (normalized === 'manual') {
      return '手动平仓';
    }
    if (normalized === 'stoploss') {
      return '固定止损';
    }
    if (normalized === 'takeprofit') {
      return '固定止盈';
    }
    if (normalized === 'trailingstop') {
      return '移动止盈止损';
    }
    return value;
  };

  const formatDateTimeLocal = (value?: string | null) => {
    if (!value) {
      return '-';
    }
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return value;
    }
    return date.toLocaleString('zh-CN', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hour12: false,
    });
  };

  const formatPercent = (value?: number) => {
    if (value === null || value === undefined || Number.isNaN(value)) {
      return '-';
    }
    const normalized = value * 100;
    return `${normalized.toFixed(2).replace(/\.00$/, '')}%`;
  };

  const formatTimestamp = (value?: number) => {
    if (!value) {
      return '-';
    }
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return String(value);
    }
    return date.toLocaleString('zh-CN', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hour12: false,
    });
  };

  const formatDuration = (value?: number) => {
    if (value === null || value === undefined || Number.isNaN(value)) {
      return '-';
    }
    if (value < 1000) {
      return `${value} ms`;
    }
    const seconds = value / 1000;
    if (seconds < 60) {
      return `${seconds.toFixed(2).replace(/\.00$/, '')} s`;
    }
    const minutes = Math.floor(seconds / 60);
    const remain = seconds % 60;
    return `${minutes} 分 ${remain.toFixed(1).replace(/\.0$/, '')} 秒`;
  };

  const parseSymbols = (value: string) => {
    const items = value
      .split(/[,，\s]+/)
      .map((item) => item.trim())
      .filter((item) => item.length > 0);
    return Array.from(new Set(items));
  };

  const toServerDateTime = (value: string) => {
    const trimmed = value.trim();
    if (!trimmed) {
      return null;
    }
    // 使用本地时间字符串，确保后端解析格式一致
    if (trimmed.includes('T')) {
      const replaced = trimmed.replace('T', ' ');
      return replaced.length === 16 ? `${replaced}:00` : replaced;
    }
    return trimmed;
  };

  const updateBacktestField = <K extends keyof BacktestFormState>(field: K, value: BacktestFormState[K]) => {
    setBacktestForm((prev) => ({
      ...prev,
      [field]: value,
    }));
  };

  const handleResetBacktest = () => {
    setBacktestForm(defaultBacktestForm);
    setBacktestResult(null);
    setBacktestError(null);
    setBacktestProgressStageCode('');
    setBacktestProgressStage('');
    setBacktestProgressMessage('');
    setBacktestProgressPercent(null);
    setBacktestFoundPositions(0);
    setBacktestTotalPositions(0);
    setBacktestWinCount(0);
    setBacktestLossCount(0);
    setBacktestWinRate(null);
    setBacktestStreamingTrades([]);
  };

  const handleRunBacktest = async () => {
    if (!strategy || isBacktestRunning) {
      return;
    }

    const parseNumberValue = (
      raw: string,
      label: string,
      options?: { required?: boolean; min?: number; integer?: boolean },
    ) => {
      const trimmed = raw.trim();
      if (!trimmed) {
        if (options?.required) {
          return { value: null, error: `${label}不能为空` };
        }
        return { value: null };
      }
      const parsed = Number(trimmed);
      if (Number.isNaN(parsed)) {
        return { value: null, error: `${label}格式不正确` };
      }
      if (options?.integer && !Number.isInteger(parsed)) {
        return { value: null, error: `${label}必须为整数` };
      }
      if (options?.min !== undefined && parsed < options.min) {
        return { value: null, error: `${label}必须大于等于 ${options.min}` };
      }
      return { value: parsed };
    };

    const errors: string[] = [];
    const exchange = backtestForm.exchange.trim();
    const timeframe = backtestForm.timeframe.trim();
    const symbols = parseSymbols(backtestForm.symbols);

    const initialCapitalResult = parseNumberValue(backtestForm.initialCapital, '初始资金', { required: true, min: 0 });
    if (initialCapitalResult.error) {
      errors.push(initialCapitalResult.error);
    }
    const feeRateResult = parseNumberValue(backtestForm.feeRate, '手续费率', { required: true, min: 0 });
    if (feeRateResult.error) {
      errors.push(feeRateResult.error);
    }
    const fundingRateResult = parseNumberValue(backtestForm.fundingRate, '资金费率', { required: true });
    if (fundingRateResult.error) {
      errors.push(fundingRateResult.error);
    }
    const slippageResult = parseNumberValue(backtestForm.slippageBps, '滑点Bps', { required: true, integer: true, min: 0 });
    if (slippageResult.error) {
      errors.push(slippageResult.error);
    }

    const orderQtyResult = parseNumberValue(backtestForm.orderQty, '单次下单数量', { min: 0.00000001 });
    if (orderQtyResult.error) {
      errors.push(orderQtyResult.error);
    }
    const leverageResult = parseNumberValue(backtestForm.leverage, '杠杆', { integer: true, min: 1 });
    if (leverageResult.error) {
      errors.push(leverageResult.error);
    }
    const takeProfitResult = parseNumberValue(backtestForm.takeProfitPct, '止盈百分比', { min: 0 });
    if (takeProfitResult.error) {
      errors.push(takeProfitResult.error);
    }
    const stopLossResult = parseNumberValue(backtestForm.stopLossPct, '止损百分比', { min: 0 });
    if (stopLossResult.error) {
      errors.push(stopLossResult.error);
    }

    let startTime: string | null = null;
    let endTime: string | null = null;
    let barCount: number | null = null;
    if (backtestForm.rangeMode === 'time') {
      startTime = toServerDateTime(backtestForm.startTime);
      endTime = toServerDateTime(backtestForm.endTime);
      if (!startTime || !endTime) {
        errors.push('时间范围需同时填写开始与结束时间');
      }
    } else {
      const barCountResult = parseNumberValue(backtestForm.barCount, '回测根数', { required: true, integer: true, min: 1 });
      if (barCountResult.error) {
        errors.push(barCountResult.error);
      } else if (barCountResult.value !== null) {
        barCount = barCountResult.value;
      }
    }

    if (errors.length > 0) {
      const message = errors[0];
      setBacktestError(message);
      error(message);
      return;
    }

    const payload: BacktestRunPayload = {
      usId: strategy.usId,
      executionMode: 'batch_open_close',
      initialCapital: initialCapitalResult.value ?? 0,
      feeRate: feeRateResult.value ?? 0,
      fundingRate: fundingRateResult.value ?? 0,
      slippageBps: slippageResult.value ? Math.trunc(slippageResult.value) : 0,
      autoReverse: backtestForm.autoReverse,
      useStrategyRuntime: backtestForm.useStrategyRuntime,
      output: {
        includeTrades: backtestForm.outputTrades,
        includeEquityCurve: backtestForm.outputEquity,
        includeEvents: backtestForm.outputEvents,
        equityCurveGranularity: backtestForm.outputEquityGranularity,
      },
    };

    if (exchange) {
      payload.exchange = exchange;
    }
    if (timeframe) {
      payload.timeframe = timeframe;
    }
    if (symbols.length > 0) {
      payload.symbols = symbols;
    }
    if (backtestForm.rangeMode === 'time') {
      payload.startTime = startTime ?? undefined;
      payload.endTime = endTime ?? undefined;
    } else if (barCount !== null) {
      payload.barCount = barCount;
    }
    if (orderQtyResult.value !== null) {
      payload.orderQtyOverride = orderQtyResult.value;
    }
    if (leverageResult.value !== null) {
      payload.leverageOverride = Math.trunc(leverageResult.value);
    }
    if (takeProfitResult.value !== null) {
      payload.takeProfitPctOverride = takeProfitResult.value;
    }
    if (stopLossResult.value !== null) {
      payload.stopLossPctOverride = stopLossResult.value;
    }

    const reqId = generateReqId();
    const ws = getWsClient();
    const unsubscribeProgress = ws.on<BacktestProgressPayload>('backtest.progress', (message) => {
      if (message.reqId !== reqId) {
        return;
      }

      const payload = (message.data ?? null) as BacktestProgressPayload | null;
      if (!payload) {
        return;
      }

      if (payload.stage) {
        setBacktestProgressStageCode(payload.stage);
      }
      if (payload.stageName) {
        setBacktestProgressStage(payload.stageName);
      }
      if (payload.message) {
        setBacktestProgressMessage(payload.message);
      }
      if (typeof payload.progress === 'number') {
        setBacktestProgressPercent(payload.progress);
      }
      if (typeof payload.foundPositions === 'number') {
        setBacktestFoundPositions(payload.foundPositions);
      }
      if (typeof payload.totalPositions === 'number') {
        setBacktestTotalPositions(payload.totalPositions);
      }
      if (typeof payload.winCount === 'number') {
        setBacktestWinCount(payload.winCount);
      }
      if (typeof payload.lossCount === 'number') {
        setBacktestLossCount(payload.lossCount);
      }
      if (typeof payload.winRate === 'number') {
        setBacktestWinRate(payload.winRate);
      }

      if (Array.isArray(payload.positions) && payload.positions.length > 0) {
        setBacktestStreamingTrades((prev) => {
          const previewLimit = 100;
          if (payload.replacePositions) {
            return (payload.positions ?? []).slice(0, previewLimit);
          }
          const merged = prev.concat(payload.positions ?? []);
          if (merged.length <= previewLimit) {
            return merged;
          }
          return merged.slice(merged.length - previewLimit);
        });
      }
    });

    setIsBacktestRunning(true);
    setBacktestError(null);
    setBacktestProgressStageCode('');
    setBacktestProgressStage('准备中');
    setBacktestProgressMessage('等待回测任务启动');
    setBacktestProgressPercent(0);
    setBacktestFoundPositions(0);
    setBacktestTotalPositions(0);
    setBacktestWinCount(0);
    setBacktestLossCount(0);
    setBacktestWinRate(null);
    setBacktestStreamingTrades([]);
    try {
      const result = await onRunBacktest(payload, reqId);
      setBacktestResult(result);
      success('回测完成');
    } catch (err) {
      const message = err instanceof Error ? err.message : '回测失败';
      setBacktestError(message);
      error(message);
    } finally {
      unsubscribeProgress();
      setIsBacktestRunning(false);
    }
  };

  const formatDurationMs = (ms?: number) => {
    if (ms === null || ms === undefined || Number.isNaN(ms)) {
      return '-';
    }
    return formatDuration(ms);
  };

  const backtestProgressCountLabel = useMemo(() => {
    if (backtestProgressStageCode === 'batch_open_phase') {
      return '已检测开仓';
    }
    if (backtestProgressStageCode === 'batch_close_phase') {
      return '已检测平仓';
    }
    if (backtestProgressStageCode === 'collect_positions') {
      return '已汇总仓位';
    }
    return '已处理数量';
  }, [backtestProgressStageCode]);

  const backtestProgressCountValue = backtestTotalPositions > 0
    ? `${backtestFoundPositions} / ${backtestTotalPositions}`
    : `${backtestFoundPositions}`;

  const renderStats = (stats: BacktestStats) => (
    <>
      <div className="backtest-stats-grid">
        <div className="backtest-stat">
          <span className="backtest-stat-label">总收益</span>
          <span className="backtest-stat-value">{formatNumber(stats.totalProfit)}</span>
        </div>
        <div className="backtest-stat">
          <span className="backtest-stat-label">总收益率</span>
          <span className="backtest-stat-value">{formatPercent(stats.totalReturn)}</span>
        </div>
        <div className="backtest-stat">
          <span className="backtest-stat-label">最大回撤</span>
          <span className="backtest-stat-value">{formatPercent(stats.maxDrawdown)}</span>
        </div>
        <div className="backtest-stat">
          <span className="backtest-stat-label">胜率</span>
          <span className="backtest-stat-value">{formatPercent(stats.winRate)}</span>
        </div>
        <div className="backtest-stat">
          <span className="backtest-stat-label">交易次数</span>
          <span className="backtest-stat-value">{stats.tradeCount}</span>
        </div>
        <div className="backtest-stat">
          <span className="backtest-stat-label">平均收益</span>
          <span className="backtest-stat-value">{formatNumber(stats.avgProfit)}</span>
        </div>
        <div className="backtest-stat">
          <span className="backtest-stat-label">盈亏比</span>
          <span className="backtest-stat-value">{formatNumber(stats.profitFactor)}</span>
        </div>
        <div className="backtest-stat">
          <span className="backtest-stat-label">平均盈利/亏损</span>
          <span className="backtest-stat-value">
            {formatNumber(stats.avgWin)} / {formatNumber(stats.avgLoss)}
          </span>
        </div>
      </div>
      <div className="backtest-section backtest-section--advanced">
        <div className="backtest-section-title">高级指标</div>
        <div className="backtest-stats-grid">
          <div className="backtest-stat">
            <span className="backtest-stat-label">夏普比率</span>
            <span className="backtest-stat-value">{formatNumber(stats.sharpeRatio)}</span>
          </div>
          <div className="backtest-stat">
            <span className="backtest-stat-label">Sortino 比率</span>
            <span className="backtest-stat-value">{formatNumber(stats.sortinoRatio)}</span>
          </div>
          <div className="backtest-stat">
            <span className="backtest-stat-label">年化收益率</span>
            <span className="backtest-stat-value">{formatPercent(stats.annualizedReturn)}</span>
          </div>
          <div className="backtest-stat">
            <span className="backtest-stat-label">Calmar 比率</span>
            <span className="backtest-stat-value">{formatNumber(stats.calmarRatio)}</span>
          </div>
          <div className="backtest-stat">
            <span className="backtest-stat-label">最大连续亏损次数</span>
            <span className="backtest-stat-value">{stats.maxConsecutiveLosses ?? '-'}</span>
          </div>
          <div className="backtest-stat">
            <span className="backtest-stat-label">最大连续盈利次数</span>
            <span className="backtest-stat-value">{stats.maxConsecutiveWins ?? '-'}</span>
          </div>
          <div className="backtest-stat">
            <span className="backtest-stat-label">平均持仓时间</span>
            <span className="backtest-stat-value">{formatDurationMs(stats.avgHoldingMs)}</span>
          </div>
          <div className="backtest-stat">
            <span className="backtest-stat-label">最大回撤持续时间</span>
            <span className="backtest-stat-value">{formatDurationMs(stats.maxDrawdownDurationMs)}</span>
          </div>
        </div>
      </div>
    </>
  );

  const renderTradeSummary = (summary?: BacktestTradeSummary) => {
    if (!summary) {
      return null;
    }
    return (
      <div className="backtest-summary-row">
        <span>总数：{summary.totalCount}</span>
        <span>
          胜/负：{summary.winCount}/{summary.lossCount}
        </span>
        <span>最大盈利：{formatNumber(summary.maxProfit)}</span>
        <span>最大亏损：{formatNumber(summary.maxLoss)}</span>
        <span>手续费：{formatNumber(summary.totalFee)}</span>
      </div>
    );
  };

  const renderEquitySummary = (summary?: BacktestEquitySummary) => {
    if (!summary) {
      return null;
    }
    return (
      <div className="backtest-summary-row">
        <span>点数：{summary.pointCount}</span>
        <span>
          最大盈利：{formatNumber(summary.maxPeriodProfit)} @ {formatTimestamp(summary.maxPeriodProfitAt)}
        </span>
        <span>
          最大亏损：{formatNumber(summary.maxPeriodLoss)} @ {formatTimestamp(summary.maxPeriodLossAt)}
        </span>
        <span>
          最高权益：{formatNumber(summary.maxEquity)} @ {formatTimestamp(summary.maxEquityAt)}
        </span>
        <span>
          最低权益：{formatNumber(summary.minEquity)} @ {formatTimestamp(summary.minEquityAt)}
        </span>
      </div>
    );
  };

  const renderEventSummary = (summary?: BacktestEventSummary) => {
    if (!summary) {
      return null;
    }
    const topTypes = Object.entries(summary.typeCounts ?? {})
      .sort((a, b) => b[1] - a[1])
      .slice(0, 3);
    return (
      <div className="backtest-summary-row">
        <span>总数：{summary.totalCount}</span>
        <span>
          时间范围：{formatTimestamp(summary.firstTimestamp)} ~ {formatTimestamp(summary.lastTimestamp)}
        </span>
        <span>
          类型分布：{topTypes.length === 0 ? '-' : topTypes.map(([key, value]) => `${key}:${value}`).join(' / ')}
        </span>
      </div>
    );
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
          鍩烘湰淇℃伅
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
        <button
          type="button"
          className={`strategy-detail-tab ${activeTab === 'positions' ? 'is-active' : ''}`}
          onClick={() => handleTabChange('positions')}
        >
          仓位
        </button>
        <button
          type="button"
          className={`strategy-detail-tab ${activeTab === 'backtest' ? 'is-active' : ''}`}
          onClick={() => handleTabChange('backtest')}
        >
          回测
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
              <div className="strategy-detail-actions strategy-detail-actions--secondary">
                <button
                  type="button"
                  className="strategy-detail-action-btn"
                  onClick={handleEditStrategy}
                  disabled={isCheckingPositions}
                >
                  {isCheckingPositions ? '检查仓位中...' : '修改策略'}
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

        {activeTab === 'positions' && (
          <div className="strategy-detail-positions">
            <div className="strategy-detail-positions-card">
              <div className="strategy-detail-positions-header">
                <div>
                  <div className="strategy-detail-positions-title">仓位历史</div>
                  <div className="strategy-detail-positions-hint">
                    协议请求：POST /api/positions/by-strategy（type=position.list.by_strategy）
                  </div>
                </div>
                <div className="strategy-detail-positions-actions">
                  <button
                    type="button"
                    className="strategy-detail-positions-action strategy-detail-positions-action--danger"
                    onClick={() => setIsClosePositionsConfirmOpen(true)}
                    disabled={isPositionsLoading || isClosingPositions}
                  >
                    一键平仓
                  </button>
                  <button
                    type="button"
                    className="strategy-detail-positions-action"
                    onClick={() => handleLoadPositions(true)}
                    disabled={isPositionsLoading}
                  >
                    {isPositionsLoading ? '加载中...' : '刷新'}
                  </button>
                </div>
              </div>
              {isPositionsLoading ? (
                <div className="strategy-detail-empty">加载中...</div>
              ) : positions.length === 0 ? (
                <div className="strategy-detail-empty">暂无仓位记录</div>
              ) : (
                <div className="strategy-detail-positions-table">
                  <div className="positions-table-header">
                    <div className="positions-table-cell">仓位ID</div>
                    <div className="positions-table-cell">浜ゆ槗鎵€</div>
                    <div className="positions-table-cell">浜ゆ槗瀵</div>
                    <div className="positions-table-cell">方向</div>
                    <div className="positions-table-cell">鐘舵€</div>
                    <div className="positions-table-cell">开仓价</div>
                    <div className="positions-table-cell">数量</div>
                    <div className="positions-table-cell">止损价</div>
                    <div className="positions-table-cell">止盈价</div>
                    <div className="positions-table-cell">启用移动止盈</div>
                    <div className="positions-table-cell">已触发</div>
                    <div className="positions-table-cell">绉诲姩止损价</div>
                    <div className="positions-table-cell">骞充粨原因</div>
                    <div className="positions-table-cell">开仓时间</div>
                    <div className="positions-table-cell">平仓时间</div>
                    <div className="positions-table-cell">操作</div>
                  </div>
                  <div className="positions-table-body">
                    {positions.map((position, index) => {
                      const isOpenPosition = position.status?.toLowerCase() === 'open';
                      return (
                        <div
                          className="positions-table-row"
                          key={position.positionId ?? `${position.openedAt ?? 'pos'}-${index}`}
                        >
                          <div className="positions-table-cell">{position.positionId ?? '-'}</div>
                          <div className="positions-table-cell">{position.exchange ?? '-'}</div>
                          <div className="positions-table-cell">{position.symbol ?? '-'}</div>
                          <div className="positions-table-cell">{formatSide(position.side)}</div>
                          <div className="positions-table-cell">{formatStatus(position.status, position.closeReason)}</div>
                          <div className="positions-table-cell">{formatNumber(position.entryPrice)}</div>
                          <div className="positions-table-cell">{formatNumber(position.qty)}</div>
                          <div className="positions-table-cell">{formatNumber(position.stopLossPrice)}</div>
                          <div className="positions-table-cell">{formatNumber(position.takeProfitPrice)}</div>
                          <div className="positions-table-cell">{formatBoolean(position.trailingEnabled)}</div>
                          <div className="positions-table-cell">{formatBoolean(position.trailingTriggered)}</div>
                          <div className="positions-table-cell">{formatNumber(position.trailingStopPrice)}</div>
                          <div className="positions-table-cell">{formatCloseReason(position.closeReason)}</div>
                          <div className="positions-table-cell">{formatDateTimeLocal(position.openedAt)}</div>
                          <div className="positions-table-cell">{formatDateTimeLocal(position.closedAt)}</div>
                          <div className="positions-table-cell positions-table-cell--actions">
                            {isOpenPosition ? (
                              <button
                                type="button"
                                className="positions-table-action positions-table-action--danger"
                                onClick={() => setClosePositionTarget(position)}
                                disabled={isClosingPosition || isClosingPositions}
                              >
                                平掉此仓
                              </button>
                            ) : (
                              <span className="positions-table-action positions-table-action--muted">-</span>
                            )}
                          </div>
                        </div>
                      );
                    })}
                  </div>
                </div>
              )}
            </div>
          </div>
        )}

        {activeTab === 'backtest' && (
          <div className="strategy-detail-backtest">
            <div className="backtest-layout">
              <div className="backtest-card">
                <div className="backtest-card-title">回测参数</div>
                <div className="backtest-form">
                  <div className="backtest-section">
                    <div className="backtest-section-title">鍩虹淇℃伅</div>
                    <div className="backtest-form-grid">
                      <label className="backtest-field">
                        <span className="backtest-label">交易所</span>
                        <select
                          className="backtest-select"
                          value={backtestForm.exchange}
                          onChange={(event) => updateBacktestField('exchange', event.target.value)}
                        >
                          <option value="">请选择</option>
                          {availableExchanges.map((exchange) => (
                            <option key={exchange} value={exchange}>
                              {exchange}
                            </option>
                          ))}
                        </select>
                      </label>
                      <label className="backtest-field">
                        <span className="backtest-label">周期</span>
                        <select
                          className="backtest-select"
                          value={backtestForm.timeframe}
                          onChange={(event) => updateBacktestField('timeframe', event.target.value)}
                          disabled={!backtestForm.exchange || availableTimeframes.length === 0}
                        >
                          <option value="">请选择</option>
                          {availableTimeframes.map((timeframe) => (
                            <option key={timeframe} value={timeframe}>
                              {timeframe}
                            </option>
                          ))}
                        </select>
                      </label>
                      <label className="backtest-field backtest-field--full">
                        <span className="backtest-label">标的列表</span>
                        <div className="backtest-symbols-input-wrapper">
                          <input
                            className="backtest-input"
                            placeholder="BTC/USDT, ETH/USDT"
                            value={backtestForm.symbols}
                            onChange={(event) => updateBacktestField('symbols', event.target.value)}
                            disabled={!backtestForm.exchange}
                          />
                          {backtestForm.exchange && availableSymbols.length > 0 && (
                            <div className="backtest-symbols-suggestions">
                              {availableSymbols.map((symbol) => {
                                const symbols = backtestForm.symbols.split(/[,\s]+/).filter(Boolean);
                                const isSelected = symbols.includes(symbol);
                                return (
                                  <button
                                    key={symbol}
                                    type="button"
                                    className={`backtest-symbol-tag ${isSelected ? 'selected' : ''}`}
                                    onClick={() => {
                                      const symbols = backtestForm.symbols.split(/[,\s]+/).filter(Boolean);
                                      if (isSelected) {
                                        const newSymbols = symbols.filter((s) => s !== symbol);
                                        updateBacktestField('symbols', newSymbols.join(', '));
                                      } else {
                                        symbols.push(symbol);
                                        updateBacktestField('symbols', symbols.join(', '));
                                      }
                                    }}
                                  >
                                    {symbol}
                                  </button>
                                );
                              })}
                            </div>
                          )}
                        </div>
                        <span className="backtest-hint">多个标的用逗号或空格分隔，或点击下方标签选择</span>
                      </label>
                    </div>
                  </div>

                  <div className="backtest-section">
                    <div className="backtest-section-title">时间范围</div>
                    {supportedTimeRange && (
                      <div className="backtest-time-range-info">
                        <span className="backtest-time-range-label">支持的回测时间范围：</span>
                        <span className="backtest-time-range-value">
                          {supportedTimeRange.start.toLocaleString('zh-CN', {
                            year: 'numeric',
                            month: '2-digit',
                            day: '2-digit',
                            hour: '2-digit',
                            minute: '2-digit',
                          })}{' '}
                          ~{' '}
                          {supportedTimeRange.end.toLocaleString('zh-CN', {
                            year: 'numeric',
                            month: '2-digit',
                            day: '2-digit',
                            hour: '2-digit',
                            minute: '2-digit',
                          })}
                        </span>
                        <button
                          type="button"
                          className="backtest-full-range-btn"
                          onClick={handleFullRangeBacktest}
                        >
                          全量回测
                        </button>
                      </div>
                    )}
                    <div className="backtest-form-grid">
                      <label className="backtest-field">
                        <span className="backtest-label">回测方式</span>
                        <select
                          className="backtest-select"
                          value={backtestForm.rangeMode}
                          onChange={(event) =>
                            updateBacktestField('rangeMode', event.target.value as BacktestFormState['rangeMode'])
                          }
                        >
                          <option value="bars">按根数</option>
                          <option value="time">按时间范围</option>
                        </select>
                      </label>
                      {backtestForm.rangeMode === 'bars' ? (
                        <label className="backtest-field">
                          <span className="backtest-label">回测根数</span>
                          <input
                            className="backtest-input"
                            type="number"
                            min={1}
                            value={backtestForm.barCount}
                            onChange={(event) => updateBacktestField('barCount', event.target.value)}
                          />
                        </label>
                      ) : (
                        <>
                          <label className="backtest-field">
                            <span className="backtest-label">开始时间</span>
                            <input
                              className="backtest-input"
                              type="datetime-local"
                              value={backtestForm.startTime}
                              onChange={(event) => updateBacktestField('startTime', event.target.value)}
                            />
                          </label>
                          <label className="backtest-field">
                            <span className="backtest-label">结束时间</span>
                            <input
                              className="backtest-input"
                              type="datetime-local"
                              value={backtestForm.endTime}
                              onChange={(event) => updateBacktestField('endTime', event.target.value)}
                            />
                          </label>
                        </>
                      )}
                    </div>
                    <div className="backtest-hint">按时间范围时使用本地时间格式，需同时填写开始与结束</div>
                  </div>

                  <div className="backtest-section">
                    <div className="backtest-section-title">资金与交易</div>
                    <div className="backtest-form-grid">
                      <label className="backtest-field">
                        <span className="backtest-label">初始资金</span>
                        <input
                          className="backtest-input"
                          type="number"
                          min={0}
                          step="0.01"
                          value={backtestForm.initialCapital}
                          onChange={(event) => updateBacktestField('initialCapital', event.target.value)}
                        />
                      </label>
                      <label className="backtest-field">
                        <span className="backtest-label">单次下单数量</span>
                        <input
                          className="backtest-input"
                          type="number"
                          min={0}
                          step="0.0001"
                          value={backtestForm.orderQty}
                          onChange={(event) => updateBacktestField('orderQty', event.target.value)}
                        />
                      </label>
                      <label className="backtest-field">
                        <span className="backtest-label">杠杆</span>
                        <input
                          className="backtest-input"
                          type="number"
                          min={1}
                          step="1"
                          value={backtestForm.leverage}
                          onChange={(event) => updateBacktestField('leverage', event.target.value)}
                        />
                      </label>
                    </div>
                  </div>

                  <div className="backtest-section">
                    <div className="backtest-section-title">止盈止损</div>
                    <div className="backtest-form-grid">
                      <label className="backtest-field">
                        <span className="backtest-label">止盈百分比</span>
                        <input
                          className="backtest-input"
                          type="number"
                          min={0}
                          step="0.0001"
                          value={backtestForm.takeProfitPct}
                          onChange={(event) => updateBacktestField('takeProfitPct', event.target.value)}
                        />
                      </label>
                      <label className="backtest-field">
                        <span className="backtest-label">止损百分比</span>
                        <input
                          className="backtest-input"
                          type="number"
                          min={0}
                          step="0.0001"
                          value={backtestForm.stopLossPct}
                          onChange={(event) => updateBacktestField('stopLossPct', event.target.value)}
                        />
                      </label>
                    </div>
                    <div className="backtest-hint">小数形式，例如 0.02 表示 2%</div>
                  </div>

                  <div className="backtest-section">
                    <div className="backtest-section-title">费用与滑点</div>
                    <div className="backtest-form-grid">
                      <label className="backtest-field">
                        <span className="backtest-label">手续费率</span>
                        <input
                          className="backtest-input"
                          type="number"
                          min={0}
                          step="0.0001"
                          value={backtestForm.feeRate}
                          onChange={(event) => updateBacktestField('feeRate', event.target.value)}
                        />
                      </label>
                      <label className="backtest-field">
                        <span className="backtest-label">资金费率</span>
                        <input
                          className="backtest-input"
                          type="number"
                          step="0.0001"
                          value={backtestForm.fundingRate}
                          onChange={(event) => updateBacktestField('fundingRate', event.target.value)}
                        />
                      </label>
                      <label className="backtest-field">
                        <span className="backtest-label">滑点Bps</span>
                        <input
                          className="backtest-input"
                          type="number"
                          min={0}
                          step="1"
                          value={backtestForm.slippageBps}
                          onChange={(event) => updateBacktestField('slippageBps', event.target.value)}
                        />
                      </label>
                    </div>
                    <div className="backtest-hint">手续费率默认 0.0004（0.04%）</div>
                  </div>

                  <div className="backtest-section">
                    <div className="backtest-section-title">运行控制</div>
                    <div className="backtest-toggle-row">
                      <label className="backtest-toggle">
                        <input
                          type="checkbox"
                          checked={backtestForm.autoReverse}
                          onChange={(event) => updateBacktestField('autoReverse', event.target.checked)}
                        />
                        <span>自动反向</span>
                      </label>
                      <label className="backtest-toggle">
                        <input
                          type="checkbox"
                          checked={backtestForm.useStrategyRuntime}
                          onChange={(event) => updateBacktestField('useStrategyRuntime', event.target.checked)}
                        />
                        <span>启用策略运行时间</span>
                      </label>
                    </div>
                  </div>

                  <div className="backtest-section">
                    <div className="backtest-section-title">输出选项</div>
                    <div className="backtest-toggle-row">
                      <label className="backtest-toggle">
                        <input
                          type="checkbox"
                          checked={backtestForm.outputTrades}
                          onChange={(event) => updateBacktestField('outputTrades', event.target.checked)}
                        />
                        <span>交易明细</span>
                      </label>
                      <label className="backtest-toggle">
                        <input
                          type="checkbox"
                          checked={backtestForm.outputEquity}
                          onChange={(event) => updateBacktestField('outputEquity', event.target.checked)}
                        />
                        <span>资金曲线</span>
                      </label>
                      <label className="backtest-toggle">
                        <input
                          type="checkbox"
                          checked={backtestForm.outputEvents}
                          onChange={(event) => updateBacktestField('outputEvents', event.target.checked)}
                        />
                        <span>事件日志</span>
                      </label>
                    </div>
                    <div className="backtest-form-grid">
                      <label className="backtest-field">
                        <span className="backtest-label">资金曲线周期</span>
                        <select
                          className="backtest-select"
                          value={backtestForm.outputEquityGranularity}
                          onChange={(event) => updateBacktestField('outputEquityGranularity', event.target.value)}
                          disabled={!backtestForm.outputEquity}
                        >
                          {equityGranularityOptions.map((item) => (
                            <option key={item.value} value={item.value}>
                              {item.label}
                            </option>
                          ))}
                        </select>
                      </label>
                    </div>
                    <div className="backtest-hint">资金曲线按周期聚合输出，显著降低结果体积与内存占用</div>
                  </div>

                  {backtestError && <div className="backtest-error">{backtestError}</div>}

                  <div className="backtest-actions">
                    <button type="button" className="backtest-btn ghost" onClick={handleResetBacktest}>
                      重置
                    </button>
                    <button
                      type="button"
                      className="backtest-btn primary"
                      onClick={handleRunBacktest}
                      disabled={isBacktestRunning}
                    >
                      {isBacktestRunning ? '回测中...' : '开始回测'}
                    </button>
                  </div>
                </div>
              </div>

              <div className="backtest-card">
                <div className="backtest-card-title">回测结果</div>
                {isBacktestRunning ? (
                  <div className="backtest-result">
                    <div className="backtest-empty">回测运行中...</div>
                    <div className="backtest-result-meta">
                      <span>当前阶段：{backtestProgressStage || '-'}</span>
                      <span>阶段说明：{backtestProgressMessage || '-'}</span>
                      <span>阶段进度：{backtestProgressPercent === null ? '-' : formatPercent(backtestProgressPercent)}</span>
                      <span>{backtestProgressCountLabel}：{backtestProgressCountValue}</span>
                      {backtestProgressStageCode === 'batch_close_phase' && (
                        <span>当前胜率：{backtestWinRate === null ? '-' : formatPercent(backtestWinRate)}</span>
                      )}
                      {backtestProgressStageCode === 'batch_close_phase' && (
                        <span>胜/负：{backtestWinCount}/{backtestLossCount}</span>
                      )}
                    </div>
                    {backtestStreamingTrades.length > 0 && (
                      <details className="backtest-details" open>
                        <summary>最近仓位预览（{backtestStreamingTrades.length}）</summary>
                        <div className="backtest-table-wrapper">
                          <table className="backtest-table">
                            <thead>
                              <tr>
                                <th>标的</th>
                                <th>方向</th>
                                <th>开仓时间</th>
                                <th>平仓时间</th>
                                <th>开仓价</th>
                                <th>平仓价</th>
                                <th>数量</th>
                                <th>盈亏</th>
                              </tr>
                            </thead>
                            <tbody>
                              {backtestStreamingTrades.map((trade, tradeIndex) => (
                                <tr key={`${trade.entryTime}-${trade.exitTime}-${tradeIndex}`}>
                                  <td>{trade.symbol || '-'}</td>
                                  <td>{trade.side}</td>
                                  <td>{formatTimestamp(trade.entryTime)}</td>
                                  <td>{formatTimestamp(trade.exitTime)}</td>
                                  <td>{formatNumber(trade.entryPrice)}</td>
                                  <td>{formatNumber(trade.exitPrice)}</td>
                                  <td>{formatNumber(trade.qty)}</td>
                                  <td>{formatNumber(trade.pnL)}</td>
                                </tr>
                              ))}
                            </tbody>
                          </table>
                        </div>
                        <div className="backtest-hint">仅展示最近 100 条仓位预览，完整结果将在回测结束后返回。</div>
                      </details>
                    )}
                  </div>
                ) : backtestResult ? (
                  <div className="backtest-result">
                    <div className="backtest-result-meta">
                      <span>交易所：{backtestResult.exchange || '-'}</span>
                      <span>周期：{backtestResult.timeframe || '-'}</span>
                      <span>资金曲线周期：{backtestResult.equityCurveGranularity || '-'}</span>
                      <span>
                        时间：{formatTimestamp(backtestResult.startTimestamp)} ~ {formatTimestamp(backtestResult.endTimestamp)}
                      </span>
                      <span>总Bar：{backtestResult.totalBars}</span>
                      <span>耗时：{formatDuration(backtestResult.durationMs)}</span>
                    </div>

                    <div className="backtest-section">
                      <div className="backtest-section-title">汇总统计</div>
                      {renderStats(backtestResult.totalStats)}
                    </div>

                    <div className="backtest-section">
                      <div className="backtest-section-title">按标的统计</div>
                      <div className="backtest-symbols">
                        {backtestResult.symbols.length === 0 ? (
                          <div className="backtest-empty">暂无标的结果</div>
                        ) : (
                          backtestResult.symbols.map((symbolResult) => {
                            const tradeCount = symbolResult.tradeSummary?.totalCount ?? symbolResult.tradesRaw?.length ?? 0;
                            const equityCount =
                              symbolResult.equitySummary?.pointCount ?? symbolResult.equityCurveRaw?.length ?? 0;
                            const eventCount = symbolResult.eventSummary?.totalCount ?? symbolResult.eventsRaw?.length ?? 0;

                            return (
                              <div className="backtest-symbol-card" key={symbolResult.symbol}>
                                <div className="backtest-symbol-header">
                                  <div className="backtest-symbol-title">{symbolResult.symbol}</div>
                                  <div className="backtest-symbol-meta">
                                    Bars: {symbolResult.bars} | 初始资金: {formatNumber(symbolResult.initialCapital)}
                                  </div>
                                </div>
                                {renderStats(symbolResult.stats)}

                                <details className="backtest-details">
                                  <summary>交易明细（{tradeCount}）</summary>
                                  {renderTradeSummary(symbolResult.tradeSummary)}
                                  <LazyTable<BacktestTrade>
                                    rawItems={symbolResult.tradesRaw}
                                    parseItem={(raw) => parseJsonSafe<BacktestTrade>(raw)}
                                    colSpan={10}
                                    emptyText="暂无交易"
                                    columns={
                                      <tr>
                                        <th>方向</th>
                                        <th>开仓时间</th>
                                        <th>平仓时间</th>
                                        <th>开仓价</th>
                                        <th>平仓价</th>
                                        <th>数量</th>
                                        <th>手续费</th>
                                        <th>盈亏</th>
                                        <th>原因</th>
                                        <th>滑点Bps</th>
                                      </tr>
                                    }
                                    renderRow={(trade, tradeIndex) => (
                                      <tr key={`${trade.entryTime}-${tradeIndex}`}>
                                        <td>{trade.side}</td>
                                        <td>{formatTimestamp(trade.entryTime)}</td>
                                        <td>{formatTimestamp(trade.exitTime)}</td>
                                        <td>{formatNumber(trade.entryPrice)}</td>
                                        <td>{formatNumber(trade.exitPrice)}</td>
                                        <td>{formatNumber(trade.qty)}</td>
                                        <td>{formatNumber(trade.fee)}</td>
                                        <td>{formatNumber(trade.pnL)}</td>
                                        <td>{trade.exitReason || '-'}</td>
                                        <td>{trade.slippageBps}</td>
                                      </tr>
                                    )}
                                  />
                                </details>

                                <details className="backtest-details">
                                  <summary>资金曲线（{equityCount}）</summary>
                                  {renderEquitySummary(symbolResult.equitySummary)}
                                  <LazyTable<BacktestEquityPoint>
                                    rawItems={symbolResult.equityCurveRaw}
                                    parseItem={(raw) => parseJsonSafe<BacktestEquityPoint>(raw)}
                                    colSpan={6}
                                    emptyText="暂无资金曲线"
                                    columns={
                                      <tr>
                                        <th>时间</th>
                                        <th>权益</th>
                                        <th>已实现</th>
                                        <th>未实现</th>
                                        <th>区间已实现</th>
                                        <th>区间未实现</th>
                                      </tr>
                                    }
                                    renderRow={(point, pointIndex) => (
                                      <tr key={`${point.timestamp}-${pointIndex}`}>
                                        <td>{formatTimestamp(point.timestamp)}</td>
                                        <td>{formatNumber(point.equity)}</td>
                                        <td>{formatNumber(point.realizedPnl)}</td>
                                        <td>{formatNumber(point.unrealizedPnl)}</td>
                                        <td>{formatNumber(point.periodRealizedPnl)}</td>
                                        <td>{formatNumber(point.periodUnrealizedPnl)}</td>
                                      </tr>
                                    )}
                                  />
                                </details>

                                <details className="backtest-details">
                                  <summary>事件日志（{eventCount}）</summary>
                                  {renderEventSummary(symbolResult.eventSummary)}
                                  <LazyTable<BacktestEvent>
                                    rawItems={symbolResult.eventsRaw}
                                    parseItem={(raw) => parseJsonSafe<BacktestEvent>(raw)}
                                    colSpan={3}
                                    emptyText="暂无事件"
                                    columns={
                                      <tr>
                                        <th>时间</th>
                                        <th>类型</th>
                                        <th>内容</th>
                                      </tr>
                                    }
                                    renderRow={(evt, evtIndex) => (
                                      <tr key={`${evt.timestamp}-${evtIndex}`}>
                                        <td>{formatTimestamp(evt.timestamp)}</td>
                                        <td>{evt.type}</td>
                                        <td>{evt.message}</td>
                                      </tr>
                                    )}
                                  />
                                </details>
                              </div>
                            );
                          })
                        )}
                      </div>
                    </div>
                  </div>
                ) : (
                  <div className="backtest-empty">暂无回测结果</div>
                )}
              </div>
            </div>
          </div>
        )}
      </div>
      <AlertDialog
        open={publishTarget !== null}
        title={publishTarget === 'official' ? '发布到官方策略库' : '发布到策略模板库'}
        description={publishTarget === 'official' ? '确认发布到官方策略库吗？发布后其他用户可使用该策略。' : '确认发布到策略模板库吗？发布后其他用户可使用该模板。'}
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
      <AlertDialog
        open={isEditConfirmOpen}
        title="提示"
        description={`当前有 ${openPositionCount} 个仓位未平仓，是否一键平仓后前往编辑？`}
        cancelText="取消"
        confirmText="前往编辑"
        onCancel={() => setIsEditConfirmOpen(false)}
        onClose={() => setIsEditConfirmOpen(false)}
        onConfirm={() => {
          if (strategy) {
            setIsEditConfirmOpen(false);
            onEditStrategy(strategy.usId);
            onClose();
          }
        }}
      />
      <AlertDialog
        open={isClosePositionsConfirmOpen}
        title="一键平仓"
        description="确认将该策略暂停并平掉所有仓位吗？系统将分多空两次平仓。"
        helperText="该操作为人工平仓，将记录为手动平仓。"
        cancelText="取消"
        confirmText={isClosingPositions ? '处理中...' : '确认平仓'}
        danger={true}
        onCancel={() => setIsClosePositionsConfirmOpen(false)}
        onClose={() => setIsClosePositionsConfirmOpen(false)}
        onConfirm={handleCloseAllPositions}
      />
      <AlertDialog
        open={closePositionTarget !== null}
        title="平掉此仓"
        description={
          closePositionTarget
            ? `确认平掉该仓位吗？${closePositionTarget.exchange ?? '-'} ${closePositionTarget.symbol ?? '-'} ${formatSide(closePositionTarget.side)} 数量 ${formatNumber(closePositionTarget.qty)}`
            : undefined
        }
        helperText="该操作为人工平仓，将记录为手动平仓。"
        cancelText="取消"
        confirmText={isClosingPosition ? '处理中...' : '确认平仓'}
        danger={true}
        onCancel={() => setClosePositionTarget(null)}
        onClose={() => setClosePositionTarget(null)}
        onConfirm={handleClosePosition}
      />
    </div>
  );
};

export default StrategyDetailDialog;






