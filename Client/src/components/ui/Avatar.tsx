import React from 'react';
import './Avatar.css';

export type AvatarSize = 'small' | 'medium' | 'large';

export type AvatarProps = {
  src?: string;
  alt?: string;
  name?: string;
  size?: AvatarSize;
  className?: string;
  onClick?: () => void;
};

const Avatar: React.FC<AvatarProps> = ({
  src,
  alt,
  name,
  size = 'medium',
  className = '',
  onClick,
}) => {
  const avatarClasses = [
    'ui-avatar',
    `ui-avatar--${size}`,
    onClick ? 'ui-avatar--clickable' : '',
    className,
  ]
    .filter(Boolean)
    .join(' ');

  // 获取首字母作为占位符
  const getInitials = (name?: string): string => {
    if (!name) return '?';
    const parts = name.trim().split(/\s+/);
    if (parts.length >= 2) {
      return (parts[0][0] + parts[1][0]).toUpperCase();
    }
    return name[0].toUpperCase();
  };

  // 根据名称生成背景色
  const getBackgroundColor = (name?: string): string => {
    if (!name) return '#E6F1FD';
    const colors = [
      '#E6F1FD', '#EDEEFC', '#ADADFB', '#7DBBFF',
      '#FFE5E5', '#FFF4E5', '#E5F5E5', '#E5F0FF',
    ];
    let hash = 0;
    for (let i = 0; i < name.length; i++) {
      hash = name.charCodeAt(i) + ((hash << 5) - hash);
    }
    return colors[Math.abs(hash) % colors.length];
  };

  const content = src ? (
    <img src={src} alt={alt || name} className="ui-avatar__image" />
  ) : (
    <div
      className="ui-avatar__placeholder"
      style={{ backgroundColor: getBackgroundColor(name) }}
    >
      {getInitials(name)}
    </div>
  );

  return (
    <div
      className={avatarClasses}
      onClick={onClick}
      role={onClick ? 'button' : undefined}
      tabIndex={onClick ? 0 : undefined}
      aria-label={alt || name}
    >
      {content}
    </div>
  );
};

export default Avatar;
