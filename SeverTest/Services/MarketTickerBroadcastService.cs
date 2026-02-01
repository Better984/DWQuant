using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ServerTest.Models;
using ServerTest.WebSockets;
using ServerTest.WebSockets.Subscriptions;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

namespace ServerTest.Services
{
    public sealed class MarketTickerBroadcastService : BackgroundService
    {
        private static readonly TimeSpan BroadcastInterval = TimeSpan.FromMilliseconds(500);

        private readonly ExchangePriceService _priceService;
        private readonly IMarketSubscriptionStore _subscriptionStore;
        private readonly IConnectionManager _connectionManager;
        private readonly ILogger<MarketTickerBroadcastService> _logger;
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, decimal>> _lastSent =
            new(StringComparer.Ordinal);

        public MarketTickerBroadcastService(
            ExchangePriceService priceService,
            IMarketSubscriptionStore subscriptionStore,
            IConnectionManager connectionManager,
            ILogger<MarketTickerBroadcastService> logger)
        {
            _priceService = priceService ?? throw new ArgumentNullException(nameof(priceService));
            _subscriptionStore = subscriptionStore ?? throw new ArgumentNullException(nameof(subscriptionStore));
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(BroadcastInterval, stoppingToken).ConfigureAwait(false);
                    await BroadcastAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Shutdown.
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "行情广播循环失败");
                }
            }
        }

        private Task BroadcastAsync(CancellationToken ct)
        {
            var prices = _priceService.GetAllPrices();
            if (prices.Count == 0)
            {
                return Task.CompletedTask;
            }

            var subscriptions = _subscriptionStore.GetAllSubscriptions();
            if (subscriptions.Count == 0)
            {
                return Task.CompletedTask;
            }

            foreach (var entry in subscriptions)
            {
                var userId = entry.Key;
                var symbols = entry.Value;
                if (symbols.Count == 0)
                {
                    continue;
                }

                var updates = BuildUpdates(userId, symbols, prices);
                if (updates.Count == 0)
                {
                    continue;
                }

                var connections = _connectionManager.GetConnections(userId);
                if (connections.Count == 0)
                {
                    continue;
                }

                var envelope = WsMessageEnvelope.Create("mkt.tick", null, updates, null);
                var json = JsonConvert.SerializeObject(envelope);
                var bytes = Encoding.UTF8.GetBytes(json);

                foreach (var connection in connections)
                {
                    _ = SendAsync(connection, bytes, ct);
                }
            }

            return Task.CompletedTask;
        }

        private List<object[]> BuildUpdates(
            string userId,
            IReadOnlyCollection<string> symbols,
            IReadOnlyDictionary<string, PriceData> prices)
        {
            var updates = new List<object[]>();
            var sentMap = _lastSent.GetOrAdd(userId, _ => new ConcurrentDictionary<string, decimal>(StringComparer.OrdinalIgnoreCase));

            foreach (var symbol in symbols)
            {
                if (string.IsNullOrWhiteSpace(symbol))
                {
                    continue;
                }

                if (!TryResolvePrice(symbol.Trim(), prices, out var priceData))
                {
                    continue;
                }

                var lastPrice = priceData.Price;
                if (sentMap.TryGetValue(symbol, out var previous) && previous == lastPrice)
                {
                    continue;
                }

                sentMap[symbol] = lastPrice;
                var ts = new DateTimeOffset(priceData.Timestamp).ToUnixTimeMilliseconds();
                updates.Add(new object[] { symbol, lastPrice, ts });
            }

            return updates;
        }

        private static bool TryResolvePrice(
            string symbol,
            IReadOnlyDictionary<string, PriceData> prices,
            out PriceData priceData)
        {
            var normalized = NormalizeSymbol(symbol);

            if (prices.TryGetValue(symbol, out priceData))
            {
                return true;
            }

            foreach (var entry in prices.Values)
            {
                var entrySymbol = NormalizeSymbol(entry.Symbol);
                if (string.Equals(entry.Exchange, symbol, StringComparison.OrdinalIgnoreCase))
                {
                    priceData = entry;
                    return true;
                }

                if (string.Equals(entrySymbol, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    priceData = entry;
                    return true;
                }

                if (!symbol.Contains('/') && entrySymbol.StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
                {
                    priceData = entry;
                    return true;
                }
            }

            priceData = null!;
            return false;
        }

        private static string NormalizeSymbol(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return string.Empty;
            }

            var trimmed = symbol.Trim();
            var colonIndex = trimmed.IndexOf(':');
            if (colonIndex > 0)
            {
                trimmed = trimmed.Substring(0, colonIndex);
            }

            return trimmed;
        }

        /// <summary>
        /// 立即推送行情数据（不受间隔限制，用于K线收线时强制推送）
        /// </summary>
        public async Task BroadcastImmediatelyAsync(CancellationToken ct = default)
        {
            await BroadcastAsync(ct).ConfigureAwait(false);
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
                _logger.LogWarning(ex, "发送行情数据到用户失败: {UserId}", connection.UserId);
            }
        }
    }
}
