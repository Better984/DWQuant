using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using ServerTest.Options;

namespace ServerTest.WebSockets
{
    public class InMemoryConnectionManager : IConnectionManager
    {
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, WebSocketConnection>> _connections = new();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, byte>> _reservations = new();
        private readonly WebSocketOptions _options;

        public InMemoryConnectionManager(IOptions<WebSocketOptions> options)
        {
            _options = options.Value;
        }

        public bool TryReserve(string userId, string system, Guid connectionId)
        {
            var key = BuildKey(userId, system);
            var connections = _connections.GetOrAdd(key, _ => new ConcurrentDictionary<Guid, WebSocketConnection>());
            var reservations = _reservations.GetOrAdd(key, _ => new ConcurrentDictionary<Guid, byte>());

            if (connections.Count + reservations.Count >= _options.MaxConnectionsPerSystem)
            {
                return false;
            }

            reservations.TryAdd(connectionId, 1);
            return true;
        }

        public void RegisterLocal(WebSocketConnection connection)
        {
            var key = BuildKey(connection.UserId, connection.System);
            var bucket = _connections.GetOrAdd(key, _ => new ConcurrentDictionary<Guid, WebSocketConnection>());
            bucket[connection.ConnectionId] = connection;

            if (_reservations.TryGetValue(key, out var reservations))
            {
                reservations.TryRemove(connection.ConnectionId, out _);
                if (reservations.IsEmpty)
                {
                    _reservations.TryRemove(key, out _);
                }
            }
        }

        public void Remove(string userId, string system, Guid connectionId)
        {
            var key = BuildKey(userId, system);
            if (_connections.TryGetValue(key, out var bucket))
            {
                bucket.TryRemove(connectionId, out _);
                if (bucket.IsEmpty)
                {
                    _connections.TryRemove(key, out _);
                }
            }

            if (_reservations.TryGetValue(key, out var reservations))
            {
                reservations.TryRemove(connectionId, out _);
                if (reservations.IsEmpty)
                {
                    _reservations.TryRemove(key, out _);
                }
            }
        }

        public void ClearUserSystem(string userId, string system)
        {
            var key = BuildKey(userId, system);
            _connections.TryRemove(key, out _);
            _reservations.TryRemove(key, out _);
        }

        public IReadOnlyList<WebSocketConnection> GetConnections(string userId)
        {
            return _connections.Values
                .SelectMany(bucket => bucket.Values)
                .Where(c => c.UserId == userId)
                .ToList();
        }

        public IReadOnlyList<WebSocketConnection> GetAllConnections()
        {
            return _connections.Values
                .SelectMany(bucket => bucket.Values)
                .ToList();
        }

        private static string BuildKey(string userId, string system)
        {
            return $"{userId}|{system.ToLowerInvariant()}";
        }
    }
}
