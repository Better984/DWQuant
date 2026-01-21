import React, { useState, useRef, useEffect } from 'react';
import './TextInput.css';

export type TextInputType = 'single' | 'textarea' | 'with-label-horizontal' | 'with-label-vertical';
export type TextInputState = 'default' | 'static' | 'hover' | 'focus';

export interface TextInputProps extends Omit<React.InputHTMLAttributes<HTMLInputElement | HTMLTextAreaElement>, 'type' | 'size'> {
  type?: TextInputType;
  label?: string;
  maxLength?: number;
  showCounter?: boolean;
  rows?: number;
  as?: 'input' | 'textarea';
  className?: string;
}

const TextInput: React.FC<TextInputProps> = ({
  type = 'single',
  label,
  maxLength,
  showCounter = false,
  rows = 3,
  as,
  value,
  defaultValue,
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
  const inputRef = useRef<HTMLInputElement | HTMLTextAreaElement>(null);
  const isControlled = value !== undefined;
  const currentValue = isControlled ? value : internalValue;
  const currentLength = currentValue ? String(currentValue).length : 0;
  
  // 确定实际使用的元素类型
  const elementType = as || (type === 'textarea' ? 'textarea' : 'input');
  const isTextarea = elementType === 'textarea';
  const shouldShowCounter = showCounter || (maxLength !== undefined && maxLength > 0);

  const handleChange = (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => {
    if (!isControlled) {
      setInternalValue(e.target.value);
    }
    onChange?.(e as any);
  };

  const handleFocus = (e: React.FocusEvent<HTMLInputElement | HTMLTextAreaElement>) => {
    setIsFocused(true);
    onFocus?.(e as any);
  };

  const handleBlur = (e: React.FocusEvent<HTMLInputElement | HTMLTextAreaElement>) => {
    setIsFocused(false);
    onBlur?.(e as any);
  };

  const state: TextInputState = disabled ? 'static' : isFocused ? 'focus' : isHovered ? 'hover' : 'default';

  const inputClasses = [
    'snowui-text-input',
    `snowui-text-input--${type}`,
    `snowui-text-input--${state}`,
    className,
  ]
    .filter(Boolean)
    .join(' ');

  // 圆角图标 SVG (RoundedCorner)
  const RoundedCornerIcon = (
    <svg
      width={16}
      height={16}
      viewBox="0 0 16 16"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
    >
      <rect x="4" y="4" width="8" height="8" rx="1" fill="rgba(0, 0, 0, 0.1)" />
    </svg>
  );

  const inputElement = isTextarea ? (
    <textarea
      ref={inputRef as React.RefObject<HTMLTextAreaElement>}
      className="snowui-text-input__field"
      value={currentValue}
      defaultValue={defaultValue}
      onChange={handleChange}
      onFocus={handleFocus}
      onBlur={handleBlur}
      disabled={disabled}
      maxLength={maxLength}
      rows={rows}
      {...(props as React.TextareaHTMLAttributes<HTMLTextAreaElement>)}
    />
  ) : (
    <input
      ref={inputRef as React.RefObject<HTMLInputElement>}
      type="text"
      className="snowui-text-input__field"
      value={currentValue}
      defaultValue={defaultValue}
      onChange={handleChange}
      onFocus={handleFocus}
      onBlur={handleBlur}
      disabled={disabled}
      maxLength={maxLength}
      {...(props as React.InputHTMLAttributes<HTMLInputElement>)}
    />
  );

  return (
    <div
      className={inputClasses}
      onMouseEnter={() => !disabled && setIsHovered(true)}
      onMouseLeave={() => setIsHovered(false)}
    >
      {type === 'with-label-horizontal' && label && (
        <div className="snowui-text-input__label-horizontal">
          <label className="snowui-text-input__label-text">{label}</label>
          {inputElement}
        </div>
      )}
      {type === 'with-label-vertical' && label && (
        <div className="snowui-text-input__label-vertical">
          <label className="snowui-text-input__label-text">{label}</label>
          {inputElement}
        </div>
      )}
      {(type === 'single' || type === 'textarea') && (
        <>
          {inputElement}
          {type === 'textarea' && shouldShowCounter && (
            <div className="snowui-text-input__counter">
              <span className="snowui-text-input__counter-text">
                {currentLength}/{maxLength || 200}
              </span>
              <div className="snowui-text-input__counter-icon">{RoundedCornerIcon}</div>
            </div>
          )}
        </>
      )}
    </div>
  );
};

export default TextInput;
