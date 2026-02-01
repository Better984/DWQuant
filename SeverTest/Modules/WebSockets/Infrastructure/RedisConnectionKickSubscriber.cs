using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServerTest.Protocol;
using StackExchange.Redis;

namespace ServerTest.WebSockets
{
    public sealed class RedisConnectionKickSubscriber : BackgroundService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly IConnectionManager _connectionManager;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RedisConnectionKickSubscriber> _logger;
        private readonly WebSocketNodeId _nodeId;

        public RedisConnectionKickSubscriber(
            IConnectionMultiplexer redis,
            IConnectionManager connectionManager,
            IServiceScopeFactory scopeFactory,
            ILogger<RedisConnectionKickSubscriber> logger,
            WebSocketNodeId nodeId)
        {
            _redis = redis ?? throw new ArgumentNullException(nameof(redis));
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _nodeId = nodeId ?? throw new ArgumentNullException(nameof(nodeId));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var subscriber = _redis.GetSubscriber();
            var channel = RedisChannel.Literal(RedisWebSocketChannels.KickChannel);
            subscriber.Subscribe(channel, OnKickMessage);

            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 服务停止
            }
            finally
            {
                subscriber.Unsubscribe(channel);
            }
        }

        private async Task HandleMessageAsync(RedisValue value)
        {
            if (value.IsNullOrEmpty)
            {
                return;
            }

            RedisKickMessage? message;
            try
            {
                message = ProtocolJson.Deserialize<RedisKickMessage>(value.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "分布式踢下线消息解析失败");
                return;
            }

            if (message == null
                || string.IsNullOrWhiteSpace(message.UserId)
                || string.IsNullOrWhiteSpace(message.System))
            {
                return;
            }

            if (string.Equals(message.NodeId, _nodeId.Value, StringComparison.Ordinal))
            {
                return;
            }

            var connections = _connectionManager.GetConnections(message.UserId)
                .Where(c => string.Equals(c.System, message.System, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (connections.Count == 0)
            {
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<WebSocketHandler>();

            foreach (var connection in connections)
            {
                try
                {
                    await handler.KickAsync(connection, message.Reason, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "分布式踢下线失败: {UserId}", connection.UserId);
                }
                finally
                {
                    _connectionManager.Remove(connection.UserId, connection.System, connection.ConnectionId);
                }
            }
        }

        private void OnKickMessage(RedisChannel channel, RedisValue value)
        {
            _ = HandleMessageAsync(value);
        }
    }
}
