using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.StrategyEngine.Application;
using ServerTest.Modules.StrategyRuntime.Domain;
using ServerTest.Modules.StrategyRuntime.Infrastructure;
using ServerTest.Options;
using ServerTest.Protocol;
using ServerTest.Services;

namespace ServerTest.Modules.StrategyRuntime.Application
{
    /// <summary>
    /// 实盘工作端客户端：主动连接核心节点，接收并执行策略增删指令。
    /// </summary>
    public sealed class LiveTradingWorkerClientHostedService : BackgroundService
    {
        private static readonly string[] RunnableStates = { "running", "paused_open_position", "testing" };

        private readonly LiveTradingWorkerOptions _options;
        private readonly ServerRoleRuntime _roleRuntime;
        private readonly StrategyRuntimeRepository _runtimeRepository;
        private readonly StrategyRuntimeLoader _runtimeLoader;
        private readonly RealTimeStrategyEngine _strategyEngine;
        private readonly StrategyOwnershipService _ownership;
        private readonly ILogger<LiveTradingWorkerClientHostedService> _logger;
        private readonly IReadOnlyList<string> _coreWsBaseUrls;
        private int _nextEndpointSeed;

        public LiveTradingWorkerClientHostedService(
            IOptions<LiveTradingWorkerOptions> options,
            ServerRoleRuntime roleRuntime,
            StrategyRuntimeRepository runtimeRepository,
            StrategyRuntimeLoader runtimeLoader,
            RealTimeStrategyEngine strategyEngine,
            StrategyOwnershipService ownership,
            ILogger<LiveTradingWorkerClientHostedService> logger)
        {
            _options = options?.Value ?? new LiveTradingWorkerOptions();
            _roleRuntime = roleRuntime ?? throw new ArgumentNullException(nameof(roleRuntime));
            _runtimeRepository = runtimeRepository ?? throw new ArgumentNullException(nameof(runtimeRepository));
            _runtimeLoader = runtimeLoader ?? throw new ArgumentNullException(nameof(runtimeLoader));
            _strategyEngine = strategyEngine ?? throw new ArgumentNullException(nameof(strategyEngine));
            _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _coreWsBaseUrls = ParseCoreWsBaseUrls(_options, _logger);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_roleRuntime.IsLiveTradingWorker)
            {
                _logger.LogInformation("当前角色非实盘工作端，跳过实盘客户端: role={Role}", _roleRuntime.Role);
                return;
            }

            if (_coreWsBaseUrls.Count == 0)
            {
                _logger.LogWarning("LiveTradingWorker.CoreWsUrls/CoreWsUrl 未配置有效地址，实盘工作端无法连接核心节点");
                return;
            }

            _logger.LogInformation(
                "实盘工作端连接配置已加载: endpointCount={Count} endpoints={Endpoints}",
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
                    _logger.LogError(ex, "实盘工作端客户端连接异常");
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
                        "实盘工作端开始连接核心节点: workerId={WorkerId} endpointIndex={Index} url={Url}",
                        workerId,
                        index,
                        MaskSensitiveUrl(wsUrl));

                    await socket.ConnectAsync(new Uri(wsUrl), ct).ConfigureAwait(false);
                    _logger.LogInformation(
                        "实盘工作端连接成功: workerId={WorkerId} endpointIndex={Index}",
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
                LiveTradingWorkerMessageTypes.Register,
                null,
                new LiveTradingWorkerRegisterRequest
                {
                    WorkerId = workerId,
                    Version = typeof(LiveTradingWorkerClientHostedService).Assembly.GetName().Version?.ToString(),
                    Tags = new[] { "live", "trading", "worker" }
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

                if (!string.Equals(envelope.Type, LiveTradingWorkerMessageTypes.Command, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var command = ProtocolJson.DeserializePayload<LiveTradingWorkerCommand>(envelope.Data);
                if (command == null || command.UsId <= 0)
                {
                    continue;
                }

                await HandleCommandAsync(socket, workerId, command, ct).ConfigureAwait(false);
            }
        }

        private async Task HandleCommandAsync(
            ClientWebSocket socket,
            string workerId,
            LiveTradingWorkerCommand command,
            CancellationToken ct)
        {
            var action = command.Action?.Trim().ToLowerInvariant() ?? string.Empty;
            try
            {
                switch (action)
                {
                    case LiveTradingWorkerCommandActions.Upsert:
                        await ApplyUpsertAsync(command.UsId, command.Reason, ct).ConfigureAwait(false);
                        break;

                    case LiveTradingWorkerCommandActions.Remove:
                        await ApplyRemoveAsync(command.UsId, command.Reason, ct).ConfigureAwait(false);
                        break;

                    default:
                        throw new InvalidOperationException($"未知实盘指令动作: {action}");
                }

                await SendAckAsync(
                    socket,
                    command,
                    success: true,
                    errorMessage: null,
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "实盘指令执行失败: workerId={WorkerId} action={Action} usId={UsId} reason={Reason}",
                    workerId,
                    action,
                    command.UsId,
                    command.Reason ?? string.Empty);

                await SendAckAsync(
                    socket,
                    command,
                    success: false,
                    errorMessage: ex.Message,
                    ct).ConfigureAwait(false);
            }
        }

        private async Task ApplyUpsertAsync(long usId, string? reason, CancellationToken ct)
        {
            var rows = await _runtimeRepository.GetByIdsAsync(new[] { usId }, ct).ConfigureAwait(false);
            var row = rows.FirstOrDefault();
            if (row == null || !IsRunnableState(row.State))
            {
                _strategyEngine.RemoveStrategy(usId.ToString());
                _ownership.Untrack(usId);
                await _ownership.ReleaseAsync(usId, ct).ConfigureAwait(false);
                _logger.LogInformation(
                    "实盘指令 upsert 命中不可运行状态，已按 remove 处理: usId={UsId} state={State} reason={Reason}",
                    usId,
                    row?.State ?? "(missing)",
                    reason ?? string.Empty);
                return;
            }

            if (!await _ownership.TryAcquireAsync(usId, ct).ConfigureAwait(false))
            {
                _strategyEngine.RemoveStrategy(usId.ToString());
                _logger.LogInformation("实盘指令 upsert 租约被其他节点持有，跳过加载: usId={UsId}", usId);
                return;
            }

            var runtimeStrategy = await _runtimeLoader.TryLoadAsync(row, ct).ConfigureAwait(false);
            if (runtimeStrategy == null)
            {
                await _ownership.ReleaseAsync(usId, ct).ConfigureAwait(false);
                throw new InvalidOperationException("策略运行时加载失败");
            }

            _strategyEngine.UpsertStrategy(runtimeStrategy);
            _ownership.TrackOwned(usId);
            _logger.LogInformation(
                "实盘指令 upsert 执行成功: usId={UsId} state={State} reason={Reason}",
                usId,
                row.State,
                reason ?? string.Empty);
        }

        private async Task ApplyRemoveAsync(long usId, string? reason, CancellationToken ct)
        {
            _strategyEngine.RemoveStrategy(usId.ToString());
            _ownership.Untrack(usId);
            await _ownership.ReleaseAsync(usId, ct).ConfigureAwait(false);
            _logger.LogInformation("实盘指令 remove 执行成功: usId={UsId} reason={Reason}", usId, reason ?? string.Empty);
        }

        private async Task SendAckAsync(
            ClientWebSocket socket,
            LiveTradingWorkerCommand command,
            bool success,
            string? errorMessage,
            CancellationToken ct)
        {
            var ack = new LiveTradingWorkerCommandAck
            {
                CommandId = command.CommandId,
                Action = command.Action,
                UsId = command.UsId,
                Success = success,
                ErrorMessage = errorMessage
            };

            await SendAsync(
                socket,
                ProtocolEnvelopeFactory.Ok<object>(LiveTradingWorkerMessageTypes.CommandAck, null, ack),
                ct).ConfigureAwait(false);
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
                    LiveTradingWorkerMessageTypes.Heartbeat,
                    null,
                    new LiveTradingWorkerHeartbeat
                    {
                        WorkerId = workerId,
                        RegisteredStrategies = _strategyEngine.GetRegisteredStrategyCount()
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
            LiveTradingWorkerOptions options,
            ILogger<LiveTradingWorkerClientHostedService> logger)
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
                builder.Path = "/ws/live-worker";
            }
            else
            {
                builder.Path = builder.Path.TrimEnd('/');
            }

            return builder.Uri.ToString();
        }

        private static bool IsRunnableState(string? state)
        {
            if (string.IsNullOrWhiteSpace(state))
            {
                return false;
            }

            var normalized = state.Trim().ToLowerInvariant();
            return RunnableStates.Contains(normalized, StringComparer.OrdinalIgnoreCase);
        }
    }
}
