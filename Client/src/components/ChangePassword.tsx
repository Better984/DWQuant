import React, { useState, useMemo } from 'react';
import { HttpClient } from '../network/httpClient';
import { getAuthProfile } from '../auth/profileStore';
import { getToken } from '../network';
import Button from './ui/Button';
import { useNotification } from './ui';
import './ChangePassword.css';

type ChangePasswordResponse = {
  status?: string;
  message?: string;
};

const ChangePassword: React.FC = () => {
  const [oldPassword, setOldPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const client = useMemo(() => new HttpClient(), []);
  const profile = getAuthProfile();
  const { success, error: showError } = useNotification();

  const validatePassword = (password: string): string | null => {
    if (password.length < 6) {
      return '密码长度至少为6位';
    }
    return null;
  };

  const handleSubmit = async (event: React.FormEvent) => {
    event.preventDefault();

    // 验证输入
    if (!oldPassword.trim() || !newPassword.trim() || !confirmPassword.trim()) {
      showError('请填写所有字段');
      return;
    }

    // 验证新密码强度
    const passwordError = validatePassword(newPassword);
    if (passwordError) {
      showError(passwordError);
      return;
    }

    // 验证两次新密码是否一致
    if (newPassword !== confirmPassword) {
      showError('两次输入的新密码不一致');
      return;
    }

    // 验证新旧密码不能相同
    if (oldPassword === newPassword) {
      showError('新密码不能与原密码相同');
      return;
    }

    setIsSubmitting(true);

    try {
      // 设置 token provider
      client.setTokenProvider(() => getToken());

      const response = await client.post<ChangePasswordResponse>('/api/auth/change-password', {
        email: profile?.email || '',
        oldPassword: oldPassword,
        newPassword: newPassword,
      });

      if (response?.status !== 'success') {
        throw new Error(response?.message || '修改密码失败');
      }

      success('密码修改成功');
      // 清空表单
      setOldPassword('');
      setNewPassword('');
      setConfirmPassword('');
    } catch (err) {
      const message = err instanceof Error ? err.message : '修改密码失败，请稍后重试';
      showError(message);
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="change-password-container">
      <div className="change-password-content">
        <h2 className="change-password-title">修改密码</h2>

        <form className="change-password-form" onSubmit={handleSubmit}>
          <div className="change-password-inputs">
            <div className="change-password-input-wrapper">
              <label className="change-password-label">原密码</label>
              <input
                type="password"
                className="change-password-input"
                placeholder="请输入原密码"
                value={oldPassword}
                onChange={(event) => setOldPassword(event.target.value)}
                autoComplete="current-password"
                disabled={isSubmitting}
              />
            </div>

            <div className="change-password-input-wrapper">
              <label className="change-password-label">新密码</label>
              <input
                type="password"
                className="change-password-input"
                placeholder="请输入新密码（至少6位）"
                value={newPassword}
                onChange={(event) => setNewPassword(event.target.value)}
                autoComplete="new-password"
                disabled={isSubmitting}
              />
            </div>

            <div className="change-password-input-wrapper">
              <label className="change-password-label">确认新密码</label>
              <input
                type="password"
                className="change-password-input"
                placeholder="请再次输入新密码"
                value={confirmPassword}
                onChange={(event) => setConfirmPassword(event.target.value)}
                autoComplete="new-password"
                disabled={isSubmitting}
              />
            </div>
          </div>

          <Button
            type="submit"
            size="large"
            style="filled"
            className="change-password-submit-btn"
            disabled={isSubmitting}
          >
            {isSubmitting ? '修改中...' : '确认修改'}
          </Button>
        </form>
      </div>
    </div>
  );
};

export default ChangePassword;
