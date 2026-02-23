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
      showSuccess('查询用户成功');
    } catch (error) {
      setTargetUser(null);
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
      const response = await client.postProtocol<CreateRandomStrategyResponse>(
        '/api/admin/user/test/create-random-strategy',
        'admin.user.test.create-random-strategy',
        {
          targetUid: targetUser.uid,
          autoSwitchToTesting: true,
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

          <div className="super-admin-test-dialog__actions">
            <button
              type="button"
              className="super-admin-test-dialog__button"
              onClick={handleCreateRandomStrategy}
              disabled={creatingStrategy || !targetUser}
            >
              {creatingStrategy ? '创建中...' : '创建随机策略并切 testing'}
            </button>
          </div>

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
        </section>
      </div>
    </Dialog>
  );
};

export default SuperAdminTestDialog;
