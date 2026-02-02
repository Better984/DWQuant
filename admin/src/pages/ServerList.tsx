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

const ServerList: React.FC = () => {
  const [servers, setServers] = useState<ServerNode[]>([]);
  const [loading, setLoading] = useState(false);
  const [expandedNodeId, setExpandedNodeId] = useState<string | null>(null);
  const [strategies, setStrategies] = useState<Strategy[]>([]);
  const [strategiesLoading, setStrategiesLoading] = useState(false);
  const [strategySearch, setStrategySearch] = useState<Record<string, string>>({});
  const [selectedStrategy, setSelectedStrategy] = useState<Strategy | null>(null);
  const [strategyPositions, setStrategyPositions] = useState<Position[]>([]);
  const [positionsLoading, setPositionsLoading] = useState(false);
  const [strategyDetailVisible, setStrategyDetailVisible] = useState(false);
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
      loadRunningStrategies();
    }
  }, [expandedNodeId]);

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

  const loadRunningStrategies = async () => {
    setStrategiesLoading(true);
    try {
      const data = await client.postProtocol<Strategy[]>('/api/strategy/list', 'strategy.list', {});
      const runningStrategies = (Array.isArray(data) ? data : []).filter(
        (s) => s.state?.toLowerCase() === 'running' || s.state?.toLowerCase() === 'paused_open_position' || s.state?.toLowerCase() === 'testing'
      );
      setStrategies(runningStrategies);
    } catch (err) {
      showError(err instanceof Error ? err.message : '加载策略列表失败');
    } finally {
      setStrategiesLoading(false);
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
    await loadStrategyPositions(strategy.usId);
  };

  const filteredStrategies = useMemo(() => {
    if (!expandedNodeId) return [];
    const searchText = strategySearch[expandedNodeId]?.toLowerCase() || '';
    if (!searchText) return strategies;
    return strategies.filter(
      (s) =>
        s.aliasName?.toLowerCase().includes(searchText) ||
        s.defName?.toLowerCase().includes(searchText) ||
        s.usId.toString().includes(searchText)
    );
  }, [strategies, strategySearch, expandedNodeId]);

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
                                <span className="strategies-count">策略数: <strong>{strategies.length}</strong></span>
                              </Space>
                              <Search
                                placeholder="搜索策略"
                                allowClear
                                size="small"
                                style={{ width: 240 }}
                                value={strategySearch[server.nodeId] || ''}
                                onChange={(e) =>
                                  setStrategySearch({ ...strategySearch, [server.nodeId]: e.target.value })
                                }
                                onSearch={(value) =>
                                  setStrategySearch({ ...strategySearch, [server.nodeId]: value })
                                }
                              />
                            </div>
                            {strategiesLoading ? (
                              <div style={{ textAlign: 'center', padding: 40 }}>
                                <Spin size="large" />
                              </div>
                            ) : filteredStrategies.length === 0 ? (
                              <Empty description="暂无运行中的策略" />
                            ) : (
                              <List
                                dataSource={filteredStrategies}
                                renderItem={(strategy) => (
                                  <List.Item
                                    className="strategy-item"
                                    actions={[
                                      <Button
                                        type="link"
                                        key="detail"
                                        onClick={(e) => {
                                          e.stopPropagation();
                                          handleViewStrategyDetail(strategy);
                                        }}
                                      >
                                        查看详情
                                      </Button>,
                                    ]}
                                  >
                                    <List.Item.Meta
                                      title={
                                        <Space size="small">
                                          <span className="strategy-name">{strategy.aliasName || strategy.defName}</span>
                                          {getStrategyStatusTag(strategy.state)}
                                          <span className="strategy-meta-inline">ID:{strategy.usId} | v{strategy.versionNo}</span>
                                        </Space>
                                      }
                                      description={
                                        strategy.description ? (
                                          <div className="strategy-description">{strategy.description}</div>
                                        ) : null
                                      }
                                    />
                                  </List.Item>
                                )}
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
    </div>
  );
};

export default ServerList;
