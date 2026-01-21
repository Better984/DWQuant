import React from 'react';
import './StatusBadge.css';

export type StatusBadgeColor = 'purple' | 'blue' | 'yellow' | 'grey' | 'green';
export type StatusBadgeSize = 'small' | 'large';
export type StatusBadgeVariant = 'dot' | 'background';

export interface StatusBadgeProps {
  color: StatusBadgeColor;
  size?: StatusBadgeSize;
  variant?: StatusBadgeVariant;
  label: string;
  className?: string;
}

const StatusBadge: React.FC<StatusBadgeProps> = ({
  color,
  size = 'small',
  variant = 'dot',
  label,
  className = '',
}) => {
  const badgeClasses = [
    'snowui-status-badge',
    `snowui-status-badge--${color}`,
    `snowui-status-badge--${size}`,
    `snowui-status-badge--${variant}`,
    className,
  ]
    .filter(Boolean)
    .join(' ');

  // 颜色映射
  const colorMap: Record<StatusBadgeColor, string> = {
    purple: '#B899EB',
    blue: '#7DBBFF',
    yellow: '#FFCC00',
    grey: 'rgba(0, 0, 0, 0.4)',
    green: '#71DD8C',
  };

  const dotColor = colorMap[color];

  // 计算背景颜色（10% 透明度）
  const getBackgroundColor = (colorValue: string): string => {
    if (colorValue.startsWith('rgba')) {
      // 对于 grey，已经是 rgba，需要提取颜色并添加透明度
      const match = colorValue.match(/rgba?\(([^)]+)\)/);
      if (match) {
        const values = match[1].split(',').map(v => v.trim());
        if (values.length >= 3) {
          return `rgba(${values[0]}, ${values[1]}, ${values[2]}, 0.1)`;
        }
      }
      return colorValue;
    }
    // 对于 hex 颜色，转换为 rgba
    const hex = colorValue.replace('#', '');
    const r = parseInt(hex.substring(0, 2), 16);
    const g = parseInt(hex.substring(2, 4), 16);
    const b = parseInt(hex.substring(4, 6), 16);
    return `rgba(${r}, ${g}, ${b}, 0.1)`;
  };

  const backgroundColor = getBackgroundColor(dotColor);

  // 图标 SVG - Small (Fill weight)
  const SmallDotIcon = (
    <svg
      width={16}
      height={16}
      viewBox="0 0 16 16"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
    >
      <circle cx="8" cy="8" r="3" fill={dotColor} />
    </svg>
  );

  // 图标 SVG - Large (Regular weight)
  const LargeDotIcon = (
    <svg
      width={20}
      height={20}
      viewBox="0 0 20 20"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
    >
      <circle cx="10" cy="10" r="4.375" stroke={dotColor} strokeWidth="1.25" fill="none" />
    </svg>
  );

  const dotIcon = size === 'small' ? SmallDotIcon : LargeDotIcon;

  return (
    <div className={badgeClasses}>
      {variant === 'dot' && (
        <div className="snowui-status-badge__dot">{dotIcon}</div>
      )}
      {variant === 'background' && (
        <div 
          className="snowui-status-badge__background" 
          style={{ backgroundColor }} 
        />
      )}
      <span className="snowui-status-badge__label">{label}</span>
    </div>
  );
};

export default StatusBadge;
