import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { clearAuthProfile, getAuthProfile } from '../auth/profileStore';
import { clearToken, disconnectWs } from '../network';
import AvatarByewind from '../assets/SnowUI/head/AvatarByewind.svg';
import PlugsConnectedIcon from '../assets/SnowUI/icon/PlugsConnected.svg';
import NotificationIcon from '../assets/SnowUI/icon/Notification.svg';
import WalletIcon from '../assets/SnowUI/icon/Wallet.svg';
import ArrowLineRightIcon from '../assets/SnowUI/icon/ArrowLineRight.svg';
import ToggleRightIcon from '../assets/SnowUI/icon/ToggleRight.svg';
import CloseIcon from '../assets/SnowUI/icon/X.svg';
import './UserSettings.css';

interface UserSettingsProps {
  onClose: () => void;
}

const UserSettings: React.FC<UserSettingsProps> = ({ onClose }) => {
  const navigate = useNavigate();
  const [activeNavIndex, setActiveNavIndex] = useState(0);
  const [supportAccess, setSupportAccess] = useState(false);
  const userProfile = getAuthProfile();
  const userName = userProfile?.nickname || 'ByeWind';
  const userEmail = userProfile?.email || 'byewind@twitter.com';

  const navItems = [
    { id: 'profile', label: userName, icon: AvatarByewind },
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
              {/* User Profile Header */}
              <div className="user-settings-header">
                <div className="user-settings-avatar-large">
                  <div className="user-settings-avatar-bg">
                    <img src={AvatarByewind} alt="Avatar" />
                  </div>
                </div>
                <div className="user-settings-header-text">
                  <div className="user-settings-header-name">{userName}</div>
                  <div className="user-settings-header-email">{userEmail}</div>
                </div>
              </div>

              {/* Avatar Field */}
              <div className="user-settings-field">
                <div className="user-settings-field-label">头像</div>
                <div className="user-settings-field-value">
                  <span>点击修改</span>
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

              {/* Logout Button - bottom right */}
              <div className="user-settings-logout-row">
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
                <div className="user-settings-field">
                  <div className="user-settings-field-content">
                    <div className="user-settings-field-label">暂无绑定的交易所</div>
                    <div className="user-settings-field-description">
                      点击下方按钮添加交易所API配置
                    </div>
                  </div>
                  <img src={ArrowLineRightIcon} alt="Add" className="user-settings-edit-icon" />
                </div>
              </div>
            </>
          )}

          {activeNavIndex === 2 && (
            <>
              <div className="user-settings-section">
                <div className="user-settings-section-title">通知设置</div>
                
                <div className="user-settings-field">
                  <div className="user-settings-field-content">
                    <div className="user-settings-field-label">消息通知</div>
                    <div className="user-settings-field-description">
                      系统消息通知开关
                    </div>
                  </div>
                  <div className="user-settings-toggle">
                    <img src={ToggleRightIcon} alt="Toggle" />
                  </div>
                </div>

                <div className="user-settings-field">
                  <div className="user-settings-field-content">
                    <div className="user-settings-field-label">钉钉</div>
                    <div className="user-settings-field-description">
                      绑定钉钉机器人接收通知
                    </div>
                  </div>
                  <img src={ArrowLineRightIcon} alt="Configure" className="user-settings-edit-icon" />
                </div>

                <div className="user-settings-field">
                  <div className="user-settings-field-content">
                    <div className="user-settings-field-label">企业微信</div>
                    <div className="user-settings-field-description">
                      绑定企业微信接收通知
                    </div>
                  </div>
                  <img src={ArrowLineRightIcon} alt="Configure" className="user-settings-edit-icon" />
                </div>

                <div className="user-settings-field">
                  <div className="user-settings-field-content">
                    <div className="user-settings-field-label">Telegram</div>
                    <div className="user-settings-field-description">
                      绑定Telegram机器人接收通知
                    </div>
                  </div>
                  <img src={ArrowLineRightIcon} alt="Configure" className="user-settings-edit-icon" />
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
