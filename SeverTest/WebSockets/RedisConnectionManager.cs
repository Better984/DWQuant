using Microsoft.Extensions.Options;
using ServerTest.Options;
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

        private readonly IDatabase _db;
        private readonly ConcurrentDictionary<Guid, WebSocketConnection> _localConnections = new();
        private readonly WebSocketOptions _options;
        private const int KeyTtlSeconds = 86400;

        public RedisConnectionManager(IConnectionMultiplexer redis, IOptions<WebSocketOptions> options)
        {
            _db = redis.GetDatabase();
            _options = options.Value;
        }

        public bool TryReserve(string userId, string system, Guid connectionId)
        {
            var key = BuildKey(userId, system);
            var result = (int)_db.ScriptEvaluate(
                ScriptReserve,
                new RedisKey[] { key },
                new RedisValue[] { connectionId.ToString(), _options.MaxConnectionsPerSystem, KeyTtlSeconds });
            return result == 1;
        }

        public void RegisterLocal(WebSocketConnection connection)
        {
            _localConnections[connection.ConnectionId] = connection;
        }

        public void Remove(string userId, string system, Guid connectionId)
        {
            var key = BuildKey(userId, system);
            _db.ScriptEvaluate(ScriptRemove, new RedisKey[] { key }, new RedisValue[] { connectionId.ToString() });
            _localConnections.TryRemove(connectionId, out _);
        }

        public void ClearUserSystem(string userId, string system)
        {
            var key = BuildKey(userId, system);
            _db.KeyDelete(key);

            foreach (var connection in _localConnections.Values)
            {
                if (string.Equals(connection.UserId, userId, StringComparison.Ordinal)
                    && string.Equals(connection.System, system, StringComparison.OrdinalIgnoreCase))
                {
                    _localConnections.TryRemove(connection.ConnectionId, out _);
                }
            }
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
    }
}
