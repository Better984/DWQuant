using Microsoft.Extensions.Options;
using ServerTest.Options;
using ServerTest.Protocol;
using StackExchange.Redis;
using System.Collections.Concurrent;

namespace ServerTest.WebSockets
{
    public class RedisConnectionManager : IConnectionManager
    {
        private const string ScriptReserve = @"
local size = redis.call('SCARD', KEYS[1])
local limit = tonumber(ARGV[2])
if size >= limit then
  return 0
end
redis.call('SADD', KEYS[1], ARGV[1])
redis.call('EXPIRE', KEYS[1], tonumber(ARGV[3]))
return 1
";

        private const string ScriptRemove = @"
redis.call('SREM', KEYS[1], ARGV[1])
if redis.call('SCARD', KEYS[1]) == 0 then
  redis.call('DEL', KEYS[1])
end
return 1
";

        private const int DefaultKeyTtlSeconds = 86400;
        private const int DefaultRefreshSeconds = 43200;
        private readonly IDatabase _db;
        private readonly ConcurrentDictionary<Guid, WebSocketConnection> _localConnections = new();
        private readonly ConcurrentDictionary<string, DateTimeOffset> _lastRefresh = new(StringComparer.Ordinal);
        private readonly WebSocketOptions _options;
        private readonly WebSocketNodeId _nodeId;

        public RedisConnectionManager(IConnectionMultiplexer redis, IOptions<WebSocketOptions> options, WebSocketNodeId nodeId)
        {
            _db = redis.GetDatabase();
            _options = options.Value;
            _nodeId = nodeId;
        }

        public bool TryReserve(string userId, string system, Guid connectionId)
        {
            var key = BuildKey(userId, system);
            var result = (int)_db.ScriptEvaluate(
                ScriptReserve,
                new RedisKey[] { key },
                new RedisValue[] { connectionId.ToString(), _options.MaxConnectionsPerSystem, GetKeyTtlSeconds() });
            return result == 1;
        }

        public void RegisterLocal(WebSocketConnection connection)
        {
            _localConnections[connection.ConnectionId] = connection;
            Refresh(connection.UserId, connection.System);
        }

        public void Refresh(string userId, string system)
        {
            var key = BuildKey(userId, system);
            var now = DateTimeOffset.UtcNow;
            var refreshSeconds = GetRefreshSeconds();

            if (_lastRefresh.TryGetValue(key, out var last)
                && (now - last).TotalSeconds < refreshSeconds)
            {
                return;
            }

            _lastRefresh[key] = now;
            // 防止频繁写 Redis，按刷新间隔延长 TTL
            _db.KeyExpire(key, TimeSpan.FromSeconds(GetKeyTtlSeconds()));
        }

        public void Remove(string userId, string system, Guid connectionId)
        {
            var key = BuildKey(userId, system);
            _db.ScriptEvaluate(ScriptRemove, new RedisKey[] { key }, new RedisValue[] { connectionId.ToString() });
            _localConnections.TryRemove(connectionId, out _);
            _lastRefresh.TryRemove(key, out _);
        }

        public void ClearUserSystem(string userId, string system)
        {
            var key = BuildKey(userId, system);
            _db.KeyDelete(key);
            _lastRefresh.TryRemove(key, out _);

            foreach (var connection in _localConnections.Values)
            {
                if (string.Equals(connection.UserId, userId, StringComparison.Ordinal)
                    && string.Equals(connection.System, system, StringComparison.OrdinalIgnoreCase))
                {
                    _localConnections.TryRemove(connection.ConnectionId, out _);
                }
            }
        }

        public void BroadcastKick(string userId, string system, string reason)
        {
            var message = new RedisKickMessage
            {
                NodeId = _nodeId.Value,
                UserId = userId,
                System = system,
                Reason = reason,
                Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var payload = ProtocolJson.Serialize(message);
            var channel = RedisChannel.Literal(RedisWebSocketChannels.KickChannel);
            _db.Multiplexer.GetSubscriber().Publish(channel, payload);
        }

        public IReadOnlyList<WebSocketConnection> GetConnections(string userId)
        {
            return _localConnections.Values.Where(c => c.UserId == userId).ToList();
        }

        public IReadOnlyList<WebSocketConnection> GetAllConnections()
        {
            return _localConnections.Values.ToList();
        }

        private static string BuildKey(string userId, string system)
        {
            return $"ws:conn:{userId}:{system.ToLowerInvariant()}";
        }

        private int GetKeyTtlSeconds()
        {
            return _options.ConnectionKeyTtlSeconds > 0
                ? _options.ConnectionKeyTtlSeconds
                : DefaultKeyTtlSeconds;
        }

        private int GetRefreshSeconds()
        {
            return _options.ConnectionKeyRefreshSeconds > 0
                ? _options.ConnectionKeyRefreshSeconds
                : DefaultRefreshSeconds;
        }
    }
}
