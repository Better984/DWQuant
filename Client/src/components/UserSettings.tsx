import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { clearAuthProfile, getAuthProfile, setAuthProfile } from '../auth/profileStore';
import { clearToken, disconnectWs, getToken, HttpClient, HttpError, getWsStatus, ensureWsConnected, onWsStatusChange } from '../network';
import AvatarByewind from '../assets/SnowUI/head/AvatarByewind.svg';
import PlugsConnectedIcon from '../assets/SnowUI/icon/PlugsConnected.svg';
import NotificationIcon from '../assets/SnowUI/icon/Notification.svg';
import WalletIcon from '../assets/SnowUI/icon/Wallet.svg';
import ArrowLineRightIcon from '../assets/SnowUI/icon/ArrowLineRight.svg';
import CloseIcon from '../assets/SnowUI/icon/X.svg';
import { useNotification } from './ui';
import './UserSettings.css';

// 头像上传限制
const MAX_AVATAR_SIZE_BYTES = 524288; // 0.5MB
const ALLOWED_IMAGE_TYPES = ['image/jpeg', 'image/png', 'image/gif', 'image/webp'];

interface UserSettingsProps {
  onClose: () => void;
}

type ExchangeApiKeyItem = {
  id: number;
  exchangeType: string;
  label: string;
  apiKeyMasked: string;
  apiSecretMasked: string;
  hasPassword: boolean;
  createdAt?: string;
  updatedAt?: string;
};

type ExchangeOption = {
  id: string;
  label: string;
  requiresPassphrase: boolean;
};

type NotifyChannelItem = {
  id: number;
  platform: string;
  addressMasked: string;
  hasSecret: boolean;
  isEnabled: boolean;
  isDefault: boolean;
  createdAt?: string;
  updatedAt?: string;
};

type NotifyPlatformOption = {
  id: string;
  label: string;
  addressLabel: string;
  addressPlaceholder: string;
  secretLabel?: string;
  secretPlaceholder?: string;
  requiresSecret?: boolean;
};

type NotifyPreferenceRule = {
  enabled: boolean;
  channel: string;
};

type NotifyPreferenceResponse = {
  rules: Record<string, NotifyPreferenceRule>;
};

const EXCHANGE_OPTIONS: ExchangeOption[] = [
  { id: 'binance', label: '币安', requiresPassphrase: false },
  { id: 'okx', label: '欧易', requiresPassphrase: true },
  { id: 'bitget', label: 'Bitget', requiresPassphrase: true },
  { id: 'bybit', label: 'Bybit', requiresPassphrase: false },
  { id: 'gate', label: 'Gate', requiresPassphrase: false },
];

const NOTIFY_PLATFORM_OPTIONS: NotifyPlatformOption[] = [
  {
    id: 'dingtalk',
    label: '钉钉',
    addressLabel: 'Webhook',
    addressPlaceholder: 'https://oapi.dingtalk.com/robot/send?...',
    secretLabel: '加签密钥(可选)',
    secretPlaceholder: '请输入加签密钥(可选)',
    requiresSecret: false,
  },
  {
    id: 'wecom',
    label: '企业微信',
    addressLabel: 'Webhook',
    addressPlaceholder: 'https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=...',
  },
  {
    id: 'email',
    label: '邮箱',
    addressLabel: '邮箱地址',
    addressPlaceholder: 'you@example.com',
  },
  {
    id: 'telegram',
    label: 'Telegram',
    addressLabel: 'Chat ID',
    addressPlaceholder: '123456789',
    secretLabel: 'Bot Token',
    secretPlaceholder: '123456:ABC-DEF...',
    requiresSecret: true,
  },
];

const USER_NOTIFY_CATEGORIES = ['Trade', 'Risk', 'Strategy', 'Security', 'Subscription'] as const;
const SYSTEM_NOTIFY_STORAGE_KEY = 'dwquant.systemNotifyPreference';

const MAX_KEYS_PER_EXCHANGE = 5;

const UserSettings: React.FC<UserSettingsProps> = ({ onClose }) => {
  const navigate = useNavigate();
  const [activeNavIndex, setActiveNavIndex] = useState(0);
  const userProfile = getAuthProfile();
  const userName = userProfile?.nickname || 'ByeWind';
  const userEmail = userProfile?.email || 'byewind@twitter.com';
  const { success, error: showError } = useNotification();
  const client = useMemo(() => new HttpClient({ tokenProvider: getToken }), []);
  
  // 头像上传相关状态
  const avatarInputRef = useRef<HTMLInputElement>(null);
  const [avatarUrl, setAvatarUrl] = useState<string | null>(userProfile?.avatarUrl ?? null);
  const [isUploadingAvatar, setIsUploadingAvatar] = useState(false);
  
  const [exchangeKeys, setExchangeKeys] = useState<ExchangeApiKeyItem[]>([]);
  const [isLoadingKeys, setIsLoadingKeys] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [deletingId, setDeletingId] = useState<number | null>(null);
  const [formExchange, setFormExchange] = useState<string>(EXCHANGE_OPTIONS[0]?.id ?? 'binance');
  const [formLabel, setFormLabel] = useState('');
  const [formApiKey, setFormApiKey] = useState('');
  const [formApiSecret, setFormApiSecret] = useState('');
  const [formApiPassword, setFormApiPassword] = useState('');
  const [notifyChannels, setNotifyChannels] = useState<NotifyChannelItem[]>([]);
  const [isLoadingNotify, setIsLoadingNotify] = useState(false);
  const [notifyError, setNotifyError] = useState<string | null>(null);
  const [notifyPlatform, setNotifyPlatform] = useState<string>(NOTIFY_PLATFORM_OPTIONS[0]?.id ?? 'dingtalk');
  const [notifyAddress, setNotifyAddress] = useState('');
  const [notifySecret, setNotifySecret] = useState('');
  const [isNotifySubmitting, setIsNotifySubmitting] = useState(false);
  const [deletingPlatform, setDeletingPlatform] = useState<string | null>(null);
  const [isLoadingNotifyPreference, setIsLoadingNotifyPreference] = useState(false);
  const [notifyPreferenceError, setNotifyPreferenceError] = useState<string | null>(null);
  const [isSavingNotifyPreference, setIsSavingNotifyPreference] = useState(false);
  const [personalNotifyEnabled, setPersonalNotifyEnabled] = useState(true);
  const [personalNotifyChannel, setPersonalNotifyChannel] = useState('inapp');
  const [systemNotifyEnabled, setSystemNotifyEnabled] = useState(true);
  const [systemNotifyChannel, setSystemNotifyChannel] = useState('inapp');
  
  // WebSocket状态管理
  const [wsStatus, setWsStatus] = useState<'disconnected' | 'connecting' | 'connected' | 'error'>(getWsStatus());
  const [isConnectingWs, setIsConnectingWs] = useState(false);

  const selectedExchange = EXCHANGE_OPTIONS.find((option) => option.id === formExchange) ?? EXCHANGE_OPTIONS[0];
  const selectedCount = exchangeKeys.filter((item) => item.exchangeType === formExchange).length;
  const selectedNotifyPlatform = NOTIFY_PLATFORM_OPTIONS.find((option) => option.id === notifyPlatform)
    ?? NOTIFY_PLATFORM_OPTIONS[0];
  const selectedNotifyBinding = notifyChannels.find((item) => item.platform === notifyPlatform);
  const notifyChannelOptions = useMemo(() => {
    const bound = new Set(notifyChannels.map((item) => item.platform));
    const options = [{ id: 'inapp', label: '站内通知' }];
    NOTIFY_PLATFORM_OPTIONS.forEach((platform) => {
      if (bound.has(platform.id)) {
        options.push({ id: platform.id, label: platform.label });
      }
    });
    return options;
  }, [notifyChannels]);

  useEffect(() => {
    if (!notifyChannelOptions.find((option) => option.id === personalNotifyChannel)) {
      setPersonalNotifyChannel('inapp');
    }
    if (!notifyChannelOptions.find((option) => option.id === systemNotifyChannel)) {
      setSystemNotifyChannel('inapp');
    }
  }, [notifyChannelOptions, personalNotifyChannel, systemNotifyChannel]);

  // ????
  const loadAvatar = useCallback(async () => {
    try {
      const data = await client.postProtocol<{ avatarUrl: string | null }>("/api/media/avatar/get", "media.avatar.get");
      if (data?.avatarUrl) {
        setAvatarUrl(data.avatarUrl);
        // ??????
        const profile = getAuthProfile();
        if (profile) {
          setAuthProfile({ ...profile, avatarUrl: data.avatarUrl });
        }
      }
    } catch {
      // ???????????????
    }
  }, [client]);

  // ?????????
  useEffect(() => {
    if (activeNavIndex === 0) {
      void loadAvatar();
    }
  }, [activeNavIndex, loadAvatar]);

  // WebSocket状态监听
  useEffect(() => {
    const unsubscribe = onWsStatusChange((status) => {
      setWsStatus(status);
      setIsConnectingWs(false);
    });
    return unsubscribe;
  }, []);

  // 断开WebSocket
  const handleDisconnectWs = useCallback(() => {
    disconnectWs();
    success('WebSocket已断开，将使用HTTP方式与服务器交互');
  }, [success]);

  // 连接WebSocket
  const handleConnectWs = useCallback(async () => {
    setIsConnectingWs(true);
    try {
      await ensureWsConnected();
      success('WebSocket连接成功');
    } catch (err) {
      const message = err instanceof Error ? err.message : '连接失败，请稍后重试';
      showError(message);
    } finally {
      setIsConnectingWs(false);
    }
  }, [success, showError]);

  // ????????
  const handleAvatarSelect = useCallback(async (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    if (!file) return;

    // ??????
    if (!ALLOWED_IMAGE_TYPES.includes(file.type)) {
      showError("???????????? JPG?PNG?GIF?WebP");
      return;
    }

    // ??????
    if (file.size > MAX_AVATAR_SIZE_BYTES) {
      const maxSizeMB = MAX_AVATAR_SIZE_BYTES / 1024 / 1024;
      showError(`???????? ${maxSizeMB}MB`);
      return;
    }

    setIsUploadingAvatar(true);
    try {
      const formData = new FormData();
      formData.append("file", file);

      const result = await client.postForm<{ avatarUrl: string }>("/api/media/avatar", "media.avatar.upload", formData);
      const newAvatarUrl = result?.avatarUrl;

      if (newAvatarUrl) {
        setAvatarUrl(newAvatarUrl);
        // ??????
        const profile = getAuthProfile();
        if (profile) {
          setAuthProfile({ ...profile, avatarUrl: newAvatarUrl });
        }
        success("??????");
      } else {
        throw new Error("????????");
      }
    } catch (err) {
      const message = err instanceof Error ? err.message : "??????????";
      showError(message);
    } finally {
      setIsUploadingAvatar(false);
      // ?????????????????
      if (avatarInputRef.current) {
        avatarInputRef.current.value = "";
      }
    }
  }, [client, showError, success]);

  // ??????????
  const handleAvatarClick = useCallback(() => {
    if (!isUploadingAvatar && avatarInputRef.current) {
      avatarInputRef.current.click();
    }
  }, [isUploadingAvatar]);

  const loadExchangeKeys = useCallback(async () => {
    setIsLoadingKeys(true);
    setLoadError(null);
    try {
      const data = await client.postProtocol<ExchangeApiKeyItem[]>("/api/userexchangeapikeys/list", "exchange.api_key.list");
      setExchangeKeys(Array.isArray(data) ? data : []);
    } catch (err) {
      const message = err instanceof HttpError ? err.message : "?????API????";
      setLoadError(message);
      showError(message);
    } finally {
      setIsLoadingKeys(false);
    }
  }, [client, showError]);

  useEffect(() => {
    if (activeNavIndex === 1) {
      void loadExchangeKeys();
    }
  }, [activeNavIndex, loadExchangeKeys]);

  const handleBind = async (event: React.FormEvent) => {
    event.preventDefault();

    const exchangeType = formExchange.trim();
    const label = formLabel.trim();
    const apiKey = formApiKey.trim();
    const apiSecret = formApiSecret.trim();
    const apiPassword = formApiPassword.trim();

    if (!label) {
      showError("?????");
      return;
    }

    if (!apiKey) {
      showError("???API Key");
      return;
    }

    if (!apiSecret) {
      showError("???API Secret");
      return;
    }

    if (selectedExchange?.requiresPassphrase && !apiPassword) {
      showError("????????Passphrase");
      return;
    }

    if (selectedCount >= MAX_KEYS_PER_EXCHANGE) {
      showError(`????????? ${MAX_KEYS_PER_EXCHANGE} ?API`);
      return;
    }

    setIsSubmitting(true);
    try {
      await client.postProtocol("/api/userexchangeapikeys/create", "exchange.api_key.create", {
        exchangeType,
        label,
        apiKey,
        apiSecret,
        apiPassword: selectedExchange?.requiresPassphrase ? apiPassword : undefined,
      });
      success("???API????");
      setFormLabel("");
      setFormApiKey("");
      setFormApiSecret("");
      setFormApiPassword("");
      await loadExchangeKeys();
    } catch (err) {
      const message = err instanceof HttpError ? err.message : "??????????";
      showError(message);
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleDelete = async (id: number) => {
    setDeletingId(id);
    try {
      await client.postProtocol("/api/userexchangeapikeys/delete", "exchange.api_key.delete", { id });
      success("???API????");
      await loadExchangeKeys();
    } catch (err) {
      const message = err instanceof HttpError ? err.message : "??????????";
      showError(message);
    } finally {
      setDeletingId(null);
    }
  };

  const loadNotifyChannels = useCallback(async () => {
    setIsLoadingNotify(true);
    setNotifyError(null);
    try {
      const data = await client.postProtocol<NotifyChannelItem[]>("/api/usernotifychannels/list", "notify_channel.list");
      setNotifyChannels(Array.isArray(data) ? data : []);
    } catch (err) {
      const message = err instanceof HttpError ? err.message : "????????";
      setNotifyError(message);
      showError(message);
    } finally {
      setIsLoadingNotify(false);
    }
  }, [client, showError]);

  const loadNotifyPreference = useCallback(async () => {
    setIsLoadingNotifyPreference(true);
    setNotifyPreferenceError(null);
    try {
      const data = await client.postProtocol<NotifyPreferenceResponse>("/api/user/notification-preference/get", "notification.preference.get");
      const rules = data?.rules ?? {};
      const enabled = USER_NOTIFY_CATEGORIES.some((key) => rules[key]?.enabled ?? true);
      let channel = "inapp";
      for (const key of USER_NOTIFY_CATEGORIES) {
        const ruleChannel = rules[key]?.channel;
        if (ruleChannel) {
          channel = ruleChannel;
          break;
        }
      }
      setPersonalNotifyEnabled(enabled);
      setPersonalNotifyChannel(channel);

      const stored = localStorage.getItem(SYSTEM_NOTIFY_STORAGE_KEY);
      if (stored) {
        const parsed = JSON.parse(stored) as { enabled?: boolean; channel?: string };
        if (typeof parsed.enabled === "boolean") {
          setSystemNotifyEnabled(parsed.enabled);
        }
        if (typeof parsed.channel === "string") {
          setSystemNotifyChannel(parsed.channel);
        }
      }
    } catch (err) {
      const message = err instanceof HttpError ? err.message : "????????";
      setNotifyPreferenceError(message);
    } finally {
      setIsLoadingNotifyPreference(false);
    }
  }, [client]);

  useEffect(() => {
    if (activeNavIndex === 2) {
      void loadNotifyChannels();
      void loadNotifyPreference();
    }
  }, [activeNavIndex, loadNotifyChannels, loadNotifyPreference]);

  const handleSaveNotifyPreference = async () => {
    setIsSavingNotifyPreference(true);
    try {
      const rules: Record<string, NotifyPreferenceRule> = {};
      USER_NOTIFY_CATEGORIES.forEach((key) => {
        rules[key] = {
          enabled: personalNotifyEnabled,
          channel: personalNotifyChannel,
        };
      });
      await client.postProtocol("/api/user/notification-preference/update", "notification.preference.update", { rules });

      localStorage.setItem(
        SYSTEM_NOTIFY_STORAGE_KEY,
        JSON.stringify({ enabled: systemNotifyEnabled, channel: systemNotifyChannel })
      );
      success("???????");
    } catch (err) {
      const message = err instanceof HttpError ? err.message : "????????";
      showError(message);
    } finally {
      setIsSavingNotifyPreference(false);
    }
  };

  const handleNotifySubmit = async (event: React.FormEvent) => {
    event.preventDefault();

    const platform = notifyPlatform.trim();
    const address = notifyAddress.trim();
    const secret = notifySecret.trim();

    if (!address) {
      showError("???????");
      return;
    }

    if (selectedNotifyPlatform?.requiresSecret && !secret) {
      showError("?????????");
      return;
    }

    setIsNotifySubmitting(true);
    try {
      await client.postProtocol("/api/usernotifychannels/upsert", "notify_channel.upsert", {
        platform,
        address,
        secret: secret || undefined,
      });
      success("????????");
      setNotifyAddress("");
      setNotifySecret("");
      await loadNotifyChannels();
    } catch (err) {
      const message = err instanceof HttpError ? err.message : "??????????";
      showError(message);
    } finally {
      setIsNotifySubmitting(false);
    }
  };

  const handleNotifyDelete = async (platform: string) => {
    setDeletingPlatform(platform);
    try {
      await client.postProtocol("/api/usernotifychannels/delete", "notify_channel.delete", { platform });
      success("????????");
      await loadNotifyChannels();
    } catch (err) {
      const message = err instanceof HttpError ? err.message : "??????????";
      showError(message);
    } finally {
      setDeletingPlatform(null);
    }
  };

  const navItems = [
    { id: 'profile', label: userName, icon: avatarUrl || AvatarByewind },
    { id: 'exchange', label: '交易所API', icon: PlugsConnectedIcon },
    { id: 'notifications', label: '通知设置', icon: NotificationIcon },
    { id: 'wallet', label: '量化钱包', icon: WalletIcon },
  ];

  return (
    <div className="user-settings-overlay" onClick={onClose}>
      <div className="user-settings-popup" onClick={(e) => e.stopPropagation()}>
        {/* Left Navigation */}
        <div className="user-settings-nav">
          {navItems.map((item, index) => (
            <div
              key={item.id}
              className={`user-settings-nav-item ${activeNavIndex === index ? 'active' : ''}`}
              onClick={() => setActiveNavIndex(index)}
            >
              <div className="user-settings-nav-icon">
                <img src={item.icon} alt={item.label} />
              </div>
              <div className="user-settings-nav-text">{item.label}</div>
            </div>
          ))}
        </div>

        {/* Right Content */}
        <div className="user-settings-content">
          {activeNavIndex === 0 && (
            <>
              {/* 隐藏的文件输入 */}
              <input
                ref={avatarInputRef}
                type="file"
                accept="image/jpeg,image/png,image/gif,image/webp"
                style={{ display: 'none' }}
                onChange={handleAvatarSelect}
              />

              {/* User Profile Header */}
              <div className="user-settings-header">
                <div 
                  className="user-settings-avatar-large"
                  onClick={handleAvatarClick}
                  style={{ cursor: isUploadingAvatar ? 'wait' : 'pointer' }}
                  title="点击更换头像"
                >
                  <div className="user-settings-avatar-bg">
                    <img src={avatarUrl || AvatarByewind} alt="Avatar" />
                    {isUploadingAvatar && (
                      <div className="user-settings-avatar-uploading">
                        上传中...
                      </div>
                    )}
                  </div>
                </div>
                <div className="user-settings-header-text">
                  <div className="user-settings-header-name">{userName}</div>
                  <div className="user-settings-header-email">{userEmail}</div>
                </div>
              </div>

              {/* Avatar Field */}
              <div 
                className="user-settings-field"
                onClick={handleAvatarClick}
                style={{ cursor: isUploadingAvatar ? 'wait' : 'pointer' }}
              >
                <div className="user-settings-field-label">头像</div>
                <div className="user-settings-field-value">
                  <span>{isUploadingAvatar ? '上传中...' : '点击修改'}</span>
                  <img src={ArrowLineRightIcon} alt="Edit" className="user-settings-edit-icon" />
                </div>
              </div>

              {/* Name Field */}
              <div className="user-settings-field">
                <div className="user-settings-field-label">昵称</div>
                <div className="user-settings-field-value">
                  <span>{userName}</span>
                  <img src={ArrowLineRightIcon} alt="Edit" className="user-settings-edit-icon" />
                </div>
              </div>

              {/* Signature Field */}
              <div className="user-settings-field">
                <div className="user-settings-field-label">个性签名</div>
                <div className="user-settings-field-value">
                  <span>未设置</span>
                  <img src={ArrowLineRightIcon} alt="Edit" className="user-settings-edit-icon" />
                </div>
              </div>

              {/* WebSocket Control and Logout Buttons - bottom right */}
              <div className="user-settings-logout-row">
                <button
                  type="button"
                  className="user-settings-ws-control-button"
                  onClick={handleDisconnectWs}
                  disabled={wsStatus === 'disconnected' || wsStatus === 'connecting'}
                >
                  {wsStatus === 'disconnected' ? '已断开' : '断开WebSocket'}
                </button>
                <button
                  type="button"
                  className="user-settings-ws-control-button"
                  onClick={handleConnectWs}
                  disabled={wsStatus === 'connected' || isConnectingWs}
                >
                  {isConnectingWs ? '连接中...' : wsStatus === 'connected' ? '已连接' : '连接WebSocket'}
                </button>
                <button
                  type="button"
                  className="user-settings-logout-button"
                  onClick={() => {
                    clearToken();
                    clearAuthProfile();
                    disconnectWs();
                    onClose();
                    navigate('/auth');
                  }}
                >
                  退出登录
                </button>
              </div>
            </>
          )}

          {activeNavIndex === 1 && (
            <>
              <div className="user-settings-section">
                <div className="user-settings-section-title">交易所API绑定</div>
                <div className="user-settings-exchange-panel">
                  <div className="user-settings-exchange-summary">
                    支持 币安 / 欧易 / Bitget / Bybit / Gate，每个交易所最多绑定5个API。
                  </div>

                  {isLoadingKeys && (
                    <div className="user-settings-exchange-state">正在加载交易所API...</div>
                  )}
                  {loadError && (
                    <div className="user-settings-exchange-state user-settings-exchange-state-error">
                      {loadError}
                    </div>
                  )}

                  <div className="user-settings-exchange-grid">
                    {EXCHANGE_OPTIONS.map((exchange) => {
                      const items = exchangeKeys.filter((item) => item.exchangeType === exchange.id);
                      return (
                        <div className="user-settings-exchange-card" key={exchange.id}>
                          <div className="user-settings-exchange-card-header">
                            <div className="user-settings-exchange-title">{exchange.label}</div>
                            <div className="user-settings-exchange-count">
                              {items.length}/{MAX_KEYS_PER_EXCHANGE}
                            </div>
                          </div>

                          {items.length === 0 ? (
                            <div className="user-settings-exchange-empty">未绑定</div>
                          ) : (
                            <div className="user-settings-exchange-key-list">
                              {items.map((item) => (
                                <div className="user-settings-exchange-key-item" key={item.id}>
                                  <div className="user-settings-exchange-key-meta">
                                    <div className="user-settings-exchange-key-label">{item.label}</div>
                                    <div className="user-settings-exchange-key-mask">
                                      API Key: {item.apiKeyMasked} · Secret: {item.apiSecretMasked}
                                      {item.hasPassword ? ' · Passphrase已设置' : ''}
                                    </div>
                                  </div>
                                  <div className="user-settings-exchange-actions">
                                    <button
                                      type="button"
                                      className="user-settings-action-button user-settings-action-button--danger"
                                      onClick={() => handleDelete(item.id)}
                                      disabled={deletingId === item.id}
                                    >
                                      {deletingId === item.id ? '解绑中...' : '解绑'}
                                    </button>
                                  </div>
                                </div>
                              ))}
                            </div>
                          )}
                        </div>
                      );
                    })}
                  </div>

                  <form className="user-settings-exchange-form" onSubmit={handleBind}>
                    <div className="user-settings-exchange-form-title">新增API</div>
                    <div className="user-settings-form-row">
                      <div className="user-settings-form-field">
                        <label className="user-settings-form-label">交易所</label>
                        <select
                          className="user-settings-select"
                          value={formExchange}
                          onChange={(event) => {
                            const next = event.target.value;
                            setFormExchange(next);
                            if (!EXCHANGE_OPTIONS.find((item) => item.id === next)?.requiresPassphrase) {
                              setFormApiPassword('');
                            }
                          }}
                          disabled={isSubmitting}
                        >
                          {EXCHANGE_OPTIONS.map((option) => (
                            <option key={option.id} value={option.id}>
                              {option.label}
                            </option>
                          ))}
                        </select>
                      </div>
                      <div className="user-settings-form-field">
                        <label className="user-settings-form-label">备注</label>
                        <input
                          className="user-settings-input"
                          value={formLabel}
                          onChange={(event) => setFormLabel(event.target.value)}
                          placeholder="例如：主账户/子账户/只读KEY"
                          disabled={isSubmitting}
                        />
                      </div>
                    </div>

                    <div className="user-settings-form-row">
                      <div className="user-settings-form-field">
                        <label className="user-settings-form-label">API Key</label>
                        <input
                          className="user-settings-input"
                          value={formApiKey}
                          onChange={(event) => setFormApiKey(event.target.value)}
                          placeholder="请输入API Key"
                          autoComplete="off"
                          disabled={isSubmitting}
                        />
                      </div>
                      <div className="user-settings-form-field">
                        <label className="user-settings-form-label">API Secret</label>
                        <input
                          className="user-settings-input"
                          type="password"
                          value={formApiSecret}
                          onChange={(event) => setFormApiSecret(event.target.value)}
                          placeholder="请输入API Secret"
                          autoComplete="off"
                          disabled={isSubmitting}
                        />
                      </div>
                    </div>

                    {selectedExchange?.requiresPassphrase && (
                      <div className="user-settings-form-row">
                        <div className="user-settings-form-field">
                          <label className="user-settings-form-label">Passphrase</label>
                          <input
                            className="user-settings-input"
                            type="password"
                            value={formApiPassword}
                            onChange={(event) => setFormApiPassword(event.target.value)}
                            placeholder="请输入Passphrase"
                            autoComplete="off"
                            disabled={isSubmitting}
                          />
                          <div className="user-settings-input-hint">OKX/Bitget 必填</div>
                        </div>
                      </div>
                    )}

                    <div className="user-settings-form-actions">
                      <div className="user-settings-exchange-limit">
                        当前 {selectedExchange?.label ?? formExchange} 已绑定 {selectedCount}/{MAX_KEYS_PER_EXCHANGE}
                      </div>
                      <button
                        type="submit"
                        className="user-settings-action-button user-settings-action-button--primary"
                        disabled={isSubmitting || selectedCount >= MAX_KEYS_PER_EXCHANGE}
                      >
                        {isSubmitting ? '绑定中...' : '绑定'}
                      </button>
                    </div>
                  </form>
                </div>
              </div>
            </>
          )}

          {activeNavIndex === 2 && (
            <>
              <div className="user-settings-section">
                <div className="user-settings-section-title">通知设置</div>
                <div className="user-settings-notify-panel">
                  <div className="user-settings-notify-summary">
                    支持 钉钉 / 企业微信 / 邮箱 / Telegram，每个平台仅可绑定一个。
                  </div>

                  <div className="user-settings-notify-preference">
                    <div className="user-settings-notify-preference-title">通知偏好</div>
                    {isLoadingNotifyPreference && (
                      <div className="user-settings-notify-state">正在加载通知偏好...</div>
                    )}
                    {notifyPreferenceError && (
                      <div className="user-settings-notify-state user-settings-notify-state-error">
                        {notifyPreferenceError}
                      </div>
                    )}
                    <div className="user-settings-notify-preference-row">
                      <div className="user-settings-notify-preference-label">个人通知</div>
                      <label className="user-settings-switch">
                        <input
                          type="checkbox"
                          checked={personalNotifyEnabled}
                          onChange={(event) => setPersonalNotifyEnabled(event.target.checked)}
                          disabled={isSavingNotifyPreference}
                        />
                        <span className="user-settings-switch-slider" />
                      </label>
                      <select
                        className="user-settings-select user-settings-notify-preference-select"
                        value={personalNotifyChannel}
                        onChange={(event) => setPersonalNotifyChannel(event.target.value)}
                        disabled={!personalNotifyEnabled || notifyChannelOptions.length <= 1 || isSavingNotifyPreference}
                      >
                        {notifyChannelOptions.map((option) => (
                          <option key={option.id} value={option.id}>
                            {option.label}
                          </option>
                        ))}
                      </select>
                    </div>

                    <div className="user-settings-notify-preference-row">
                      <div className="user-settings-notify-preference-label">系统通知</div>
                      <label className="user-settings-switch">
                        <input
                          type="checkbox"
                          checked={systemNotifyEnabled}
                          onChange={(event) => setSystemNotifyEnabled(event.target.checked)}
                          disabled={isSavingNotifyPreference}
                        />
                        <span className="user-settings-switch-slider" />
                      </label>
                      <select
                        className="user-settings-select user-settings-notify-preference-select"
                        value={systemNotifyChannel}
                        onChange={(event) => setSystemNotifyChannel(event.target.value)}
                        disabled={!systemNotifyEnabled || notifyChannelOptions.length <= 1 || isSavingNotifyPreference}
                      >
                        {notifyChannelOptions.map((option) => (
                          <option key={option.id} value={option.id}>
                            {option.label}
                          </option>
                        ))}
                      </select>
                    </div>
                    <div className="user-settings-notify-preference-hint">
                      系统通知默认站内可见，外发需要管理员开启；未绑定的平台无法选择。
                    </div>
                    <div className="user-settings-notify-preference-actions">
                      <button
                        type="button"
                        className="user-settings-action-button user-settings-action-button--primary"
                        onClick={handleSaveNotifyPreference}
                        disabled={isSavingNotifyPreference || isLoadingNotifyPreference}
                      >
                        {isSavingNotifyPreference ? '保存中...' : '保存通知偏好'}
                      </button>
                    </div>
                  </div>

                  {isLoadingNotify && (
                    <div className="user-settings-notify-state">正在加载通知渠道...</div>
                  )}
                  {notifyError && (
                    <div className="user-settings-notify-state user-settings-notify-state-error">
                      {notifyError}
                    </div>
                  )}

                  <div className="user-settings-notify-grid">
                    {NOTIFY_PLATFORM_OPTIONS.map((platformOption) => {
                      const channel = notifyChannels.find((item) => item.platform === platformOption.id);
                      return (
                        <div className="user-settings-notify-card" key={platformOption.id}>
                          <div className="user-settings-notify-card-header">
                            <div className="user-settings-notify-title">{platformOption.label}</div>
                            <div
                              className={`user-settings-notify-status ${
                                channel ? 'user-settings-notify-status--active' : 'user-settings-notify-status--idle'
                              }`}
                            >
                              {channel ? '已绑定' : '未绑定'}
                            </div>
                          </div>

                          {channel ? (
                            <div className="user-settings-notify-body">
                              <div className="user-settings-notify-line">地址: {channel.addressMasked}</div>
                              {channel.hasSecret && (
                                <div className="user-settings-notify-line">密钥: 已设置</div>
                              )}
                              <div className="user-settings-notify-actions">
                                <button
                                  type="button"
                                  className="user-settings-action-button user-settings-action-button--danger"
                                  onClick={() => handleNotifyDelete(platformOption.id)}
                                  disabled={deletingPlatform === platformOption.id}
                                >
                                  {deletingPlatform === platformOption.id ? '解绑中...' : '解绑'}
                                </button>
                              </div>
                            </div>
                          ) : (
                            <div className="user-settings-notify-empty">未绑定</div>
                          )}
                        </div>
                      );
                    })}
                  </div>

                  <form className="user-settings-notify-form" onSubmit={handleNotifySubmit}>
                    <div className="user-settings-notify-form-title">绑定通知渠道</div>
                    <div className="user-settings-form-row">
                      <div className="user-settings-form-field">
                        <label className="user-settings-form-label">平台</label>
                        <select
                          className="user-settings-select"
                          value={notifyPlatform}
                          onChange={(event) => {
                            const next = event.target.value;
                            setNotifyPlatform(next);
                            if (!NOTIFY_PLATFORM_OPTIONS.find((item) => item.id === next)?.requiresSecret) {
                              setNotifySecret('');
                            }
                          }}
                          disabled={isNotifySubmitting}
                        >
                          {NOTIFY_PLATFORM_OPTIONS.map((option) => (
                            <option key={option.id} value={option.id}>
                              {option.label}
                            </option>
                          ))}
                        </select>
                      </div>
                      <div className="user-settings-form-field">
                        <label className="user-settings-form-label">{selectedNotifyPlatform.addressLabel}</label>
                        <input
                          className="user-settings-input"
                          value={notifyAddress}
                          onChange={(event) => setNotifyAddress(event.target.value)}
                          placeholder={selectedNotifyPlatform.addressPlaceholder}
                          disabled={isNotifySubmitting}
                        />
                      </div>
                    </div>

                    {selectedNotifyPlatform.secretLabel && (
                      <div className="user-settings-form-row">
                        <div className="user-settings-form-field">
                          <label className="user-settings-form-label">{selectedNotifyPlatform.secretLabel}</label>
                          <input
                            className="user-settings-input"
                            type="password"
                            value={notifySecret}
                            onChange={(event) => setNotifySecret(event.target.value)}
                            placeholder={selectedNotifyPlatform.secretPlaceholder}
                            autoComplete="off"
                            disabled={isNotifySubmitting}
                          />
                          {selectedNotifyPlatform.requiresSecret && (
                            <div className="user-settings-input-hint">Telegram 必填</div>
                          )}
                        </div>
                      </div>
                    )}

                    {selectedNotifyBinding && (
                      <div className="user-settings-notify-hint">
                        该平台已绑定，提交将覆盖原有配置。
                      </div>
                    )}

                    <div className="user-settings-form-actions">
                      <div className="user-settings-notify-limit">每个平台仅可绑定一个</div>
                      <button
                        type="submit"
                        className="user-settings-action-button user-settings-action-button--primary"
                        disabled={isNotifySubmitting}
                      >
                        {isNotifySubmitting ? '绑定中...' : '绑定'}
                      </button>
                    </div>
                  </form>
                </div>
              </div>
            </>
          )}

          {activeNavIndex === 3 && (
            <>
              <div className="user-settings-section">
                <div className="user-settings-section-title">量化钱包</div>
                <div className="user-settings-field">
                  <div className="user-settings-field-content">
                    <div className="user-settings-field-label">钱包管理</div>
                    <div className="user-settings-field-description">
                      管理您的量化交易钱包
                    </div>
                  </div>
                  <img src={ArrowLineRightIcon} alt="Manage" className="user-settings-edit-icon" />
                </div>
              </div>
            </>
          )}
        </div>

        {/* Close Button */}
        <button className="user-settings-close" onClick={onClose}>
          <img src={CloseIcon} alt="Close" />
        </button>
      </div>
    </div>
  );
};

export default UserSettings;