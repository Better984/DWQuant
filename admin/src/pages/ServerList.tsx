import React, { useState, useEffect, useMemo, useCallback } from 'react';
import {
  Card,
  Tag,
  Space,
  Button,
  Descriptions,
  Badge,
  Tabs,
  Input,
  List,
  Empty,
  Modal,
  Table,
  Statistic,
  Row,
  Col,
  Spin,
  Divider,
  Popconfirm,
  Tree,
} from 'antd';
import {
  ReloadOutlined,
  CloudServerOutlined,
  InfoCircleOutlined,
  PlayCircleOutlined,
  DownOutlined,
  UpOutlined,
  DeleteOutlined,
  PartitionOutlined,
  ThunderboltOutlined,
  ApartmentOutlined,
} from '@ant-design/icons';
import { HttpClient } from '../network/httpClient';
import { getToken } from '../network';
import { useNotification } from '../components/ui';
import './ServerList.css';

const { Search } = Input;

interface ProcessInfo {
  processId: number;
  processName: string;
  startTime: string;
  cpuUsage: number;
  memoryUsage: number;
  threadCount: number;
}

interface SystemInfo {
  machineName: string;
  osVersion: string;
  processorCount: number;
  totalMemory: number;
  is64BitProcess: boolean;
  is64BitOperatingSystem: boolean;
}

interface RuntimeInfo {
  dotNetVersion: string;
  frameworkDescription: string;
  clrVersion: string;
}

interface ServerNode {
  nodeId: string;
  machineName: string;
  isCurrentNode: boolean;
  status: string;
  connectionCount: number;
  systems: string[];
  lastHeartbeat: string;
  processInfo?: ProcessInfo;
  systemInfo?: SystemInfo;
  runtimeInfo?: RuntimeInfo;
}

interface Strategy {
  uid?: number;
  usId: number;
  defId: number;
  defName: string;
  aliasName: string;
  description?: string;
  state: string;
  versionNo: number;
  exchangeApiKeyId?: number;
  configJson?: any;
  updatedAt: string;
}

interface Position {
  positionId: number;
  usId: number;
  exchange: string;
  symbol: string;
  side: string;
  quantity: number;
  entryPrice: number;
  currentPrice?: number;
  status: string;
  openedAt: string;
  closedAt?: string;
}

interface CheckLog {
  id: number;
  uid: number;
  usId: number;
  exchange: string;
  symbol: string;
  timeframe: number;
  candleTimestamp: number;
  stage: string;
  groupIndex: number;
  conditionKey: string;
  method: string;
  isRequired: boolean;
  success: boolean;
  message?: string;
  checkProcess?: string;
  createdAt: string;
}

interface RiskPositionSnapshot {
  positionId: number;
  uid: number;
  usId: number;
  exchangeApiKeyId?: number;
  exchange: string;
  symbol: string;
  side: string;
  entryPrice: number;
  qty: number;
  stopLossPrice?: number | null;
  takeProfitPrice?: number | null;
  trailingEnabled: boolean;
  trailingStopPrice?: number | null;
  trailingTriggered: boolean;
  trailingActivationPct?: number | null;
  trailingDrawdownPct?: number | null;
  trailingActivationPrice?: number | null;
  trailingUpdateThresholdPrice?: number | null;
  status: string;
}

interface Level3TreeSnapshot {
  key: number;
  low: number;
  high: number;
  positionIds: number[];
  count: number;
}

interface Level2TreeSnapshot {
  key: number;
  low: number;
  high: number;
  level3: Level3TreeSnapshot[];
  count: number;
}

interface Level1TreeSnapshot {
  key: number;
  low: number;
  high: number;
  level2: Level2TreeSnapshot[];
  count: number;
}

interface ScaleTreeSnapshot {
  scale: number;
  step1: number;
  step2: number;
  step3: number;
  level1: Level1TreeSnapshot[];
  count: number;
}

interface IndexTreeSnapshot {
  indexType: string;
  scales: ScaleTreeSnapshot[];
  count: number;
}

interface PositionRiskIndexSnapshot {
  exchange: string;
  symbol: string;
  totalPositions: number;
  generatedAt: string;
  positions: RiskPositionSnapshot[];
  indexTrees: IndexTreeSnapshot[];
}

interface RiskIndexResponse {
  generatedAt: string;
  items: PositionRiskIndexSnapshot[];
}

interface RiskTreeNodeData {
  positionIds: number[];
  count: number;
  level: 'symbol' | 'index' | 'scale' | 'level1' | 'level2' | 'level3';
  rangeText?: string;
}

/** 实盘布局项：按 exchange/symbol/timeframe 聚合 */
interface TaskTraceLayoutItem {
  exchange: string;
  symbol: string;
  timeframe: string;
  strategyCount: number;
  taskCount: number;
  totalDurationMs: number;
  lastExecutedAt: string;
}

interface MarketByMarketItem {
  exchange: string;
  symbol: string;
  timeframe: string;
  strategyCount: number;
  lastRunAt?: string | null;
}

interface MarketTaskSummary {
  exchange: string;
  symbol: string;
  timeframe: string;
  taskCount: number;
  avgDurationMs: number;
  successRatePct: number;
  stageStats?: { eventStage: string; traceCount: number; avgDurationMs: number }[];
  recentOrders?: {
    orderId?: string | null;
    uid?: number | null;
    usId?: number | null;
    positionSide?: string | null;
    qty?: number | null;
    averagePrice?: number | null;
    createdAt?: string;
  }[];
  recentTasks?: {
    runAt: string;
    traceId?: string;
    runStatus?: string;
    exchange: string;
    symbol: string;
    timeframe: string;
    candleTimestamp: number;
    isBarClose: boolean;
    durationMs: number;
    lookupMs: number;
    indicatorMs: number;
    executeMs: number;
    matchedCount: number;
    runnableStrategyCount: number;
    executedCount: number;
    skippedCount: number;
    conditionEvalCount: number;
    actionExecCount: number;
    openTaskCount: number;
    stateSkippedCount: number;
    runtimeGateSkippedCount: number;
    indicatorRequestCount: number;
    indicatorSuccessCount: number;
    indicatorTotalCount: number;
    executedStrategyIds?: string | null;
    openTaskStrategyIds?: string | null;
    openTaskTraceIds?: string | null;
    openOrderIds?: string | null;
    engineInstance?: string | null;
    perStrategySamples: number;
    perStrategyAvgMs: number;
    perStrategyMaxMs: number;
    successRatePct: number;
    openTaskRatePct: number;
  }[];
}

type MarketRecentTaskItem = NonNullable<MarketTaskSummary['recentTasks']>[number];

interface StrategyRunMetricItem {
  runAt: string;
  exchange: string;
  symbol: string;
  timeframe: string;
  candleTimestamp: number;
  isBarClose: boolean;
  durationMs: number;
  matchedCount: number;
  executedCount: number;
  skippedCount: number;
  conditionEvalCount: number;
  actionExecCount: number;
  openTaskCount: number;
  engineInstance?: string | null;
  traceId?: string | null;
  runStatus?: string | null;
  lookupMs: number;
  indicatorMs: number;
  executeMs: number;
  perStrategySamples: number;
  perStrategyAvgMs: number;
  perStrategyMaxMs: number;
  successRatePct: number;
  openTaskRatePct: number;
}

/** 实盘任务按 trace_id 聚合的摘要（列表展示） */
interface TaskTraceSummaryItem {
  traceId: string;
  exchange?: string | null;
  symbol?: string | null;
  timeframe?: string | null;
  candleTimestamp?: number | null;
  strategyCount: number;
  totalDurationMs: number;
  firstCreatedAt: string;
  lastCreatedAt: string;
}

/** 实盘任务链路追踪明细项（详情弹窗展示） */
interface TaskTraceLogItem {
  id: number;
  traceId: string;
  parentTraceId?: string | null;
  eventStage: string;
  eventStatus: string;
  actorModule: string;
  actorInstance: string;
  uid?: number | null;
  usId?: number | null;
  strategyUid?: string | null;
  exchange?: string | null;
  symbol?: string | null;
  timeframe?: string | null;
  candleTimestamp?: number | null;
  isBarClose?: boolean | null;
  method?: string | null;
  flow?: string | null;
  durationMs?: number | null;
  metricsJson?: string | null;
  errorMessage?: string | null;
  createdAt: string;
}

const ServerList: React.FC = () => {
  const [servers, setServers] = useState<ServerNode[]>([]);
  const [loading, setLoading] = useState(false);
  const [expandedNodeId, setExpandedNodeId] = useState<string | null>(null);
  const [selectedStrategy, setSelectedStrategy] = useState<Strategy | null>(null);
  const [strategyPositions, setStrategyPositions] = useState<Position[]>([]);
  const [positionsLoading, setPositionsLoading] = useState(false);
  const [strategyDetailVisible, setStrategyDetailVisible] = useState(false);
  const [strategyRunMetrics, setStrategyRunMetrics] = useState<StrategyRunMetricItem[]>([]);
  const [strategyRunMetricsLoading, setStrategyRunMetricsLoading] = useState(false);
  const [selectedRunMetric, setSelectedRunMetric] = useState<StrategyRunMetricItem | null>(null);
  const [runMetricDetailVisible, setRunMetricDetailVisible] = useState(false);
  const [checkLogs, setCheckLogs] = useState<CheckLog[]>([]);
  const [checkLogsLoading, setCheckLogsLoading] = useState(false);
  const [checkLogsVisible, setCheckLogsVisible] = useState(false);
  const [selectedCheckLog, setSelectedCheckLog] = useState<CheckLog | null>(null);
  const [checkLogDetailVisible, setCheckLogDetailVisible] = useState(false);
  const [checkLogFilter, setCheckLogFilter] = useState<'all' | 'success' | 'failed'>('all');
  const [clearingLogs, setClearingLogs] = useState(false);
  const [currentStrategyUsId, setCurrentStrategyUsId] = useState<number | null>(null);
  const [riskIndexLoading, setRiskIndexLoading] = useState(false);
  const [riskIndexData, setRiskIndexData] = useState<RiskIndexResponse | null>(null);
  const [riskSelectedPositionIds, setRiskSelectedPositionIds] = useState<number[]>([]);
  const [riskSelectedNodeTitle, setRiskSelectedNodeTitle] = useState<string>('未选择节点');
  const [riskPositionSearch, setRiskPositionSearch] = useState<string>('');
  const [riskPositionDetailVisible, setRiskPositionDetailVisible] = useState(false);
  const [selectedRiskPosition, setSelectedRiskPosition] = useState<RiskPositionSnapshot | null>(null);
  const [taskTraceItems, setTaskTraceItems] = useState<TaskTraceSummaryItem[]>([]);
  const [taskTraceTotal, setTaskTraceTotal] = useState(0);
  const [taskTracePage, setTaskTracePage] = useState(1);
  const [taskTracePageSize, setTaskTracePageSize] = useState(50);
  const [taskTraceLoading, setTaskTraceLoading] = useState(false);
  const [taskTraceMachineName, setTaskTraceMachineName] = useState<string>('');
  const [taskTraceDetailVisible, setTaskTraceDetailVisible] = useState(false);
  const [taskTraceDetailItems, setTaskTraceDetailItems] = useState<TaskTraceLogItem[]>([]);
  const [taskTraceDetailLoading, setTaskTraceDetailLoading] = useState(false);
  const [selectedTaskTraceId, setSelectedTaskTraceId] = useState<string | null>(null);
  const [taskTraceLayoutItems, setTaskTraceLayoutItems] = useState<TaskTraceLayoutItem[]>([]);
  const [taskTraceLayoutLoading, setTaskTraceLayoutLoading] = useState(false);
  const [layoutFilterExchange, setLayoutFilterExchange] = useState('');
  const [layoutFilterSymbol, setLayoutFilterSymbol] = useState('');
  const [layoutFilterTimeframe, setLayoutFilterTimeframe] = useState('');
  const [marketItems, setMarketItems] = useState<MarketByMarketItem[]>([]);
  const [marketLoading, setMarketLoading] = useState(false);
  const [marketMachineName, setMarketMachineName] = useState('');
  const [selectedMarket, setSelectedMarket] = useState<MarketByMarketItem | null>(null);
  const [marketDetailVisible, setMarketDetailVisible] = useState(false);
  const [marketTaskSummary, setMarketTaskSummary] = useState<MarketTaskSummary | null>(null);
  const [marketTaskSummaryLoading, setMarketTaskSummaryLoading] = useState(false);
  const [selectedMarketTask, setSelectedMarketTask] = useState<MarketRecentTaskItem | null>(null);
  const [marketTaskDetailVisible, setMarketTaskDetailVisible] = useState(false);
  const [marketStrategies, setMarketStrategies] = useState<Strategy[]>([]);
  const [marketStrategiesTotal, setMarketStrategiesTotal] = useState(0);
  const [marketStrategiesPage, setMarketStrategiesPage] = useState(1);
  const [marketStrategiesPageSize, setMarketStrategiesPageSize] = useState(20);
  const [marketStrategiesLoading, setMarketStrategiesLoading] = useState(false);
  const [marketStrategiesSearch, setMarketStrategiesSearch] = useState('');

  const client = new HttpClient();
  client.setTokenProvider(getToken);
  const { success, error: showError } = useNotification();

  useEffect(() => {
    loadServers();
    // 每10秒自动刷新
    const interval = setInterval(loadServers, 10000);
    return () => clearInterval(interval);
  }, []);

  useEffect(() => {
    if (expandedNodeId) {
      const server = servers.find((s) => s.nodeId === expandedNodeId);
      if (server) {
        loadMarketByMarket(server.machineName);
      }
    }
  }, [expandedNodeId, servers]);

  const loadServers = async () => {
    setLoading(true);
    try {
      const response = await client.postProtocol<{ servers: ServerNode[] }>(
        '/api/admin/server/list',
        'admin.server.list',
        {}
      );
      setServers(response.servers || []);
    } catch (err) {
      showError(err instanceof Error ? err.message : '加载服务器列表失败');
    } finally {
      setLoading(false);
    }
  };

  const loadMarketByMarket = async (machineName: string) => {
    setMarketLoading(true);
    setMarketMachineName(machineName);
    try {
      const items = await client.postProtocol<MarketByMarketItem[]>(
        '/api/admin/strategy/running/by-market',
        'admin.strategy.running.by-market',
        { machineName }
      );
      setMarketItems(Array.isArray(items) ? items : []);
    } catch (err) {
      showError(err instanceof Error ? err.message : '加载市场列表失败');
    } finally {
      setMarketLoading(false);
    }
  };

  const loadMarketTaskSummary = async (
    machineName: string,
    exchange: string,
    symbol: string,
    timeframe: string
  ) => {
    setMarketTaskSummaryLoading(true);
    try {
      const data = await client.postProtocol<MarketTaskSummary>(
        '/api/admin/strategy/task-trace/market-summary',
        'admin.strategy.task.trace.market-summary',
        { machineName, exchange, symbol, timeframe }
      );
      setMarketTaskSummary(data ?? null);
    } catch (err) {
      showError(err instanceof Error ? err.message : '加载任务报告失败');
    } finally {
      setMarketTaskSummaryLoading(false);
    }
  };

  const loadMarketStrategies = async (
    exchange: string,
    symbol: string,
    timeframe: string,
    page = 1,
    pageSize = 20,
    search = ''
  ) => {
    setMarketStrategiesLoading(true);
    try {
      const data = await client.postProtocol<{ total: number; items: Strategy[] }>(
        '/api/admin/strategy/running/list-by-market',
        'admin.strategy.running.list-by-market',
        { exchange, symbol, timeframe, page, pageSize, search: search || undefined }
      );
      const items = data?.items ?? [];
      const total = data?.total ?? 0;
      setMarketStrategies(Array.isArray(items) ? items : []);
      setMarketStrategiesTotal(typeof total === 'number' ? total : 0);
      setMarketStrategiesPage(page);
      setMarketStrategiesPageSize(pageSize);
    } catch (err) {
      showError(err instanceof Error ? err.message : '加载策略列表失败');
    } finally {
      setMarketStrategiesLoading(false);
    }
  };

  const renderMarketTaskSummary = () => {
    if (!marketTaskSummary) {
      return <Empty description="暂无任务报告数据" />;
    }

    const { taskCount, avgDurationMs, successRatePct, stageStats, recentOrders, recentTasks } = marketTaskSummary;
    const successTasks = Math.round((taskCount * successRatePct) / 100);
    const failedTasks = Math.max(taskCount - successTasks, 0);
    const safeStageStats = Array.isArray(stageStats) ? stageStats : [];
    const totalStageAvgMs = safeStageStats.reduce((sum, s) => sum + (s.avgDurationMs || 0), 0);
    const enrichedStageStats = safeStageStats.map((s) => {
      const coveragePct =
        taskCount > 0 ? Number(((s.traceCount / taskCount) * 100).toFixed(1)) : 0;
      const sharePct =
        totalStageAvgMs > 0 ? Number(((s.avgDurationMs / totalStageAvgMs) * 100).toFixed(1)) : 0;
      return { ...s, coveragePct, sharePct };
    });
    const bottleneckStage =
      enrichedStageStats.length > 0
        ? enrichedStageStats.reduce((max, cur) =>
            cur.avgDurationMs > max.avgDurationMs ? cur : max,
          enrichedStageStats[0])
        : null;

    return (
      <div>
        <Row gutter={16} style={{ marginBottom: 16 }}>
          <Col span={6}>
            <Statistic title="任务数" value={taskCount} />
          </Col>
          <Col span={6}>
            <Statistic title="平均耗时(ms)" value={avgDurationMs} />
          </Col>
          <Col span={6}>
            <Statistic title="成功率" value={successRatePct} suffix="%" precision={2} />
          </Col>
          <Col span={6}>
            <Statistic
              title="成功/失败任务数"
              valueRender={() => (
                <span>
                  {successTasks} / {failedTasks}
                </span>
              )}
            />
          </Col>
        </Row>
        {enrichedStageStats.length > 0 && (
          <>
            <Divider>按阶段统计</Divider>
            <Table
              dataSource={enrichedStageStats}
              rowKey="eventStage"
              size="small"
              pagination={false}
              columns={[
                { title: '阶段', dataIndex: 'eventStage', key: 'eventStage', width: 180 },
                { title: '任务数', dataIndex: 'traceCount', key: 'traceCount', width: 90 },
                {
                  title: '平均耗时(ms)',
                  dataIndex: 'avgDurationMs',
                  key: 'avgDurationMs',
                  width: 110,
                },
                {
                  title: '覆盖率',
                  dataIndex: 'coveragePct',
                  key: 'coveragePct',
                  width: 110,
                  render: (value: number) => `${value}%`,
                },
                {
                  title: '耗时占比',
                  dataIndex: 'sharePct',
                  key: 'sharePct',
                  width: 110,
                  render: (value: number) => `${value}%`,
                },
              ]}
            />
            {bottleneckStage && (
              <div style={{ marginTop: 8, fontSize: 12, color: '#64748b' }}>
                瓶颈阶段：{bottleneckStage.eventStage}（平均 {bottleneckStage.avgDurationMs}ms，
                约占阶段总耗时 {bottleneckStage.sharePct}%）
              </div>
            )}
          </>
        )}
        {Array.isArray(recentOrders) && recentOrders.length > 0 && (
          <>
            <Divider>最近下单记录（样本）</Divider>
            <Table
              dataSource={recentOrders}
              rowKey={(row, index) => `${row.orderId || 'order'}-${index}`}
              size="small"
              pagination={false}
              columns={[
                {
                  title: '时间',
                  dataIndex: 'createdAt',
                  key: 'createdAt',
                  width: 180,
                  render: (value?: string) => (value ? new Date(value).toLocaleString() : '-'),
                },
                { title: '订单ID', dataIndex: 'orderId', key: 'orderId', width: 200 },
                { title: '策略ID(usId)', dataIndex: 'usId', key: 'usId', width: 120 },
                { title: 'UID', dataIndex: 'uid', key: 'uid', width: 120 },
                { title: '方向', dataIndex: 'positionSide', key: 'positionSide', width: 90 },
                { title: '数量', dataIndex: 'qty', key: 'qty', width: 120 },
                { title: '成交价', dataIndex: 'averagePrice', key: 'averagePrice', width: 120 },
              ]}
            />
          </>
        )}
        {Array.isArray(recentTasks) && recentTasks.length > 0 && (
          <>
            <Divider>最近5次任务（主记录）</Divider>
            <Table
              dataSource={recentTasks}
              rowKey={(row, index) => `${row.traceId || row.runAt}-${index}`}
              size="small"
              pagination={false}
              onRow={(row) => ({
                onClick: () => {
                  setSelectedMarketTask(row);
                  setMarketTaskDetailVisible(true);
                },
                style: { cursor: 'pointer' },
              })}
              columns={[
                {
                  title: '时间',
                  dataIndex: 'runAt',
                  key: 'runAt',
                  width: 180,
                  render: (value?: string) => (value ? new Date(value).toLocaleString() : '-'),
                },
                {
                  title: '追踪ID',
                  dataIndex: 'traceId',
                  key: 'traceId',
                  width: 210,
                  ellipsis: true,
                  render: (v?: string) => <code>{v || '-'}</code>,
                },
                {
                  title: '状态',
                  dataIndex: 'runStatus',
                  key: 'runStatus',
                  width: 110,
                  render: (v?: string) => getRunStatusTag(v),
                },
                { title: '总耗时(ms)', dataIndex: 'durationMs', key: 'durationMs', width: 110 },
                {
                  title: '分段(ms)',
                  key: 'segments',
                  width: 180,
                  render: (_: unknown, row: NonNullable<MarketTaskSummary['recentTasks']>[number]) =>
                    `查找 ${row.lookupMs} / 指标 ${row.indicatorMs} / 执行 ${row.executeMs}`,
                },
                {
                  title: '匹配/执行',
                  key: 'matchExecute',
                  width: 120,
                  render: (_: unknown, row: NonNullable<MarketTaskSummary['recentTasks']>[number]) =>
                    `${row.matchedCount} / ${row.executedCount}`,
                },
                { title: '开仓任务数', dataIndex: 'openTaskCount', key: 'openTaskCount', width: 100 },
                {
                  title: '执行成功率',
                  dataIndex: 'successRatePct',
                  key: 'successRatePct',
                  width: 110,
                  render: (value: number) => `${value}%`,
                },
                {
                  title: '操作',
                  key: 'action',
                  width: 100,
                  render: (_: unknown, row: NonNullable<MarketTaskSummary['recentTasks']>[number]) => (
                    <Button
                      type="link"
                      size="small"
                      onClick={(e) => {
                        e.stopPropagation();
                        setSelectedMarketTask(row);
                        setMarketTaskDetailVisible(true);
                      }}
                    >
                      查看详情
                    </Button>
                  ),
                },
              ]}
            />
          </>
        )}
      </div>
    );
  };

  const handleMarketRowClick = (market: MarketByMarketItem) => {
    setSelectedMarket(market);
    setMarketDetailVisible(true);
    setMarketTaskSummary(null);
    setMarketStrategies([]);
    setMarketStrategiesTotal(0);
    setMarketStrategiesPage(1);
    if (marketMachineName) {
      loadMarketTaskSummary(marketMachineName, market.exchange, market.symbol, market.timeframe);
      loadMarketStrategies(market.exchange, market.symbol, market.timeframe, 1, 20, '');
    }
  };

  const loadStrategyPositions = async (usId: number) => {
    setPositionsLoading(true);
    try {
      const response = await client.postProtocol<{ items: Position[] }>(
        '/api/positions/by-strategy',
        'position.list.by_strategy',
        { usId, status: 'all' }
      );
      setStrategyPositions(response.items || []);
    } catch (err) {
      showError(err instanceof Error ? err.message : '加载仓位历史失败');
    } finally {
      setPositionsLoading(false);
    }
  };

  const loadStrategyRunMetrics = async (usId: number) => {
    const machineName =
      marketMachineName || servers.find((s) => s.nodeId === expandedNodeId)?.machineName || '';
    if (!machineName) {
      setStrategyRunMetrics([]);
      return;
    }

    setStrategyRunMetricsLoading(true);
    try {
      const data = await client.postProtocol<StrategyRunMetricItem[]>(
        '/api/admin/strategy/run-metrics/recent',
        'admin.strategy.run.metrics.recent',
        { machineName, usId, limit: 5 }
      );
      setStrategyRunMetrics(Array.isArray(data) ? data : []);
    } catch (err) {
      showError(err instanceof Error ? err.message : '加载策略运行画像失败');
      setStrategyRunMetrics([]);
    } finally {
      setStrategyRunMetricsLoading(false);
    }
  };

  const loadCheckLogs = async (usId: number) => {
    setCheckLogsLoading(true);
    setCurrentStrategyUsId(usId);
    try {
      const data = await client.postProtocol<CheckLog[]>(
        '/api/admin/strategy-check/list',
        'admin.strategy.check.list',
        { usId, limit: 1000 }
      );
      setCheckLogs(Array.isArray(data) ? data : []);
      setCheckLogsVisible(true);
    } catch (err) {
      showError(err instanceof Error ? err.message : '加载检查日志失败');
    } finally {
      setCheckLogsLoading(false);
    }
  };

  const loadRiskIndex = async () => {
    setRiskIndexLoading(true);
    try {
      const response = await client.postProtocol<RiskIndexResponse>(
        '/api/admin/position-risk/index',
        'admin.position.risk.index',
        {}
      );
      setRiskIndexData(response);
      setRiskSelectedPositionIds([]);
      setRiskSelectedNodeTitle('未选择节点');
      setRiskPositionSearch('');
    } catch (err) {
      showError(err instanceof Error ? err.message : '加载风控索引失败');
    } finally {
      setRiskIndexLoading(false);
    }
  };

  const loadTaskTrace = async (machineName: string, page = 1, pageSize = 50) => {
    setTaskTraceLoading(true);
    setTaskTraceMachineName(machineName);
    try {
      const data = await client.postProtocol<{ total: number; items: TaskTraceSummaryItem[] }>(
        '/api/admin/strategy/task-trace/list',
        'admin.strategy.task.trace.list',
        { machineName, page, pageSize: Math.min(pageSize, 100) }
      );
      const items = data?.items ?? [];
      const total = data?.total ?? 0;
      setTaskTraceItems(Array.isArray(items) ? items : []);
      setTaskTraceTotal(typeof total === 'number' ? total : 0);
      setTaskTracePage(page);
      setTaskTracePageSize(Math.min(pageSize, 100));
    } catch (err) {
      showError(err instanceof Error ? err.message : '加载实盘任务详情失败');
    } finally {
      setTaskTraceLoading(false);
    }
  };

  const loadTaskTraceLayout = async (machineName: string) => {
    setTaskTraceLayoutLoading(true);
    try {
      const items = await client.postProtocol<TaskTraceLayoutItem[]>(
        '/api/admin/strategy/task-trace/layout',
        'admin.strategy.task.trace.layout',
        { machineName }
      );
      setTaskTraceLayoutItems(Array.isArray(items) ? items : []);
    } catch (err) {
      showError(err instanceof Error ? err.message : '加载布局失败');
    } finally {
      setTaskTraceLayoutLoading(false);
    }
  };

  const loadTaskTraceDetail = async (traceId: string) => {
    setSelectedTaskTraceId(traceId);
    setTaskTraceDetailVisible(true);
    setTaskTraceDetailLoading(true);
    setTaskTraceDetailItems([]);
    try {
      const items = await client.postProtocol<TaskTraceLogItem[]>(
        '/api/admin/strategy/task-trace/detail',
        'admin.strategy.task.trace.detail',
        { traceId }
      );
      setTaskTraceDetailItems(Array.isArray(items) ? items : []);
    } catch (err) {
      showError(err instanceof Error ? err.message : '加载任务明细失败');
    } finally {
      setTaskTraceDetailLoading(false);
    }
  };

  const clearCheckLogs = async () => {
    if (!currentStrategyUsId) {
      return;
    }

    setClearingLogs(true);
    try {
      const response = await client.postProtocol<{ deletedCount: number }>(
        '/api/admin/strategy-check/clear',
        'admin.strategy.check.clear',
        { usId: currentStrategyUsId }
      );
      success(`已清空 ${response.deletedCount || 0} 条检查记录`);
      // 重新加载列表
      await loadCheckLogs(currentStrategyUsId);
    } catch (err) {
      showError(err instanceof Error ? err.message : '清空检查记录失败');
    } finally {
      setClearingLogs(false);
    }
  };

  const formatBytes = (bytes: number) => {
    if (bytes === 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return Math.round((bytes / Math.pow(k, i)) * 100) / 100 + ' ' + sizes[i];
  };

  const formatDate = (dateStr: string) => {
    return new Date(dateStr).toLocaleString('zh-CN');
  };

  const formatRelativeTime = (dateStr: string) => {
    const date = new Date(dateStr);
    const now = new Date();
    const diffMs = now.getTime() - date.getTime();
    const diffMins = Math.floor(diffMs / 60000);
    const diffHours = Math.floor(diffMs / 3600000);
    const diffDays = Math.floor(diffMs / 86400000);

    if (diffMins < 1) {
      return '刚刚';
    } else if (diffMins < 60) {
      return `${diffMins}分钟前`;
    } else if (diffHours < 24) {
      return `${diffHours}小时前`;
    } else if (diffDays < 7) {
      return `${diffDays}天前`;
    } else {
      return formatDate(dateStr);
    }
  };

  const formatDecimal = useCallback((value?: number | null) => {
    if (value === null || value === undefined || Number.isNaN(value)) {
      return '-';
    }

    const abs = Math.abs(value);
    if (abs >= 1000) {
      return value.toFixed(2);
    }

    if (abs >= 1) {
      return value.toFixed(4);
    }

    if (abs >= 0.01) {
      return value.toFixed(6);
    }

    return value.toFixed(8);
  }, []);

  const formatRange = useCallback((low: number, high: number) => {
    return `${formatDecimal(low)} ~ ${formatDecimal(high)}`;
  }, [formatDecimal]);

  const getStatusTag = (status: string) => {
    const statusMap: Record<string, { color: string; text: string }> = {
      Online: { color: 'success', text: '在线' },
      Warning: { color: 'warning', text: '警告' },
      Offline: { color: 'error', text: '离线' },
      Unknown: { color: 'default', text: '未知' },
    };
    const statusInfo = statusMap[status] || statusMap.Unknown;
    return <Tag color={statusInfo.color}>{statusInfo.text}</Tag>;
  };

  const getStrategyStatusTag = (state: string) => {
    const statusMap: Record<string, { color: string; text: string }> = {
      running: { color: 'success', text: '运行中' },
      paused: { color: 'warning', text: '已暂停' },
      paused_open_position: { color: 'warning', text: '暂停(持仓)' },
      completed: { color: 'default', text: '已完成' },
      testing: { color: 'processing', text: '测试中' },
    };
    const normalized = state?.toLowerCase() || '';
    const statusInfo = statusMap[normalized] || { color: 'default', text: state || '未知' };
    return <Tag color={statusInfo.color}>{statusInfo.text}</Tag>;
  };

  const getRunStatusTag = (status?: string | null) => {
    const normalized = (status || '').toLowerCase();
    if (normalized === 'success') {
      return <Tag color="success">success</Tag>;
    }
    if (normalized.startsWith('skip')) {
      return <Tag color="default">{status}</Tag>;
    }
    if (normalized === 'fail') {
      return <Tag color="error">fail</Tag>;
    }
    return <Tag color="processing">{status || '-'}</Tag>;
  };

  const handleNodeClick = (nodeId: string) => {
    if (expandedNodeId === nodeId) {
      setExpandedNodeId(null);
    } else {
      setExpandedNodeId(nodeId);
    }
  };

  const handleViewStrategyDetail = async (strategy: Strategy) => {
    setSelectedStrategy(strategy);
    setStrategyDetailVisible(true);
    await Promise.all([
      loadStrategyPositions(strategy.usId),
      loadStrategyRunMetrics(strategy.usId),
    ]);
  };

  const filteredCheckLogs = useMemo(() => {
    if (checkLogFilter === 'all') {
      return checkLogs;
    } else if (checkLogFilter === 'success') {
      return checkLogs.filter((log) => log.success);
    } else {
      return checkLogs.filter((log) => !log.success);
    }
  }, [checkLogs, checkLogFilter]);

  const riskPositionMap = useMemo(() => {
    const map = new Map<number, RiskPositionSnapshot>();
    if (!riskIndexData?.items) {
      return map;
    }

    riskIndexData.items.forEach((item) => {
      item.positions.forEach((position) => {
        map.set(position.positionId, position);
      });
    });

    return map;
  }, [riskIndexData]);

  const riskSummary = useMemo(() => {
    const totalSymbols = riskIndexData?.items.length ?? 0;
    const totalPositions = riskIndexData?.items.reduce((sum, item) => sum + item.totalPositions, 0) ?? 0;
    return {
      totalSymbols,
      totalPositions,
      generatedAt: riskIndexData?.generatedAt,
    };
  }, [riskIndexData]);

  const riskTreeData = useMemo(() => {
    if (!riskIndexData?.items) {
      return [];
    }

    const buildLevel3Node = (level3: Level3TreeSnapshot, keyPrefix: string) => ({
      title: `L3 ${formatRange(level3.low, level3.high)}（${level3.count}）`,
      key: `${keyPrefix}-l3-${level3.key}`,
      dataRef: {
        positionIds: level3.positionIds ?? [],
        count: level3.count,
        level: 'level3',
        rangeText: formatRange(level3.low, level3.high),
      } as RiskTreeNodeData,
      children: [],
    });

    const buildLevel2Node = (level2: Level2TreeSnapshot, keyPrefix: string) => {
      const children = level2.level3.map((level3) => buildLevel3Node(level3, `${keyPrefix}-l2-${level2.key}`));
      const positionIds = level2.level3.flatMap((level3) => level3.positionIds ?? []);
      return {
        title: `L2 ${formatRange(level2.low, level2.high)}（${level2.count}）`,
        key: `${keyPrefix}-l2-${level2.key}`,
        dataRef: {
          positionIds,
          count: level2.count,
          level: 'level2',
          rangeText: formatRange(level2.low, level2.high),
        } as RiskTreeNodeData,
        children,
      };
    };

    const buildLevel1Node = (level1: Level1TreeSnapshot, keyPrefix: string) => {
      const children = level1.level2.map((level2) => buildLevel2Node(level2, `${keyPrefix}-l1-${level1.key}`));
      const positionIds = level1.level2.flatMap((level2) =>
        level2.level3.flatMap((level3) => level3.positionIds ?? [])
      );
      return {
        title: `L1 ${formatRange(level1.low, level1.high)}（${level1.count}）`,
        key: `${keyPrefix}-l1-${level1.key}`,
        dataRef: {
          positionIds,
          count: level1.count,
          level: 'level1',
          rangeText: formatRange(level1.low, level1.high),
        } as RiskTreeNodeData,
        children,
      };
    };

    const buildScaleNode = (scale: ScaleTreeSnapshot, keyPrefix: string) => {
      const children = scale.level1.map((level1) => buildLevel1Node(level1, `${keyPrefix}-scale-${scale.scale}`));
      const positionIds = scale.level1.flatMap((level1) =>
        level1.level2.flatMap((level2) => level2.level3.flatMap((level3) => level3.positionIds ?? []))
      );
      return {
        title: `数量级 ${scale.scale}（${scale.count}） 步长=${formatDecimal(scale.step1)}/${formatDecimal(scale.step2)}/${formatDecimal(scale.step3)}`,
        key: `${keyPrefix}-scale-${scale.scale}`,
        dataRef: {
          positionIds,
          count: scale.count,
          level: 'scale',
        } as RiskTreeNodeData,
        children,
      };
    };

    const buildIndexNode = (index: IndexTreeSnapshot, keyPrefix: string) => {
      const children = index.scales.map((scale) => buildScaleNode(scale, `${keyPrefix}-index-${index.indexType}`));
      const positionIds = index.scales.flatMap((scale) =>
        scale.level1.flatMap((level1) =>
          level1.level2.flatMap((level2) => level2.level3.flatMap((level3) => level3.positionIds ?? []))
        )
      );
      return {
        title: `${index.indexType}（${index.count}）`,
        key: `${keyPrefix}-index-${index.indexType}`,
        dataRef: {
          positionIds,
          count: index.count,
          level: 'index',
        } as RiskTreeNodeData,
        children,
      };
    };

    return riskIndexData.items.map((item) => {
      const symbolKey = `${item.exchange}|${item.symbol}`;
      const positionIds = item.positions.map((position) => position.positionId);
      return {
        title: `${item.exchange} ${item.symbol}（${item.totalPositions}）`,
        key: `symbol-${symbolKey}`,
        dataRef: {
          positionIds,
          count: item.totalPositions,
          level: 'symbol',
        } as RiskTreeNodeData,
        children: item.indexTrees.map((index) => buildIndexNode(index, symbolKey)),
      };
    });
  }, [riskIndexData, formatDecimal, formatRange]);

  const filteredLayoutItems = useMemo(() => {
    if (!taskTraceLayoutItems.length) return [];
    const ex = layoutFilterExchange.trim().toLowerCase();
    const sym = layoutFilterSymbol.trim().toLowerCase();
    const tf = layoutFilterTimeframe.trim().toLowerCase();
    if (!ex && !sym && !tf) return taskTraceLayoutItems;
    return taskTraceLayoutItems.filter((r) => {
      if (ex && !(r.exchange?.toLowerCase().includes(ex))) return false;
      if (sym && !(r.symbol?.toLowerCase().includes(sym))) return false;
      if (tf && !(r.timeframe?.toLowerCase().includes(tf))) return false;
      return true;
    });
  }, [taskTraceLayoutItems, layoutFilterExchange, layoutFilterSymbol, layoutFilterTimeframe]);

  const riskSelectedPositions = useMemo(() => {
    const allPositions = riskSelectedPositionIds.length > 0
      ? riskSelectedPositionIds
      : Array.from(riskPositionMap.keys());

    const searchText = riskPositionSearch.trim().toLowerCase();
    const list = allPositions
      .map((id) => riskPositionMap.get(id))
      .filter((item): item is RiskPositionSnapshot => !!item);

    if (!searchText) {
      return list;
    }

    return list.filter((item) => {
      return (
        item.positionId.toString().includes(searchText) ||
        item.uid.toString().includes(searchText) ||
        item.usId.toString().includes(searchText) ||
        item.exchange.toLowerCase().includes(searchText) ||
        item.symbol.toLowerCase().includes(searchText) ||
        item.side.toLowerCase().includes(searchText)
      );
    });
  }, [riskPositionMap, riskSelectedPositionIds, riskPositionSearch]);

  // 统计信息
  const stats = useMemo(() => {
    const total = servers.length;
    const online = servers.filter((s) => s.status === 'Online').length;
    const warning = servers.filter((s) => s.status === 'Warning').length;
    const offline = servers.filter((s) => s.status === 'Offline').length;
    const totalConnections = servers.reduce((sum, s) => sum + s.connectionCount, 0);
    return { total, online, warning, offline, totalConnections };
  }, [servers]);

  const positionColumns = [
    {
      title: '交易所',
      dataIndex: 'exchange',
      key: 'exchange',
      width: 100,
    },
    {
      title: '交易对',
      dataIndex: 'symbol',
      key: 'symbol',
      width: 150,
    },
    {
      title: '方向',
      dataIndex: 'side',
      key: 'side',
      width: 80,
      render: (side: string) => (
        <Tag color={side === 'Buy' ? 'green' : 'red'}>{side === 'Buy' ? '买入' : '卖出'}</Tag>
      ),
    },
    {
      title: '数量',
      dataIndex: 'quantity',
      key: 'quantity',
      width: 120,
    },
    {
      title: '入场价格',
      dataIndex: 'entryPrice',
      key: 'entryPrice',
      width: 120,
    },
    {
      title: '当前价格',
      dataIndex: 'currentPrice',
      key: 'currentPrice',
      width: 120,
      render: (price?: number) => price ? price.toFixed(8) : '-',
    },
    {
      title: '状态',
      dataIndex: 'status',
      key: 'status',
      width: 100,
      render: (status: string) => (
        <Tag color={status === 'Open' ? 'success' : 'default'}>{status === 'Open' ? '持仓中' : '已平仓'}</Tag>
      ),
    },
    {
      title: '开仓时间',
      dataIndex: 'openedAt',
      key: 'openedAt',
      width: 180,
      render: (dateStr: string) => formatDate(dateStr),
    },
    {
      title: '平仓时间',
      dataIndex: 'closedAt',
      key: 'closedAt',
      width: 180,
      render: (dateStr?: string) => dateStr ? formatDate(dateStr) : '-',
    },
  ];

  const riskPositionColumns = [
    {
      title: '仓位ID',
      dataIndex: 'positionId',
      key: 'positionId',
      width: 100,
    },
    {
      title: '用户ID',
      dataIndex: 'uid',
      key: 'uid',
      width: 100,
    },
    {
      title: '策略ID',
      dataIndex: 'usId',
      key: 'usId',
      width: 100,
    },
    {
      title: '交易所',
      dataIndex: 'exchange',
      key: 'exchange',
      width: 100,
    },
    {
      title: '交易对',
      dataIndex: 'symbol',
      key: 'symbol',
      width: 140,
    },
    {
      title: '方向',
      dataIndex: 'side',
      key: 'side',
      width: 80,
      render: (side: string) => (
        <Tag color={side?.toLowerCase() === 'long' ? 'green' : 'red'}>
          {side?.toLowerCase() === 'long' ? '多头' : '空头'}
        </Tag>
      ),
    },
    {
      title: '数量',
      dataIndex: 'qty',
      key: 'qty',
      width: 120,
      render: (value: number) => formatDecimal(value),
    },
    {
      title: '入场价',
      dataIndex: 'entryPrice',
      key: 'entryPrice',
      width: 120,
      render: (value: number) => formatDecimal(value),
    },
    {
      title: '止损价',
      dataIndex: 'stopLossPrice',
      key: 'stopLossPrice',
      width: 120,
      render: (value?: number | null) => formatDecimal(value),
    },
    {
      title: '止盈价',
      dataIndex: 'takeProfitPrice',
      key: 'takeProfitPrice',
      width: 120,
      render: (value?: number | null) => formatDecimal(value),
    },
    {
      title: '移动止盈价',
      dataIndex: 'trailingStopPrice',
      key: 'trailingStopPrice',
      width: 140,
      render: (value?: number | null) => formatDecimal(value),
    },
    {
      title: '状态',
      dataIndex: 'status',
      key: 'status',
      width: 100,
      render: (status: string) => (
        <Tag color={status?.toLowerCase() === 'open' ? 'success' : 'default'}>
          {status?.toLowerCase() === 'open' ? '持仓中' : '已平仓'}
        </Tag>
      ),
    },
  ];

  return (
    <div className="server-list">
      <div className="server-list-header">
        <Space>
          <CloudServerOutlined style={{ fontSize: 24 }} />
          <h2>服务器列表</h2>
        </Space>
        <Space size="middle" className="server-list-stats">
          <Space size="small" split={<Divider type="vertical" style={{ margin: 0, height: 16 }} />}>
            <span className="stat-item">
              <span className="stat-label">总服务器</span>
              <span className="stat-value">{stats.total}</span>
            </span>
            <span className="stat-item">
              <span className="stat-label">在线</span>
              <span className="stat-value" style={{ color: '#3f8600' }}>
                <Badge status="success" style={{ marginRight: 4 }} />
                {stats.online}
              </span>
            </span>
            <span className="stat-item">
              <span className="stat-label">警告</span>
              <span className="stat-value" style={{ color: '#cf1322' }}>
                <Badge status="warning" style={{ marginRight: 4 }} />
                {stats.warning}
              </span>
            </span>
            <span className="stat-item">
              <span className="stat-label">连接数</span>
              <span className="stat-value" style={{ color: '#1890ff' }}>{stats.totalConnections}</span>
            </span>
          </Space>
          <Button type="primary" icon={<ReloadOutlined />} onClick={loadServers} loading={loading} size="small">
            刷新
          </Button>
        </Space>
      </div>

      {/* 服务器节点列表 */}
      <div className="server-nodes-container">
        {servers.map((server) => {
          const isExpanded = expandedNodeId === server.nodeId;
          return (
            <Card
              key={server.nodeId}
              className={`server-node-card ${isExpanded ? 'expanded' : ''}`}
              hoverable
            >
              <div 
                className="server-node-header"
                onClick={() => handleNodeClick(server.nodeId)}
                style={{ cursor: 'pointer' }}
              >
                <div className="server-node-title-section">
                  <div className="server-node-title">
                    <Space size="small">
                      {server.isCurrentNode && <Badge status="processing" />}
                      <span className="server-node-id" title={`完整节点ID: ${server.nodeId}`}>
                        {server.machineName}
                      </span>
                      {getStatusTag(server.status)}
                      <span className="server-node-meta-inline">
                        <span className="meta-inline-item">连接数: {server.connectionCount}</span>
                        {server.systems.length > 0 && (
                          <>
                            <span className="meta-divider">|</span>
                            <span className="meta-inline-item">系统: {server.systems.join(', ')}</span>
                          </>
                        )}
                      </span>
                    </Space>
                  </div>
                  <div className="server-node-meta-compact">
                    <span className="meta-item-compact" title={server.nodeId}>
                      <span className="meta-label-compact">节点ID:</span>
                      <span className="meta-value-compact">{server.nodeId}</span>
                    </span>
                    <span className="meta-item-compact">
                      <span className="meta-label-compact">心跳:</span>
                      <span className="meta-value-compact">{formatDate(server.lastHeartbeat)}</span>
                    </span>
                  </div>
                </div>
                <div className="server-node-expand-icon">
                  {isExpanded ? <UpOutlined /> : <DownOutlined />}
                </div>
              </div>

              {isExpanded && (
                <div className="server-node-details" onClick={(e) => e.stopPropagation()}>
                  <Tabs
                    defaultActiveKey="strategies"
                    onChange={(key) => {
                      if (key === 'position-risk') {
                        loadRiskIndex();
                      } else if (key === 'layout') {
                        loadTaskTraceLayout(server.machineName);
                      } else if (key === 'strategies') {
                        loadMarketByMarket(server.machineName);
                      }
                    }}
                    items={[
                      {
                        key: 'strategies',
                        label: (
                          <span>
                            <PlayCircleOutlined />
                            当前服务器实盘情况
                          </span>
                        ),
                        children: (
                          <div className="strategies-tab-content">
                            <div className="strategies-header">
                              <Space size="small">
                                <span className="strategies-count">
                                  共 <strong>{marketItems.reduce((sum, m) => sum + m.strategyCount, 0)}</strong> 条运行策略，
                                  <strong>{marketItems.length}</strong> 个市场（按 交易所/币种/周期 聚合）
                                </span>
                                <Button
                                  type="text"
                                  size="small"
                                  icon={<ReloadOutlined />}
                                  onClick={() => loadMarketByMarket(server.machineName)}
                                  loading={marketLoading}
                                >
                                  刷新
                                </Button>
                              </Space>
                            </div>
                            {marketLoading ? (
                              <div style={{ textAlign: 'center', padding: 40 }}>
                                <Spin size="large" />
                              </div>
                            ) : marketItems.length === 0 ? (
                              <Empty description="暂无运行中的策略市场" />
                            ) : (
                              <Table
                                dataSource={marketItems}
                                rowKey={(r) => `${r.exchange}|${r.symbol}|${r.timeframe}`}
                                size="small"
                                onRow={(r) => ({
                                  onClick: () => handleMarketRowClick(r),
                                  style: { cursor: 'pointer' },
                                })}
                                columns={[
                                  { title: '交易所', dataIndex: 'exchange', key: 'exchange', width: 90 },
                                  { title: '币种', dataIndex: 'symbol', key: 'symbol', width: 120 },
                                  { title: '周期', dataIndex: 'timeframe', key: 'timeframe', width: 70 },
                                  { title: '运行策略数', dataIndex: 'strategyCount', key: 'strategyCount', width: 100 },
                                  {
                                    title: '最后运行时间',
                                    dataIndex: 'lastRunAt',
                                    key: 'lastRunAt',
                                    width: 175,
                                    render: (v: string | null | undefined) => (v ? formatDate(v) : '-'),
                                  },
                                ]}
                              />
                            )}
                          </div>
                        ),
                      },
                      {
                        key: 'layout',
                        label: (
                          <span>
                            <ApartmentOutlined />
                            布局查看
                          </span>
                        ),
                        children: (
                          <div className="task-trace-layout-tab">
                            <div className="task-trace-layout-header">
                              <Space wrap>
                                <span>本机实盘引擎布局（主记录）：按 交易所/币对/周期 聚合</span>
                                <Input
                                  placeholder="筛选交易所"
                                  allowClear
                                  size="small"
                                  style={{ width: 120 }}
                                  value={layoutFilterExchange}
                                  onChange={(e) => setLayoutFilterExchange(e.target.value)}
                                />
                                <Input
                                  placeholder="筛选币种"
                                  allowClear
                                  size="small"
                                  style={{ width: 120 }}
                                  value={layoutFilterSymbol}
                                  onChange={(e) => setLayoutFilterSymbol(e.target.value)}
                                />
                                <Input
                                  placeholder="筛选周期"
                                  allowClear
                                  size="small"
                                  style={{ width: 100 }}
                                  value={layoutFilterTimeframe}
                                  onChange={(e) => setLayoutFilterTimeframe(e.target.value)}
                                />
                                <Button
                                  type="primary"
                                  icon={<ReloadOutlined />}
                                  onClick={() => loadTaskTraceLayout(server.machineName)}
                                  loading={taskTraceLayoutLoading}
                                  size="small"
                                >
                                  刷新
                                </Button>
                              </Space>
                              <span className="task-trace-summary">
                                共 {filteredLayoutItems.length} 个市场
                                {filteredLayoutItems.length !== taskTraceLayoutItems.length && `（已筛选，共 ${taskTraceLayoutItems.length}）`}
                              </span>
                            </div>
                            {taskTraceLayoutLoading ? (
                              <div style={{ textAlign: 'center', padding: 40 }}>
                                <Spin size="large" />
                              </div>
                            ) : taskTraceLayoutItems.length === 0 ? (
                              <Empty description="暂无布局数据（主记录表暂无任务）" />
                            ) : (
                              <Table
                                dataSource={filteredLayoutItems}
                                rowKey={(r) => `${r.exchange}|${r.symbol}|${r.timeframe}`}
                                size="small"
                                scroll={{ x: 700, y: 400 }}
                                columns={[
                                  { title: '交易所', dataIndex: 'exchange', key: 'exchange', width: 90 },
                                  { title: '币对', dataIndex: 'symbol', key: 'symbol', width: 120 },
                                  { title: '周期', dataIndex: 'timeframe', key: 'timeframe', width: 80 },
                                  { title: '策略数', dataIndex: 'strategyCount', key: 'strategyCount', width: 80 },
                                  { title: '任务数', dataIndex: 'taskCount', key: 'taskCount', width: 80 },
                                  { title: '总耗时(ms)', dataIndex: 'totalDurationMs', key: 'totalDurationMs', width: 100 },
                                  {
                                    title: '最后执行',
                                    dataIndex: 'lastExecutedAt',
                                    key: 'lastExecutedAt',
                                    width: 175,
                                    render: (v: string) => formatDate(v),
                                  },
                                ]}
                              />
                            )}
                          </div>
                        ),
                      },
                      {
                        key: 'position-risk',
                        label: (
                          <span>
                            <PartitionOutlined />
                            实盘仓位检测
                          </span>
                        ),
                        children: (
                          <div className="risk-index-tab">
                            <div className="risk-index-header">
                              <Space>
                                <strong>当前仓位：</strong>
                                <Badge count={riskSummary.totalPositions} showZero style={{ backgroundColor: '#1890ff' }} />
                                <span>交易对数：{riskSummary.totalSymbols}</span>
                                {riskSummary.generatedAt && (
                                  <span>快照时间：{formatDate(riskSummary.generatedAt)}</span>
                                )}
                              </Space>
                              <Button
                                type="primary"
                                icon={<ReloadOutlined />}
                                onClick={loadRiskIndex}
                                loading={riskIndexLoading}
                              >
                                刷新
                              </Button>
                            </div>

                            {riskIndexLoading ? (
                              <div style={{ textAlign: 'center', padding: 40 }}>
                                <Spin size="large" />
                              </div>
                            ) : riskIndexData?.items?.length ? (
                              <div className="risk-index-body">
                                <div className="risk-index-tree">
                                  <Tree
                                    treeData={riskTreeData}
                                    showLine={{ showLeafIcon: false }}
                                    defaultExpandAll={false}
                                    height={420}
                                    blockNode
                                    onSelect={(_, info) => {
                                      const node = info.node as any;
                                      const dataRef = node?.dataRef as RiskTreeNodeData | undefined;
                                      setRiskSelectedPositionIds(dataRef?.positionIds ?? []);
                                      setRiskSelectedNodeTitle(typeof node?.title === 'string' ? node.title : '已选择节点');
                                    }}
                                  />
                                </div>
                                <div className="risk-index-detail">
                                  <div className="risk-index-detail-header">
                                    <div className="risk-index-detail-title">
                                      已选节点：{riskSelectedNodeTitle}（{riskSelectedPositionIds.length || riskSelectedPositions.length}）
                                    </div>
                                    <Search
                                      placeholder="搜索仓位（仓位ID/用户ID/策略ID/交易对/方向）"
                                      allowClear
                                      style={{ width: 360 }}
                                      value={riskPositionSearch}
                                      onChange={(e) => setRiskPositionSearch(e.target.value)}
                                    />
                                  </div>
                                  <Table
                                    dataSource={riskSelectedPositions}
                                    columns={riskPositionColumns}
                                    rowKey="positionId"
                                    pagination={{ pageSize: 10 }}
                                    size="small"
                                    scroll={{ x: 1200, y: 360 }}
                                    onRow={(record) => ({
                                      onClick: (e) => {
                                        e.stopPropagation();
                                        setSelectedRiskPosition(record);
                                        setRiskPositionDetailVisible(true);
                                      },
                                    })}
                                  />
                                </div>
                              </div>
                            ) : (
                              <Empty description="暂无风控索引数据" />
                            )}
                          </div>
                        ),
                      },
                      {
                        key: 'info',
                        label: (
                          <span>
                            <InfoCircleOutlined />
                            基础信息
                          </span>
                        ),
                        children: (
                          <div className="info-tab-content">
                            {server.processInfo && (
                              <div className="info-section">
                                <h3>进程信息</h3>
                                <Descriptions column={2} bordered size="small">
                                  <Descriptions.Item label="进程ID">{server.processInfo.processId}</Descriptions.Item>
                                  <Descriptions.Item label="进程名">{server.processInfo.processName}</Descriptions.Item>
                                  <Descriptions.Item label="启动时间">
                                    {formatDate(server.processInfo.startTime)}
                                  </Descriptions.Item>
                                  <Descriptions.Item label="CPU使用率">
                                    {server.processInfo.cpuUsage.toFixed(2)}%
                                  </Descriptions.Item>
                                  <Descriptions.Item label="内存使用">
                                    {formatBytes(server.processInfo.memoryUsage)}
                                  </Descriptions.Item>
                                  <Descriptions.Item label="线程数">{server.processInfo.threadCount}</Descriptions.Item>
                                </Descriptions>
                              </div>
                            )}

                            {server.systemInfo && (
                              <>
                                <Divider />
                                <div className="info-section">
                                  <h3>系统信息</h3>
                                  <Descriptions column={2} bordered size="small">
                                    <Descriptions.Item label="机器名">{server.systemInfo.machineName}</Descriptions.Item>
                                    <Descriptions.Item label="操作系统">{server.systemInfo.osVersion}</Descriptions.Item>
                                    <Descriptions.Item label="CPU核心数">{server.systemInfo.processorCount}</Descriptions.Item>
                                    <Descriptions.Item label="总内存">
                                      {formatBytes(server.systemInfo.totalMemory)}
                                    </Descriptions.Item>
                                    <Descriptions.Item label="进程架构">
                                      {server.systemInfo.is64BitProcess ? '64位' : '32位'}
                                    </Descriptions.Item>
                                    <Descriptions.Item label="系统架构">
                                      {server.systemInfo.is64BitOperatingSystem ? '64位' : '32位'}
                                    </Descriptions.Item>
                                  </Descriptions>
                                </div>
                              </>
                            )}

                            {server.runtimeInfo && (
                              <>
                                <Divider />
                                <div className="info-section">
                                  <h3>运行时信息</h3>
                                  <Descriptions column={1} bordered size="small">
                                    <Descriptions.Item label=".NET版本">{server.runtimeInfo.dotNetVersion}</Descriptions.Item>
                                    <Descriptions.Item label="框架描述">{server.runtimeInfo.frameworkDescription}</Descriptions.Item>
                                    <Descriptions.Item label="CLR版本">{server.runtimeInfo.clrVersion}</Descriptions.Item>
                                  </Descriptions>
                                </div>
                              </>
                            )}
                          </div>
                        ),
                      },
                    ]}
                  />
                </div>
              )}
            </Card>
          );
        })}
      </div>

      {/* 策略详情弹窗 */}
      <Modal
        title={`策略详情 - ${selectedStrategy?.aliasName || selectedStrategy?.defName}`}
        open={strategyDetailVisible}
        onCancel={() => {
          setStrategyDetailVisible(false);
          setSelectedStrategy(null);
          setStrategyPositions([]);
          setStrategyRunMetrics([]);
          setSelectedRunMetric(null);
          setRunMetricDetailVisible(false);
          if (marketMachineName) {
            loadMarketByMarket(marketMachineName);
          }
        }}
        footer={null}
        width={1200}
        className="strategy-detail-modal"
      >
        {selectedStrategy && (
          <div>
            <Descriptions column={2} bordered style={{ marginBottom: 24 }}>
              <Descriptions.Item label="策略ID">{selectedStrategy.usId}</Descriptions.Item>
              <Descriptions.Item label="定义ID">{selectedStrategy.defId}</Descriptions.Item>
              <Descriptions.Item label="策略名称">{selectedStrategy.aliasName || selectedStrategy.defName}</Descriptions.Item>
              <Descriptions.Item label="状态">{getStrategyStatusTag(selectedStrategy.state)}</Descriptions.Item>
              <Descriptions.Item label="版本号">v{selectedStrategy.versionNo}</Descriptions.Item>
              <Descriptions.Item label="最后更新">{formatDate(selectedStrategy.updatedAt)}</Descriptions.Item>
              {selectedStrategy.description && (
                <Descriptions.Item label="描述" span={2}>
                  {selectedStrategy.description}
                </Descriptions.Item>
              )}
            </Descriptions>

            <div style={{ marginBottom: 24, textAlign: 'right' }}>
              <Button
                type="primary"
                icon={<InfoCircleOutlined />}
                onClick={() => loadCheckLogs(selectedStrategy.usId)}
                loading={checkLogsLoading}
              >
                查看检查过程
              </Button>
            </div>

            <Divider>最近5条策略运行画像</Divider>
            {strategyRunMetricsLoading ? (
              <div style={{ textAlign: 'center', padding: 20 }}>
                <Spin size="small" />
              </div>
            ) : strategyRunMetrics.length === 0 ? (
              <Empty description="暂无运行画像数据" />
            ) : (
              <Table
                dataSource={strategyRunMetrics}
                rowKey={(row, index) => `${row.runAt}-${index}`}
                size="small"
                pagination={false}
                onRow={(row) => ({
                  onClick: () => {
                    setSelectedRunMetric(row);
                    setRunMetricDetailVisible(true);
                  },
                  style: { cursor: 'pointer' },
                })}
                columns={[
                  {
                    title: '时间',
                    dataIndex: 'runAt',
                    key: 'runAt',
                    width: 170,
                    render: (v: string) => formatDate(v),
                  },
                  {
                    title: '模式',
                    dataIndex: 'isBarClose',
                    key: 'isBarClose',
                    width: 80,
                    render: (v: boolean) => (v ? 'close' : 'update'),
                  },
                  { title: '总耗时(ms)', dataIndex: 'durationMs', key: 'durationMs', width: 100 },
                  { title: '匹配', dataIndex: 'matchedCount', key: 'matchedCount', width: 80 },
                  { title: '执行', dataIndex: 'executedCount', key: 'executedCount', width: 80 },
                  { title: '开仓次数', dataIndex: 'openTaskCount', key: 'openTaskCount', width: 90 },
                  {
                    title: '执行成功率',
                    dataIndex: 'successRatePct',
                    key: 'successRatePct',
                    width: 110,
                    render: (v: number) => `${v}%`,
                  },
                  {
                    title: '状态',
                    dataIndex: 'runStatus',
                    key: 'runStatus',
                    width: 110,
                    render: (v?: string | null) => getRunStatusTag(v),
                  },
                  {
                    title: '操作',
                    key: 'action',
                    width: 100,
                    render: (_: unknown, row: StrategyRunMetricItem) => (
                      <Button
                        type="link"
                        size="small"
                        onClick={(e) => {
                          e.stopPropagation();
                          setSelectedRunMetric(row);
                          setRunMetricDetailVisible(true);
                        }}
                      >
                        查看详情
                      </Button>
                    ),
                  },
                ]}
              />
            )}

            <Divider>历史开仓</Divider>
            {positionsLoading ? (
              <div style={{ textAlign: 'center', padding: 40 }}>
                <Spin size="large" />
              </div>
            ) : strategyPositions.length === 0 ? (
              <Empty description="暂无仓位记录" />
            ) : (
              <Table
                dataSource={strategyPositions}
                columns={positionColumns}
                rowKey="positionId"
                pagination={{ pageSize: 10 }}
                size="small"
              />
            )}
          </div>
        )}
      </Modal>

      {/* 策略运行画像详情弹窗 */}
      <Modal
        title={`运行画像详情 - ${selectedRunMetric ? formatDate(selectedRunMetric.runAt) : ''}`}
        open={runMetricDetailVisible}
        onCancel={() => {
          setRunMetricDetailVisible(false);
          setSelectedRunMetric(null);
        }}
        footer={null}
        width={1000}
      >
        {selectedRunMetric ? (
          <Descriptions column={2} bordered size="small">
            <Descriptions.Item label="时间">{formatDate(selectedRunMetric.runAt)}</Descriptions.Item>
            <Descriptions.Item label="状态">{getRunStatusTag(selectedRunMetric.runStatus)}</Descriptions.Item>
            <Descriptions.Item label="追踪ID" span={2}>
              <code>{selectedRunMetric.traceId || '-'}</code>
            </Descriptions.Item>
            <Descriptions.Item label="处理方" span={2}>
              <code>{selectedRunMetric.engineInstance || '-'}</code>
            </Descriptions.Item>
            <Descriptions.Item label="交易所">{selectedRunMetric.exchange}</Descriptions.Item>
            <Descriptions.Item label="币对">{selectedRunMetric.symbol}</Descriptions.Item>
            <Descriptions.Item label="周期">{selectedRunMetric.timeframe}</Descriptions.Item>
            <Descriptions.Item label="模式">{selectedRunMetric.isBarClose ? 'close' : 'update'}</Descriptions.Item>
            <Descriptions.Item label="总耗时(ms)">{selectedRunMetric.durationMs}</Descriptions.Item>
            <Descriptions.Item label="分段耗时(ms)">
              查找 {selectedRunMetric.lookupMs} / 指标 {selectedRunMetric.indicatorMs} / 执行 {selectedRunMetric.executeMs}
            </Descriptions.Item>
            <Descriptions.Item label="匹配/执行/跳过">
              {selectedRunMetric.matchedCount} / {selectedRunMetric.executedCount} / {selectedRunMetric.skippedCount}
            </Descriptions.Item>
            <Descriptions.Item label="条件/动作/开仓">
              {selectedRunMetric.conditionEvalCount} / {selectedRunMetric.actionExecCount} / {selectedRunMetric.openTaskCount}
            </Descriptions.Item>
            <Descriptions.Item label="执行成功率">{selectedRunMetric.successRatePct}%</Descriptions.Item>
            <Descriptions.Item label="开仓触发率">{selectedRunMetric.openTaskRatePct}%</Descriptions.Item>
            <Descriptions.Item label="单策略样本/均值/最大">
              {selectedRunMetric.perStrategySamples} / {selectedRunMetric.perStrategyAvgMs}ms / {selectedRunMetric.perStrategyMaxMs}ms
            </Descriptions.Item>
          </Descriptions>
        ) : (
          <Empty description="暂无明细" />
        )}
      </Modal>

      {/* 检查日志弹窗 */}
      <Modal
        title="策略检查过程"
        open={checkLogsVisible}
        onCancel={() => {
          setCheckLogsVisible(false);
          setCheckLogs([]);
          setCheckLogFilter('all');
          setCurrentStrategyUsId(null);
        }}
        footer={null}
        width={1400}
        className="strategy-check-logs-modal"
      >
        {checkLogsLoading ? (
          <div style={{ textAlign: 'center', padding: 40 }}>
            <Spin size="large" />
          </div>
        ) : checkLogs.length === 0 ? (
          <Empty description="暂无检查记录" />
        ) : (
          <>
            <div style={{ marginBottom: 16, display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <Space>
                <span>筛选：</span>
                <Button
                  type={checkLogFilter === 'all' ? 'primary' : 'default'}
                  size="small"
                  onClick={() => setCheckLogFilter('all')}
                >
                  全部
                </Button>
                <Button
                  type={checkLogFilter === 'success' ? 'primary' : 'default'}
                  size="small"
                  onClick={() => setCheckLogFilter('success')}
                >
                  只看成功
                </Button>
                <Button
                  type={checkLogFilter === 'failed' ? 'primary' : 'default'}
                  size="small"
                  onClick={() => setCheckLogFilter('failed')}
                >
                  只看失败
                </Button>
                <Divider type="vertical" style={{ margin: '0 8px' }} />
                <Popconfirm
                  title="确定要清空所有检查记录吗？"
                  description="此操作将删除该策略的所有检查记录，且无法恢复。"
                  onConfirm={clearCheckLogs}
                  okText="确定"
                  cancelText="取消"
                  okButtonProps={{ danger: true }}
                >
                  <Button
                    danger
                    size="small"
                    icon={<DeleteOutlined />}
                    loading={clearingLogs}
                  >
                    清空
                  </Button>
                </Popconfirm>
                <Divider type="vertical" style={{ margin: '0 8px' }} />
                <Button
                  type="default"
                  size="small"
                  icon={<ReloadOutlined />}
                  loading={checkLogsLoading}
                  onClick={() => {
                    if (currentStrategyUsId) {
                      loadCheckLogs(currentStrategyUsId);
                    }
                  }}
                >
                  刷新
                </Button>
              </Space>
              <span style={{ color: '#999', fontSize: '12px' }}>
                共 {checkLogs.length} 条记录
                {checkLogFilter !== 'all' && `，显示 ${filteredCheckLogs.length} 条`}
              </span>
            </div>
            <Table
              dataSource={filteredCheckLogs}
              columns={[
                {
                  title: '时间',
                  dataIndex: 'createdAt',
                  key: 'createdAt',
                  width: 180,
                  render: (dateStr: string) => (
                    <div>
                      <div>{formatRelativeTime(dateStr)}</div>
                      <div style={{ fontSize: '11px', color: '#999', marginTop: '2px' }}>
                        {formatDate(dateStr)}
                      </div>
                    </div>
                  ),
                },
                {
                  title: '阶段',
                  dataIndex: 'stage',
                  key: 'stage',
                  width: 150,
                  ellipsis: true,
                },
                {
                  title: '组索引',
                  dataIndex: 'groupIndex',
                  key: 'groupIndex',
                  width: 80,
                },
                {
                  title: '方法',
                  dataIndex: 'method',
                  key: 'method',
                  width: 150,
                  ellipsis: true,
                },
                {
                  title: '必需',
                  dataIndex: 'isRequired',
                  key: 'isRequired',
                  width: 80,
                  render: (isRequired: boolean) => (
                    <Tag color={isRequired ? 'red' : 'default'}>{isRequired ? '是' : '否'}</Tag>
                  ),
                },
                {
                  title: '结果',
                  dataIndex: 'success',
                  key: 'success',
                  width: 100,
                  render: (success: boolean) => (
                    <Tag color={success ? 'success' : 'error'}>{success ? '成功' : '失败'}</Tag>
                  ),
                },
                {
                  title: '消息',
                  dataIndex: 'message',
                  key: 'message',
                  width: 200,
                  ellipsis: true,
                },
                {
                  title: '操作',
                  key: 'action',
                  width: 100,
                  render: (_: any, record: CheckLog) => (
                    <Button
                      type="link"
                      size="small"
                      onClick={(e) => {
                        e.stopPropagation();
                        setSelectedCheckLog(record);
                        setCheckLogDetailVisible(true);
                      }}
                    >
                      查看详情
                    </Button>
                  ),
                },
              ]}
              rowKey="id"
              pagination={{ pageSize: 20 }}
              size="small"
              scroll={{ x: 1200, y: 600 }}
              onRow={(record) => ({
                onClick: () => {
                  setSelectedCheckLog(record);
                  setCheckLogDetailVisible(true);
                },
                style: { cursor: 'pointer' },
              })}
            />
          </>
        )}
      </Modal>

      {/* 检查记录详情全屏弹窗 */}
      <Modal
        title={`检查记录详情 - ${selectedCheckLog ? formatDate(selectedCheckLog.createdAt) : ''}`}
        open={checkLogDetailVisible}
        onCancel={() => {
          setCheckLogDetailVisible(false);
          setSelectedCheckLog(null);
        }}
        footer={null}
        width="100%"
        style={{ top: 0, paddingBottom: 0 }}
        className="check-log-detail-modal"
        styles={{
          body: { height: 'calc(100vh - 55px)', overflow: 'auto', padding: '24px' },
        }}
      >
        {selectedCheckLog && (
          <div
            className="check-log-detail-content"
            onDoubleClick={() => {
              setCheckLogDetailVisible(false);
              setSelectedCheckLog(null);
            }}
          >
            <Descriptions column={2} bordered size="middle" style={{ marginBottom: 24 }}>
              <Descriptions.Item label="记录ID" span={1}>
                {selectedCheckLog.id}
              </Descriptions.Item>
              <Descriptions.Item label="创建时间" span={1}>
                {formatDate(selectedCheckLog.createdAt)}
              </Descriptions.Item>
              <Descriptions.Item label="用户ID" span={1}>
                {selectedCheckLog.uid}
              </Descriptions.Item>
              <Descriptions.Item label="策略实例ID" span={1}>
                {selectedCheckLog.usId}
              </Descriptions.Item>
              <Descriptions.Item label="交易所" span={1}>
                {selectedCheckLog.exchange}
              </Descriptions.Item>
              <Descriptions.Item label="交易对" span={1}>
                {selectedCheckLog.symbol}
              </Descriptions.Item>
              <Descriptions.Item label="时间周期（秒）" span={1}>
                {selectedCheckLog.timeframe}
              </Descriptions.Item>
              <Descriptions.Item label="K线时间戳" span={1}>
                {selectedCheckLog.candleTimestamp} (
                {formatDate(new Date(selectedCheckLog.candleTimestamp).toISOString())})
              </Descriptions.Item>
              <Descriptions.Item label="检查阶段" span={1}>
                {selectedCheckLog.stage}
              </Descriptions.Item>
              <Descriptions.Item label="组索引" span={1}>
                {selectedCheckLog.groupIndex}
              </Descriptions.Item>
              <Descriptions.Item label="条件键" span={2}>
                <code style={{ fontSize: '12px', wordBreak: 'break-all' }}>{selectedCheckLog.conditionKey}</code>
              </Descriptions.Item>
              <Descriptions.Item label="检查方法" span={1}>
                <code>{selectedCheckLog.method}</code>
              </Descriptions.Item>
              <Descriptions.Item label="是否必需" span={1}>
                <Tag color={selectedCheckLog.isRequired ? 'red' : 'default'}>
                  {selectedCheckLog.isRequired ? '是' : '否'}
                </Tag>
              </Descriptions.Item>
              <Descriptions.Item label="检查结果" span={1}>
                <Tag color={selectedCheckLog.success ? 'success' : 'error'}>
                  {selectedCheckLog.success ? '成功' : '失败'}
                </Tag>
              </Descriptions.Item>
              <Descriptions.Item label="返回消息" span={2}>
                <div style={{ maxWidth: '100%', wordBreak: 'break-word', whiteSpace: 'pre-wrap' }}>
                  {selectedCheckLog.message || '-'}
                </div>
              </Descriptions.Item>
            </Descriptions>

            <Divider>检查过程详情</Divider>
            <div style={{ background: '#f5f5f5', padding: '16px', borderRadius: '4px' }}>
              {selectedCheckLog.checkProcess ? (
                <pre
                  style={{
                    margin: 0,
                    fontSize: '13px',
                    lineHeight: '1.6',
                    whiteSpace: 'pre-wrap',
                    wordBreak: 'break-word',
                    maxHeight: 'calc(100vh - 400px)',
                    overflow: 'auto',
                  }}
                >
                  {(() => {
                    try {
                      const obj = JSON.parse(selectedCheckLog.checkProcess);
                      return JSON.stringify(obj, null, 2);
                    } catch {
                      return selectedCheckLog.checkProcess;
                    }
                  })()}
                </pre>
              ) : (
                <div style={{ color: '#999' }}>无检查过程详情</div>
              )}
            </div>

            <div style={{ marginTop: 24, textAlign: 'center', color: '#999', fontSize: '12px' }}>
              提示：双击可关闭此窗口
            </div>
          </div>
        )}
      </Modal>

      {/* 风控仓位详情弹窗 */}
      <Modal
        title={`仓位详情 - ${selectedRiskPosition?.positionId ?? ''}`}
        open={riskPositionDetailVisible}
        onCancel={() => {
          setRiskPositionDetailVisible(false);
          setSelectedRiskPosition(null);
        }}
        footer={null}
        width={900}
      >
        {selectedRiskPosition && (
          <Descriptions column={2} bordered size="small">
            <Descriptions.Item label="仓位ID">{selectedRiskPosition.positionId}</Descriptions.Item>
            <Descriptions.Item label="状态">{selectedRiskPosition.status}</Descriptions.Item>
            <Descriptions.Item label="用户ID">{selectedRiskPosition.uid}</Descriptions.Item>
            <Descriptions.Item label="策略ID">{selectedRiskPosition.usId}</Descriptions.Item>
            <Descriptions.Item label="交易所">{selectedRiskPosition.exchange}</Descriptions.Item>
            <Descriptions.Item label="交易对">{selectedRiskPosition.symbol}</Descriptions.Item>
            <Descriptions.Item label="方向">{selectedRiskPosition.side}</Descriptions.Item>
            <Descriptions.Item label="数量">{formatDecimal(selectedRiskPosition.qty)}</Descriptions.Item>
            <Descriptions.Item label="入场价">{formatDecimal(selectedRiskPosition.entryPrice)}</Descriptions.Item>
            <Descriptions.Item label="止损价">{formatDecimal(selectedRiskPosition.stopLossPrice)}</Descriptions.Item>
            <Descriptions.Item label="止盈价">{formatDecimal(selectedRiskPosition.takeProfitPrice)}</Descriptions.Item>
            <Descriptions.Item label="移动止盈价">{formatDecimal(selectedRiskPosition.trailingStopPrice)}</Descriptions.Item>
            <Descriptions.Item label="移动止盈激活率">{formatDecimal(selectedRiskPosition.trailingActivationPct)}</Descriptions.Item>
            <Descriptions.Item label="移动止盈回撤率">{formatDecimal(selectedRiskPosition.trailingDrawdownPct)}</Descriptions.Item>
            <Descriptions.Item label="激活价">{formatDecimal(selectedRiskPosition.trailingActivationPrice)}</Descriptions.Item>
            <Descriptions.Item label="更新阈值价">{formatDecimal(selectedRiskPosition.trailingUpdateThresholdPrice)}</Descriptions.Item>
          </Descriptions>
        )}
      </Modal>

      {/* 实盘任务链路明细弹窗 */}
      <Modal
        title={`任务明细 - ${selectedTaskTraceId ?? ''}`}
        open={taskTraceDetailVisible}
        onCancel={() => {
          setTaskTraceDetailVisible(false);
          setSelectedTaskTraceId(null);
          setTaskTraceDetailItems([]);
        }}
        footer={null}
        width={1400}
        styles={{ body: { maxHeight: '70vh', overflow: 'auto' } }}
      >
        {taskTraceDetailLoading ? (
          <div style={{ textAlign: 'center', padding: 40 }}>
            <Spin size="large" />
          </div>
        ) : taskTraceDetailItems.length === 0 ? (
          <Empty description="暂无明细" />
        ) : (
          <Table
            dataSource={taskTraceDetailItems}
            rowKey="id"
            size="small"
            scroll={{ x: 1400 }}
            pagination={{ pageSize: 20 }}
            columns={[
              { title: 'ID', dataIndex: 'id', key: 'id', width: 60 },
              { title: '时间', dataIndex: 'createdAt', key: 'createdAt', width: 175, render: (v: string) => formatDate(v) },
              { title: '阶段', dataIndex: 'eventStage', key: 'eventStage', width: 140, ellipsis: true },
              {
                title: '状态',
                dataIndex: 'eventStatus',
                key: 'eventStatus',
                width: 80,
                render: (v: string) => {
                  const color = v === 'success' ? 'success' : v === 'fail' ? 'error' : v === 'skip' ? 'default' : 'processing';
                  return <Tag color={color}>{v}</Tag>;
                },
              },
              { title: '模块', dataIndex: 'actorModule', key: 'actorModule', width: 120, ellipsis: true },
              { title: '实例', dataIndex: 'actorInstance', key: 'actorInstance', width: 180, ellipsis: true },
              { title: '交易所', dataIndex: 'exchange', key: 'exchange', width: 90 },
              { title: '币对', dataIndex: 'symbol', key: 'symbol', width: 100 },
              { title: '周期', dataIndex: 'timeframe', key: 'timeframe', width: 80 },
              { title: '策略ID', dataIndex: 'usId', key: 'usId', width: 90 },
              {
                title: '方法/流程',
                key: 'methodFlow',
                width: 140,
                render: (_: unknown, r: TaskTraceLogItem) => (
                  <span title={r.flow ?? undefined}>{r.method ?? '-'}{r.flow ? ` / ${r.flow}` : ''}</span>
                ),
              },
              { title: '耗时(ms)', dataIndex: 'durationMs', key: 'durationMs', width: 90 },
              {
                title: '错误',
                dataIndex: 'errorMessage',
                key: 'errorMessage',
                width: 180,
                ellipsis: true,
                render: (v?: string | null) => (v ? <span style={{ color: '#cf1322' }}>{v}</span> : '-'),
              },
              {
                title: '明细JSON',
                dataIndex: 'metricsJson',
                key: 'metricsJson',
                width: 200,
                ellipsis: true,
                render: (v?: string | null) =>
                  v ? (
                    <pre style={{ margin: 0, fontSize: 11, maxWidth: 300, overflow: 'auto' }} title={v}>
                      {v.length > 150 ? v.slice(0, 150) + '...' : v}
                    </pre>
                  ) : (
                    '-'
                  ),
              },
            ]}
          />
        )}
      </Modal>

      {/* 市场详情弹窗：任务报告 + 运行策略 */}
      <Modal
        title={
          selectedMarket
            ? `市场详情 - ${selectedMarket.exchange} ${selectedMarket.symbol} ${selectedMarket.timeframe}`
            : '市场详情'
        }
        open={marketDetailVisible}
        onCancel={() => {
          setMarketDetailVisible(false);
          setSelectedMarket(null);
          setMarketTaskSummary(null);
          setMarketStrategies([]);
          setSelectedMarketTask(null);
          setMarketTaskDetailVisible(false);
        }}
        footer={null}
        width={900}
        styles={{ body: { maxHeight: '70vh', overflow: 'auto' } }}
      >
        {selectedMarket && (
          <Tabs
            defaultActiveKey="task-report"
            items={[
              {
                key: 'task-report',
                label: <span>任务执行报告</span>,
                children: (
                  <div>
                    {marketTaskSummaryLoading ? (
                      <div style={{ textAlign: 'center', padding: 40 }}>
                        <Spin size="large" />
                      </div>
                    ) : (
                      renderMarketTaskSummary()
                    )}
                  </div>
                ),
              },
              {
                key: 'strategies',
                label: <span>运行策略</span>,
                children: (
                  <div>
                    <div style={{ marginBottom: 12 }}>
                      <Space>
                        <Search
                          placeholder="搜索策略（名称/ID）"
                          allowClear
                          size="small"
                          style={{ width: 220 }}
                          value={marketStrategiesSearch}
                          onChange={(e) => setMarketStrategiesSearch(e.target.value)}
                          onSearch={() =>
                            loadMarketStrategies(
                              selectedMarket.exchange,
                              selectedMarket.symbol,
                              selectedMarket.timeframe,
                              1,
                              marketStrategiesPageSize,
                              marketStrategiesSearch
                            )
                          }
                        />
                        <Button
                          type="primary"
                          size="small"
                          icon={<ReloadOutlined />}
                          onClick={() =>
                            loadMarketStrategies(
                              selectedMarket.exchange,
                              selectedMarket.symbol,
                              selectedMarket.timeframe,
                              marketStrategiesPage,
                              marketStrategiesPageSize,
                              marketStrategiesSearch
                            )
                          }
                          loading={marketStrategiesLoading}
                        >
                          刷新
                        </Button>
                      </Space>
                    </div>
                    {marketStrategiesLoading ? (
                      <div style={{ textAlign: 'center', padding: 20 }}>
                        <Spin size="small" />
                      </div>
                    ) : marketStrategies.length === 0 ? (
                      <Empty description="暂无运行中的策略" />
                    ) : (
                      <Table
                        dataSource={marketStrategies}
                        rowKey="usId"
                        size="small"
                        pagination={{
                          current: marketStrategiesPage,
                          pageSize: marketStrategiesPageSize,
                          total: marketStrategiesTotal,
                          pageSizeOptions: ['10', '20', '50'],
                          showSizeChanger: true,
                          showTotal: (t) => `共 ${t} 条`,
                          onChange: (page, pageSize) => {
                            loadMarketStrategies(
                              selectedMarket.exchange,
                              selectedMarket.symbol,
                              selectedMarket.timeframe,
                              page,
                              pageSize ?? marketStrategiesPageSize,
                              marketStrategiesSearch
                            );
                          },
                        }}
                        columns={[
                          {
                            title: '策略',
                            key: 'strategy',
                            render: (_: unknown, s: Strategy) => (
                              <Space size="small">
                                <span className="strategy-name">{s.aliasName || s.defName}</span>
                                {getStrategyStatusTag(s.state)}
                                <span className="strategy-meta-inline">ID:{s.usId} | v{s.versionNo}</span>
                              </Space>
                            ),
                          },
                          {
                            title: '描述',
                            dataIndex: 'description',
                            key: 'description',
                            ellipsis: true,
                            render: (v: string) => (v ? <div className="strategy-description">{v}</div> : '-'),
                          },
                          {
                            title: '操作',
                            key: 'action',
                            width: 100,
                            render: (_: unknown, s: Strategy) => (
                              <Button
                                type="link"
                                size="small"
                                onClick={(e) => {
                                  e.stopPropagation();
                                  handleViewStrategyDetail(s);
                                }}
                              >
                                查看详情
                              </Button>
                            ),
                          },
                        ]}
                      />
                    )}
                  </div>
                ),
              },
            ]}
          />
        )}
      </Modal>

      {/* 市场任务详情弹窗（主记录） */}
      <Modal
        title={`任务详情 - ${selectedMarketTask?.traceId || ''}`}
        open={marketTaskDetailVisible}
        onCancel={() => {
          setMarketTaskDetailVisible(false);
          setSelectedMarketTask(null);
        }}
        footer={null}
        width={1100}
        styles={{ body: { maxHeight: '70vh', overflow: 'auto' } }}
        className="market-task-detail-modal"
      >
        {selectedMarketTask ? (
          <div className="market-task-detail">
            <div className="market-task-detail-header">
              <div className="market-task-detail-header-main">
                <span className="market-task-label">任务</span>
                <code className="market-task-trace-id">{selectedMarketTask.traceId || '-'}</code>
              </div>
              <div className="market-task-detail-header-meta">
                <span className="market-task-time">{formatDate(selectedMarketTask.runAt)}</span>
                {getRunStatusTag(selectedMarketTask.runStatus)}
              </div>
            </div>

            <Divider className="market-task-divider" />

            <Row gutter={16} className="market-task-section-row">
              <Col span={24}>
                <Descriptions column={2} size="small" bordered>
                  <Descriptions.Item label="交易所">{selectedMarketTask.exchange}</Descriptions.Item>
                  <Descriptions.Item label="币对">{selectedMarketTask.symbol}</Descriptions.Item>
                  <Descriptions.Item label="周期">{selectedMarketTask.timeframe}</Descriptions.Item>
                  <Descriptions.Item label="模式">{selectedMarketTask.isBarClose ? 'close' : 'update'}</Descriptions.Item>
                  <Descriptions.Item label="处理方" span={2}>
                    <code>{selectedMarketTask.engineInstance || '-'}</code>
                  </Descriptions.Item>
                  <Descriptions.Item label="总耗时(ms)" span={2}>
                    {selectedMarketTask.durationMs}
                  </Descriptions.Item>
                  <Descriptions.Item label="分段耗时(ms)" span={2}>
                    查找 {selectedMarketTask.lookupMs} / 指标 {selectedMarketTask.indicatorMs} / 执行{' '}
                    {selectedMarketTask.executeMs}
                  </Descriptions.Item>
                </Descriptions>
              </Col>

              <Col span={24}>
                <Descriptions column={1} size="small" bordered>
                  <Descriptions.Item label="匹配/可运行/执行">
                    {selectedMarketTask.matchedCount} / {selectedMarketTask.runnableStrategyCount} /{' '}
                    {selectedMarketTask.executedCount}
                  </Descriptions.Item>
                  <Descriptions.Item label="状态跳过/门禁跳过">
                    {selectedMarketTask.stateSkippedCount} / {selectedMarketTask.runtimeGateSkippedCount}
                  </Descriptions.Item>
                  <Descriptions.Item label="条件/动作/开仓">
                    {selectedMarketTask.conditionEvalCount} / {selectedMarketTask.actionExecCount} /{' '}
                    {selectedMarketTask.openTaskCount}
                  </Descriptions.Item>
                  <Descriptions.Item label="指标请求/成功">
                    {selectedMarketTask.indicatorRequestCount} / {selectedMarketTask.indicatorSuccessCount}/
                    {selectedMarketTask.indicatorTotalCount}
                  </Descriptions.Item>
                  <Descriptions.Item label="执行成功率">
                    {selectedMarketTask.successRatePct}%
                  </Descriptions.Item>
                  <Descriptions.Item label="开仓触发率">
                    {selectedMarketTask.openTaskRatePct}%
                  </Descriptions.Item>
                  <Descriptions.Item label="单策略样本/均值/最大">
                    {selectedMarketTask.perStrategySamples} / {selectedMarketTask.perStrategyAvgMs}ms /{' '}
                    {selectedMarketTask.perStrategyMaxMs}ms
                  </Descriptions.Item>
                </Descriptions>
              </Col>
            </Row>

            <Divider className="market-task-divider" />

            <Descriptions column={1} size="small" bordered>
              <Descriptions.Item label="追踪ID">
                <code>{selectedMarketTask.traceId || '-'}</code>
              </Descriptions.Item>
              <Descriptions.Item label="执行策略ID集合">
                <code>{selectedMarketTask.executedStrategyIds || '-'}</code>
              </Descriptions.Item>
              <Descriptions.Item label="开仓策略ID集合">
                <code>{selectedMarketTask.openTaskStrategyIds || '-'}</code>
              </Descriptions.Item>
              <Descriptions.Item label="开仓任务ID集合">
                <code>{selectedMarketTask.openTaskTraceIds || '-'}</code>
              </Descriptions.Item>
              <Descriptions.Item label="订单ID集合">
                <code>{selectedMarketTask.openOrderIds || '-'}</code>
              </Descriptions.Item>
            </Descriptions>
          </div>
        ) : (
          <Empty description="暂无任务明细" />
        )}
      </Modal>
    </div>
  );
};

export default ServerList;
