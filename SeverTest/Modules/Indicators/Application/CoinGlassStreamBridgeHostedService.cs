using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.Indicators.Infrastructure;
using ServerTest.Options;
using ServerTest.Protocol;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace ServerTest.Modules.Indicators.Application
{
    /// <summary>
    /// CoinGlass 平台推送 WS 桥接服务：
    /// - 连接第三方聚合商 WS（开发阶段）
    /// - 认证并订阅频道
    /// - 将频道最新消息落入服务器缓存，供前端通过 HTTP 查询
    /// </summary>
    public sealed class CoinGlassStreamBridgeHostedService : BackgroundService
    {
        private readonly CoinGlassRealtimeCache _realtimeCache;
        private readonly CoinGlassOptions _options;
        private readonly ILogger<CoinGlassStreamBridgeHostedService> _logger;

        public CoinGlassStreamBridgeHostedService(
            CoinGlassRealtimeCache realtimeCache,
            IOptions<CoinGlassOptions> options,
            ILogger<CoinGlassStreamBridgeHostedService> logger)
        {
            _realtimeCache = realtimeCache ?? throw new ArgumentNullException(nameof(realtimeCache));
            _options = options?.Value ?? new CoinGlassOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.Enabled)
            {
                _logger.LogInformation("[coinglass][WS桥接] 未启用，WS 桥接服务不启动");
                return;
            }

            if (!_options.EnableStreamWsBridge)
            {
                _logger.LogInformation("[coinglass][WS桥接] WS 桥接开关未开启，跳过启动");
                return;
            }

            if (string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                _logger.LogWarning("[coinglass][WS桥接] 未启动：ApiKey 为空");
                return;
            }

            if (string.IsNullOrWhiteSpace(_options.StreamWsUrl))
            {
                _logger.LogWarning("[coinglass][WS桥接] 未启动：StreamWsUrl 为空");
                return;
            }

            if (_options.StreamChannels == null || _options.StreamChannels.Count == 0)
            {
                _logger.LogWarning("[coinglass][WS桥接] 未启动：StreamChannels 为空");
                return;
            }

            if (string.Equals(_options.SourceMode, "pirated_proxy", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("[coinglass][WS桥接] 当前使用第三方非官方聚合商（盗版源）数据，仅用于开发联调；正式上线前必须切换官方。");
            }

            var reconnectDelay = TimeSpan.FromSeconds(Math.Max(1, _options.WsReconnectDelaySeconds));
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunConnectionOnceAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[coinglass][WS桥接] 异常，等待重连");
                }

                try
                {
                    await Task.Delay(reconnectDelay, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        private async Task RunConnectionOnceAsync(CancellationToken ct)
        {
            using var socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);

            var uri = new Uri(_options.StreamWsUrl);
            await socket.ConnectAsync(uri, ct).ConfigureAwait(false);
            _logger.LogInformation("[coinglass][WS桥接] 已连接: {Url}", _options.StreamWsUrl);

            await SendJsonAsync(socket, new { type = "auth", key = _options.ApiKey }, ct).ConfigureAwait(false);

            var subscribed = false;
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var message = await ReceiveTextAsync(socket, ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(message))
                {
                    continue;
                }

                if (!TryParseJson(message, out var root))
                {
                    // _logger.LogDebug("[coinglass][WS桥接] 收到非 JSON 消息: {Message}", Truncate(message, 200));
                    continue;
                }

                using (root)
                {
                    var element = root.RootElement;
                    var type = TryGetString(element, "type");

                    if (!subscribed && string.Equals(type, "auth_ok", StringComparison.OrdinalIgnoreCase))
                    {
                        await SendJsonAsync(socket, new
                        {
                            type = "subscribe",
                            channels = _options.StreamChannels
                        }, ct).ConfigureAwait(false);

                        subscribed = true;
                        _logger.LogInformation("[coinglass][WS桥接] 鉴权成功，已发送订阅: channels={Channels}", string.Join(",", _options.StreamChannels));
                        continue;
                    }

                    if (string.Equals(type, "subscribed", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("[coinglass][WS桥接] 订阅确认: {Message}", Truncate(message, 300));
                        continue;
                    }

                    var channel = TryGetString(element, "channel");
                    if (!string.IsNullOrWhiteSpace(channel))
                    {
                        // _logger.LogDebug("[coinglass][WS桥接] 收到频道数据: channel={Channel}, 消息长度={Length}", channel, message.Length);
                        await _realtimeCache.SetAsync(channel, message, ct).ConfigureAwait(false);
                    }
                }
            }
        }

        private static async Task SendJsonAsync(ClientWebSocket socket, object payload, CancellationToken ct)
        {
            var text = ProtocolJson.Serialize(payload);
            var bytes = Encoding.UTF8.GetBytes(text);
            var segment = new ArraySegment<byte>(bytes);
            await socket.SendAsync(segment, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        }

        private static async Task<string> ReceiveTextAsync(ClientWebSocket socket, CancellationToken ct)
        {
            var buffer = new byte[16 * 1024];
            using var stream = new MemoryStream();

            while (true)
            {
                var segment = new ArraySegment<byte>(buffer);
                var result = await socket.ReceiveAsync(segment, ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw new WebSocketException("[coinglass][WS桥接] 远端主动关闭 WS 连接");
                }

                if (result.Count > 0)
                {
                    await stream.WriteAsync(buffer.AsMemory(0, result.Count), ct).ConfigureAwait(false);
                }

                if (result.EndOfMessage)
                {
                    break;
                }
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static bool TryParseJson(string text, out JsonDocument document)
        {
            try
            {
                document = JsonDocument.Parse(text);
                return true;
            }
            catch
            {
                document = null!;
                return false;
            }
        }

        private static string? TryGetString(JsonElement obj, string propertyName)
        {
            if (!obj.TryGetProperty(propertyName, out var value))
            {
                return null;
            }

            return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }

        private static string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            {
                return text;
            }

            return text[..maxLength] + "...";
        }
    }
}
