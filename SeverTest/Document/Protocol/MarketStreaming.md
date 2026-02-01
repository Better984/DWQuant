# MarketStreaming 模块协议

## HTTP

### market.price.list
- 路径：`POST /api/price/all`
- data：无
- 响应 data：`Dictionary<string, PriceData>`

### market.price.get
- 路径：`POST /api/price/get`
- data：
  - `exchange` string
- 响应 data：`PriceData`

---

## WebSocket

### market.subscribe
- data：
  - `symbols` string[]
- 响应 type：`market.subscribe.ack`

### market.unsubscribe
- data：
  - `symbols` string[]
- 响应 type：`market.unsubscribe.ack`

### mkt.tick（服务端推送）
- data：`[symbol, price, ts][]`
- reqId：无
