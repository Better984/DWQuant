using System.Net.WebSockets;
using System.Text;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.Backtest.Domain;
using ServerTest.Options;
using ServerTest.Protocol;
using ServerTest.Services;

namespace ServerTest.Modules.Backtest.Application
{
    /// <summary>
    /// 核心节点算力接入网关：处理 /ws/worker 连接与消息分发。
    /// </summary>
    public sealed class BacktestWorkerGateway
    {
        private readonly BacktestWorkerRegistry _registry;
        private readonly BacktestWorkerMessageService _messageService;
        private readonly BacktestWorkerOptions _options;
        private readonly ServerRoleRuntime _roleRuntime;
        private readonly ILogger<BacktestWorkerGateway> _logger;

        public BacktestWorkerGateway(
            BacktestWorkerRegistry registry,
            BacktestWorkerMessageService messageService,
            IOptions<BacktestWorkerOptions> options,
            ServerRoleRuntime roleRuntime,
            ILogger<BacktestWorkerGateway> logger)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
            _options = options?.Value ?? new BacktestWorkerOptions();
            _roleRuntime = roleRuntime ?? throw new ArgumentNullException(nameof(roleRuntime));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task HandleAsync(HttpContext context)
        {
            if (!_roleRuntime.IsCoreLike)
            {
                await WriteErrorAsync(context, StatusCodes.Status403Forbidden, ProtocolErrorCodes.Forbidden, "当前节点未启用算力接入功能")
                    .ConfigureAwait(false);
                return;
            }

            if (!context.WebSockets.IsWebSocketRequest)
            {
                await WriteErrorAsync(context, StatusCodes.Status400BadRequest, ProtocolErrorCodes.InvalidRequest, "WebSocket request required")
                    .ConfigureAwait(false);
                return;
            }

            if (!ValidateAccessKey(context, out var errorMessage))
            {
                await WriteErrorAsync(context, StatusCodes.Status401Unauthorized, ProtocolErrorCodes.Unauthorized, errorMessage)
                    .ConfigureAwait(false);
                return;
            }

            var workerId = ResolveWorkerId(context);
            var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
            using var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
            var session = _registry.Register(workerId, socket, remoteIp);

            try
            {
                await ReceiveLoopAsync(session, context.RequestAborted).ConfigureAwait(false);
            }
            finally
            {
                var leases = _registry.Unregister(workerId);
                await _messageService.RequeueLostTasksAsync(workerId, leases, context.RequestAborted).ConfigureAwait(false);
                if (socket.State == WebSocketState.Open)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        private async Task ReceiveLoopAsync(BacktestWorkerSession session, CancellationToken ct)
        {
            var socket = session.Socket;
            var buffer = new byte[16 * 1024];

            while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var text = await ReadTextMessageAsync(socket, buffer, ct).ConfigureAwait(false);
                if (text == null)
                {
                    break;
                }

                var envelope = ProtocolJson.Deserialize<ProtocolEnvelope<object>>(text);
                if (envelope == null || string.IsNullOrWhiteSpace(envelope.Type))
                {
                    continue;
                }

                switch (envelope.Type.Trim().ToLowerInvariant())
                {
                    case "ping":
                        await _registry.SendAsync(
                            session.WorkerId,
                            ProtocolEnvelopeFactory.Ok<object>("pong", envelope.ReqId, null),
                            ct).ConfigureAwait(false);
                        break;

                    case BacktestWorkerMessageTypes.Register:
                        var register = ProtocolJson.DeserializePayload<BacktestWorkerRegisterRequest>(envelope.Data);
                        register ??= new BacktestWorkerRegisterRequest();
                        register.WorkerId = string.IsNullOrWhiteSpace(register.WorkerId) ? session.WorkerId : register.WorkerId;
                        session.UpdateRegistration(register);
                        _logger.LogInformation(
                            "算力节点注册信息更新: workerId={WorkerId} cores={Cores} memMb={Memory} maxParallel={Parallel}",
                            session.WorkerId,
                            session.CpuCores,
                            session.MemoryMb,
                            session.MaxParallelTasks);
                        break;

                    case BacktestWorkerMessageTypes.Heartbeat:
                        session.TouchHeartbeat();
                        break;

                    case BacktestWorkerMessageTypes.Progress:
                        session.TouchHeartbeat();
                        var progress = ProtocolJson.DeserializePayload<BacktestWorkerProgressReport>(envelope.Data);
                        if (progress != null)
                        {
                            await _messageService.HandleProgressAsync(session.WorkerId, progress, ct).ConfigureAwait(false);
                        }
                        break;

                    case BacktestWorkerMessageTypes.Result:
                        session.TouchHeartbeat();
                        var result = ProtocolJson.DeserializePayload<BacktestWorkerResultReport>(envelope.Data);
                        if (result != null)
                        {
                            await _messageService.HandleResultAsync(session.WorkerId, result, ct).ConfigureAwait(false);
                        }
                        break;
                }
            }
        }

        private bool ValidateAccessKey(HttpContext context, out string errorMessage)
        {
            errorMessage = "invalid access key";
            if (string.IsNullOrWhiteSpace(_options.AccessKey))
            {
                // 未配置密钥时仅本机调试可连
                var remoteIp = context.Connection.RemoteIpAddress;
                if (remoteIp != null && !IPAddress.IsLoopback(remoteIp))
                {
                    errorMessage = "worker access key is required";
                    return false;
                }

                return true;
            }

            var incoming = context.Request.Query["workerKey"].ToString();
            if (string.IsNullOrWhiteSpace(incoming))
            {
                incoming = context.Request.Headers["X-Worker-Key"].ToString();
            }

            return string.Equals(incoming, _options.AccessKey, StringComparison.Ordinal);
        }

        private static string ResolveWorkerId(HttpContext context)
        {
            var workerId = context.Request.Query["workerId"].ToString();
            if (!string.IsNullOrWhiteSpace(workerId))
            {
                return workerId.Trim();
            }

            return $"{context.Connection.RemoteIpAddress}-{Guid.NewGuid():N}";
        }

        private static async Task<string?> ReadTextMessageAsync(WebSocket socket, byte[] buffer, CancellationToken ct)
        {
            using var ms = new MemoryStream();
            while (true)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    if (result.EndOfMessage)
                    {
                        return null;
                    }

                    continue;
                }

                ms.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    break;
                }
            }

            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private static async Task WriteErrorAsync(HttpContext context, int statusCode, int code, string message)
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            var payload = ProtocolEnvelopeFactory.Error(null, code, message, null, context.TraceIdentifier);
            await context.Response.WriteAsync(ProtocolJson.Serialize(payload)).ConfigureAwait(false);
        }
    }
}
