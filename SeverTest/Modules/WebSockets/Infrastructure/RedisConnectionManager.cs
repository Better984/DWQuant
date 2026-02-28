using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Options;
using ServerTest.Protocol;
using StackExchange.Redis;
using System.Collections.Concurrent;

namespace ServerTest.WebSockets
{
    public class RedisConnectionManager : IConnectionManager
    {
        /// <summary>
        /// Redis 集合成员格式：nodeId|connectionId，用于按节点识别并清理陈旧槽位。
        /// </summary>
        private const char MemberSeparator = '|';
        private const string ConnectionKeyPattern = "ws:conn:*";

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

        /// <summary>
        /// 针对单个 key 清理当前节点槽位（可选清理旧格式槽位）。
        /// </summary>
        private const string ScriptClearStaleMembersForKey = @"
local prefix = ARGV[1]
local clearLegacy = ARGV[2] == '1'
local removed = 0
local members = redis.call('SMEMBERS', KEYS[1])
for _, m in ipairs(members) do
  local s = tostring(m)
  local isOurs = string.sub(s, 1, #prefix) == prefix
  local isLegacy = not string.find(s, '|', 1, true)
  if isOurs or (clearLegacy and isLegacy) then
    redis.call('SREM', KEYS[1], m)
    removed = removed + 1
  end
end
if redis.call('SCARD', KEYS[1]) == 0 then
  redis.call('DEL', KEYS[1])
end
return removed
";

        private const int DefaultKeyTtlSeconds = 86400;
        private const int DefaultRefreshSeconds = 43200;

        private readonly IConnectionMultiplexer _redis;
        private readonly IDatabase _db;
        private readonly ConcurrentDictionary<Guid, WebSocketConnection> _localConnections = new();
        private readonly ConcurrentDictionary<Guid, string> _localReservations = new();
        private readonly ConcurrentDictionary<string, DateTimeOffset> _lastRefresh = new(StringComparer.Ordinal);
        private readonly WebSocketOptions _options;
        private readonly WebSocketNodeId _nodeId;
        private readonly ILogger<RedisConnectionManager>? _logger;

        public RedisConnectionManager(
            IConnectionMultiplexer redis,
            IOptions<WebSocketOptions> options,
            WebSocketNodeId nodeId,
            ILogger<RedisConnectionManager>? logger = null)
        {
            _redis = redis ?? throw new ArgumentNullException(nameof(redis));
            _db = redis.GetDatabase();
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _nodeId = nodeId ?? throw new ArgumentNullException(nameof(nodeId));
            _logger = logger;
        }

        public bool TryReserve(string userId, string system, Guid connectionId)
        {
            var key = BuildKey(userId, system);
            var member = FormatMember(connectionId);
            var result = (int)_db.ScriptEvaluate(
                ScriptReserve,
                new RedisKey[] { key },
                new RedisValue[] { member, _options.MaxConnectionsPerSystem, GetKeyTtlSeconds() });
            if (result == 1)
            {
                _localReservations[connectionId] = key;
            }

            return result == 1;
        }

        public void RegisterLocal(WebSocketConnection connection)
        {
            _localConnections[connection.ConnectionId] = connection;
            _localReservations.TryRemove(connection.ConnectionId, out _);
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
            var member = FormatMember(connectionId);
            _db.ScriptEvaluate(ScriptRemove, new RedisKey[] { key }, new RedisValue[] { member });
            _localConnections.TryRemove(connectionId, out _);
            _localReservations.TryRemove(connectionId, out _);
            _lastRefresh.TryRemove(key, out _);
        }

        public void ClearStaleEntriesForCurrentNode()
        {
            var nodePrefix = GetNodeMemberPrefix();
            var clearLegacy = _options.ClearAllConnectionsOnStartup;
            long scanned = 0;
            long removed = 0;

            try
            {
                foreach (var key in EnumerateConnectionKeys())
                {
                    scanned++;
                    removed += ClearStaleMembersByScript(key, nodePrefix, clearLegacy);
                }

                if (scanned > 0)
                {
                    _logger?.LogInformation(
                        "WebSocket 启动清理完成：扫描 {Scanned} 个连接键，清除 {Removed} 个陈旧槽位（ClearAll={ClearAll}）",
                        scanned,
                        removed,
                        _options.ClearAllConnectionsOnStartup);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "WebSocket 启动清理失败，陈旧槽位可能仍存在");
            }
        }

        public long ClearStaleEntriesForCurrentNode(string userId, string system)
        {
            var key = BuildKey(userId, system);
            var nodePrefix = GetNodeMemberPrefix();
            var clearLegacy = _options.ClearAllConnectionsOnStartup;

            try
            {
                var removed = ClearStaleMembersForUserSystemKey(key, userId, system, nodePrefix, clearLegacy);
                if (removed > 0)
                {
                    _logger?.LogInformation(
                        "WebSocket 握手兜底清理：user={UserId} system={System} 清除 {Removed} 个当前节点陈旧槽位（ClearLegacy={ClearLegacy}）",
                        userId,
                        system,
                        removed,
                        clearLegacy);
                }

                return removed;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(
                    ex,
                    "WebSocket 握手兜底清理失败：user={UserId} system={System}",
                    userId,
                    system);
                return 0;
            }
        }

        private long ClearStaleMembersForUserSystemKey(
            string key,
            string userId,
            string system,
            string nodePrefix,
            bool clearLegacy)
        {
            var members = _db.SetMembers(key);
            if (members.Length == 0)
            {
                return 0;
            }

            var protectedLocalIds = GetProtectedLocalConnectionIds(userId, system, key);
            var toRemove = new List<RedisValue>(members.Length);

            foreach (var member in members)
            {
                var memberText = member.ToString();
                if (string.IsNullOrWhiteSpace(memberText))
                {
                    continue;
                }

                if (memberText.StartsWith(nodePrefix, StringComparison.Ordinal))
                {
                    var idText = memberText[nodePrefix.Length..];
                    if (Guid.TryParse(idText, out var connectionId)
                        && protectedLocalIds.Contains(connectionId))
                    {
                        continue;
                    }

                    toRemove.Add(member);
                    continue;
                }

                if (clearLegacy && IsLegacyMember(memberText))
                {
                    toRemove.Add(member);
                }
            }

            if (toRemove.Count == 0)
            {
                return 0;
            }

            var removed = _db.SetRemove(key, toRemove.ToArray());
            if (_db.SetLength(key) == 0)
            {
                _db.KeyDelete(key);
            }

            return removed;
        }

        private HashSet<Guid> GetProtectedLocalConnectionIds(string userId, string system, string key)
        {
            var ids = _localConnections.Values
                .Where(c => string.Equals(c.UserId, userId, StringComparison.Ordinal)
                    && string.Equals(c.System, system, StringComparison.OrdinalIgnoreCase))
                .Select(c => c.ConnectionId)
                .ToHashSet();

            foreach (var reservation in _localReservations)
            {
                if (string.Equals(reservation.Value, key, StringComparison.Ordinal))
                {
                    ids.Add(reservation.Key);
                }
            }

            return ids;
        }

        private long ClearStaleMembersByScript(RedisKey key, string nodePrefix, bool clearLegacy)
        {
            return (long)_db.ScriptEvaluate(
                ScriptClearStaleMembersForKey,
                new RedisKey[] { key },
                new RedisValue[] { nodePrefix, clearLegacy ? "1" : "0" });
        }

        private IEnumerable<RedisKey> EnumerateConnectionKeys()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var endpoints = _redis.GetEndPoints();
            foreach (var endpoint in endpoints)
            {
                IServer server;
                try
                {
                    server = _redis.GetServer(endpoint);
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "Redis 端点不可用，跳过连接键扫描: {Endpoint}", endpoint);
                    continue;
                }

                if (!server.IsConnected || server.IsReplica)
                {
                    continue;
                }

                IEnumerable<RedisKey> keys;
                try
                {
                    // 优先走 SCAN，避免使用 KEYS 对 Redis 造成阻塞。
                    keys = server.Keys(database: _db.Database, pattern: ConnectionKeyPattern, pageSize: 200);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "扫描 Redis 连接键失败: {Endpoint}", endpoint);
                    continue;
                }

                foreach (var key in keys)
                {
                    var keyText = key.ToString();
                    if (string.IsNullOrWhiteSpace(keyText))
                    {
                        continue;
                    }

                    if (seen.Add(keyText))
                    {
                        yield return keyText;
                    }
                }
            }
        }

        private string FormatMember(Guid connectionId) => _nodeId.Value + MemberSeparator + connectionId;

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

            foreach (var reservation in _localReservations)
            {
                if (string.Equals(reservation.Value, key, StringComparison.Ordinal))
                {
                    _localReservations.TryRemove(reservation.Key, out _);
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

        private string GetNodeMemberPrefix()
        {
            return _nodeId.Value + MemberSeparator;
        }

        private static bool IsLegacyMember(string member)
        {
            return member.IndexOf(MemberSeparator) < 0;
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
