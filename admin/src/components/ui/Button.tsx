import React from 'react';
import './Button.css';

export type ButtonSize = 'small' | 'medium' | 'large';
export type ButtonStyle = 'borderless' | 'gray' | 'outline' | 'filled';
export type ButtonState = 'default' | 'hover' | 'disabled';

export interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  size?: ButtonSize;
  style?: ButtonStyle;
  children: React.ReactNode;
  leftIcon?: React.ReactNode;
  rightIcon?: React.ReactNode;
}

const Button: React.FC<ButtonProps> = ({
  size = 'medium',
  style = 'filled',
  children,
  leftIcon,
  rightIcon,
  disabled,
  className = '',
  ...props
}) => {
  const state: ButtonState = disabled ? 'disabled' : 'default';
  
  const buttonClasses = [
    'snowui-button',
    `snowui-button--${size}`,
    `snowui-button--${style}`,
    `snowui-button--${state}`,
    className,
  ]
    .filter(Boolean)
    .join(' ');

  return (
    <button
      className={buttonClasses}
      disabled={disabled}
      {...props}
    >
      {leftIcon && <span className="snowui-button__icon snowui-button__icon--left">{leftIcon}</span>}
      <span className="snowui-button__text">{children}</span>
      {rightIcon && <span className="snowui-button__icon snowui-button__icon--right">{rightIcon}</span>}
    </button>
  );
};

export default Button;
