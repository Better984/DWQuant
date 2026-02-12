using System.Net.WebSockets;
using System.Text.Json.Serialization;

namespace ServerTest.Modules.Backtest.Domain
{
    public static class BacktestWorkerMessageTypes
    {
        public const string Register = "worker.register";
        public const string Heartbeat = "worker.heartbeat";
        public const string Execute = "backtest.worker.execute";
        public const string Progress = "backtest.worker.progress";
        public const string Result = "backtest.worker.result";
    }

    public sealed class BacktestWorkerRegisterRequest
    {
        public string WorkerId { get; set; } = string.Empty;

        public int CpuCores { get; set; } = Environment.ProcessorCount;

        public long MemoryMb { get; set; }

        public string? Version { get; set; }

        public int MaxParallelTasks { get; set; } = 1;

        public string[] Tags { get; set; } = Array.Empty<string>();
    }

    public sealed class BacktestWorkerHeartbeat
    {
        public string WorkerId { get; set; } = string.Empty;

        public long Ts { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        public int RunningTasks { get; set; }
    }

    public sealed class BacktestWorkerExecuteRequest
    {
        public long TaskId { get; set; }

        public long UserId { get; set; }

        public string? ReqId { get; set; }

        public BacktestRunRequest Request { get; set; } = new();
    }

    public sealed class BacktestWorkerProgressReport
    {
        public long TaskId { get; set; }

        public long UserId { get; set; }

        public string? ReqId { get; set; }

        public string Stage { get; set; } = string.Empty;

        public string StageName { get; set; } = string.Empty;

        public string? Message { get; set; }

        public decimal? Progress { get; set; }

        public long? ElapsedMs { get; set; }

        public bool? Completed { get; set; }
    }

    public sealed class BacktestWorkerResultReport
    {
        public long TaskId { get; set; }

        public long UserId { get; set; }

        public string? ReqId { get; set; }

        public bool Success { get; set; }

        public string? ErrorMessage { get; set; }

        [JsonIgnore]
        public BacktestRunResult? Result { get; set; }

        public string? ResultJson { get; set; }

        public long DurationMs { get; set; }
    }

    public sealed class BacktestWorkerTaskLease
    {
        public long TaskId { get; set; }

        public long UserId { get; set; }

        public string? ReqId { get; set; }

        public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;
    }

    public sealed class BacktestWorkerSession
    {
        private readonly object _sync = new();
        private readonly Dictionary<long, BacktestWorkerTaskLease> _runningTasks = new();

        public required string WorkerId { get; init; }

        public required WebSocket Socket { get; init; }

        public required string RemoteIp { get; init; }

        public DateTime ConnectedAtUtc { get; init; } = DateTime.UtcNow;

        public DateTime LastHeartbeatUtc { get; private set; } = DateTime.UtcNow;

        public int CpuCores { get; private set; } = Environment.ProcessorCount;

        public long MemoryMb { get; private set; }

        public string? Version { get; private set; }

        public string[] Tags { get; private set; } = Array.Empty<string>();

        public int MaxParallelTasks { get; private set; } = 1;

        public bool IsOnline => Socket.State == WebSocketState.Open;

        public int RunningTaskCount
        {
            get
            {
                lock (_sync)
                {
                    return _runningTasks.Count;
                }
            }
        }

        public void UpdateRegistration(BacktestWorkerRegisterRequest request)
        {
            if (request == null)
            {
                return;
            }

            lock (_sync)
            {
                CpuCores = request.CpuCores > 0 ? request.CpuCores : Environment.ProcessorCount;
                MemoryMb = Math.Max(0, request.MemoryMb);
                Version = request.Version;
                Tags = request.Tags ?? Array.Empty<string>();
                MaxParallelTasks = Math.Max(1, request.MaxParallelTasks);
                LastHeartbeatUtc = DateTime.UtcNow;
            }
        }

        public void TouchHeartbeat()
        {
            lock (_sync)
            {
                LastHeartbeatUtc = DateTime.UtcNow;
            }
        }

        public bool TryAssignTask(BacktestWorkerTaskLease lease)
        {
            if (lease == null)
            {
                return false;
            }

            lock (_sync)
            {
                if (_runningTasks.ContainsKey(lease.TaskId))
                {
                    return true;
                }

                if (_runningTasks.Count >= MaxParallelTasks)
                {
                    return false;
                }

                _runningTasks[lease.TaskId] = lease;
                LastHeartbeatUtc = DateTime.UtcNow;
                return true;
            }
        }

        public bool CompleteTask(long taskId)
        {
            lock (_sync)
            {
                var removed = _runningTasks.Remove(taskId);
                LastHeartbeatUtc = DateTime.UtcNow;
                return removed;
            }
        }

        public IReadOnlyList<BacktestWorkerTaskLease> SnapshotLeases()
        {
            lock (_sync)
            {
                return _runningTasks.Values.ToList();
            }
        }
    }
}
