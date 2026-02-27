using System.Net;
using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.StrategyRuntime.Domain;
using ServerTest.Options;
using ServerTest.Protocol;
using ServerTest.Services;

namespace ServerTest.Modules.StrategyRuntime.Application
{
    /// <summary>
    /// 核心节点实盘工作端接入网关：处理 /ws/live-worker 连接与消息分发。
    /// </summary>
    public sealed class LiveTradingWorkerGateway
    {
        private readonly LiveTradingWorkerRegistry _registry;
        private readonly LiveTradingWorkerDispatchService _dispatchService;
        private readonly LiveTradingWorkerOptions _options;
        private readonly ServerRoleRuntime _roleRuntime;
        private readonly ILogger<LiveTradingWorkerGateway> _logger;

        public LiveTradingWorkerGateway(
            LiveTradingWorkerRegistry registry,
            LiveTradingWorkerDispatchService dispatchService,
            IOptions<LiveTradingWorkerOptions> options,
            ServerRoleRuntime roleRuntime,
            ILogger<LiveTradingWorkerGateway> logger)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _dispatchService = dispatchService ?? throw new ArgumentNullException(nameof(dispatchService));
            _options = options?.Value ?? new LiveTradingWorkerOptions();
            _roleRuntime = roleRuntime ?? throw new ArgumentNullException(nameof(roleRuntime));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task HandleAsync(HttpContext context)
        {
            if (!_roleRuntime.IsCoreLike)
            {
                await WriteErrorAsync(context, StatusCodes.Status403Forbidden, ProtocolErrorCodes.Forbidden, "当前节点未启用实盘节点接入功能")
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
                _registry.Unregister(workerId);
                if (socket.State == WebSocketState.Open)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        private async Task ReceiveLoopAsync(LiveTradingWorkerSession session, CancellationToken ct)
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

                    case LiveTradingWorkerMessageTypes.Register:
                        var register = ProtocolJson.DeserializePayload<LiveTradingWorkerRegisterRequest>(envelope.Data);
                        register ??= new LiveTradingWorkerRegisterRequest();
                        register.WorkerId = string.IsNullOrWhiteSpace(register.WorkerId) ? session.WorkerId : register.WorkerId;
                        session.UpdateRegistration(register);
                        _logger.LogInformation(
                            "实盘节点注册信息更新: workerId={WorkerId} version={Version} tags={Tags}",
                            session.WorkerId,
                            session.Version ?? string.Empty,
                            string.Join(",", session.Tags));

                        await _dispatchService.SyncRunnableStrategiesToWorkerAsync(session.WorkerId, ct).ConfigureAwait(false);
                        break;

                    case LiveTradingWorkerMessageTypes.Heartbeat:
                        session.TouchHeartbeat();
                        break;

                    case LiveTradingWorkerMessageTypes.CommandAck:
                        session.TouchHeartbeat();
                        var ack = ProtocolJson.DeserializePayload<LiveTradingWorkerCommandAck>(envelope.Data);
                        if (ack != null)
                        {
                            if (ack.Success)
                            {
                                _logger.LogInformation(
                                    "实盘节点指令执行成功: workerId={WorkerId} action={Action} usId={UsId} commandId={CommandId}",
                                    session.WorkerId,
                                    ack.Action,
                                    ack.UsId,
                                    ack.CommandId);
                            }
                            else
                            {
                                _logger.LogWarning(
                                    "实盘节点指令执行失败: workerId={WorkerId} action={Action} usId={UsId} commandId={CommandId} error={Error}",
                                    session.WorkerId,
                                    ack.Action,
                                    ack.UsId,
                                    ack.CommandId,
                                    ack.ErrorMessage ?? string.Empty);
                            }
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
                // 未配置密钥时仅允许本机调试连接。
                var remoteIp = context.Connection.RemoteIpAddress;
                if (remoteIp != null && !IPAddress.IsLoopback(remoteIp))
                {
                    errorMessage = "live worker access key is required";
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
