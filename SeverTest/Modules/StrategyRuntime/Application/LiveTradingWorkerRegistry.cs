using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Logging;
using ServerTest.Modules.StrategyRuntime.Domain;
using ServerTest.Protocol;

namespace ServerTest.Modules.StrategyRuntime.Application
{
    /// <summary>
    /// 核心节点中的实盘节点连接注册中心。
    /// </summary>
    public sealed class LiveTradingWorkerRegistry
    {
        private sealed class SessionHolder
        {
            public required LiveTradingWorkerSession Session { get; init; }

            public SemaphoreSlim SendLock { get; } = new(1, 1);
        }

        private readonly ConcurrentDictionary<string, SessionHolder> _sessions = new(StringComparer.OrdinalIgnoreCase);
        private readonly ILogger<LiveTradingWorkerRegistry> _logger;

        public LiveTradingWorkerRegistry(ILogger<LiveTradingWorkerRegistry> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public LiveTradingWorkerSession Register(string workerId, WebSocket socket, string remoteIp)
        {
            if (string.IsNullOrWhiteSpace(workerId))
            {
                throw new ArgumentException("workerId 不能为空", nameof(workerId));
            }

            var session = new LiveTradingWorkerSession
            {
                WorkerId = workerId.Trim(),
                Socket = socket ?? throw new ArgumentNullException(nameof(socket)),
                RemoteIp = remoteIp ?? string.Empty,
                ConnectedAtUtc = DateTime.UtcNow,
            };
            _sessions[session.WorkerId] = new SessionHolder { Session = session };
            _logger.LogInformation("实盘节点已注册: workerId={WorkerId} remoteIp={RemoteIp}", session.WorkerId, session.RemoteIp);
            return session;
        }

        public void Unregister(string workerId)
        {
            if (string.IsNullOrWhiteSpace(workerId))
            {
                return;
            }

            if (_sessions.TryRemove(workerId.Trim(), out var holder))
            {
                _logger.LogWarning("实盘节点已下线: workerId={WorkerId}", holder.Session.WorkerId);
            }
        }

        public IReadOnlyList<LiveTradingWorkerSession> GetOnlineSessions()
        {
            var now = DateTime.UtcNow;
            var result = new List<LiveTradingWorkerSession>();

            foreach (var holder in _sessions.Values)
            {
                var session = holder.Session;
                if (!session.IsOnline)
                {
                    continue;
                }

                if ((now - session.LastHeartbeatUtc).TotalSeconds > 60)
                {
                    continue;
                }

                result.Add(session);
            }

            return result.OrderBy(s => s.ConnectedAtUtc).ToList();
        }

        public async Task<bool> SendAsync(string workerId, ProtocolEnvelope<object> envelope, CancellationToken ct)
        {
            if (!_sessions.TryGetValue(workerId, out var holder))
            {
                return false;
            }

            var socket = holder.Session.Socket;
            if (socket.State != WebSocketState.Open)
            {
                return false;
            }

            var json = ProtocolJson.Serialize(envelope);
            var payload = Encoding.UTF8.GetBytes(json);

            await holder.SendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (socket.State != WebSocketState.Open)
                {
                    return false;
                }

                await socket.SendAsync(payload, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "向实盘节点发送消息失败: workerId={WorkerId} type={Type}", workerId, envelope.Type);
                return false;
            }
            finally
            {
                holder.SendLock.Release();
            }
        }
    }
}
