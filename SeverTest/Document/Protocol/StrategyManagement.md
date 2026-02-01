# StrategyManagement 模块协议

> 说明：请求 data 使用 `ServerTest.Models.Strategy` 下的请求结构体。

## 基础

### strategy.create
- 路径：`POST /api/strategy/create`
- data：`StrategyCreateRequest`

### strategy.update
- 路径：`POST /api/strategy/update`
- data：`StrategyUpdateRequest`

### strategy.delete
- 路径：`POST /api/strategy/delete`
- data：`StrategyDeleteRequest`

### strategy.list
- 路径：`POST /api/strategy/list`
- data：无

### strategy.versions
- 路径：`POST /api/strategy/versions`
- data：`{ usId }`

---

## 官方/模板/广场

### strategy.official.list
- 路径：`POST /api/strategy/official/list`
- data：无

### strategy.official.versions
- 路径：`POST /api/strategy/official/versions`
- data：`{ defId }`

### strategy.template.list
- 路径：`POST /api/strategy/template/list`
- data：无

### strategy.market.list
- 路径：`POST /api/strategy/market/list`
- data：无

### strategy.market.publish
- 路径：`POST /api/strategy/market/publish`
- data：`StrategyMarketPublishRequest`

### strategy.market.sync
- 路径：`POST /api/strategy/market/sync`
- data：`StrategyMarketPublishRequest`

---

## 发布/同步/移除

### strategy.publish
- 路径：`POST /api/strategy/publish`
- data：`StrategyPublishRequest`

### strategy.official.publish
- 路径：`POST /api/strategy/publish/official`
- data：`StrategyCatalogPublishRequest`

### strategy.template.publish
- 路径：`POST /api/strategy/publish/template`
- data：`StrategyCatalogPublishRequest`

### strategy.official.sync
- 路径：`POST /api/strategy/official/sync`
- data：`StrategyCatalogPublishRequest`

### strategy.template.sync
- 路径：`POST /api/strategy/template/sync`
- data：`StrategyCatalogPublishRequest`

### strategy.official.remove
- 路径：`POST /api/strategy/official/remove`
- data：`StrategyCatalogPublishRequest`

### strategy.template.remove
- 路径：`POST /api/strategy/template/remove`
- data：`StrategyCatalogPublishRequest`

---

## 分享

### strategy.share.create
- 路径：`POST /api/strategy/share/create-code`
- data：`StrategyShareCreateRequest`

### strategy.share.import
- 路径：`POST /api/strategy/import/share-code`
- data：`StrategyImportShareCodeRequest`

---

## 实例状态

### strategy.instance.state.update
- 路径：`POST /api/strategy/instances/state`
- data：
  - `id` long
  - `state` string
  - `exchangeApiKeyId` long?
