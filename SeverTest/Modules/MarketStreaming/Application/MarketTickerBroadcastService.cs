using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServerTest.Models;
using ServerTest.Modules.MarketStreaming.Infrastructure;
using ServerTest.WebSockets;
using ServerTest.Protocol;
using System.Collections.Concurrent;
using System.Text;

namespace ServerTest.Modules.MarketStreaming.Application
{
    public sealed class MarketTickerBroadcastService : BackgroundService
    {
        private static readonly TimeSpan BroadcastInterval = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan LastSentCleanupInterval = TimeSpan.FromMinutes(1);

        private readonly ExchangePriceService _priceService;
        private readonly IMarketSubscriptionStore _subscriptionStore;
        private readonly IConnectionManager _connectionManager;
        private readonly ILogger<MarketTickerBroadcastService> _logger;
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, decimal>> _lastSent =
            new(StringComparer.Ordinal);
        private DateTime _nextLastSentCleanupAtUtc = DateTime.UtcNow.Add(LastSentCleanupInterval);

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
                    // 已停止
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
                ClearAllLastSentCacheIfNeeded();
                return Task.CompletedTask;
            }

            var activeUsers = new HashSet<string>(StringComparer.Ordinal);
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
                    // 用户无在线连接时立即释放该用户缓存，避免长期累积。
                    _lastSent.TryRemove(userId, out _);
                    continue;
                }
                activeUsers.Add(userId);

                var envelope = ProtocolEnvelopeFactory.Ok("mkt.tick", null, updates);
                var json = ProtocolJson.Serialize(envelope);
                var bytes = Encoding.UTF8.GetBytes(json);

                foreach (var connection in connections)
                {
                    _ = SendAsync(connection, bytes, ct);
                }
            }
            CleanupInactiveUserCache(activeUsers);

            return Task.CompletedTask;
        }

        private List<object[]> BuildUpdates(
            string userId,
            IReadOnlyCollection<string> symbols,
            IReadOnlyDictionary<string, PriceData> prices)
        {
            var updates = new List<object[]>();
            var sentMap = _lastSent.GetOrAdd(userId, _ => new ConcurrentDictionary<string, decimal>(StringComparer.OrdinalIgnoreCase));
            var activeSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var symbol in symbols)
            {
                if (string.IsNullOrWhiteSpace(symbol))
                {
                    continue;
                }

                var trimmedSymbol = symbol.Trim();
                if (trimmedSymbol.Length == 0)
                {
                    continue;
                }
                activeSymbols.Add(trimmedSymbol);

                if (!TryResolvePrice(trimmedSymbol, prices, out var priceData))
                {
                    continue;
                }

                var lastPrice = priceData.Price;
                if (sentMap.TryGetValue(trimmedSymbol, out var previous) && previous == lastPrice)
                {
                    continue;
                }

                sentMap[trimmedSymbol] = lastPrice;
                var ts = new DateTimeOffset(priceData.Timestamp).ToUnixTimeMilliseconds();
                updates.Add(new object[] { trimmedSymbol, lastPrice, ts });
            }
            CleanupStaleSymbolCache(sentMap, activeSymbols);

            return updates;
        }

        /// <summary>
        /// 按当前订阅符号清理已取消订阅的历史价格缓存，防止 key 只增不减。
        /// </summary>
        private static void CleanupStaleSymbolCache(
            ConcurrentDictionary<string, decimal> sentMap,
            HashSet<string> activeSymbols)
        {
            if (sentMap.IsEmpty)
            {
                return;
            }

            if (activeSymbols.Count == 0)
            {
                sentMap.Clear();
                return;
            }

            foreach (var cachedSymbol in sentMap.Keys)
            {
                if (!activeSymbols.Contains(cachedSymbol))
                {
                    sentMap.TryRemove(cachedSymbol, out _);
                }
            }
        }

        /// <summary>
        /// 周期清理不再活跃的用户缓存，避免用户离线后缓存长期驻留。
        /// </summary>
        private void CleanupInactiveUserCache(HashSet<string> activeUsers)
        {
            var now = DateTime.UtcNow;
            if (now < _nextLastSentCleanupAtUtc)
            {
                return;
            }
            _nextLastSentCleanupAtUtc = now.Add(LastSentCleanupInterval);

            var removedUsers = 0;
            foreach (var cachedUserId in _lastSent.Keys)
            {
                if (activeUsers.Contains(cachedUserId))
                {
                    continue;
                }

                if (_lastSent.TryRemove(cachedUserId, out _))
                {
                    removedUsers++;
                }
            }

            if (removedUsers > 0)
            {
                _logger.LogDebug("行情广播缓存已清理离线用户: count={Count}", removedUsers);
            }
        }

        private void ClearAllLastSentCacheIfNeeded()
        {
            if (_lastSent.IsEmpty)
            {
                return;
            }

            var removedUsers = _lastSent.Count;
            _lastSent.Clear();
            _logger.LogInformation("行情广播缓存已清空: removedUsers={Count}", removedUsers);
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
        /// 立即推送行情数据（不受间隔限制，用于补线或重连时强制推送）
        /// </summary>
        public async Task BroadcastImmediatelyAsync(CancellationToken ct = default)
        {
            await BroadcastAsync(ct).ConfigureAwait(false);
        }

        private async Task SendAsync(WebSocketConnection connection, byte[] bytes, CancellationToken ct)
        {
            try
            {
                await connection.SendTextAsync(bytes, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "发送行情数据到用户失败: {UserId}", connection.UserId);
            }
        }
    }
}
