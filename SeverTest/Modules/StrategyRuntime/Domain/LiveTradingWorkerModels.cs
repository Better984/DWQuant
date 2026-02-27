using System.Net.WebSockets;

namespace ServerTest.Modules.StrategyRuntime.Domain
{
    public static class LiveTradingWorkerMessageTypes
    {
        public const string Register = "live.worker.register";
        public const string Heartbeat = "live.worker.heartbeat";
        public const string Command = "live.worker.command";
        public const string CommandAck = "live.worker.command.ack";
    }

    public static class LiveTradingWorkerCommandActions
    {
        public const string Upsert = "upsert";
        public const string Remove = "remove";
    }

    public sealed class LiveTradingWorkerRegisterRequest
    {
        public string WorkerId { get; set; } = string.Empty;

        public string? Version { get; set; }

        public string[] Tags { get; set; } = Array.Empty<string>();
    }

    public sealed class LiveTradingWorkerHeartbeat
    {
        public string WorkerId { get; set; } = string.Empty;

        public long Ts { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        public int RegisteredStrategies { get; set; }
    }

    public sealed class LiveTradingWorkerCommand
    {
        public string CommandId { get; set; } = string.Empty;

        public string Action { get; set; } = string.Empty;

        public long UsId { get; set; }

        public string? Reason { get; set; }
    }

    public sealed class LiveTradingWorkerCommandAck
    {
        public string CommandId { get; set; } = string.Empty;

        public string Action { get; set; } = string.Empty;

        public long UsId { get; set; }

        public bool Success { get; set; }

        public string? ErrorMessage { get; set; }
    }

    public sealed class LiveTradingWorkerSession
    {
        private readonly object _sync = new();

        public required string WorkerId { get; init; }

        public required WebSocket Socket { get; init; }

        public required string RemoteIp { get; init; }

        public DateTime ConnectedAtUtc { get; init; } = DateTime.UtcNow;

        public DateTime LastHeartbeatUtc { get; private set; } = DateTime.UtcNow;

        public string? Version { get; private set; }

        public string[] Tags { get; private set; } = Array.Empty<string>();

        public bool IsOnline => Socket.State == WebSocketState.Open;

        public void UpdateRegistration(LiveTradingWorkerRegisterRequest request)
        {
            if (request == null)
            {
                return;
            }

            lock (_sync)
            {
                Version = request.Version;
                Tags = request.Tags ?? Array.Empty<string>();
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
    }
}
