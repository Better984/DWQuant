import React, { useState, useRef, useCallback } from 'react';
import './Slider.css';

export interface SliderProps {
  value?: number;
  defaultValue?: number;
  min?: number;
  max?: number;
  step?: number;
  disabled?: boolean;
  label?: string;
  showValue?: boolean;
  valueSuffix?: string;
  onChange?: (value: number) => void;
  onValueChange?: (value: number) => void;
  className?: string;
}

const Slider: React.FC<SliderProps> = ({
  value: controlledValue,
  defaultValue = 0,
  min = 0,
  max = 100,
  step = 1,
  disabled = false,
  label,
  showValue = true,
  valueSuffix = '%',
  onChange,
  onValueChange,
  className = '',
}) => {
  const [internalValue, setInternalValue] = useState(defaultValue);
  const [isActive, setIsActive] = useState(false);
  const sliderRef = useRef<HTMLDivElement>(null);
  const isControlled = controlledValue !== undefined;
  const currentValue = isControlled ? controlledValue : internalValue;

  const percentage = ((currentValue - min) / (max - min)) * 100;

  const handleChange = useCallback(
    (newValue: number) => {
      const clampedValue = Math.max(min, Math.min(max, newValue));
      
      if (!isControlled) {
        setInternalValue(clampedValue);
      }
      
      onValueChange?.(clampedValue);
      onChange?.(clampedValue);
    },
    [min, max, isControlled, onChange, onValueChange]
  );

  const handleMouseDown = useCallback(
    (e: React.MouseEvent<HTMLDivElement>) => {
      if (disabled) return;
      
      setIsActive(true);
      
      const handleMouseMove = (e: MouseEvent) => {
        if (!sliderRef.current) return;
        
        const rect = sliderRef.current.getBoundingClientRect();
        const x = e.clientX - rect.left;
        const percentage = Math.max(0, Math.min(1, x / rect.width));
        const newValue = min + percentage * (max - min);
        const steppedValue = Math.round(newValue / step) * step;
        
        handleChange(steppedValue);
      };

      const handleMouseUp = () => {
        setIsActive(false);
        document.removeEventListener('mousemove', handleMouseMove);
        document.removeEventListener('mouseup', handleMouseUp);
      };

      handleMouseMove(e.nativeEvent);
      document.addEventListener('mousemove', handleMouseMove);
      document.addEventListener('mouseup', handleMouseUp);
    },
    [disabled, min, max, step, handleChange]
  );

  const handleClick = useCallback(
    (e: React.MouseEvent<HTMLDivElement>) => {
      if (disabled) return;
      
      if (!sliderRef.current) return;
      
      const rect = sliderRef.current.getBoundingClientRect();
      const x = e.clientX - rect.left;
      const percentage = Math.max(0, Math.min(1, x / rect.width));
      const newValue = min + percentage * (max - min);
      const steppedValue = Math.round(newValue / step) * step;
      
      handleChange(steppedValue);
    },
    [disabled, min, max, step, handleChange]
  );

  const formatValue = (val: number): string => {
    return `${Math.round(val)}${valueSuffix}`;
  };

  const sliderClasses = [
    'snowui-slider',
    disabled && 'snowui-slider--disabled',
    isActive && 'snowui-slider--active',
    className,
  ]
    .filter(Boolean)
    .join(' ');

  return (
    <div className={sliderClasses}>
      <div
        ref={sliderRef}
        className="snowui-slider__track"
        onClick={handleClick}
        onMouseDown={handleMouseDown}
      >
        <div
          className="snowui-slider__fill"
          style={{ width: `${percentage}%` }}
        >
          {label && (
            <span className="snowui-slider__label">{label}</span>
          )}
          {isActive && (
            <div className="snowui-slider__active-line" />
          )}
        </div>
      </div>
      {showValue && (
        <div 
          className={`snowui-slider__value ${percentage >= 100 ? 'snowui-slider__value--full' : ''}`}
        >
          {formatValue(currentValue)}
        </div>
      )}
    </div>
  );
};

export default Slider;
