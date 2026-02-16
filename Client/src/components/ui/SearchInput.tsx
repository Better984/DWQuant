import React, { useState, useRef, useEffect } from 'react';
import './SearchInput.css';

export type SearchInputType = 'gray' | 'glass' | 'outline' | 'typing';
export type SearchInputState = 'default' | 'static' | 'hover' | 'focus';

export interface SearchInputProps extends Omit<React.InputHTMLAttributes<HTMLInputElement>, 'type' | 'size'> {
  type?: SearchInputType;
  showShortcut?: boolean;
  shortcutKey?: string;
  onClear?: () => void;
  className?: string;
}

const SearchInput: React.FC<SearchInputProps> = ({
  type = 'gray',
  showShortcut = true,
  shortcutKey = '/',
  value,
  defaultValue,
  onClear,
  onChange,
  onFocus,
  onBlur,
  className = '',
  disabled,
  ...props
}) => {
  const [internalValue, setInternalValue] = useState(defaultValue || '');
  const [isFocused, setIsFocused] = useState(false);
  const [isHovered, setIsHovered] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);
  const isControlled = value !== undefined;
  const currentValue = isControlled ? value : internalValue;
  const hasValue = currentValue !== undefined && currentValue !== null && String(currentValue).length > 0;
  const isTyping = hasValue && (type === 'typing' || hasValue);

  const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (!isControlled) {
      setInternalValue(e.target.value);
    }
    onChange?.(e);
  };

  const handleFocus = (e: React.FocusEvent<HTMLInputElement>) => {
    setIsFocused(true);
    onFocus?.(e);
  };

  const handleBlur = (e: React.FocusEvent<HTMLInputElement>) => {
    setIsFocused(false);
    onBlur?.(e);
  };

  const handleClear = (e: React.MouseEvent<HTMLButtonElement>) => {
    e.preventDefault();
    e.stopPropagation();
    if (!isControlled) {
      setInternalValue('');
    }
    if (inputRef.current) {
      const nativeInputValueSetter = Object.getOwnPropertyDescriptor(
        window.HTMLInputElement.prototype,
        'value'
      )?.set;
      nativeInputValueSetter?.call(inputRef.current, '');
      const event = new Event('input', { bubbles: true });
      inputRef.current.dispatchEvent(event);
    }
    onClear?.();
    setTimeout(() => {
      inputRef.current?.focus();
    }, 0);
  };

  const handleKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Escape' && hasValue) {
      handleClear();
    }
    props.onKeyDown?.(e);
  };

  // 确定实际显示的类型：当有值时，如果不是 typing 类型，自动切换到 typing
  const displayType = hasValue && type !== 'typing' ? 'typing' : type;
  const state: SearchInputState = disabled ? 'static' : isFocused ? 'focus' : isHovered ? 'hover' : 'default';

  const inputClasses = [
    'ui-search-input',
    `ui-search-input--${displayType}`,
    `ui-search-input--${state}`,
    className,
  ]
    .filter(Boolean)
    .join(' ');

  // 搜索图标 SVG
  const SearchIcon = (
    <svg
      width={16}
      height={16}
      viewBox="0 0 16 16"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
    >
      <path
        d="M7 12C9.76142 12 12 9.76142 12 7C12 4.23858 9.76142 2 7 2C4.23858 2 2 4.23858 2 7C2 9.76142 4.23858 12 7 12Z"
        stroke="currentColor"
        strokeWidth="1.48"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <path
        d="M10.5 10.5L14 14"
        stroke="currentColor"
        strokeWidth="1.48"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  );

  // 清除图标 SVG (XCircle)
  const ClearIcon = (
    <svg
      width={16}
      height={16}
      viewBox="0 0 16 16"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
    >
      <circle cx="8" cy="8" r="6.5" stroke="currentColor" strokeWidth="1" />
      <path
        d="M10 6L6 10M6 6L10 10"
        stroke="currentColor"
        strokeWidth="1"
        strokeLinecap="round"
      />
    </svg>
  );

  return (
    <div
      className={inputClasses}
      onMouseEnter={() => !disabled && setIsHovered(true)}
      onMouseLeave={() => setIsHovered(false)}
    >
      <div className="ui-search-input__content">
        <div className="ui-search-input__icon">{SearchIcon}</div>
        <input
          ref={inputRef}
          type="text"
          className="ui-search-input__field"
          value={currentValue}
          defaultValue={defaultValue}
          onChange={handleChange}
          onFocus={handleFocus}
          onBlur={handleBlur}
          onKeyDown={handleKeyDown}
          disabled={disabled}
          placeholder={props.placeholder || 'Search'}
          {...props}
        />
        {hasValue && (
          <button
            className="ui-search-input__clear"
            onClick={handleClear}
            type="button"
            aria-label="清除"
            tabIndex={-1}
          >
            {ClearIcon}
          </button>
        )}
      </div>
      {showShortcut && !hasValue && (
        <div className="ui-search-input__shortcut">
          <span>{shortcutKey}</span>
        </div>
      )}
    </div>
  );
};

export default SearchInput;
