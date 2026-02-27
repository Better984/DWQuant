该目录用于存放本模块的 SQL 脚本，请按功能接口或功能点拆分文件。

## 运行时间模板

- 表：`strategy_runtime_template`
- 用途：存储运行时间模板与日历异常（JSON 字段）。
- 关键字段：`template_id`、`name`、`timezone`、`days_json`、`time_ranges_json`、`calendar_json`。

## 策略链路追踪日志

- 表：`strategy_task_trace_log`
- 用途：记录行情任务分发、策略引擎认领与执行、动作入队、交易动作消费的全链路路径与阶段耗时。
- 关键字段：`trace_id`、`parent_trace_id`、`event_stage`、`event_status`、`actor_instance`、`duration_ms`、`metrics_json`。
- 脚本：`20260225_strategy_task_trace_log.md`

## 策略任务主记录

- 表：`strategy_engine_run_log`
- 用途：按“单任务单行”沉淀任务主画像，优先用于管理端市场任务报告与最近任务详情。
- 关键字段：`trace_id`、`run_status`、`lookup_ms/indicator_ms/execute_ms`、`executed_strategy_ids`、`open_task_strategy_ids`、`open_task_trace_ids`、`open_order_ids`、`engine_instance`。
