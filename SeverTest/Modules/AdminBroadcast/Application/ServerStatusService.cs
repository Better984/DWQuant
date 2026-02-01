using Microsoft.Extensions.Logging;
using ServerTest.Protocol;
using ServerTest.WebSockets;
using StackExchange.Redis;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;

namespace ServerTest.Modules.AdminBroadcast.Application
{
    public sealed class ServerStatusService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly WebSocketNodeId _nodeId;
        private readonly IConnectionManager _connectionManager;
        private readonly ILogger<ServerStatusService> _logger;
        private readonly ConcurrentDictionary<string, ServerNodeInfo> _nodeCache = new(StringComparer.Ordinal);

        public ServerStatusService(
            IConnectionMultiplexer redis,
            WebSocketNodeId nodeId,
            IConnectionManager connectionManager,
            ILogger<ServerStatusService> logger)
        {
            _redis = redis ?? throw new ArgumentNullException(nameof(redis));
            _nodeId = nodeId ?? throw new ArgumentNullException(nameof(nodeId));
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IReadOnlyList<ServerNodeInfo>> GetAllServersAsync(CancellationToken ct = default)
        {
            var servers = new List<ServerNodeInfo>();
            var db = _redis.GetDatabase();
            var server = _redis.GetServer(_redis.GetEndPoints().First());

            // 更新当前节点心跳
            await UpdateNodeHeartbeatAsync(db).ConfigureAwait(false);

            // 扫描 Redis 中所有节点心跳键
            var nodeKeys = new List<RedisKey>();
            await foreach (var key in server.KeysAsync(pattern: "server:node:*", pageSize: 1000).WithCancellation(ct))
            {
                nodeKeys.Add(key);
            }

            // 获取当前节点的连接信息
            var currentNodeId = _nodeId.Value;
            var currentConnections = _connectionManager.GetAllConnections();
            var currentSystems = currentConnections.Select(c => c.System).Distinct().ToList();

            // 构建当前节点信息
            var currentNodeInfo = await BuildServerNodeInfoAsync(
                currentNodeId,
                currentConnections.Count,
                currentSystems,
                true,
                ct).ConfigureAwait(false);
            servers.Add(currentNodeInfo);

            // 从Redis中获取其他节点信息
            foreach (var key in nodeKeys)
            {
                var nodeId = ExtractNodeIdFromKey(key.ToString());
                if (string.IsNullOrEmpty(nodeId) || string.Equals(nodeId, currentNodeId, StringComparison.Ordinal))
                {
                    continue;
                }

                var nodeData = await db.StringGetAsync(key).ConfigureAwait(false);
                if (nodeData.HasValue)
                {
                    try
                    {
                        var heartbeat = ProtocolJson.Deserialize<NodeHeartbeat>(nodeData.ToString());
                        if (heartbeat != null)
                        {
                            var nodeInfo = await BuildServerNodeInfoAsync(
                                nodeId,
                                heartbeat.ConnectionCount,
                                heartbeat.Systems ?? Array.Empty<string>(),
                                false,
                                ct).ConfigureAwait(false);
                            nodeInfo.LastHeartbeat = DateTimeOffset.FromUnixTimeMilliseconds(heartbeat.Timestamp);
                            servers.Add(nodeInfo);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "解析节点心跳数据失败: NodeId={NodeId}", nodeId);
                    }
                }
            }

            // 按节点ID排序
            return servers.OrderBy(s => s.NodeId).ToList();
        }

        private async Task UpdateNodeHeartbeatAsync(IDatabase db)
        {
            var currentNodeId = _nodeId.Value;
            var connections = _connectionManager.GetAllConnections();
            var systems = connections.Select(c => c.System).Distinct().ToList();

            var heartbeat = new NodeHeartbeat
            {
                NodeId = currentNodeId,
                MachineName = Environment.MachineName,
                ConnectionCount = connections.Count,
                Systems = systems,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };

            var key = $"server:node:{currentNodeId}";
            var value = ProtocolJson.Serialize(heartbeat);
            await db.StringSetAsync(key, value, TimeSpan.FromMinutes(5)).ConfigureAwait(false);
        }

        private async Task<ServerNodeInfo> BuildServerNodeInfoAsync(
            string nodeId,
            int connectionCount,
            IReadOnlyList<string> systems,
            bool isCurrentNode,
            CancellationToken ct)
        {
            var machineName = ExtractMachineName(nodeId);
            var nodeInfo = new ServerNodeInfo
            {
                NodeId = nodeId,
                MachineName = machineName,
                IsCurrentNode = isCurrentNode,
                ConnectionCount = connectionCount,
                Systems = systems.Distinct().ToList(),
                LastHeartbeat = DateTimeOffset.UtcNow,
            };

            if (isCurrentNode)
            {
                // 获取当前节点的详细信息
                nodeInfo.Status = "Online";
                nodeInfo.ProcessInfo = GetCurrentProcessInfo();
                nodeInfo.SystemInfo = GetSystemInfo();
                nodeInfo.RuntimeInfo = GetRuntimeInfo();
            }
            else
            {
                // 其他节点根据心跳时间判断状态
                var timeSinceHeartbeat = DateTimeOffset.UtcNow - nodeInfo.LastHeartbeat;
                if (timeSinceHeartbeat.TotalMinutes < 5)
                {
                    nodeInfo.Status = "Online";
                }
                else if (timeSinceHeartbeat.TotalMinutes < 10)
                {
                    nodeInfo.Status = "Warning";
                }
                else
                {
                    nodeInfo.Status = "Offline";
                }
            }

            return nodeInfo;
        }

        private static string? ExtractNodeIdFromKey(string key)
        {
            // server:node:{nodeId}
            var parts = key.Split(':');
            return parts.Length >= 3 ? parts[2] : null;
        }

        private static string ExtractMachineName(string nodeId)
        {
            // 节点ID格式：MachineName-Guid
            var parts = nodeId.Split('-');
            return parts.Length > 0 ? parts[0] : nodeId;
        }

        private ProcessInfo GetCurrentProcessInfo()
        {
            var process = Process.GetCurrentProcess();
            return new ProcessInfo
            {
                ProcessId = process.Id,
                ProcessName = process.ProcessName,
                StartTime = process.StartTime,
                CpuUsage = GetCpuUsage(process),
                MemoryUsage = process.WorkingSet64,
                ThreadCount = process.Threads.Count,
            };
        }

        private SystemInfo GetSystemInfo()
        {
            return new SystemInfo
            {
                MachineName = Environment.MachineName,
                OsVersion = Environment.OSVersion.ToString(),
                ProcessorCount = Environment.ProcessorCount,
                TotalMemory = GC.GetTotalMemory(false),
                Is64BitProcess = Environment.Is64BitProcess,
                Is64BitOperatingSystem = Environment.Is64BitOperatingSystem,
            };
        }

        private RuntimeInfo GetRuntimeInfo()
        {
            return new RuntimeInfo
            {
                DotNetVersion = Environment.Version.ToString(),
                FrameworkDescription = RuntimeInformation.FrameworkDescription,
                ClrVersion = Environment.Version.ToString(),
            };
        }

        private double GetCpuUsage(Process process)
        {
            try
            {
                var startTime = DateTime.UtcNow;
                var startCpuUsage = process.TotalProcessorTime;
                Thread.Sleep(100);
                var endTime = DateTime.UtcNow;
                var endCpuUsage = process.TotalProcessorTime;
                var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
                var totalMsPassed = (endTime - startTime).TotalMilliseconds;
                var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);
                return cpuUsageTotal * 100;
            }
            catch
            {
                return 0;
            }
        }
    }

    public sealed class ServerNodeInfo
    {
        public string NodeId { get; set; } = string.Empty;
        public string MachineName { get; set; } = string.Empty;
        public bool IsCurrentNode { get; set; }
        public string Status { get; set; } = "Unknown";
        public int ConnectionCount { get; set; }
        public IReadOnlyList<string> Systems { get; set; } = Array.Empty<string>();
        public DateTimeOffset LastHeartbeat { get; set; }
        public ProcessInfo? ProcessInfo { get; set; }
        public SystemInfo? SystemInfo { get; set; }
        public RuntimeInfo? RuntimeInfo { get; set; }
    }

    public sealed class ProcessInfo
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public double CpuUsage { get; set; }
        public long MemoryUsage { get; set; }
        public int ThreadCount { get; set; }
    }

    public sealed class SystemInfo
    {
        public string MachineName { get; set; } = string.Empty;
        public string OsVersion { get; set; } = string.Empty;
        public int ProcessorCount { get; set; }
        public long TotalMemory { get; set; }
        public bool Is64BitProcess { get; set; }
        public bool Is64BitOperatingSystem { get; set; }
    }

    public sealed class RuntimeInfo
    {
        public string DotNetVersion { get; set; } = string.Empty;
        public string FrameworkDescription { get; set; } = string.Empty;
        public string ClrVersion { get; set; } = string.Empty;
    }

    internal sealed class NodeHeartbeat
    {
        public string NodeId { get; set; } = string.Empty;
        public string MachineName { get; set; } = string.Empty;
        public int ConnectionCount { get; set; }
        public IReadOnlyList<string>? Systems { get; set; }
        public long Timestamp { get; set; }
    }
}
