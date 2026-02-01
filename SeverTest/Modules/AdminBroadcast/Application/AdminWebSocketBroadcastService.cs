using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.Accounts.Infrastructure;
using ServerTest.Options;
using ServerTest.Protocol;
using ServerTest.WebSockets;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

namespace ServerTest.Modules.AdminBroadcast.Application
{
    /// <summary>
    /// 管理员 WebSocket 实时推送服务
    /// </summary>
    public sealed class AdminWebSocketBroadcastService
    {
        private readonly IConnectionManager _connectionManager;
        private readonly AccountRepository _accountRepository;
        private readonly BusinessRulesOptions _businessRules;
        private readonly ILogger<AdminWebSocketBroadcastService> _logger;
        private readonly ConcurrentDictionary<string, bool> _adminCache = new(StringComparer.Ordinal);
        private DateTime _lastCacheRefresh = DateTime.UtcNow;
        private readonly TimeSpan _cacheRefreshInterval = TimeSpan.FromMinutes(1);

        public AdminWebSocketBroadcastService(
            IConnectionManager connectionManager,
            AccountRepository accountRepository,
            IOptions<BusinessRulesOptions> businessRules,
            ILogger<AdminWebSocketBroadcastService> logger)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _accountRepository = accountRepository ?? throw new ArgumentNullException(nameof(accountRepository));
            _businessRules = businessRules?.Value ?? new BusinessRulesOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 推送连接统计信息给所有管理员
        /// </summary>
        public async Task BroadcastConnectionStatsAsync(CancellationToken ct = default)
        {
            try
            {
                var allConnections = _connectionManager.GetAllConnections();
                var totalConnections = allConnections.Count;
                var uniqueUsers = allConnections.Select(c => c.UserId).Distinct().Count();

                var connectionsBySystem = allConnections
                    .GroupBy(c => c.System)
                    .ToDictionary(g => g.Key, g => g.Count());

                var data = new
                {
                    totalConnections,
                    totalUsers = uniqueUsers,
                    connectionsBySystem,
                };

                var envelope = ProtocolEnvelopeFactory.Ok<object>("admin.connection.stats", null, data);
                await BroadcastToAdminsAsync(envelope, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "推送连接统计失败");
            }
        }

        /// <summary>
        /// 推送在线用户列表给所有管理员
        /// </summary>
        public async Task BroadcastOnlineUsersAsync(CancellationToken ct = default)
        {
            try
            {
                var allConnections = _connectionManager.GetAllConnections();
                var users = allConnections.Select(c => new
                {
                    userId = c.UserId,
                    system = c.System,
                    connectionId = c.ConnectionId.ToString(),
                    connectedAt = c.ConnectedAt.ToString("O"),
                    remoteIp = c.RemoteIp,
                }).ToList();

                var data = new { users };
                var envelope = ProtocolEnvelopeFactory.Ok<object>("admin.connection.users", null, data);
                await BroadcastToAdminsAsync(envelope, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "推送在线用户列表失败");
            }
        }

        /// <summary>
        /// 推送日志消息给所有管理员
        /// </summary>
        public async Task BroadcastLogAsync(string level, string message, string category, CancellationToken ct = default)
        {
            try
            {
                var data = new
                {
                    level,
                    message,
                    category,
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                };

                var envelope = ProtocolEnvelopeFactory.Ok<object>("admin.log", null, data);
                await BroadcastToAdminsAsync(envelope, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "推送日志消息失败");
            }
        }

        private async Task BroadcastToAdminsAsync(ProtocolEnvelope<object> envelope, CancellationToken ct)
        {
            await RefreshAdminCacheIfNeededAsync(ct).ConfigureAwait(false);

            var allConnections = _connectionManager.GetAllConnections();
            var adminConnections = allConnections.Where(c => IsAdmin(c.UserId)).ToList();

            if (adminConnections.Count == 0)
            {
                return;
            }

            var json = ProtocolJson.Serialize(envelope);
            var bytes = Encoding.UTF8.GetBytes(json);

            var tasks = adminConnections.Select(connection => SendAsync(connection, bytes, ct));
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private bool IsAdmin(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return false;
            }

            return _adminCache.TryGetValue(userId, out var isAdmin) && isAdmin;
        }

        private async Task RefreshAdminCacheIfNeededAsync(CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            if (now - _lastCacheRefresh < _cacheRefreshInterval)
            {
                return;
            }

            _lastCacheRefresh = now;

            try
            {
                var allConnections = _connectionManager.GetAllConnections();
                var userIds = allConnections.Select(c => c.UserId).Distinct().ToList();

                // 清理离线用户的缓存
                var onlineUserIds = new HashSet<string>(userIds, StringComparer.Ordinal);
                var keysToRemove = _adminCache.Keys.Where(k => !onlineUserIds.Contains(k)).ToList();
                foreach (var key in keysToRemove)
                {
                    _adminCache.TryRemove(key, out _);
                }

                // 检查在线用户是否是管理员
                foreach (var userId in userIds)
                {
                    if (_adminCache.ContainsKey(userId))
                    {
                        continue;
                    }

                    if (long.TryParse(userId, out var uid))
                    {
                        var role = await _accountRepository.GetRoleAsync((ulong)uid, null, ct).ConfigureAwait(false);
                        var isAdmin = role.HasValue && role.Value == _businessRules.SuperAdminRole;
                        _adminCache[userId] = isAdmin;
                    }
                    else
                    {
                        _adminCache[userId] = false;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "刷新管理员缓存失败");
            }
        }

        private async Task SendAsync(WebSocketConnection connection, byte[] bytes, CancellationToken ct)
        {
            try
            {
                if (connection.Socket.State != WebSocketState.Open)
                {
                    return;
                }

                await connection.Socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "发送消息到管理员失败: {UserId}", connection.UserId);
            }
        }
    }
}
