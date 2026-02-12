using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Logging;
using ServerTest.Modules.Backtest.Domain;
using ServerTest.Protocol;

namespace ServerTest.Modules.Backtest.Application
{
    /// <summary>
    /// 核心节点中的算力节点连接注册中心。
    /// </summary>
    public sealed class BacktestWorkerRegistry
    {
        private sealed class SessionHolder
        {
            public required BacktestWorkerSession Session { get; init; }

            public SemaphoreSlim SendLock { get; } = new(1, 1);
        }

        private readonly ConcurrentDictionary<string, SessionHolder> _sessions = new(StringComparer.OrdinalIgnoreCase);
        private readonly ILogger<BacktestWorkerRegistry> _logger;

        public BacktestWorkerRegistry(ILogger<BacktestWorkerRegistry> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public BacktestWorkerSession Register(string workerId, WebSocket socket, string remoteIp)
        {
            if (string.IsNullOrWhiteSpace(workerId))
            {
                throw new ArgumentException("workerId 不能为空", nameof(workerId));
            }

            var session = new BacktestWorkerSession
            {
                WorkerId = workerId.Trim(),
                Socket = socket ?? throw new ArgumentNullException(nameof(socket)),
                RemoteIp = remoteIp ?? string.Empty,
                ConnectedAtUtc = DateTime.UtcNow,
            };
            _sessions[session.WorkerId] = new SessionHolder { Session = session };
            _logger.LogInformation("回测算力节点已注册: workerId={WorkerId} remoteIp={RemoteIp}", session.WorkerId, session.RemoteIp);
            return session;
        }

        public IReadOnlyList<BacktestWorkerTaskLease> Unregister(string workerId)
        {
            if (string.IsNullOrWhiteSpace(workerId))
            {
                return Array.Empty<BacktestWorkerTaskLease>();
            }

            if (!_sessions.TryRemove(workerId.Trim(), out var holder))
            {
                return Array.Empty<BacktestWorkerTaskLease>();
            }

            var leases = holder.Session.SnapshotLeases();
            _logger.LogWarning(
                "回测算力节点已下线: workerId={WorkerId} runningTasks={TaskCount}",
                holder.Session.WorkerId,
                leases.Count);

            return leases;
        }

        public bool TryGetSession(string workerId, out BacktestWorkerSession session)
        {
            session = null!;
            if (string.IsNullOrWhiteSpace(workerId))
            {
                return false;
            }

            if (!_sessions.TryGetValue(workerId.Trim(), out var holder))
            {
                return false;
            }

            session = holder.Session;
            return true;
        }

        public IReadOnlyList<BacktestWorkerSession> GetAvailableSessions()
        {
            var now = DateTime.UtcNow;
            var result = new List<BacktestWorkerSession>();

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

                if (session.RunningTaskCount >= session.MaxParallelTasks)
                {
                    continue;
                }

                result.Add(session);
            }

            return result.OrderBy(s => s.RunningTaskCount).ThenByDescending(s => s.CpuCores).ToList();
        }

        public IReadOnlyList<BacktestWorkerSession> GetAllSessions()
        {
            return _sessions.Values.Select(v => v.Session).ToList();
        }

        public bool TryAssignTask(string workerId, BacktestWorkerTaskLease lease)
        {
            if (!_sessions.TryGetValue(workerId, out var holder))
            {
                return false;
            }

            return holder.Session.TryAssignTask(lease);
        }

        public void CompleteTask(string workerId, long taskId)
        {
            if (!_sessions.TryGetValue(workerId, out var holder))
            {
                return;
            }

            holder.Session.CompleteTask(taskId);
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
                _logger.LogWarning(ex, "向算力节点发送消息失败: workerId={WorkerId} type={Type}", workerId, envelope.Type);
                return false;
            }
            finally
            {
                holder.SendLock.Release();
            }
        }
    }
}
