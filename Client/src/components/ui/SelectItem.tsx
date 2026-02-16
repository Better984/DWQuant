import React from 'react';
import './SelectItem.css';

export interface SelectItemProps {
  children: React.ReactNode;
  selected?: boolean;
  disabled?: boolean;
  onClick?: () => void;
  className?: string;
}

const SelectItem: React.FC<SelectItemProps> = ({
  children,
  selected = false,
  disabled = false,
  onClick,
  className = '',
}) => {
  const itemClasses = [
    'ui-select-item',
    selected ? 'ui-select-item--selected' : '',
    disabled ? 'ui-select-item--disabled' : '',
    className,
  ]
    .filter(Boolean)
    .join(' ');

  return (
    <div
      className={itemClasses}
      onClick={disabled ? undefined : onClick}
      role="option"
      aria-selected={selected}
      aria-disabled={disabled}
      tabIndex={disabled ? -1 : 0}
      onKeyDown={(e) => {
        if ((e.key === 'Enter' || e.key === ' ') && !disabled && onClick) {
          e.preventDefault();
          onClick();
        }
      }}
    >
      <span className="ui-select-item__text">{children}</span>
      {selected && (
        <svg
          width="24"
          height="24"
          viewBox="0 0 24 24"
          fill="none"
          xmlns="http://www.w3.org/2000/svg"
          className="ui-select-item__icon"
        >
          <circle
            cx="12"
            cy="12"
            r="10.5"
            fill="url(#paint0_radial_radio)"
            stroke="none"
          />
          <defs>
            <radialGradient
              id="paint0_radial_radio"
              cx="0"
              cy="0"
              r="1"
              gradientUnits="userSpaceOnUse"
              gradientTransform="translate(12 12) rotate(90) scale(10.5)"
            >
              <stop stopColor="#FFFFFF" />
              <stop offset="1" stopColor="#000000" />
            </radialGradient>
          </defs>
        </svg>
      )}
    </div>
  );
};

export default SelectItem;
