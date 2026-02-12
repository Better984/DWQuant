using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Infrastructure.Config;
using ServerTest.Modules.Backtest.Domain;
using ServerTest.Options;
using ServerTest.Protocol;
using ServerTest.Services;

namespace ServerTest.Modules.Backtest.Application
{
    /// <summary>
    /// 回测算力节点客户端：主动连接核心节点，接收并执行回测任务。
    /// </summary>
    public sealed class BacktestWorkerClientHostedService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly BacktestWorkerOptions _options;
        private readonly ServerConfigStore _configStore;
        private readonly ServerRoleRuntime _roleRuntime;
        private readonly ILogger<BacktestWorkerClientHostedService> _logger;
        private readonly IReadOnlyList<string> _coreWsBaseUrls;
        private int _nextEndpointSeed;

        public BacktestWorkerClientHostedService(
            IServiceProvider serviceProvider,
            IOptions<BacktestWorkerOptions> options,
            ServerConfigStore configStore,
            ServerRoleRuntime roleRuntime,
            ILogger<BacktestWorkerClientHostedService> logger)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _options = options?.Value ?? new BacktestWorkerOptions();
            _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
            _roleRuntime = roleRuntime ?? throw new ArgumentNullException(nameof(roleRuntime));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _coreWsBaseUrls = ParseCoreWsBaseUrls(_options, _logger);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_roleRuntime.IsBacktestWorker)
            {
                _logger.LogInformation("当前角色非回测算力节点，跳过 Worker 客户端: role={Role}", _roleRuntime.Role);
                return;
            }

            if (_coreWsBaseUrls.Count == 0)
            {
                _logger.LogWarning(
                    "BacktestWorker.CoreWsUrls/CoreWsUrl 未配置有效地址，回测算力节点无法连接核心节点");
                return;
            }

            _logger.LogInformation(
                "回测算力节点连接配置已加载: endpointCount={Count} endpoints={Endpoints}",
                _coreWsBaseUrls.Count,
                string.Join(" | ", _coreWsBaseUrls));

            var reconnectDelay = TimeSpan.FromSeconds(Math.Max(1, _options.ReconnectDelaySeconds));
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunClientSessionAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "回测算力客户端连接异常");
                }

                try
                {
                    await Task.Delay(reconnectDelay, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task RunClientSessionAsync(CancellationToken ct)
        {
            var workerId = ResolveWorkerId();
            var workerWsUrls = BuildWorkerWsUrls(workerId);
            if (workerWsUrls.Count == 0)
            {
                throw new InvalidOperationException("未找到可用核心节点地址");
            }

            var startIndex = Math.Abs(Interlocked.Increment(ref _nextEndpointSeed)) % workerWsUrls.Count;
            for (var offset = 0; offset < workerWsUrls.Count; offset++)
            {
                var index = (startIndex + offset) % workerWsUrls.Count;
                var wsUrl = workerWsUrls[index];
                using var socket = new ClientWebSocket();
                if (!string.IsNullOrWhiteSpace(_options.AccessKey))
                {
                    socket.Options.SetRequestHeader("X-Worker-Key", _options.AccessKey);
                }

                try
                {
                    _logger.LogInformation(
                        "回测算力节点开始连接核心节点: workerId={WorkerId} endpointIndex={Index} url={Url}",
                        workerId,
                        index,
                        MaskSensitiveUrl(wsUrl));

                    await socket.ConnectAsync(new Uri(wsUrl), ct).ConfigureAwait(false);
                    _logger.LogInformation(
                        "回测算力节点连接成功: workerId={WorkerId} endpointIndex={Index}",
                        workerId,
                        index);

                    var heartbeatTask = SendHeartbeatLoopAsync(socket, workerId, ct);
                    await SendRegisterAsync(socket, workerId, ct).ConfigureAwait(false);

                    try
                    {
                        await ReceiveLoopAsync(socket, workerId, ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        try
                        {
                            await heartbeatTask.ConfigureAwait(false);
                        }
                        catch
                        {
                            // 忽略心跳任务退出异常
                        }
                    }

                    return;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "连接核心节点失败，准备尝试下一个地址: workerId={WorkerId} endpointIndex={Index}",
                        workerId,
                        index);
                }
            }

            throw new InvalidOperationException("所有核心节点地址连接均失败");
        }

        private async Task SendRegisterAsync(ClientWebSocket socket, string workerId, CancellationToken ct)
        {
            await SendAsync(socket, ProtocolEnvelopeFactory.Ok<object>(
                BacktestWorkerMessageTypes.Register,
                null,
                new BacktestWorkerRegisterRequest
                {
                    WorkerId = workerId,
                    CpuCores = Environment.ProcessorCount,
                    MemoryMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024),
                    Version = typeof(BacktestWorkerClientHostedService).Assembly.GetName().Version?.ToString(),
                    MaxParallelTasks = Math.Max(1, _options.MaxParallelTasksPerWorker),
                    Tags = new[] { "backtest", "worker" }
                }),
                ct).ConfigureAwait(false);
        }

        private async Task ReceiveLoopAsync(ClientWebSocket socket, string workerId, CancellationToken ct)
        {
            var buffer = new byte[16 * 1024];
            while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var text = await ReadTextMessageAsync(socket, buffer, ct).ConfigureAwait(false);
                if (text == null)
                {
                    break;
                }

                var envelope = ProtocolJson.Deserialize<ProtocolEnvelope<object>>(text);
                if (envelope == null || string.IsNullOrWhiteSpace(envelope.Type))
                {
                    continue;
                }

                if (string.Equals(envelope.Type, "ping", StringComparison.OrdinalIgnoreCase))
                {
                    await SendAsync(socket, ProtocolEnvelopeFactory.Ok<object>("pong", envelope.ReqId, null), ct)
                        .ConfigureAwait(false);
                    continue;
                }

                if (!string.Equals(envelope.Type, BacktestWorkerMessageTypes.Execute, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var payload = ProtocolJson.DeserializePayload<BacktestWorkerExecuteRequest>(envelope.Data);
                if (payload == null || payload.TaskId <= 0 || payload.Request == null)
                {
                    continue;
                }

                await ExecuteTaskAsync(socket, workerId, payload, ct).ConfigureAwait(false);
            }
        }

        private async Task ExecuteTaskAsync(
            ClientWebSocket socket,
            string workerId,
            BacktestWorkerExecuteRequest payload,
            CancellationToken ct)
        {
            var taskWatch = System.Diagnostics.Stopwatch.StartNew();
            _logger.LogInformation(
                "算力节点开始执行任务: workerId={WorkerId} taskId={TaskId} userId={UserId}",
                workerId,
                payload.TaskId,
                payload.UserId);

            await SendAsync(socket, ProtocolEnvelopeFactory.Ok<object>(
                BacktestWorkerMessageTypes.Progress,
                payload.ReqId,
                new BacktestWorkerProgressReport
                {
                    TaskId = payload.TaskId,
                    UserId = payload.UserId,
                    ReqId = payload.ReqId,
                    Stage = "running",
                    StageName = "远端算力执行中",
                    Message = $"算力节点 {workerId} 已开始执行",
                    Progress = 0m,
                    Completed = false,
                }),
                ct).ConfigureAwait(false);

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<BacktestService>();
                var timeoutMinutes = Math.Max(1, _configStore.GetInt("Backtest:TaskTimeoutMinutes", 10));
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(timeoutMinutes));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                var result = await service.RunAsync(
                    payload.Request,
                    payload.ReqId,
                    payload.UserId,
                    payload.TaskId,
                    linkedCts.Token).ConfigureAwait(false);

                taskWatch.Stop();
                await SendAsync(socket, ProtocolEnvelopeFactory.Ok<object>(
                    BacktestWorkerMessageTypes.Result,
                    payload.ReqId,
                    new BacktestWorkerResultReport
                    {
                        TaskId = payload.TaskId,
                        UserId = payload.UserId,
                        ReqId = payload.ReqId,
                        Success = true,
                        Result = result,
                        ResultJson = ProtocolJson.Serialize(result),
                        DurationMs = result.DurationMs > 0 ? result.DurationMs : taskWatch.ElapsedMilliseconds,
                    }),
                    ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                taskWatch.Stop();
                _logger.LogWarning(
                    "算力节点执行任务超时: workerId={WorkerId} taskId={TaskId}",
                    workerId,
                    payload.TaskId);

                await SendAsync(socket, ProtocolEnvelopeFactory.Ok<object>(
                    BacktestWorkerMessageTypes.Result,
                    payload.ReqId,
                    new BacktestWorkerResultReport
                    {
                        TaskId = payload.TaskId,
                        UserId = payload.UserId,
                        ReqId = payload.ReqId,
                        Success = false,
                        ErrorMessage = "回测执行超时",
                        DurationMs = taskWatch.ElapsedMilliseconds,
                    }),
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                taskWatch.Stop();
                _logger.LogError(
                    ex,
                    "算力节点执行任务失败: workerId={WorkerId} taskId={TaskId}",
                    workerId,
                    payload.TaskId);

                await SendAsync(socket, ProtocolEnvelopeFactory.Ok<object>(
                    BacktestWorkerMessageTypes.Result,
                    payload.ReqId,
                    new BacktestWorkerResultReport
                    {
                        TaskId = payload.TaskId,
                        UserId = payload.UserId,
                        ReqId = payload.ReqId,
                        Success = false,
                        ErrorMessage = ex.Message,
                        DurationMs = taskWatch.ElapsedMilliseconds,
                    }),
                    ct).ConfigureAwait(false);
            }
        }

        private async Task SendHeartbeatLoopAsync(ClientWebSocket socket, string workerId, CancellationToken ct)
        {
            var interval = TimeSpan.FromSeconds(Math.Max(1, _options.HeartbeatSeconds));
            while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                try
                {
                    await Task.Delay(interval, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (socket.State != WebSocketState.Open)
                {
                    break;
                }

                await SendAsync(socket, ProtocolEnvelopeFactory.Ok<object>(
                    BacktestWorkerMessageTypes.Heartbeat,
                    null,
                    new BacktestWorkerHeartbeat
                    {
                        WorkerId = workerId,
                        RunningTasks = 0,
                    }),
                    ct).ConfigureAwait(false);
            }
        }

        private async Task SendAsync(ClientWebSocket socket, ProtocolEnvelope<object> envelope, CancellationToken ct)
        {
            if (socket.State != WebSocketState.Open)
            {
                return;
            }

            var json = ProtocolJson.Serialize(envelope);
            var bytes = Encoding.UTF8.GetBytes(json);
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        }

        private static async Task<string?> ReadTextMessageAsync(ClientWebSocket socket, byte[] buffer, CancellationToken ct)
        {
            using var ms = new MemoryStream();
            while (true)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    if (result.EndOfMessage)
                    {
                        return null;
                    }

                    continue;
                }

                ms.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    break;
                }
            }

            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private string ResolveWorkerId()
        {
            if (!string.IsNullOrWhiteSpace(_options.WorkerId))
            {
                return _options.WorkerId.Trim();
            }

            return $"{Environment.MachineName}-{Environment.ProcessId}";
        }

        private List<string> BuildWorkerWsUrls(string workerId)
        {
            var result = new List<string>(_coreWsBaseUrls.Count);
            foreach (var baseUrl in _coreWsBaseUrls)
            {
                var sep = baseUrl.Contains('?') ? "&" : "?";
                result.Add($"{baseUrl}{sep}workerId={Uri.EscapeDataString(workerId)}");
            }

            return result;
        }

        private static string MaskSensitiveUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return url;
            }

            var query = uri.Query;
            if (string.IsNullOrWhiteSpace(query))
            {
                return url;
            }

            var queryText = query.TrimStart('?');
            if (string.IsNullOrWhiteSpace(queryText))
            {
                return url;
            }

            var parts = queryText
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part =>
                {
                    var idx = part.IndexOf('=');
                    if (idx <= 0)
                    {
                        return part;
                    }

                    var key = part.Substring(0, idx);
                    if (key.Equals("workerKey", StringComparison.OrdinalIgnoreCase))
                    {
                        return $"{key}=***";
                    }

                    return part;
                });

            return $"{uri.GetLeftPart(UriPartial.Path)}?{string.Join("&", parts)}";
        }

        private static IReadOnlyList<string> ParseCoreWsBaseUrls(
            BacktestWorkerOptions options,
            ILogger<BacktestWorkerClientHostedService> logger)
        {
            var raw = string.IsNullOrWhiteSpace(options.CoreWsUrls)
                ? options.CoreWsUrl
                : options.CoreWsUrls;

            if (string.IsNullOrWhiteSpace(raw))
            {
                return Array.Empty<string>();
            }

            var values = raw.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (values.Length == 0)
            {
                return Array.Empty<string>();
            }

            var normalized = new List<string>(values.Length);
            foreach (var value in values)
            {
                var one = NormalizeCoreWsBaseUrl(value);
                if (string.IsNullOrWhiteSpace(one))
                {
                    logger.LogWarning("忽略无效核心节点地址: {Value}", value);
                    continue;
                }

                if (!normalized.Contains(one, StringComparer.OrdinalIgnoreCase))
                {
                    normalized.Add(one);
                }
            }

            return normalized;
        }

        private static string? NormalizeCoreWsBaseUrl(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            var value = raw.Trim();
            if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                value = "ws://" + value.Substring("http://".Length);
            }
            else if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                value = "wss://" + value.Substring("https://".Length);
            }
            else if (!value.Contains("://", StringComparison.Ordinal))
            {
                value = "ws://" + value;
            }

            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                return null;
            }

            if (!string.Equals(uri.Scheme, "ws", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var builder = new UriBuilder(uri);
            if (string.IsNullOrWhiteSpace(builder.Path) || builder.Path == "/")
            {
                builder.Path = "/ws/worker";
            }
            else
            {
                builder.Path = builder.Path.TrimEnd('/');
            }

            return builder.Uri.ToString();
        }
    }
}
