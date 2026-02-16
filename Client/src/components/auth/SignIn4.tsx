import React, { useMemo, useState } from "react";
import "./SignIn4.css";
import { Button, useNotification } from "../ui/index.ts";
import { HttpClient } from "../../network/httpClient";
import { getNetworkConfig } from "../../network/config";
import { ensureWsConnected, setToken } from "../../network/index.ts";
import { setAuthProfile, type AuthProfile } from "../../auth/profileStore.ts";

type AuthMode = "signin" | "signup";

type SignIn4Props = {
  initialMode?: AuthMode;
  onAuthenticated?: (profile: AuthProfile) => void;
};

type LoginResponse = {
  token?: string;
  role?: number;
  system?: string;
  kickedOtherSession?: boolean;
};

type RegisterResponse = {
  token?: string;
};

const SignIn4: React.FC<SignIn4Props> = ({ initialMode = "signin", onAuthenticated }) => {
  const [mode, setMode] = useState<AuthMode>(initialMode);
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);
  const client = useMemo(() => new HttpClient(), []);
  const networkConfig = useMemo(() => getNetworkConfig(), []);
  const { success, error: showError } = useNotification();

  const title = mode === "signin" ? "登录" : "注册";
  const submitLabel = mode === "signin" ? "登录" : "创建账户";

  const handleSubmit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!email.trim() || !password.trim()) {
      showError("请输入邮箱和密码");
      return;
    }

    setIsSubmitting(true);

    try {
      let token: string | undefined;
      let role: number | undefined;

      if (mode === "signup") {
        if (password.length < 6) {
          showError("密码长度至少为6位");
          setIsSubmitting(false);
          return;
        }

        const registerResponse = await client.postProtocol<RegisterResponse>("/api/auth/register", "auth.register", {
          email: email.trim(),
          password,
        });
        token = registerResponse.token;
        role = 0;
        success("注册成功");
      } else {
        const loginResponse = await client.postProtocol<LoginResponse>("/api/auth/login", "auth.login", {
          email: email.trim(),
          password,
          system: networkConfig.system,
        });

        if (!loginResponse?.token) {
          throw new Error("登录失败");
        }
        token = loginResponse.token;
        role = loginResponse.role ?? 0;
        success(loginResponse.kickedOtherSession ? "登录成功，另一台同类型设备已下线" : "登录成功");
      }

      if (!token) {
        throw new Error("获取令牌失败");
      }

      setToken(token);
      const profile: AuthProfile = {
        email: email.trim(),
        nickname: email.trim(),
        role: role ?? 0,
      };
      setAuthProfile(profile);
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
          <h1 className="signin4-title">{title}</h1>

          <form className="signin4-form" onSubmit={handleSubmit}>
            <div className="signin4-inputs">
              <div className="signin4-input-wrapper">
                <input
                  type="email"
                  className="signin4-input"
                  placeholder="邮箱"
                  value={email}
                  onChange={(event) => setEmail(event.target.value)}
                  autoComplete="email"
                />
              </div>
              <div className="signin4-input-wrapper">
                <input
                  type="password"
                  className="signin4-input"
                  placeholder="密码"
                  value={password}
                  onChange={(event) => setPassword(event.target.value)}
                  autoComplete={mode === "signin" ? "current-password" : "new-password"}
                />
              </div>
            </div>

            <Button
              size="large"
              style="filled"
              className="signin4-continue-btn"
              disabled={isSubmitting}
            >
              {isSubmitting ? "请稍候..." : submitLabel}
            </Button>
          </form>

          <div className="signin4-links">
            <button
              type="button"
              className="signin4-link signin4-link-button"
              onClick={() => {
                setMode(mode === "signin" ? "signup" : "signin");
              }}
            >
              {mode === "signin" ? "注册" : "登录"}
            </button>
            <span className="signin4-link signin4-link-muted">忘记密码</span>
          </div>
        </div>
      </div>
    </div>
  );
};

export default SignIn4;

