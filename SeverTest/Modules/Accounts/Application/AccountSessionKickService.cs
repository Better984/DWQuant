using Microsoft.Extensions.Logging;
using ServerTest.WebSockets;

namespace ServerTest.Modules.Accounts.Application
{
    /// <summary>
    /// 账户会话处理：用于登录替换时按 system 维度踢掉旧连接。
    /// </summary>
    public sealed class AccountSessionKickService
    {
        private readonly IConnectionManager _connectionManager;
        private readonly WebSocketHandler _webSocketHandler;
        private readonly ILogger<AccountSessionKickService> _logger;

        public AccountSessionKickService(
            IConnectionManager connectionManager,
            WebSocketHandler webSocketHandler,
            ILogger<AccountSessionKickService> logger)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _webSocketHandler = webSocketHandler ?? throw new ArgumentNullException(nameof(webSocketHandler));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 按 userId + system 维度踢掉旧连接，返回本节点踢掉的连接数。
        /// </summary>
        public async Task<int> KickByUserSystemAsync(string userId, string system, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return 0;
            }

            var normalizedSystem = AuthTokenService.NormalizeSystem(system);
            var kickedLocal = 0;

            // 先广播，确保分布式节点也能感知同 user/system 的替换登录。
            _connectionManager.BroadcastKick(userId, normalizedSystem, "replaced_by_login");

            var existing = _connectionManager.GetConnections(userId)
                .Where(c => string.Equals(c.System, normalizedSystem, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var old in existing)
            {
                try
                {
                    await _webSocketHandler.KickAsync(old, "replaced_by_login", cancellationToken).ConfigureAwait(false);
                    kickedLocal++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "登录替换会话时踢线失败: userId={UserId} system={System} connectionId={ConnectionId}",
                        userId,
                        normalizedSystem,
                        old.ConnectionId);
                }
                finally
                {
                    _connectionManager.Remove(old.UserId, old.System, old.ConnectionId);
                }
            }

            // 清理同 system 的占位 key，避免旧连接异常残留导致新连接握手被占位。
            _connectionManager.ClearUserSystem(userId, normalizedSystem);

            if (kickedLocal > 0)
            {
                _logger.LogInformation(
                    "登录替换会话完成: userId={UserId} system={System} kickedLocal={KickedLocal}",
                    userId,
                    normalizedSystem,
                    kickedLocal);
            }

            return kickedLocal;
        }
    }
}
