import React, { useMemo, useState } from "react";
import { Button, Input } from "antd";
import "./SignIn4.css";
import { useNotification } from "./ui";
import { HttpClient } from "../network/httpClient";
import { setToken, getToken, ensureWsConnected } from "../network";
import { setAuthProfile, type AuthProfile } from "../auth/profileStore";

type SignIn4Props = {
  onAuthenticated?: (profile: AuthProfile) => void;
};

type LoginResponse = {
  token?: string;
  role?: number;
};

const ADMIN_ROLE = 255; // 超级管理员角色

const SignIn4: React.FC<SignIn4Props> = ({ onAuthenticated }) => {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);
  const client = useMemo(() => {
    const httpClient = new HttpClient();
    httpClient.setTokenProvider(getToken);
    return httpClient;
  }, []);
  const { success, error: showError } = useNotification();

  const handleSubmit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!email.trim() || !password.trim()) {
      showError("请输入邮箱和密码");
      return;
    }

    setIsSubmitting(true);

    try {
      const loginResponse = await client.postProtocol<LoginResponse>("/api/auth/login", "auth.login", {
        email: email.trim(),
        password,
      });

      if (!loginResponse?.token) {
        throw new Error("登录失败");
      }

      const role = loginResponse.role ?? 0;

      // 只允许角色为255的超级管理员登录
      if (role !== ADMIN_ROLE) {
        showError("权限不足，仅超级管理员可访问后台管理系统");
        setIsSubmitting(false);
        return;
      }

      const token = loginResponse.token;
      setToken(token);
      const profile: AuthProfile = {
        email: email.trim(),
        nickname: email.trim(),
        role: role,
      };
      setAuthProfile(profile);
      success("登录成功");
      
      // 登录成功后自动连接WebSocket
      ensureWsConnected().catch(() => {
        // WebSocket 连接异常由全局逻辑处理
      });
      
      onAuthenticated?.(profile);
    } catch (err) {
      const message = err instanceof Error ? err.message : "请求失败，请稍后重试";
      showError(message);
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="signin4-container">
      <div className="signin4-main">
        <div className="signin4-content">
          <h1 className="signin4-title">管理员登录</h1>

          <form className="signin4-form" onSubmit={handleSubmit}>
            <div className="signin4-inputs">
              <div className="signin4-input-wrapper">
                <Input
                  type="email"
                  className="signin4-input"
                  placeholder="邮箱"
                  value={email}
                  onChange={(event) => setEmail(event.target.value)}
                  autoComplete="email"
                  size="large"
                  bordered={false}
                />
              </div>
              <div className="signin4-input-wrapper">
                <Input.Password
                  className="signin4-input"
                  placeholder="密码"
                  value={password}
                  onChange={(event) => setPassword(event.target.value)}
                  autoComplete="current-password"
                  size="large"
                  bordered={false}
                />
              </div>
            </div>

            <Button
              type="primary"
              size="large"
              className="signin4-continue-btn"
              disabled={isSubmitting}
              loading={isSubmitting}
              htmlType="submit"
              block
            >
              {isSubmitting ? "请稍候..." : "登录"}
            </Button>
          </form>
        </div>
      </div>
    </div>
  );
};

export default SignIn4;
