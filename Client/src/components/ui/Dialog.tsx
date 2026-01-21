import React, { useEffect } from 'react';
import './Dialog.css';

export interface DialogProps {
  open: boolean;
  onClose: () => void;
  title?: string;
  children?: React.ReactNode;
  cancelText?: string;
  confirmText?: string;
  onCancel?: () => void;
  onConfirm?: () => void;
  showCloseButton?: boolean;
  className?: string;
}

const Dialog: React.FC<DialogProps> = ({
  open,
  onClose,
  title,
  children,
  cancelText = 'Cancel',
  confirmText,
  onCancel,
  onConfirm,
  showCloseButton = true,
  className = '',
}) => {
  // 处理 ESC 键关闭
  useEffect(() => {
    const handleEscape = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && open) {
        onClose();
      }
    };

    if (open) {
      document.addEventListener('keydown', handleEscape);
      // 防止背景滚动
      document.body.style.overflow = 'hidden';
    }

    return () => {
      document.removeEventListener('keydown', handleEscape);
      document.body.style.overflow = '';
    };
  }, [open, onClose]);

  if (!open) return null;

  const handleBackdropClick = (e: React.MouseEvent<HTMLDivElement>) => {
    if (e.target === e.currentTarget) {
      onClose();
    }
  };

  const handleCancel = () => {
    if (onCancel) {
      onCancel();
    } else {
      onClose();
    }
  };

  const handleConfirm = () => {
    if (onConfirm) {
      onConfirm();
    }
  };

  // 关闭图标 SVG
  const CloseIcon = (
    <svg
      width={24}
      height={24}
      viewBox="0 0 24 24"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
    >
      <path
        d="M18 6L6 18M6 6L18 18"
        stroke="currentColor"
        strokeWidth="1.5"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  );

  // 分隔线
  const Divider = <div className="snowui-dialog__divider-line" />;

  return (
    <div className="snowui-dialog-overlay" onClick={handleBackdropClick}>
      <div className={`snowui-dialog ${className}`} onClick={(e) => e.stopPropagation()}>
        {/* 标题区域 */}
        {(title || showCloseButton) && (
          <>
            <div className="snowui-dialog__header">
              {title && (
                <h2 className="snowui-dialog__title">{title}</h2>
              )}
              {showCloseButton && (
                <button
                  className="snowui-dialog__close"
                  onClick={onClose}
                  aria-label="关闭"
                >
                  {CloseIcon}
                </button>
              )}
            </div>
            <div className="snowui-dialog__divider">{Divider}</div>
          </>
        )}

        {/* 内容区域 */}
        {children && (
          <div className="snowui-dialog__content">
            {children}
          </div>
        )}

        {/* 按钮区域 */}
        {(cancelText || confirmText) && (
          <>
            <div className="snowui-dialog__divider">{Divider}</div>
            <div className="snowui-dialog__footer">
              {cancelText && (
                <button
                  className="snowui-dialog__button snowui-dialog__button--cancel"
                  onClick={handleCancel}
                >
                  {cancelText}
                </button>
              )}
              {confirmText && (
                <button
                  className="snowui-dialog__button snowui-dialog__button--confirm"
                  onClick={handleConfirm}
                >
                  {confirmText}
                </button>
              )}
            </div>
          </>
        )}
      </div>
    </div>
  );
};

export default Dialog;
