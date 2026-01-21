import React from 'react';
import './Notification.css';

export type NotificationState = 'success' | 'failure';
export type NotificationSize = 'small' | 'large';
export type NotificationVariant = 'default' | 'glass';

export interface NotificationProps {
  state: NotificationState;
  size?: NotificationSize;
  variant?: NotificationVariant;
  message: string;
  className?: string;
  onClose?: () => void;
}

const Notification: React.FC<NotificationProps> = ({
  state,
  size = 'large',
  variant = 'default',
  message,
  className = '',
  onClose,
}) => {
  const notificationClasses = [
    'snowui-notification',
    `snowui-notification--${state}`,
    `snowui-notification--${size}`,
    `snowui-notification--${variant}`,
    className,
  ]
    .filter(Boolean)
    .join(' ');

  // 图标 SVG
  const CheckCircleIcon = (
    <svg
      width={size === 'large' ? 24 : 16}
      height={size === 'large' ? 24 : 16}
      viewBox="0 0 24 24"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
    >
      <path
        d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 15l-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8l-9 9z"
        fill="#71DD8C"
      />
    </svg>
  );

  const WarningIcon = (
    <svg
      width={size === 'large' ? 24 : 16}
      height={size === 'large' ? 24 : 16}
      viewBox="0 0 24 24"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
    >
      <path
        d="M1 21h22L12 2 1 21zm12-3h-2v-2h2v2zm0-4h-2v-4h2v4z"
        fill="#FFCC00"
      />
    </svg>
  );

  const icon = state === 'success' ? CheckCircleIcon : WarningIcon;

  return (
    <div className={notificationClasses}>
      <div className="snowui-notification__icon">{icon}</div>
      <span className="snowui-notification__message">{message}</span>
      {onClose && (
        <button
          className="snowui-notification__close"
          onClick={onClose}
          aria-label="关闭"
        >
          ×
        </button>
      )}
    </div>
  );
};

export default Notification;
