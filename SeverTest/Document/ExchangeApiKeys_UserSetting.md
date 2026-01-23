# 用户交易所 API 绑定说明

## 目标
- 在前端 UserSetting 中提供交易所 API 绑定/解绑能力
- 支持 币安 / 欧易 / Bitget / Bybit / Gate
- 每个交易所最多 5 个 API 绑定
- 每个 API 支持备注，方便用户区分

## ccxt 必填参数（本地 C# 版本代码）
以下来自本地 ccxt C# 代码的 `requiredCredentials` 定义或私有接口要求：
- Binance: apiKey + secret
- OKX: apiKey + secret + password (Passphrase)
- Bitget: apiKey + secret + password (Passphrase)
- Bybit: apiKey + secret
- Gate: apiKey + secret

参考路径：
- `D:\UGit\ccxt\cs\ccxt\exchanges\okx.cs`
- `D:\UGit\ccxt\cs\ccxt\exchanges\bitget.cs`
- `D:\UGit\ccxt\cs\ccxt\exchanges\gate.cs`
- `D:\UGit\ccxt\cs\ccxt\exchanges\bybit.cs`
- `D:\UGit\ccxt\cs\ccxt\exchanges\binance.cs`

## 数据库
表：`user_exchange_api_keys`

关键字段（已使用）：
- `uid`
- `exchange_type`
- `label`
- `api_key`
- `api_secret`
- `api_password`

## 后端接口
基于 `ApiResponse<T>` 输出，前端 `HttpClient` 会自动解包。

### 获取列表
`GET /api/UserExchangeApiKeys`

返回字段（不返回明文秘钥）：
- `id`
- `exchangeType`
- `label`
- `apiKeyMasked`
- `apiSecretMasked`
- `hasPassword`
- `createdAt`
- `updatedAt`

### 绑定 API
`POST /api/UserExchangeApiKeys`

请求体：
```json
{
  "exchangeType": "okx",
  "label": "主账户",
  "apiKey": "xxx",
  "apiSecret": "yyy",
  "apiPassword": "zzz"
}
```
说明：
- `apiPassword` 仅 OKX/Bitget 必填
- 每个交易所最多 5 个（服务端校验）

### 解绑 API
`DELETE /api/UserExchangeApiKeys/{id}`

## 前端
入口：
- `Client/src/components/UserSettings.tsx`
- `Client/src/components/UserSettings.css`

行为要点：
- 列表展示按交易所分组，显示掩码后的 Key/Secret
- 新增绑定支持交易所选择 + 备注 + 密钥输入
- OKX/Bitget 强制 Passphrase
- 达到 5 个时禁止继续绑定

## 安全提示
当前存储为明文（数据库字段已有注释建议加密）。如需加密存储或前端脱敏展示策略调整，可在后续迭代中补充。
