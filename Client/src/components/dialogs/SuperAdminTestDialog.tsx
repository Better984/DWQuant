import React, { useMemo, useState } from 'react';
import { Dialog, useNotification } from '../ui/index.ts';
import { HttpClient } from '../../network/httpClient.ts';
import { getToken } from '../../network/index.ts';
import './SuperAdminTestDialog.css';

interface SuperAdminTestDialogProps {
  open: boolean;
  onClose: () => void;
}

interface UniversalSearchAccount {
  uid: number;
  email?: string;
  nickname?: string;
  role?: number;
  status?: number;
}

interface UniversalSearchResponse {
  account?: UniversalSearchAccount;
}

interface CreateUserResponse {
  uid: number;
  email: string;
  nickname: string;
  role: number;
  status: number;
}

interface CreateRandomStrategyResponse {
  strategy: {
    defId: number;
    usId: number;
    versionId: number;
    versionNo: number;
    name: string;
    state: string;
  };
  random: {
    entryMethod: string;
    exitMethod: string;
    leftIndicator: string;
    rightIndicator: string;
    timeframe: string;
    exchange: string;
    symbol: string;
  };
  testing: {
    requested: boolean;
    success: boolean;
    message: string;
    state: string;
  };
}

interface LatencySummary {
  count: number;
  minMs: number;
  maxMs: number;
  avgMs: number;
  p50Ms: number;
  p95Ms: number;
  p99Ms: number;
}

interface BacktestStressRuntimeSnapshot {
  cpuCores: number;
  processThreadCount: number;
  workingSetMb: number;
  privateMemoryMb: number;
  gcHeapMb: number;
  totalAllocatedMb: number;
  threadPool: {
    maxWorkerThreads: number;
    availableWorkerThreads: number;
    busyWorkerThreads: number;
    maxIoThreads: number;
    availableIoThreads: number;
    busyIoThreads: number;
  };
}

interface BatchCreateTestingResponse {
  targetUid: number;
  requested: number;
  created: number;
  failed: number;
  switchSuccess: number;
  switchFailed: number;
  elapsedMs: number;
  failures: Array<{ phase: string; strategyName?: string; usId?: number; index?: number; error: string }>;
}

interface DeleteTestStrategiesResponse {
  targetUid: number;
  deleted: number;
  failed: number;
  elapsedMs: number;
  failures: Array<{ usId: number; error: string }>;
}

interface BacktestStressResponse {
  requested: {
    targetUid: number;
    strategyCount: number;
    tasksPerStrategy: number;
    totalSubmitRequested: number;
    submitParallelism: number;
    barCount: number;
    initialCapital: number;
    executionMode: string;
    includeDetailedOutput: boolean;
    pollAfterSubmitSeconds: number;
  };
  strategies: {
    created: number;
    failed: number;
    createElapsedMs: number;
    createLatency: LatencySummary;
    sample: Array<{
      usId: number;
      strategyName: string;
      exchange: string;
      symbol: string;
      timeframe: string;
    }>;
    failures: Array<{
      phase: string;
      strategyName?: string;
      error: string;
      elapsedMs: number;
    }>;
  };
  submissions: {
    success: number;
    failed: number;
    successRatePct: number;
    submitElapsedMs: number;
    submitLatency: LatencySummary;
    submittedTaskIdsSample: number[];
    workerThreadParticipation: {
      distinctThreadCount: number;
      threadIds: number[];
    };
    failures: Array<{
      phase: string;
      usId?: number;
      submitRound?: number;
      error: string;
      elapsedMs: number;
    }>;
  };
  taskStatus: {
    immediate: Record<string, number>;
    afterWaitSeconds: number;
    afterWait?: Record<string, number> | null;
  };
  runtime: {
    before: BacktestStressRuntimeSnapshot;
    afterSubmit: BacktestStressRuntimeSnapshot;
    afterPoll: BacktestStressRuntimeSnapshot;
  };
}

const EXCHANGE_OPTIONS = [
  { value: '', label: '随机' },
  { value: 'binance', label: 'Binance' },
  { value: 'okx', label: 'OKX' },
  { value: 'bitget', label: 'Bitget' },
];

const SYMBOL_OPTIONS = [
  { value: '', label: '随机' },
  { value: 'BTC/USDT', label: 'BTC/USDT' },
  { value: 'ETH/USDT', label: 'ETH/USDT' },
  { value: 'SOL/USDT', label: 'SOL/USDT' },
  { value: 'XRP/USDT', label: 'XRP/USDT' },
];

const TIMEFRAME_OPTIONS = [
  { value: '', label: '随机' },
  { value: '60', label: '1分钟' },
  { value: '300', label: '5分钟' },
  { value: '900', label: '15分钟' },
  { value: '3600', label: '1小时' },
  { value: '14400', label: '4小时' },
];

const BACKTEST_EXECUTION_MODE_OPTIONS = [
  { value: 'batch_open_close', label: '批量模式（推荐）' },
  { value: 'timeline', label: '时间轴模式' },
];

const SuperAdminTestDialog: React.FC<SuperAdminTestDialogProps> = ({ open, onClose }) => {
  const { success: showSuccess, error: showError } = useNotification();
  const client = useMemo(() => new HttpClient({ tokenProvider: getToken }), []);

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('123456');
  const [nickname, setNickname] = useState('');
  const [creatingUser, setCreatingUser] = useState(false);
  const [createdUser, setCreatedUser] = useState<CreateUserResponse | null>(null);

  const [targetQuery, setTargetQuery] = useState('');
  const [searchingUser, setSearchingUser] = useState(false);
  const [targetUser, setTargetUser] = useState<UniversalSearchAccount | null>(null);
  const [creatingStrategy, setCreatingStrategy] = useState(false);
  const [strategyResult, setStrategyResult] = useState<CreateRandomStrategyResponse | null>(null);
  const [preferredExchange, setPreferredExchange] = useState('');
  const [preferredSymbol, setPreferredSymbol] = useState('');
  const [preferredTimeframeSec, setPreferredTimeframeSec] = useState('');
  const [conditionCount, setConditionCount] = useState('5');
  const [backtestStrategyCount, setBacktestStrategyCount] = useState('20');
  const [backtestTasksPerStrategy, setBacktestTasksPerStrategy] = useState('1');
  const [backtestSubmitParallelism, setBacktestSubmitParallelism] = useState('4');
  const [backtestBarCount, setBacktestBarCount] = useState('1500');
  const [backtestInitialCapital, setBacktestInitialCapital] = useState('10000');
  const [backtestPollSeconds, setBacktestPollSeconds] = useState('5');
  const [backtestExecutionMode, setBacktestExecutionMode] = useState('batch_open_close');
  const [backtestIncludeDetailedOutput, setBacktestIncludeDetailedOutput] = useState(false);
  const [runningBacktestStress, setRunningBacktestStress] = useState(false);
  const [backtestStressResult, setBacktestStressResult] = useState<BacktestStressResponse | null>(null);

  const [batchTestingCount, setBatchTestingCount] = useState('10');
  const [runningBatchTesting, setRunningBatchTesting] = useState(false);
  const [batchTestingResult, setBatchTestingResult] = useState<BatchCreateTestingResponse | null>(null);
  const [deletingTestStrategies, setDeletingTestStrategies] = useState(false);
  const [deleteTestStrategiesResult, setDeleteTestStrategiesResult] = useState<DeleteTestStrategiesResponse | null>(null);

  const parseIntValue = (value: string, fallback: number) => {
    const parsed = Number(value);
    if (!Number.isFinite(parsed)) {
      return fallback;
    }

    return Math.floor(parsed);
  };

  const formatStatusDistribution = (distribution?: Record<string, number> | null) => {
    if (!distribution) {
      return '-';
    }

    const entries = Object.entries(distribution);
    if (entries.length === 0) {
      return '-';
    }

    return entries.map(([status, count]) => `${status}:${count}`).join(' / ');
  };

  const handleCreateUser = async () => {
    const trimmedEmail = email.trim();
    const trimmedPassword = password.trim();
    if (!trimmedEmail) {
      showError('请输入邮箱');
      return;
    }
    if (!trimmedPassword) {
      showError('请输入密码');
      return;
    }

    setCreatingUser(true);
    try {
      const response = await client.postProtocol<CreateUserResponse>(
        '/api/admin/user/test/create-user',
        'admin.user.test.create-user',
        {
          email: trimmedEmail,
          password: trimmedPassword,
          nickname: nickname.trim() || undefined,
        },
      );

      setCreatedUser(response);
      showSuccess('测试用户创建成功');
      setTargetUser({
        uid: response.uid,
        email: response.email,
        nickname: response.nickname,
        role: response.role,
        status: response.status,
      });
      setTargetQuery(String(response.uid));
      setBacktestStressResult(null);
      setBatchTestingResult(null);
    } catch (error) {
      showError(error instanceof Error ? error.message : '创建测试用户失败');
    } finally {
      setCreatingUser(false);
    }
  };

  const handleSearchUser = async () => {
    const query = targetQuery.trim();
    if (!query) {
      showError('请输入 UID 或邮箱');
      return;
    }

    setSearchingUser(true);
    try {
      const response = await client.postProtocol<UniversalSearchResponse>(
        '/api/admin/user/universal-search',
        'admin.user.universal-search',
        { query },
      );

      if (!response?.account || !response.account.uid) {
        throw new Error('未找到目标用户');
      }

      setTargetUser(response.account);
      setStrategyResult(null);
      setBacktestStressResult(null);
      setBatchTestingResult(null);
      setDeleteTestStrategiesResult(null);
      showSuccess('查询用户成功');
    } catch (error) {
      setTargetUser(null);
      setBacktestStressResult(null);
      setBatchTestingResult(null);
      showError(error instanceof Error ? error.message : '查询用户失败');
    } finally {
      setSearchingUser(false);
    }
  };

  const handleCreateRandomStrategy = async () => {
    if (!targetUser?.uid) {
      showError('请先查询并确认目标用户');
      return;
    }

    setCreatingStrategy(true);
    try {
      const condCount = parseIntValue(conditionCount, 5);
      const response = await client.postProtocol<CreateRandomStrategyResponse>(
        '/api/admin/user/test/create-random-strategy',
        'admin.user.test.create-random-strategy',
        {
          targetUid: targetUser.uid,
          autoSwitchToTesting: true,
          conditionCount: condCount >= 1 && condCount <= 20 ? condCount : undefined,
          preferredExchange: preferredExchange || undefined,
          preferredSymbol: preferredSymbol || undefined,
          preferredTimeframeSec: preferredTimeframeSec ? Number(preferredTimeframeSec) : undefined,
        },
      );

      setStrategyResult(response);
      if (response.testing.success) {
        showSuccess('随机策略创建并切换 testing 成功');
      } else {
        showError(`策略已创建，但切换 testing 失败：${response.testing.message}`);
      }
    } catch (error) {
      showError(error instanceof Error ? error.message : '创建随机策略失败');
    } finally {
      setCreatingStrategy(false);
    }
  };

  const handleBatchCreateTesting = async () => {
    if (!targetUser?.uid) {
      showError('请先查询并确认目标用户');
      return;
    }

    const count = parseIntValue(batchTestingCount, 10);
    if (count <= 0 || count > 1000) {
      showError('策略数量需在 1～1000 之间');
      return;
    }

    const condCount = parseIntValue(conditionCount, 5);
    setRunningBatchTesting(true);
    try {
      const response = await client.postProtocol<BatchCreateTestingResponse>(
        '/api/admin/user/test/batch-create-testing-strategies',
        'admin.user.test.batch-create-testing-strategies',
        {
          targetUid: targetUser.uid,
          strategyCount: count,
          conditionCount: condCount >= 1 && condCount <= 20 ? condCount : undefined,
          preferredExchange: preferredExchange || undefined,
          preferredSymbol: preferredSymbol || undefined,
          preferredTimeframeSec: preferredTimeframeSec ? Number(preferredTimeframeSec) : undefined,
        },
        { timeoutMs: 120000 },
      );

      setBatchTestingResult(response);
      showSuccess(`批量创建完成：${response.created} 个策略已切 testing`);
    } catch (error) {
      showError(error instanceof Error ? error.message : '批量创建测试策略失败');
    } finally {
      setRunningBatchTesting(false);
    }
  };

  const handleDeleteTestStrategies = async () => {
    if (!targetUser?.uid) {
      showError('请先查询并确认目标用户');
      return;
    }

    setDeletingTestStrategies(true);
    setDeleteTestStrategiesResult(null);
    try {
      const response = await client.postProtocol<DeleteTestStrategiesResponse>(
        '/api/admin/user/test/delete-test-strategies',
        'admin.user.test.delete-test-strategies',
        { targetUid: targetUser.uid },
      );

      setDeleteTestStrategiesResult(response);
      if (response.deleted > 0) {
        showSuccess(`已删除 ${response.deleted} 个测试策略`);
      } else if (response.failed === 0) {
        showSuccess('该用户暂无测试策略');
      } else {
        showError(`删除失败 ${response.failed} 个`);
      }
    } catch (error) {
      showError(error instanceof Error ? error.message : '删除测试策略失败');
    } finally {
      setDeletingTestStrategies(false);
    }
  };

  const handleRunBacktestStress = async () => {
    if (!targetUser?.uid) {
      showError('请先查询并确认目标用户');
      return;
    }

    const strategyCount = parseIntValue(backtestStrategyCount, 20);
    const tasksPerStrategy = parseIntValue(backtestTasksPerStrategy, 1);
    const submitParallelism = parseIntValue(backtestSubmitParallelism, 4);
    const barCount = parseIntValue(backtestBarCount, 1500);
    const initialCapital = Number(backtestInitialCapital);
    const pollAfterSubmitSeconds = parseIntValue(backtestPollSeconds, 5);

    if (strategyCount <= 0 || tasksPerStrategy <= 0 || submitParallelism <= 0 || barCount <= 0) {
      showError('压测参数必须大于 0');
      return;
    }

    if (!Number.isFinite(initialCapital) || initialCapital <= 0) {
      showError('初始资金必须大于 0');
      return;
    }

    setRunningBacktestStress(true);
    try {
      const response = await client.postProtocol<BacktestStressResponse>(
        '/api/admin/user/test/backtest-stress',
        'admin.user.test.backtest-stress',
        {
          targetUid: targetUser.uid,
          strategyCount,
          conditionCount: (() => {
            const c = parseIntValue(conditionCount, 5);
            return c >= 1 && c <= 20 ? c : undefined;
          })(),
          tasksPerStrategy,
          submitParallelism,
          barCount,
          initialCapital,
          executionMode: backtestExecutionMode,
          includeDetailedOutput: backtestIncludeDetailedOutput,
          pollAfterSubmitSeconds,
          preferredExchange: preferredExchange || undefined,
          preferredSymbol: preferredSymbol || undefined,
          preferredTimeframeSec: preferredTimeframeSec ? Number(preferredTimeframeSec) : undefined,
        },
        { timeoutMs: 180000 },
      );

      setBacktestStressResult(response);
      showSuccess(`回测压测完成：提交成功 ${response.submissions.success}/${response.requested.totalSubmitRequested}`);
    } catch (error) {
      showError(error instanceof Error ? error.message : '执行回测压测失败');
    } finally {
      setRunningBacktestStress(false);
    }
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="超级测试台"
      cancelText="关闭"
      className="super-admin-test-dialog"
    >
      <div className="super-admin-test-dialog__content ui-scrollable">
        <section className="super-admin-test-dialog__section">
          <h3 className="super-admin-test-dialog__section-title">创建测试用户</h3>
          <div className="super-admin-test-dialog__grid">
            <label className="super-admin-test-dialog__field">
              <span>邮箱</span>
              <input
                value={email}
                onChange={(event) => setEmail(event.target.value)}
                placeholder="test_user_xxx@example.com"
                autoComplete="off"
              />
            </label>
            <label className="super-admin-test-dialog__field">
              <span>密码</span>
              <input
                value={password}
                onChange={(event) => setPassword(event.target.value)}
                placeholder="例如 123456"
                autoComplete="off"
              />
            </label>
          </div>
          <label className="super-admin-test-dialog__field">
            <span>昵称（可选）</span>
            <input
              value={nickname}
              onChange={(event) => setNickname(event.target.value)}
              placeholder="为空时自动生成测试昵称"
              autoComplete="off"
            />
          </label>
          <div className="super-admin-test-dialog__actions">
            <button
              type="button"
              className="super-admin-test-dialog__button"
              onClick={handleCreateUser}
              disabled={creatingUser}
            >
              {creatingUser ? '创建中...' : '创建测试用户'}
            </button>
          </div>

          {createdUser && (
            <div className="super-admin-test-dialog__result-card">
              <div>UID: {createdUser.uid}</div>
              <div>邮箱: {createdUser.email}</div>
              <div>昵称: {createdUser.nickname}</div>
              <div>角色: {createdUser.role}</div>
              <div>状态: {createdUser.status}</div>
            </div>
          )}
        </section>

        <section className="super-admin-test-dialog__section">
          <h3 className="super-admin-test-dialog__section-title">给指定用户创建随机策略</h3>
          <div className="super-admin-test-dialog__query-row">
            <input
              value={targetQuery}
              onChange={(event) => setTargetQuery(event.target.value)}
              placeholder="输入 UID 或邮箱"
              autoComplete="off"
            />
            <button
              type="button"
              className="super-admin-test-dialog__button super-admin-test-dialog__button--secondary"
              onClick={handleSearchUser}
              disabled={searchingUser}
            >
              {searchingUser ? '查询中...' : '查询用户'}
            </button>
          </div>

          {targetUser && (
            <div className="super-admin-test-dialog__user-card">
              <div>UID: {targetUser.uid}</div>
              <div>邮箱: {targetUser.email || '-'}</div>
              <div>昵称: {targetUser.nickname || '-'}</div>
              <div>角色: {targetUser.role ?? '-'}</div>
              <div>状态: {targetUser.status ?? '-'}</div>
            </div>
          )}

          <div className="super-admin-test-dialog__grid">
            <label className="super-admin-test-dialog__field">
              <span>交易所</span>
              <select
                value={preferredExchange}
                onChange={(event) => setPreferredExchange(event.target.value)}
              >
                {EXCHANGE_OPTIONS.map((option) => (
                  <option key={option.value || 'random'} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </select>
            </label>
            <label className="super-admin-test-dialog__field">
              <span>时间周期</span>
              <select
                value={preferredTimeframeSec}
                onChange={(event) => setPreferredTimeframeSec(event.target.value)}
              >
                {TIMEFRAME_OPTIONS.map((option) => (
                  <option key={option.value || 'random'} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </select>
            </label>
          </div>
          <label className="super-admin-test-dialog__field">
            <span>币种</span>
            <select
              value={preferredSymbol}
              onChange={(event) => setPreferredSymbol(event.target.value)}
            >
              {SYMBOL_OPTIONS.map((option) => (
                <option key={option.value || 'random'} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </label>
          <label className="super-admin-test-dialog__field">
            <span>条件数量（开多/开空各）</span>
            <input
              type="number"
              min={1}
              max={20}
              value={conditionCount}
              onChange={(e) => setConditionCount(e.target.value)}
              style={{ width: '60px' }}
            />
          </label>

          <div className="super-admin-test-dialog__actions">
            <button
              type="button"
              className="super-admin-test-dialog__button"
              onClick={handleCreateRandomStrategy}
              disabled={creatingStrategy || !targetUser}
            >
              {creatingStrategy ? '创建中...' : '创建随机策略并切 testing'}
            </button>
            <button
              type="button"
              className="super-admin-test-dialog__button super-admin-test-dialog__button--secondary"
              onClick={handleDeleteTestStrategies}
              disabled={deletingTestStrategies || !targetUser}
            >
              {deletingTestStrategies ? '删除中...' : '一键删除测试策略'}
            </button>
          </div>

          {deleteTestStrategiesResult && (
            <div className="super-admin-test-dialog__result-card">
              <div>删除成功/失败: {deleteTestStrategiesResult.deleted} / {deleteTestStrategiesResult.failed}</div>
              <div>耗时: {deleteTestStrategiesResult.elapsedMs}ms</div>
              {deleteTestStrategiesResult.failures.length > 0 && (
                <div className="super-admin-test-dialog__mono">
                  失败样本: {deleteTestStrategiesResult.failures.map((f) => `usId=${f.usId}: ${f.error}`).join('; ')}
                </div>
              )}
            </div>
          )}

          {strategyResult && (
            <div
              className={`super-admin-test-dialog__result-card ${
                strategyResult.testing.success ? '' : 'is-failed'
              }`}
            >
              <div>策略名称: {strategyResult.strategy.name}</div>
              <div>defId/usId/versionId/versionNo: {`${strategyResult.strategy.defId} / ${strategyResult.strategy.usId} / ${strategyResult.strategy.versionId} / ${strategyResult.strategy.versionNo}`}</div>
              <div>当前状态: {strategyResult.strategy.state}</div>
              <div>随机条件: {`${strategyResult.random.leftIndicator} ${strategyResult.random.entryMethod} ${strategyResult.random.rightIndicator}`}</div>
              <div>反向条件: {strategyResult.random.exitMethod}</div>
              <div>交易参数: {`${strategyResult.random.exchange} / ${strategyResult.random.symbol} / ${strategyResult.random.timeframe}`}</div>
              <div>testing 请求: {strategyResult.testing.requested ? '是' : '否'}</div>
              <div>testing 结果: {strategyResult.testing.success ? '成功' : '失败'}</div>
              <div>testing 信息: {strategyResult.testing.message}</div>
            </div>
          )}

          <div className="super-admin-test-dialog__actions" style={{ marginTop: '12px' }}>
            <label className="super-admin-test-dialog__field" style={{ marginRight: '8px' }}>
              <span>批量数量</span>
              <input
                type="text"
                value={batchTestingCount}
                onChange={(e) => setBatchTestingCount(e.target.value)}
                placeholder="1～1000"
                style={{ width: '100px' }}
              />
            </label>
            <button
              type="button"
              className="super-admin-test-dialog__button super-admin-test-dialog__button--secondary"
              onClick={handleBatchCreateTesting}
              disabled={runningBatchTesting || !targetUser}
            >
              {runningBatchTesting ? '创建中...' : '批量创建并切 testing'}
            </button>
          </div>

          {batchTestingResult && (
            <div className="super-admin-test-dialog__result-card">
              <div>请求/创建成功/创建失败: {batchTestingResult.requested} / {batchTestingResult.created} / {batchTestingResult.failed}</div>
              <div>切换 testing 成功/失败: {batchTestingResult.switchSuccess} / {batchTestingResult.switchFailed}</div>
              <div>耗时: {batchTestingResult.elapsedMs}ms</div>
              {batchTestingResult.failures.length > 0 && (
                <div className="super-admin-test-dialog__mono">
                  失败样本: {batchTestingResult.failures.map((f) => `${f.phase}:${f.error}`).join('; ')}
                </div>
              )}
            </div>
          )}
        </section>

        <section className="super-admin-test-dialog__section">
          <h3 className="super-admin-test-dialog__section-title">回测压测（批量创建策略并提交任务）</h3>
          <div className="super-admin-test-dialog__hint">
            先批量创建随机策略，再并发调用后端回测任务提交，返回耗时、状态分布和线程快照。
          </div>

          <div className="super-admin-test-dialog__grid super-admin-test-dialog__grid--triple">
            <label className="super-admin-test-dialog__field">
              <span>策略数量</span>
              <input
                type="number"
                min={1}
                value={backtestStrategyCount}
                onChange={(event) => setBacktestStrategyCount(event.target.value)}
              />
            </label>
            <label className="super-admin-test-dialog__field">
              <span>每策略任务数</span>
              <input
                type="number"
                min={1}
                value={backtestTasksPerStrategy}
                onChange={(event) => setBacktestTasksPerStrategy(event.target.value)}
              />
            </label>
            <label className="super-admin-test-dialog__field">
              <span>提交并发</span>
              <input
                type="number"
                min={1}
                value={backtestSubmitParallelism}
                onChange={(event) => setBacktestSubmitParallelism(event.target.value)}
              />
            </label>
            <label className="super-admin-test-dialog__field">
              <span>K线数量</span>
              <input
                type="number"
                min={100}
                value={backtestBarCount}
                onChange={(event) => setBacktestBarCount(event.target.value)}
              />
            </label>
            <label className="super-admin-test-dialog__field">
              <span>初始资金</span>
              <input
                type="number"
                min={1}
                value={backtestInitialCapital}
                onChange={(event) => setBacktestInitialCapital(event.target.value)}
              />
            </label>
            <label className="super-admin-test-dialog__field">
              <span>提交后等待秒数</span>
              <input
                type="number"
                min={0}
                value={backtestPollSeconds}
                onChange={(event) => setBacktestPollSeconds(event.target.value)}
              />
            </label>
          </div>

          <div className="super-admin-test-dialog__grid">
            <label className="super-admin-test-dialog__field">
              <span>执行模式</span>
              <select
                value={backtestExecutionMode}
                onChange={(event) => setBacktestExecutionMode(event.target.value)}
              >
                {BACKTEST_EXECUTION_MODE_OPTIONS.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </select>
            </label>
            <label className="super-admin-test-dialog__field super-admin-test-dialog__switch">
              <span>返回详细明细</span>
              <input
                type="checkbox"
                checked={backtestIncludeDetailedOutput}
                onChange={(event) => setBacktestIncludeDetailedOutput(event.target.checked)}
              />
            </label>
          </div>

          <div className="super-admin-test-dialog__actions">
            <button
              type="button"
              className="super-admin-test-dialog__button"
              onClick={handleRunBacktestStress}
              disabled={runningBacktestStress || !targetUser}
            >
              {runningBacktestStress ? '压测中...' : '执行回测压测'}
            </button>
          </div>

          {backtestStressResult && (
            <div className="super-admin-test-dialog__result-card">
              <div>
                目标UID/请求总任务: {backtestStressResult.requested.targetUid} /{' '}
                {backtestStressResult.requested.totalSubmitRequested}
              </div>
              <div>
                策略创建成功/失败: {backtestStressResult.strategies.created} / {backtestStressResult.strategies.failed}
              </div>
              <div>
                任务提交成功/失败: {backtestStressResult.submissions.success} /{' '}
                {backtestStressResult.submissions.failed}（成功率 {backtestStressResult.submissions.successRatePct}%）
              </div>
              <div>
                创建耗时: {backtestStressResult.strategies.createElapsedMs}ms，提交耗时:{' '}
                {backtestStressResult.submissions.submitElapsedMs}ms
              </div>
              <div>
                提交延迟P50/P95/P99: {backtestStressResult.submissions.submitLatency.p50Ms} /{' '}
                {backtestStressResult.submissions.submitLatency.p95Ms} /{' '}
                {backtestStressResult.submissions.submitLatency.p99Ms} ms
              </div>
              <div>即时状态分布: {formatStatusDistribution(backtestStressResult.taskStatus.immediate)}</div>
              <div>
                等待后状态分布({backtestStressResult.taskStatus.afterWaitSeconds}s):{' '}
                {formatStatusDistribution(backtestStressResult.taskStatus.afterWait)}
              </div>
              <div>
                线程参与数: {backtestStressResult.submissions.workerThreadParticipation.distinctThreadCount}
              </div>
              <div>
                进程线程数(前/后): {backtestStressResult.runtime.before.processThreadCount} /{' '}
                {backtestStressResult.runtime.afterPoll.processThreadCount}
              </div>
              <div>
                Worker线程池忙线程(前/后): {backtestStressResult.runtime.before.threadPool.busyWorkerThreads} /{' '}
                {backtestStressResult.runtime.afterPoll.threadPool.busyWorkerThreads}
              </div>
              <div className="super-admin-test-dialog__mono">
                任务样本ID: {backtestStressResult.submissions.submittedTaskIdsSample.join(', ') || '-'}
              </div>
            </div>
          )}
        </section>
      </div>
    </Dialog>
  );
};

export default SuperAdminTestDialog;
