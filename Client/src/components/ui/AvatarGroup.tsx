import React from 'react';
import Avatar, { type AvatarProps, type AvatarSize } from './Avatar';
import './AvatarGroup.css';

export interface AvatarGroupItem {
  src?: string;
  alt?: string;
  name?: string;
}

export interface AvatarGroupProps {
  avatars: AvatarGroupItem[];
  size?: AvatarSize;
  max?: number;
  showMore?: boolean;
  moreText?: string;
  className?: string;
  onAvatarClick?: (index: number, avatar: AvatarGroupItem) => void;
  onMoreClick?: () => void;
}

const AvatarGroup: React.FC<AvatarGroupProps> = ({
  avatars,
  size = 'medium',
  max = 4,
  showMore = true,
  moreText,
  className = '',
  onAvatarClick,
  onMoreClick,
}) => {
  const displayAvatars = avatars.slice(0, max);
  const remainingCount = avatars.length - max;
  const shouldShowMore = showMore && remainingCount > 0;

  const groupClasses = [
    'snowui-avatar-group',
    `snowui-avatar-group--${size}`,
    className,
  ]
    .filter(Boolean)
    .join(' ');

  const handleAvatarClick = (index: number, avatar: AvatarGroupItem) => {
    if (onAvatarClick) {
      onAvatarClick(index, avatar);
    }
  };

  return (
    <div className={groupClasses}>
      {displayAvatars.map((avatar, index) => (
        <div
          key={index}
          className="snowui-avatar-group__item"
          style={{ zIndex: displayAvatars.length - index }}
        >
          <Avatar
            src={avatar.src}
            alt={avatar.alt}
            name={avatar.name}
            size={size}
            onClick={onAvatarClick ? () => handleAvatarClick(index, avatar) : undefined}
          />
        </div>
      ))}
      {shouldShowMore && (
        <div
          className="snowui-avatar-group__more"
          style={{ zIndex: 0 }}
          onClick={onMoreClick}
          role={onMoreClick ? 'button' : undefined}
          tabIndex={onMoreClick ? 0 : undefined}
        >
          <div className="snowui-avatar-group__more-content">
            {moreText || `+${remainingCount}`}
          </div>
        </div>
      )}
    </div>
  );
};

export default AvatarGroup;
