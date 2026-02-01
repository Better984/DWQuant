# Notifications 模块协议

## 通知列表

### notification.list
- 路径：`POST /api/notifications/list`
- data：
  - `limit` int?
  - `cursor` long?
  - `unreadOnly` bool?
- 响应 data：`NotificationInboxPageDto`

### notification.unread.count
- 路径：`POST /api/notifications/unread-count`
- data：无
- 响应 data：`{ unreadCount }`

### notification.read
- 路径：`POST /api/notifications/read`
- data：
  - `notificationId` long
- 响应 data：`null`

---

## 通知渠道

### notify_channel.list
- 路径：`POST /api/usernotifychannels/list`
- data：无
- 响应 data：`UserNotifyChannelDto[]`

### notify_channel.upsert
- 路径：`POST /api/usernotifychannels/upsert`
- data：
  - `platform` string
  - `address` string
  - `secret` string?
  - `isEnabled` bool?
  - `isDefault` bool?
- 响应 data：`null`

### notify_channel.delete
- 路径：`POST /api/usernotifychannels/delete`
- data：
  - `platform` string
- 响应 data：`null`

---

## 通知偏好

### notification.preference.get
- 路径：`POST /api/user/notification-preference/get`
- data：无
- 响应 data：`NotificationPreferenceDto`

### notification.preference.update
- 路径：`POST /api/user/notification-preference/update`
- data：
  - `rules` Dictionary<string, NotificationPreferenceRuleDto>
- 响应 data：`null`
