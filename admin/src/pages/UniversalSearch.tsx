import React, { useState } from 'react';
import { Tabs, Form, Input, Select, DatePicker, Button, Space } from 'antd';
import type { Dayjs } from 'dayjs';
import dayjs from 'dayjs';
import { HttpClient } from '../network/httpClient';
import { getToken } from '../network';
import { useNotification } from '../components/ui';
import './UniversalSearch.css';

interface AccountInfo {
  uid: number;
  email: string;
  nickname?: string;
  avatarUrl?: string;
  signature?: string;
  status: number;
  role: number;
  vipExpiredAt?: string;
  currentNotificationPlatform?: string;
  strategyIds?: string;
  lastLoginAt?: string;
  registerAt?: string;
  createdAt?: string;
  updatedAt?: string;
  passwordUpdatedAt?: string;
  deletedAt?: string;
}

interface UserStrategy {
  usId: number;
  defId: number;
  aliasName: string;
  state: string;
  visibility: string;
  shareCode?: string;
  exchangeApiKeyId?: number;
  sourceType: string;
  createdAt: string;
  updatedAt: string;
}

interface ShareCode {
  shareCode: string;
  defId: number;
  createdByUid: number;
  isActive: boolean;
  expiredAt?: string;
  createdAt: string;
}

interface ShareEvent {
  eventId: number;
  shareCode: string;
  defId: number;
  fromUid: number;
  toUid: number;
  fromInstanceId: number;
  toInstanceId: number;
  eventType: string;
  createdAt: string;
}

interface ImportLog {
  id: number;
  uid: number;
  usId: number;
  sourceType: string;
  sourceRef?: string;
  createdAt: string;
}

interface ExchangeApiKey {
  id: number;
  uid: number;
  exchangeType: string;
  label?: string;
  createdAt: string;
  updatedAt: string;
}

interface NotifyChannel {
  id: number;
  uid: number;
  platform: string;
  address: string;
  isEnabled: boolean;
  isDefault: boolean;
  createdAt: string;
  updatedAt: string;
}

interface StrategyPosition {
  positionId: number;
  uid: number;
  usId: number;
  exchangeApiKeyId?: number;
  exchange: string;
  symbol: string;
  side: string;
  entryPrice: number;
  qty: number;
  status: string;
  openedAt: string;
  closedAt?: string;
}

interface UserBehavior {
  account: AccountInfo;
  strategies: UserStrategy[];
  shareCodesCreated: ShareCode[];
  shareEvents: ShareEvent[];
  importLogs: ImportLog[];
  exchangeApiKeys: ExchangeApiKey[];
  notifyChannels: NotifyChannel[];
  positions: StrategyPosition[];
}

const UniversalSearch: React.FC = () => {
  const [searchKey, setSearchKey] = useState('');
  const [loading, setLoading] = useState(false);
  const [userBehavior, setUserBehavior] = useState<UserBehavior | null>(null);
  const [activeTab, setActiveTab] = useState('search');
  const [form] = Form.useForm();
  const [updating, setUpdating] = useState(false);
  const client = new HttpClient();
  client.setTokenProvider(getToken);
  const { success, error: showError } = useNotification();

  const searchUser = async () => {
    if (!searchKey.trim()) {
      showError('请输入查询关键词');
      return;
    }

    setLoading(true);
    try {
      // 调用万向查询API
      const response = await client.postProtocol<UserBehavior>(
        '/api/admin/user/universal-search',
        'admin.user.universal-search',
        { query: searchKey.trim() }
      );
      
      setUserBehavior(response);
      // 填充表单数据
      form.setFieldsValue({
        uid: response.account.uid,
        role: response.account.role,
        status: response.account.status,
        nickname: response.account.nickname || '',
        avatarUrl: response.account.avatarUrl || '',
        signature: response.account.signature || '',
        vipExpiredAt: response.account.vipExpiredAt ? dayjs(response.account.vipExpiredAt) : null,
        currentNotificationPlatform: response.account.currentNotificationPlatform || '',
        strategyIds: response.account.strategyIds || '',
      });
      success('查询成功');
    } catch (err) {
      showError(err instanceof Error ? err.message : '查询失败');
      setUserBehavior(null);
    } finally {
      setLoading(false);
    }
  };

  const handleUpdate = async () => {
    if (!userBehavior) {
      showError('请先查询用户');
      return;
    }

    try {
      const values = await form.validateFields();
      setUpdating(true);

      const updateData: any = {
        uid: values.uid,
      };

      if (values.role !== undefined) updateData.role = values.role;
      if (values.status !== undefined) updateData.status = values.status;
      if (values.nickname !== undefined) updateData.nickname = values.nickname;
      if (values.avatarUrl !== undefined) updateData.avatarUrl = values.avatarUrl;
      if (values.signature !== undefined) updateData.signature = values.signature;
      if (values.vipExpiredAt) updateData.vipExpiredAt = values.vipExpiredAt.toISOString();
      if (values.currentNotificationPlatform !== undefined) updateData.currentNotificationPlatform = values.currentNotificationPlatform;
      if (values.strategyIds !== undefined) updateData.strategyIds = values.strategyIds;

      const response = await client.postProtocol<any>(
        '/api/admin/user/update',
        'admin.user.update',
        updateData
      );

      success('更新成功');
      // 重新查询以刷新数据
      if (searchKey.trim()) {
        await searchUser();
      }
    } catch (err) {
      if (err && typeof err === 'object' && 'errorFields' in err) {
        // 表单验证错误
        return;
      }
      showError(err instanceof Error ? err.message : '更新失败');
    } finally {
      setUpdating(false);
    }
  };

  const formatDate = (dateStr?: string) => {
    if (!dateStr) return '-';
    return new Date(dateStr).toLocaleString('zh-CN');
  };

  const tabItems = [
    {
      key: 'search',
      label: '查询',
      children: (
        <>
          <div className="search-box">
            <input
              type="text"
              placeholder="输入任意用户相关信息进行查询..."
              value={searchKey}
              onChange={(e) => setSearchKey(e.target.value)}
              onKeyPress={(e) => e.key === 'Enter' && searchUser()}
              className="search-input"
            />
            <button onClick={searchUser} disabled={loading} className="search-button">
              {loading ? '查询中...' : '查询'}
            </button>
          </div>

          {userBehavior && (
        <div className="user-behavior">
          {/* 用户基础信息 */}
          <div className="behavior-section">
            <h3>用户基础信息</h3>
            <div className="info-grid">
              <div className="info-item">
                <label>UID:</label>
                <span>{userBehavior.account.uid}</span>
              </div>
              <div className="info-item">
                <label>邮箱:</label>
                <span>{userBehavior.account.email}</span>
              </div>
              <div className="info-item">
                <label>昵称:</label>
                <span>{userBehavior.account.nickname || '-'}</span>
              </div>
              <div className="info-item">
                <label>角色:</label>
                <span>{userBehavior.account.role === 255 ? '超级管理员' : userBehavior.account.role}</span>
              </div>
              <div className="info-item">
                <label>状态:</label>
                <span>{userBehavior.account.status === 0 ? '正常' : userBehavior.account.status === 1 ? '禁用' : '已删除'}</span>
              </div>
              <div className="info-item">
                <label>注册时间:</label>
                <span>{formatDate(userBehavior.account.registerAt)}</span>
              </div>
              <div className="info-item">
                <label>最后登录:</label>
                <span>{formatDate(userBehavior.account.lastLoginAt)}</span>
              </div>
              <div className="info-item">
                <label>VIP到期:</label>
                <span>{formatDate(userBehavior.account.vipExpiredAt)}</span>
              </div>
            </div>
          </div>

          {/* 用户策略 */}
          <div className="behavior-section">
            <h3>用户策略 ({userBehavior.strategies.length})</h3>
            {userBehavior.strategies.length === 0 ? (
              <div className="empty-state">暂无策略</div>
            ) : (
              <div className="table-container">
                <table>
                  <thead>
                    <tr>
                      <th>策略ID</th>
                      <th>定义ID</th>
                      <th>别名</th>
                      <th>状态</th>
                      <th>可见性</th>
                      <th>分享码</th>
                      <th>来源</th>
                      <th>创建时间</th>
                    </tr>
                  </thead>
                  <tbody>
                    {userBehavior.strategies.map((strategy) => (
                      <tr key={strategy.usId}>
                        <td>{strategy.usId}</td>
                        <td>{strategy.defId}</td>
                        <td>{strategy.aliasName}</td>
                        <td>
                          <span className={`status-badge status-${strategy.state.toLowerCase()}`}>
                            {strategy.state}
                          </span>
                        </td>
                        <td>{strategy.visibility}</td>
                        <td>{strategy.shareCode || '-'}</td>
                        <td>{strategy.sourceType}</td>
                        <td>{formatDate(strategy.createdAt)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>

          {/* 创建的分享码 */}
          <div className="behavior-section">
            <h3>创建的分享码 ({userBehavior.shareCodesCreated.length})</h3>
            {userBehavior.shareCodesCreated.length === 0 ? (
              <div className="empty-state">未创建分享码</div>
            ) : (
              <div className="table-container">
                <table>
                  <thead>
                    <tr>
                      <th>分享码</th>
                      <th>策略定义ID</th>
                      <th>状态</th>
                      <th>过期时间</th>
                      <th>创建时间</th>
                    </tr>
                  </thead>
                  <tbody>
                    {userBehavior.shareCodesCreated.map((code) => (
                      <tr key={code.shareCode}>
                        <td className="share-code-cell">{code.shareCode}</td>
                        <td>{code.defId}</td>
                        <td>
                          <span className={code.isActive ? 'status-active' : 'status-inactive'}>
                            {code.isActive ? '活跃' : '已停用'}
                          </span>
                        </td>
                        <td>{formatDate(code.expiredAt)}</td>
                        <td>{formatDate(code.createdAt)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>

          {/* 分享事件 */}
          <div className="behavior-section">
            <h3>分享事件 ({userBehavior.shareEvents.length})</h3>
            {userBehavior.shareEvents.length === 0 ? (
              <div className="empty-state">无分享事件</div>
            ) : (
              <div className="table-container">
                <table>
                  <thead>
                    <tr>
                      <th>事件类型</th>
                      <th>分享码</th>
                      <th>来源用户</th>
                      <th>目标用户</th>
                      <th>来源实例</th>
                      <th>目标实例</th>
                      <th>时间</th>
                    </tr>
                  </thead>
                  <tbody>
                    {userBehavior.shareEvents.map((event) => (
                      <tr key={event.eventId}>
                        <td>
                          <span className={`event-type event-${event.eventType}`}>
                            {event.eventType === 'create_code' ? '创建分享码' : 
                             event.eventType === 'claim' ? '领取' : 
                             event.eventType === 'fork' ? '分叉' : event.eventType}
                          </span>
                        </td>
                        <td className="share-code-cell">{event.shareCode}</td>
                        <td>{event.fromUid}</td>
                        <td>{event.toUid}</td>
                        <td>{event.fromInstanceId}</td>
                        <td>{event.toInstanceId}</td>
                        <td>{formatDate(event.createdAt)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>

          {/* 导入日志 */}
          <div className="behavior-section">
            <h3>导入日志 ({userBehavior.importLogs.length})</h3>
            {userBehavior.importLogs.length === 0 ? (
              <div className="empty-state">无导入记录</div>
            ) : (
              <div className="table-container">
                <table>
                  <thead>
                    <tr>
                      <th>日志ID</th>
                      <th>策略实例ID</th>
                      <th>来源类型</th>
                      <th>来源引用</th>
                      <th>导入时间</th>
                    </tr>
                  </thead>
                  <tbody>
                    {userBehavior.importLogs.map((log) => (
                      <tr key={log.id}>
                        <td>{log.id}</td>
                        <td>{log.usId}</td>
                        <td>{log.sourceType}</td>
                        <td>{log.sourceRef || '-'}</td>
                        <td>{formatDate(log.createdAt)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>

          {/* 交易所API密钥 */}
          <div className="behavior-section">
            <h3>交易所API密钥 ({userBehavior.exchangeApiKeys.length})</h3>
            {userBehavior.exchangeApiKeys.length === 0 ? (
              <div className="empty-state">未绑定API密钥</div>
            ) : (
              <div className="table-container">
                <table>
                  <thead>
                    <tr>
                      <th>ID</th>
                      <th>交易所</th>
                      <th>标签</th>
                      <th>创建时间</th>
                      <th>更新时间</th>
                    </tr>
                  </thead>
                  <tbody>
                    {userBehavior.exchangeApiKeys.map((key) => (
                      <tr key={key.id}>
                        <td>{key.id}</td>
                        <td>{key.exchangeType}</td>
                        <td>{key.label || '-'}</td>
                        <td>{formatDate(key.createdAt)}</td>
                        <td>{formatDate(key.updatedAt)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>

          {/* 通知渠道 */}
          <div className="behavior-section">
            <h3>通知渠道 ({userBehavior.notifyChannels.length})</h3>
            {userBehavior.notifyChannels.length === 0 ? (
              <div className="empty-state">未配置通知渠道</div>
            ) : (
              <div className="table-container">
                <table>
                  <thead>
                    <tr>
                      <th>ID</th>
                      <th>平台</th>
                      <th>地址</th>
                      <th>启用</th>
                      <th>默认</th>
                      <th>创建时间</th>
                    </tr>
                  </thead>
                  <tbody>
                    {userBehavior.notifyChannels.map((channel) => (
                      <tr key={channel.id}>
                        <td>{channel.id}</td>
                        <td>{channel.platform}</td>
                        <td>{channel.address}</td>
                        <td>
                          <span className={channel.isEnabled ? 'status-active' : 'status-inactive'}>
                            {channel.isEnabled ? '是' : '否'}
                          </span>
                        </td>
                        <td>
                          <span className={channel.isDefault ? 'status-active' : 'status-inactive'}>
                            {channel.isDefault ? '是' : '否'}
                          </span>
                        </td>
                        <td>{formatDate(channel.createdAt)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>

          {/* 仓位记录 */}
          <div className="behavior-section">
            <h3>仓位记录 ({userBehavior.positions.length})</h3>
            {userBehavior.positions.length === 0 ? (
              <div className="empty-state">无仓位记录</div>
            ) : (
              <div className="table-container">
                <table>
                  <thead>
                    <tr>
                      <th>仓位ID</th>
                      <th>策略实例</th>
                      <th>交易所</th>
                      <th>币对</th>
                      <th>方向</th>
                      <th>入场价</th>
                      <th>数量</th>
                      <th>状态</th>
                      <th>开仓时间</th>
                      <th>平仓时间</th>
                    </tr>
                  </thead>
                  <tbody>
                    {userBehavior.positions.map((position) => (
                      <tr key={position.positionId}>
                        <td>{position.positionId}</td>
                        <td>{position.usId}</td>
                        <td>{position.exchange}</td>
                        <td>{position.symbol}</td>
                        <td>{position.side}</td>
                        <td>{position.entryPrice}</td>
                        <td>{position.qty}</td>
                        <td>
                          <span className={`status-badge status-${position.status.toLowerCase()}`}>
                            {position.status}
                          </span>
                        </td>
                        <td>{formatDate(position.openedAt)}</td>
                        <td>{formatDate(position.closedAt)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </div>
          )}
        </>
      ),
    },
    {
      key: 'edit',
      label: '修改',
      children: (
        <div className="edit-form-container">
          {!userBehavior ? (
            <div className="empty-state">
              <p>请先在"查询"页签中查询用户信息</p>
            </div>
          ) : (
            <Form
              form={form}
              layout="vertical"
              className="user-edit-form"
              initialValues={{
                uid: userBehavior.account.uid,
                role: userBehavior.account.role,
                status: userBehavior.account.status,
                nickname: userBehavior.account.nickname || '',
                avatarUrl: userBehavior.account.avatarUrl || '',
                signature: userBehavior.account.signature || '',
                vipExpiredAt: userBehavior.account.vipExpiredAt ? dayjs(userBehavior.account.vipExpiredAt) : null,
                currentNotificationPlatform: userBehavior.account.currentNotificationPlatform || '',
                strategyIds: userBehavior.account.strategyIds || '',
              }}
            >
              <Form.Item label="用户ID" name="uid">
                <Input disabled />
              </Form.Item>

              <Form.Item label="邮箱">
                <Input value={userBehavior.account.email} disabled />
              </Form.Item>

              <Form.Item label="角色" name="role" rules={[{ required: true, message: '请选择角色' }]}>
                <Select>
                  <Select.Option value={0}>普通用户</Select.Option>
                  <Select.Option value={255}>超级管理员</Select.Option>
                </Select>
              </Form.Item>

              <Form.Item label="状态" name="status" rules={[{ required: true, message: '请选择状态' }]}>
                <Select>
                  <Select.Option value={0}>正常</Select.Option>
                  <Select.Option value={1}>禁用</Select.Option>
                  <Select.Option value={2}>已删除</Select.Option>
                </Select>
              </Form.Item>

              <Form.Item label="昵称" name="nickname">
                <Input placeholder="请输入昵称" />
              </Form.Item>

              <Form.Item label="头像URL" name="avatarUrl">
                <Input placeholder="请输入头像URL" />
              </Form.Item>

              <Form.Item label="签名" name="signature">
                <Input.TextArea rows={3} placeholder="请输入签名" />
              </Form.Item>

              <Form.Item label="VIP到期时间" name="vipExpiredAt">
                <DatePicker
                  showTime
                  format="YYYY-MM-DD HH:mm:ss"
                  style={{ width: '100%' }}
                  placeholder="选择VIP到期时间"
                />
              </Form.Item>

              <Form.Item label="当前通知平台" name="currentNotificationPlatform">
                <Select allowClear placeholder="选择通知平台">
                  <Select.Option value="email">Email</Select.Option>
                  <Select.Option value="dingtalk">钉钉</Select.Option>
                  <Select.Option value="wecom">企业微信</Select.Option>
                  <Select.Option value="telegram">Telegram</Select.Option>
                </Select>
              </Form.Item>

              <Form.Item label="策略ID列表" name="strategyIds">
                <Input.TextArea rows={2} placeholder="策略ID列表，逗号分隔" />
              </Form.Item>

              <Form.Item>
                <Space>
                  <Button type="primary" onClick={handleUpdate} loading={updating}>
                    保存修改
                  </Button>
                  <Button onClick={() => form.resetFields()}>重置</Button>
                </Space>
              </Form.Item>
            </Form>
          )}
        </div>
      ),
    },
  ];

  return (
    <div className="universal-search">
      <div className="universal-search-header">
        <h2>万向查询</h2>
        <p className="search-hint">
          支持通过以下任意信息查询用户：用户ID、邮箱、策略ID(us_id/def_id)、策略分享码、API Key ID、策略名称等
        </p>
      </div>

      <Tabs
        activeKey={activeTab}
        onChange={setActiveTab}
        items={tabItems}
        className="universal-search-tabs"
      />
    </div>
  );
};

export default UniversalSearch;
