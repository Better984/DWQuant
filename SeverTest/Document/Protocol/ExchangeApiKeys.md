# ExchangeApiKeys 模块协议

### exchange.api_key.list
- 路径：`POST /api/userexchangeapikeys/list`
- data：无
- 响应 data：`UserExchangeApiKeyDto[]`

### exchange.api_key.create
- 路径：`POST /api/userexchangeapikeys/create`
- data：
  - `exchangeType` string
  - `label` string
  - `apiKey` string
  - `apiSecret` string
  - `apiPassword` string?
- 响应 data：`null`

### exchange.api_key.delete
- 路径：`POST /api/userexchangeapikeys/delete`
- data：
  - `id` long
- 响应 data：`null`
