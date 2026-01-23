# 消息平台绑定说明

## 目标
- 用户在 UserSetting 中绑定/解绑通知渠道
- 支持 钉钉 / 企业微信 / 邮箱 / Telegram
- 每个平台仅允许绑定一个

## 数据库
表：`user_notify_channels`

关键字段（已使用）：
- `uid`
- `platform` (dingtalk/wecom/email/telegram)
- `address` (webhook / email / chat_id)
- `secret` (可选，钉钉加签或 Telegram bot token)
- `is_enabled`
- `is_default`

## 后端接口
### 获取列表
`GET /api/UserNotifyChannels`

返回：
- `id`
- `platform`
- `addressMasked`
- `hasSecret`
- `isEnabled`
- `isDefault`
- `createdAt`
- `updatedAt`

### 绑定/更新
`POST /api/UserNotifyChannels`

请求体：
```json
{
  "platform": "telegram",
  "address": "123456789",
  "secret": "123456:ABC-DEF"
}
```

说明：
- address 必填
- Telegram 需要填写 secret (bot token)
- 再次提交同平台会覆盖原配置

### 解绑
`DELETE /api/UserNotifyChannels/{platform}`

## 前端
入口：
- `Client/src/components/UserSettings.tsx`
- `Client/src/components/UserSettings.css`

行为要点：
- 列出已绑定平台，展示地址脱敏
- 新增/更新：平台选择 + 地址 + (可选)密钥
- 平台仅允许一个绑定
