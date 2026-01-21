using System.Net.WebSockets;
using System.Text;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Options;
using ServerTest.RateLimit;

namespace ServerTest.WebSockets
{
    public class WebSocketHandler
    {
        private readonly ILogger<WebSocketHandler> _logger;
        private readonly IRateLimiter _rateLimiter;
        private readonly IConnectionManager _connectionManager;
        private readonly WebSocketOptions _options;
        private readonly Dictionary<string, Func<WebSocketConnection, WsMessageEnvelope, CancellationToken, Task<WsMessageEnvelope>>> _routes;

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

            _routes = new Dictionary<string, Func<WebSocketConnection, WsMessageEnvelope, CancellationToken, Task<WsMessageEnvelope>>>(StringComparer.OrdinalIgnoreCase);
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

            var kickMessage = new
            {
                type = "kicked",
                reason,
                ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            try
            {
                var json = JsonConvert.SerializeObject(kickMessage);
                var bytes = Encoding.UTF8.GetBytes(json);
                await connection.Socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send kicked message for {UserId}", connection.UserId);
            }

            try
            {
                await connection.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to close kicked connection for {UserId}", connection.UserId);
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

                if (readResult.TooLarge)
                {
                    await SendAsync(connection, WsMessageEnvelope.Error(null, "message_too_large", "Message exceeds max size"), cancellationToken);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(readResult.Text))
                {
                    await SendAsync(connection, WsMessageEnvelope.Error(null, "bad_request", "Empty message"), cancellationToken);
                    continue;
                }

                WsMessageEnvelope? envelope;
                try
                {
                    envelope = JsonConvert.DeserializeObject<WsMessageEnvelope>(readResult.Text);
                }
                catch (JsonException)
                {
                    await SendAsync(connection, WsMessageEnvelope.Error(null, "bad_request", "Invalid JSON"), cancellationToken);
                    continue;
                }

                if (envelope == null || string.IsNullOrWhiteSpace(envelope.Type))
                {
                    await SendAsync(connection, WsMessageEnvelope.Error(envelope?.ReqId, "bad_request", "Missing type"), cancellationToken);
                    continue;
                }

                if (string.Equals(envelope.Type, "ping", StringComparison.OrdinalIgnoreCase))
                {
                    var pong = WsMessageEnvelope.Create("pong", envelope.ReqId, null, null);
                    await SendAsync(connection, pong, cancellationToken);
                    continue;
                }

                if (!_rateLimiter.Allow(connection.UserId, Protocol.Ws))
                {
                    _logger.LogWarning("WS rate limit hit for user {UserId}", connection.UserId);
                    await SendAsync(connection, WsMessageEnvelope.Error(envelope.ReqId, "rate_limit", "WebSocket rate limit exceeded"), cancellationToken);
                    continue;
                }

                if (!_routes.TryGetValue(envelope.Type, out var handler))
                {
                    await SendAsync(connection, WsMessageEnvelope.Error(envelope.ReqId, "unknown_type", "Unknown message type"), cancellationToken);
                    continue;
                }

                try
                {
                    var response = await handler(connection, envelope, cancellationToken);
                    response.ReqId = envelope.ReqId;
                    if (response.Ts == 0)
                    {
                        response.Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    }

                    await SendAsync(connection, response, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "WS handler failed for type {Type}", envelope.Type);
                    await SendAsync(connection, WsMessageEnvelope.Error(envelope.ReqId, "internal_error", "Handler error"), cancellationToken);
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

        private async Task SendAsync(WebSocketConnection connection, WsMessageEnvelope envelope, CancellationToken cancellationToken)
        {
            if (connection.Socket.State != WebSocketState.Open)
            {
                return;
            }

            var json = JsonConvert.SerializeObject(envelope);
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
