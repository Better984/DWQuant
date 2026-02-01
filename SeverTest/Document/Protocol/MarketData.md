# MarketData 模块协议

### marketdata.kline.latest
- 路径：`POST /api/marketdata/latest`
- data：
  - `exchange` string（枚举名）
  - `timeframe` string（枚举名）
  - `symbol` string（枚举名）
- 响应 data：`MarketKlineDto`

### marketdata.kline.history
- 路径：`POST /api/marketdata/history`
- data：
  - `exchange` string（枚举名）
  - `timeframe` string（枚举名）
  - `symbol` string（枚举名）
  - `startTime` string?（yyyy-MM-dd HH:mm:ss）
  - `endTime` string?（yyyy-MM-dd HH:mm:ss）
  - `count` int
- 响应 data：`MarketKlineDto[]`
