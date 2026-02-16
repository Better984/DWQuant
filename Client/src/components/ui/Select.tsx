import React, { useState, useRef, useEffect } from 'react';
import './Select.css';

export interface SelectOption {
  value: string;
  label: string;
  disabled?: boolean;
}

export interface SelectProps {
  options: SelectOption[];
  value?: string;
  defaultValue?: string;
  placeholder?: string;
  disabled?: boolean;
  onChange?: (value: string) => void;
  className?: string;
}

const ArrowIcon: React.FC<{ isOpen: boolean }> = ({ isOpen }) => (
  <svg
    width="16"
    height="16"
    viewBox="0 0 16 16"
    fill="none"
    xmlns="http://www.w3.org/2000/svg"
    className={`ui-select__arrow ${isOpen ? 'ui-select__arrow--open' : ''}`}
  >
    <path
      d="M3.5 5.5L8 10L12.5 5.5"
      stroke="rgba(0, 0, 0, 0.4)"
      strokeWidth="1.5"
      strokeLinecap="round"
      strokeLinejoin="round"
    />
  </svg>
);

const Select: React.FC<SelectProps> = ({
  options,
  value: controlledValue,
  defaultValue,
  placeholder = 'Select...',
  disabled = false,
  onChange,
  className = '',
}) => {
  const [isOpen, setIsOpen] = useState(false);
  const [internalValue, setInternalValue] = useState<string>(defaultValue || '');
  const selectRef = useRef<HTMLDivElement>(null);
  const dropdownRef = useRef<HTMLDivElement>(null);

  const isControlled = controlledValue !== undefined;
  const currentValue = isControlled ? controlledValue : internalValue;

  const selectedOption = options.find((opt) => opt.value === currentValue);

  useEffect(() => {
    const handleClickOutside = (event: MouseEvent) => {
      if (
        selectRef.current &&
        !selectRef.current.contains(event.target as Node) &&
        dropdownRef.current &&
        !dropdownRef.current.contains(event.target as Node)
      ) {
        setIsOpen(false);
      }
    };

    if (isOpen) {
      document.addEventListener('mousedown', handleClickOutside);
      return () => {
        document.removeEventListener('mousedown', handleClickOutside);
      };
    }
  }, [isOpen]);

  useEffect(() => {
    if (isOpen && dropdownRef.current && selectRef.current) {
      // 定位下拉菜单
      const rect = selectRef.current.getBoundingClientRect();
      const scrollY = window.scrollY || window.pageYOffset;
      const scrollX = window.scrollX || window.pageXOffset;
      
      dropdownRef.current.style.top = `${rect.bottom + scrollY + 8}px`;
      dropdownRef.current.style.left = `${rect.left + scrollX}px`;
      dropdownRef.current.style.width = `${rect.width}px`;
    }
  }, [isOpen]);

  const handleToggle = () => {
    if (!disabled) {
      setIsOpen(!isOpen);
    }
  };

  const handleSelect = (optionValue: string) => {
    if (!isControlled) {
      setInternalValue(optionValue);
    }
    if (onChange) {
      onChange(optionValue);
    }
    setIsOpen(false);
  };

  const selectClasses = [
    'ui-select',
    isOpen ? 'ui-select--open' : '',
    disabled ? 'ui-select--disabled' : '',
    className,
  ]
    .filter(Boolean)
    .join(' ');

  return (
    <>
      <div
        ref={selectRef}
        className={selectClasses}
        onClick={handleToggle}
        role="combobox"
        aria-expanded={isOpen}
        aria-haspopup="listbox"
        aria-disabled={disabled}
        tabIndex={disabled ? -1 : 0}
        onKeyDown={(e) => {
          if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault();
            handleToggle();
          } else if (e.key === 'Escape') {
            setIsOpen(false);
          }
        }}
      >
        <div className="ui-select__content">
          <span className="ui-select__text">
            {selectedOption ? selectedOption.label : placeholder}
          </span>
        </div>
        <ArrowIcon isOpen={isOpen} />
      </div>

      {isOpen && (
        <div
          ref={dropdownRef}
          className="ui-select__dropdown"
          role="listbox"
        >
          {options.map((option) => (
            <div
              key={option.value}
              className={`ui-select__option ${
                option.value === currentValue ? 'ui-select__option--selected' : ''
              } ${option.disabled ? 'ui-select__option--disabled' : ''}`}
              onClick={() => !option.disabled && handleSelect(option.value)}
              role="option"
              aria-selected={option.value === currentValue}
            >
              <span className="ui-select__option-text">{option.label}</span>
              {option.value === currentValue && (
                <svg
                  width="24"
                  height="24"
                  viewBox="0 0 24 24"
                  fill="none"
                  xmlns="http://www.w3.org/2000/svg"
                  className="ui-select__option-icon"
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
          ))}
        </div>
      )}
    </>
  );
};

export default Select;
