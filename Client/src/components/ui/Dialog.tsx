import React, { useEffect } from 'react';
import { createPortal } from 'react-dom';
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
  footer?: React.ReactNode;
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
  footer,
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
  if (typeof document === 'undefined') return null;

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
  const Divider = <div className="ui-dialog__divider-line" />;

  return createPortal(
    <div className="ui-dialog-overlay" onClick={handleBackdropClick}>
      <div className={`ui-dialog ${className}`} onClick={(e) => e.stopPropagation()}>
        {/* 标题区域 */}
        {(title || showCloseButton) && (
          <>
            <div className="ui-dialog__header">
              {title && (
                <h2 className="ui-dialog__title">{title}</h2>
              )}
              {showCloseButton && (
                <button
                  className="ui-dialog__close"
                  onClick={onClose}
                  aria-label="关闭"
                >
                  {CloseIcon}
                </button>
              )}
            </div>
            <div className="ui-dialog__divider">{Divider}</div>
          </>
        )}

        {/* 内容区域 */}
        {children && (
          <div className="ui-dialog__content">
            {children}
          </div>
        )}

        {/* 按钮区域 */}
        {(footer || cancelText || confirmText) && (
          <>
            <div className="ui-dialog__divider">{Divider}</div>
            <div className="ui-dialog__footer">
              {footer || (
                <>
                  {cancelText && (
                    <button
                      className="ui-dialog__button ui-dialog__button--cancel"
                      onClick={handleCancel}
                    >
                      {cancelText}
                    </button>
                  )}
                  {confirmText && (
                    <button
                      className="ui-dialog__button ui-dialog__button--confirm"
                      onClick={handleConfirm}
                    >
                      {confirmText}
                    </button>
                  )}
                </>
              )}
            </div>
          </>
        )}
      </div>
    </div>,
    document.body,
  );
};

export default Dialog;
