# Accounts 模块协议

## 认证

### auth.register
- 路径：`POST /api/auth/register`
- data：
  - `email` string
  - `password` string
  - `avatarUrl` string?
- 说明：无需邮箱验证码，直接注册并返回 token
- 响应 data：`{ token }`

### auth.login
- 路径：`POST /api/auth/login`
- data：
  - `email` string
  - `password` string
- 响应 data：`{ token, role }`

### auth.password.change
- 路径：`POST /api/auth/change-password`
- data：
  - `email` string
  - `oldPassword` string
  - `newPassword` string
- 响应 data：`null`

### auth.account.delete
- 路径：`POST /api/auth/delete-account`
- data：
  - `email` string
  - `password` string
- 响应 data：`null`

---

## 交易设置

### trading.sandbox.get
- 路径：`POST /api/trading/sandbox/get`
- data：无
- 响应 data：`{ enableSandboxMode: bool }`

### trading.sandbox.set
- 路径：`POST /api/trading/sandbox/set`
- data：
  - `enableSandboxMode` bool
- 响应 data：`{ enableSandboxMode: bool }`
