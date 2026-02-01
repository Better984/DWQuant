# D:\\UGit\\DWQuant 开发约定

## 项目概述
- C# .NET 8.0 后端 + React 前端的 C 端量化项目，支持用户创建/托管运行币圈交易所策略。

## 文档位置与更新要求
- 主文档目录：`D:\UGit\DWQuant\SeverTest\Document\`。
- 模块功能文档：`D:\UGit\DWQuant\SeverTest\Modules\<模块名>\功能说明.md`。
- 模块 SQL 说明：`D:\UGit\DWQuant\SeverTest\Modules\<模块名>\Sql\README.md`。
- 变更模块功能或联动关系时，必须同步更新对应模块的 `功能说明.md`，必要时更新 `SeverTest\Document\项目结构说明.md`。

## 服务器开发流程（必须遵循）
1. 需求确认：明确接口/消息类型、错误码、字段含义与联动模块。
2. 方案设计：先更新/新增文档，再实现代码。
3. 实现代码：遵循分层（Controller → Application → Infrastructure/Domain）。
4. 变更同步：更新模块功能文档、SQL 文档、配置说明与关键流程文档。
5. 自检：关注日志、错误码、异常处理、性能与并发安全。

## 文档同步硬性要求
- 任何代码改动后，必须同步更新相关文档（含模块 `功能说明.md`、`Document/` 中的流程/结构说明等）。

## 代码风格与规范
- 编码：UTF-8（建议无 BOM），中文日志/注释必须清晰可读。
- 协议与响应：HTTP/WS 统一使用 `ProtocolJson` 与 `ProtocolEnvelope/ProtocolRequest`；错误码使用 `ProtocolErrorCodes`。
- 分层原则：Controller 只做参数校验与调用编排；业务逻辑进 Application；数据访问进 Infrastructure。
- 日志：日志与注释使用中文，包含关键上下文（uid、system、reqId 等）。
- 异步：I/O 必须 async，传递 `CancellationToken`，避免阻塞等待。
- 依赖注入：统一在 `Program.cs` 注册；新增 Handler/Service 必须注入。
- WebSocket：新增消息类型需实现 `IWsMessageHandler` 并注册；保持 type 命名与 ack 规则一致。
- JSON 库：禁止新增混用，保持 `System.Text.Json` + `ProtocolJson` 统一序列化。
