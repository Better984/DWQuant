import React, { useState } from 'react';
import './SelectCard.css';

export type SelectCardState = 'default' | 'static' | 'hover' | 'selected';

export interface SelectCardProps {
  children: React.ReactNode;
  state?: SelectCardState;
  selected?: boolean;
  disabled?: boolean;
  onClick?: () => void;
  className?: string;
}

const SelectCard: React.FC<SelectCardProps> = ({
  children,
  state,
  selected = false,
  disabled = false,
  onClick,
  className = '',
}) => {
  const [isHovered, setIsHovered] = useState(false);
  
  // 确定实际状态
  const actualState: SelectCardState = 
    disabled ? 'static' : 
    state || (selected ? 'selected' : isHovered ? 'hover' : 'default');

  const cardClasses = [
    'snowui-select-card',
    `snowui-select-card--${actualState}`,
    className,
  ]
    .filter(Boolean)
    .join(' ');

  const handleClick = () => {
    if (!disabled && onClick) {
      onClick();
    }
  };

  const handleMouseEnter = () => {
    if (!disabled) {
      setIsHovered(true);
    }
  };

  const handleMouseLeave = () => {
    setIsHovered(false);
  };

  // 选中图标 SVG (Radio2 - Selected)
  const SelectedIcon = (
    <svg
      width={24}
      height={24}
      viewBox="0 0 24 24"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
    >
      <circle cx="12" cy="12" r="10.5" stroke="#000000" strokeWidth="1" />
      <circle cx="12" cy="12" r="7.5" fill="#000000" />
    </svg>
  );

  return (
    <div
      className={cardClasses}
      onClick={handleClick}
      onMouseEnter={handleMouseEnter}
      onMouseLeave={handleMouseLeave}
      role={onClick ? 'button' : undefined}
      tabIndex={onClick && !disabled ? 0 : undefined}
      aria-pressed={selected}
      aria-disabled={disabled}
    >
      <div className="snowui-select-card__content">
        {children}
      </div>
      {actualState === 'selected' && (
        <div className="snowui-select-card__icon">
          {SelectedIcon}
        </div>
      )}
    </div>
  );
};

export default SelectCard;
