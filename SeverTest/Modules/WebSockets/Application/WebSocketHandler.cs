using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Options;
using ServerTest.RateLimit;
using ServerTest.Protocol;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace ServerTest.WebSockets
{
    public class WebSocketHandler
    {
        private readonly ILogger<WebSocketHandler> _logger;
        private readonly IRateLimiter _rateLimiter;
        private readonly IConnectionManager _connectionManager;
        private readonly WebSocketOptions _options;
        private readonly Dictionary<string, Func<WebSocketConnection, ProtocolEnvelope<object>, CancellationToken, Task<ProtocolEnvelope<object>>>> _routes;

        public WebSocketHandler(
            ILogger<WebSocketHandler> logger,
            IRateLimiter rateLimiter,
            IConnectionManager connectionManager,
            IOptions<WebSocketOptions> options,
            IEnumerable<IWsMessageHandler> handlers)
        {
            _logger = logger;
            _rateLimiter = rateLimiter;
            _connectionManager = connectionManager;
            _options = options.Value;

            _routes = new Dictionary<string, Func<WebSocketConnection, ProtocolEnvelope<object>, CancellationToken, Task<ProtocolEnvelope<object>>>>(StringComparer.OrdinalIgnoreCase);
            foreach (var handler in handlers)
            {
                _routes[handler.Type] = handler.HandleAsync;
            }
        }

        public async Task HandleAsync(WebSocketConnection connection, CancellationToken cancellationToken)
        {
            try
            {
                await ReceiveLoopAsync(connection, cancellationToken);
            }
            finally
            {
                _connectionManager.Remove(connection.UserId, connection.System, connection.ConnectionId);
            }
        }

        public async Task KickAsync(WebSocketConnection connection, string reason, CancellationToken cancellationToken)
        {
            if (connection.Socket.State != WebSocketState.Open)
            {
                return;
            }

            var kickMessage = ProtocolEnvelopeFactory.Ok("kicked", null, new { reason }, "kicked");

            try
            {
                var json = ProtocolJson.Serialize(kickMessage);
                var bytes = Encoding.UTF8.GetBytes(json);
                await connection.Socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "发送踢下线消息失败: {UserId}", connection.UserId);
            }

            try
            {
                await connection.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "关闭踢下线连接失败: {UserId}", connection.UserId);
            }
        }

        private async Task ReceiveLoopAsync(WebSocketConnection connection, CancellationToken cancellationToken)
        {
            var socket = connection.Socket;

            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var readResult = await ReadMessageAsync(socket, cancellationToken);
                if (readResult.IsClose)
                {
                    break;
                }

                _connectionManager.Refresh(connection.UserId, connection.System);

                if (readResult.TooLarge)
                {
                    await SendAsync(connection, ProtocolEnvelopeFactory.Error(null, ProtocolErrorCodes.OutOfRange, "消息过大"), cancellationToken);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(readResult.Text))
                {
                    await SendAsync(connection, ProtocolEnvelopeFactory.Error(null, ProtocolErrorCodes.InvalidRequest, "消息为空"), cancellationToken);
                    continue;
                }

                ProtocolEnvelope<object>? envelope;
                try
                {
                    envelope = ProtocolJson.Deserialize<ProtocolEnvelope<object>>(readResult.Text);
                }
                catch (JsonException)
                {
                    await SendAsync(connection, ProtocolEnvelopeFactory.Error(null, ProtocolErrorCodes.InvalidRequest, "JSON 格式错误"), cancellationToken);
                    continue;
                }

                if (envelope == null)
                {
                    await SendAsync(connection, ProtocolEnvelopeFactory.Error(null, ProtocolErrorCodes.InvalidRequest, "请求解析失败"), cancellationToken);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(envelope.Type))
                {
                    await SendAsync(connection, ProtocolEnvelopeFactory.Error(envelope.ReqId, ProtocolErrorCodes.MissingField, "缺少协议类型"), cancellationToken);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(envelope.ReqId))
                {
                    await SendAsync(connection, ProtocolEnvelopeFactory.Error(envelope.ReqId, ProtocolErrorCodes.MissingField, "缺少 reqId"), cancellationToken);
                    continue;
                }

                if (envelope.Ts <= 0)
                {
                    await SendAsync(connection, ProtocolEnvelopeFactory.Error(envelope.ReqId, ProtocolErrorCodes.InvalidFormat, "缺少有效时间戳"), cancellationToken);
                    continue;
                }

                if (string.Equals(envelope.Type, "ping", StringComparison.OrdinalIgnoreCase))
                {
                    var pong = ProtocolEnvelopeFactory.Ok<object>("pong", envelope.ReqId, null);
                    await SendAsync(connection, pong, cancellationToken);
                    continue;
                }

                if (!_rateLimiter.Allow(connection.UserId, RateLimit.Protocol.Ws))
                {
                    _logger.LogWarning("WebSocket 触发限流: 用户 {UserId}", connection.UserId);
                    await SendAsync(connection, ProtocolEnvelopeFactory.Error(envelope.ReqId, ProtocolErrorCodes.RateLimited, "请求过于频繁"), cancellationToken);
                    continue;
                }

                if (!_routes.TryGetValue(envelope.Type, out var handler))
                {
                    await SendAsync(connection, ProtocolEnvelopeFactory.Error(envelope.ReqId, ProtocolErrorCodes.Unsupported, "未知协议类型"), cancellationToken);
                    continue;
                }

                try
                {
                    var response = await handler(connection, envelope, cancellationToken);
                    if (response == null)
                    {
                        await SendAsync(connection, ProtocolEnvelopeFactory.Error(envelope.ReqId, ProtocolErrorCodes.InternalError, "处理失败"), cancellationToken);
                        continue;
                    }

                    response.ReqId = envelope.ReqId;
                    if (response.Ts == 0)
                    {
                        response.Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    }
                    if (string.IsNullOrWhiteSpace(response.Type))
                    {
                        response.Type = ProtocolEnvelopeFactory.BuildAckType(envelope.Type);
                    }

                    await SendAsync(connection, response, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "WebSocket 处理失败: 类型 {Type}", envelope.Type);
                    await SendAsync(connection, ProtocolEnvelopeFactory.Error(envelope.ReqId, ProtocolErrorCodes.InternalError, "处理异常"), cancellationToken);
                }
            }

            if (socket.State == WebSocketState.Open)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", cancellationToken);
            }
        }

        private async Task<ReadResult> ReadMessageAsync(WebSocket socket, CancellationToken cancellationToken)
        {
            var buffer = new byte[8192];
            using var stream = new MemoryStream();
            var tooLarge = false;

            while (true)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return new ReadResult { IsClose = true };
                }

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    if (result.EndOfMessage)
                    {
                        return new ReadResult { Text = null };
                    }

                    continue;
                }

                if (!tooLarge)
                {
                    if (stream.Length + result.Count > _options.MaxMessageBytes)
                    {
                        tooLarge = true;
                    }
                    else
                    {
                        stream.Write(buffer, 0, result.Count);
                    }
                }

                if (result.EndOfMessage)
                {
                    break;
                }
            }

            if (tooLarge)
            {
                return new ReadResult { TooLarge = true };
            }

            var text = Encoding.UTF8.GetString(stream.ToArray());
            return new ReadResult { Text = text };
        }

        private async Task SendAsync(WebSocketConnection connection, ProtocolEnvelope<object> envelope, CancellationToken cancellationToken)
        {
            if (connection.Socket.State != WebSocketState.Open)
            {
                return;
            }

            var json = ProtocolJson.Serialize(envelope);
            var bytes = Encoding.UTF8.GetBytes(json);
            await connection.Socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
        }

        private sealed class ReadResult
        {
            public string? Text { get; set; }
            public bool TooLarge { get; set; }
            public bool IsClose { get; set; }
        }
    }
}
