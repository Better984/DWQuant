import React from 'react';
import { createPortal } from 'react-dom';
import './AlertDialog.css';

export interface AlertDialogProps {
  /** 是否显示弹窗 */
  open: boolean;
  /** 弹窗标题 */
  title: string;
  /** 主要描述文本 */
  description?: string;
  /** 辅助描述文本（较小字体，灰色） */
  helperText?: string;
  /** 取消按钮文本 */
  cancelText?: string;
  /** 确认按钮文本 */
  confirmText?: string;
  /** 确认按钮是否为危险操作（红色） */
  danger?: boolean;
  /** 点击遮罩层是否关闭 */
  closeOnOverlayClick?: boolean;
  /** 取消按钮点击回调 */
  onCancel?: () => void;
  /** 确认按钮点击回调 */
  onConfirm?: () => void;
  /** 关闭弹窗回调 */
  onClose?: () => void;
}

const AlertDialog: React.FC<AlertDialogProps> = ({
  open,
  title,
  description,
  helperText,
  cancelText = 'Cancel',
  confirmText = 'Confirm',
  danger = false,
  closeOnOverlayClick = true,
  onCancel,
  onConfirm,
  onClose,
}) => {
  if (!open) return null;

  const handleOverlayClick = (e: React.MouseEvent<HTMLDivElement>) => {
    if (e.target === e.currentTarget && closeOnOverlayClick) {
      onClose?.();
    }
  };

  const handleCancel = () => {
    onCancel?.();
    onClose?.();
  };

  const handleConfirm = () => {
    onConfirm?.();
    onClose?.();
  };

  return createPortal(
    <div className="alert-dialog-overlay" onClick={handleOverlayClick}>
      <div className="alert-dialog-container">
        {/* 标题 */}
        <div className="alert-dialog-title">
          {title}
        </div>

        {/* 分隔线 */}
        <div className="alert-dialog-divider"></div>

        {/* 内容区域 */}
        {(description || helperText) && (
          <div className="alert-dialog-content">
            {description && (
              <div className="alert-dialog-description">
                {description}
              </div>
            )}
            {helperText && (
              <div className="alert-dialog-helper">
                {helperText}
              </div>
            )}
          </div>
        )}

        {/* 按钮组 */}
        <div className="alert-dialog-actions">
          <button
            type="button"
            className="alert-dialog-button alert-dialog-button-outline"
            onClick={handleCancel}
          >
            {cancelText}
          </button>
          <button
            type="button"
            className={`alert-dialog-button alert-dialog-button-filled ${danger ? 'alert-dialog-button-danger' : ''}`}
            onClick={handleConfirm}
          >
            {confirmText}
          </button>
        </div>
      </div>
    </div>,
    document.body
  );
};

export default AlertDialog;
