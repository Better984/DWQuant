# SQL

本目录用于存放监控模块相关的 SQL 脚本，统一按模块管理。

## 文件说明
- `protocol_performance_metrics.sql`：协议性能监控表。用于合并记录以下信息：
  - 服务端 HTTP / WebSocket 处理耗时
  - 前端 HTTP / WebSocket 总往返耗时
  - 协议错误码、HTTP 状态码、超时与错误信息
  - 按 `reqId + transport` 合并的前后端关联数据

## 使用建议
- 首次部署前先确认 `Monitoring.EnableProtocolPerformanceStorage` 是否需要开启；默认关闭时不会自动入库。
- 需要启用协议性能监控时，再执行建表脚本或直接启动已开启开关的服务完成自动建表。
- 后续如需扩展统计字段，优先增量 `ALTER TABLE`，避免整表重建。
- 分析慢协议时，建议优先查看 `client_elapsed_ms`、`server_elapsed_ms` 和 `client_network_overhead_ms` 三个字段的组合关系。
