using Microsoft.Extensions.Logging;
using ServerTest.Models;
using ServerTest.Models.Trading;
using ServerTest.Modules.MarketStreaming.Application;
using ServerTest.Modules.Notifications.Infrastructure;
using ServerTest.Modules.Positions.Infrastructure;
using System.Globalization;
using System.Text.Json;

namespace ServerTest.Modules.Positions.Application
{
    /// <summary>
    /// 仓位总览聚合服务：
    /// 一次查询返回开仓历史、策略统计、版本参与信息、当前持仓及浮动盈亏。
    /// </summary>
    public sealed class PositionOverviewService
    {
        private readonly StrategyPositionRepository _positionRepository;
        private readonly MarketDataEngine _marketDataEngine;
        private readonly NotificationRepository _notificationRepository;
        private readonly ILogger<PositionOverviewService> _logger;

        public PositionOverviewService(
            StrategyPositionRepository positionRepository,
            MarketDataEngine marketDataEngine,
            NotificationRepository notificationRepository,
            ILogger<PositionOverviewService> logger)
        {
            _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
            _marketDataEngine = marketDataEngine ?? throw new ArgumentNullException(nameof(marketDataEngine));
            _notificationRepository = notificationRepository ?? throw new ArgumentNullException(nameof(notificationRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<PositionOverviewResponse> BuildAsync(
            long uid,
            DateTime? from,
            DateTime? to,
            int recentLimit,
            int currentOpenLimit,
            CancellationToken ct = default)
        {
            var safeRecentLimit = Math.Clamp(recentLimit, 1, 200);
            var safeCurrentOpenLimit = Math.Clamp(currentOpenLimit, 1, 500);

            var historyCountTask = _positionRepository.CountByUidAsync(uid, from, to, ct);
            var recentTask = _positionRepository.GetDetailsByUidAsync(uid, from, to, safeRecentLimit, 0, ct);
            var statsTask = _positionRepository.GetOpenStatsByUidAsync(uid, from, to, ct);
            var versionsTask = _positionRepository.GetVersionParticipationByUidAsync(uid, from, to, ct);
            var rangeSummaryTask = _positionRepository.GetWindowSummaryByUidAsync(uid, from, to, ct);
            var openCountTask = _positionRepository.CountCurrentOpenByUidAsync(uid, ct);
            var currentOpenTask = _positionRepository.GetCurrentOpenDetailsByUidAsync(uid, safeCurrentOpenLimit, ct);

            await Task.WhenAll(
                historyCountTask,
                recentTask,
                statsTask,
                versionsTask,
                rangeSummaryTask,
                openCountTask,
                currentOpenTask).ConfigureAwait(false);

            var currentRows = currentOpenTask.Result;
            var priceMap = ResolveLatestPrices(currentRows.Select(row => (row.Exchange, row.Symbol)));
            var floatingItems = new List<PositionFloatingItem>(currentRows.Count);

            decimal totalFloatingPnl = 0m;
            decimal totalNotional = 0m;
            var hitCount = 0;
            var missCount = 0;

            foreach (var row in currentRows)
            {
                var normalizedExchange = MarketDataKeyNormalizer.NormalizeExchange(row.Exchange);
                var normalizedSymbol = MarketDataKeyNormalizer.NormalizeSymbol(row.Symbol);
                var key = BuildSymbolKey(normalizedExchange, normalizedSymbol);
                var markPrice = priceMap.TryGetValue(key, out var latestPrice) ? latestPrice : null;

                decimal? floatingPnl = null;
                decimal? floatingPnlRatio = null;
                if (markPrice.HasValue)
                {
                    hitCount++;
                    floatingPnl = CalculateFloatingPnl(row.Side, row.EntryPrice, row.Qty, markPrice.Value);
                    var entryNotional = row.EntryPrice * row.Qty;
                    if (entryNotional > 0)
                    {
                        floatingPnlRatio = floatingPnl / entryNotional;
                        totalNotional += entryNotional;
                    }

                    totalFloatingPnl += floatingPnl ?? 0m;
                }
                else
                {
                    missCount++;
                }

                floatingItems.Add(new PositionFloatingItem
                {
                    PositionId = row.PositionId,
                    UsId = row.UsId,
                    AliasName = row.AliasName,
                    StrategyName = row.StrategyName,
                    EffectiveVersionId = row.EffectiveVersionId,
                    EffectiveVersionNo = row.EffectiveVersionNo,
                    VersionSource = row.VersionSource,
                    Exchange = row.Exchange,
                    Symbol = row.Symbol,
                    Side = row.Side,
                    EntryPrice = row.EntryPrice,
                    Qty = row.Qty,
                    OpenedAt = row.OpenedAt,
                    LatestPrice = markPrice,
                    FloatingPnl = floatingPnl,
                    FloatingPnlRatio = floatingPnlRatio
                });
            }

            var historyItems = recentTask.Result.Select(MapToHistoryItem).ToList();
            var statItems = statsTask.Result.Select(item => new PositionStrategyOpenStatItem
            {
                UsId = item.UsId,
                DefId = item.DefId,
                AliasName = item.AliasName,
                StrategyName = item.StrategyName,
                OpenSuccessCount = item.OpenSuccessCount,
                CurrentOpenCount = item.CurrentOpenCount,
                ClosedCount = item.ClosedCount,
                FirstOpenedAt = item.FirstOpenedAt,
                LastOpenedAt = item.LastOpenedAt
            }).ToList();

            var versionItems = versionsTask.Result.Select(item => new PositionVersionParticipationItem
            {
                VersionId = item.EffectiveVersionId,
                VersionNo = item.EffectiveVersionNo,
                VersionCreatedAt = item.EffectiveVersionCreatedAt,
                Changelog = item.EffectiveVersionChangelog,
                OpenSuccessCount = item.OpenSuccessCount,
                StrategyCount = item.StrategyCount,
                StrategyUsIds = ParseStrategyIds(item.StrategyUsIdsCsv),
                StrategyAliasNames = item.StrategyAliasNames,
                SnapshotCount = item.SnapshotCount,
                InferredCount = item.InferredCount,
                PinnedCount = item.PinnedCount
            }).ToList();
            var rangeSummary = rangeSummaryTask.Result;

            _logger.LogInformation(
                "仓位总览查询完成: uid={Uid} from={From} to={To} history={HistoryCount} currentOpen={CurrentOpen} priceHit={Hit} priceMiss={Miss}",
                uid,
                from,
                to,
                historyCountTask.Result,
                openCountTask.Result,
                hitCount,
                missCount);

            return new PositionOverviewResponse
            {
                QueryAt = DateTime.UtcNow,
                From = from,
                To = to,
                HistoryTotalCount = historyCountTask.Result,
                RecentOpenCount = historyItems.Count,
                CurrentOpenCount = openCountTask.Result,
                RangeClosedCount = rangeSummary.ClosedCount,
                RangeWinCount = rangeSummary.WinCount,
                RangeWinRate = rangeSummary.ClosedCount > 0 ? rangeSummary.WinCount / (decimal)rangeSummary.ClosedCount : null,
                RangeRealizedPnl = rangeSummary.RealizedPnl,
                FloatingPriceHitCount = hitCount,
                FloatingPriceMissCount = missCount,
                TotalFloatingPnl = totalFloatingPnl,
                TotalFloatingPnlRatio = totalNotional > 0 ? totalFloatingPnl / totalNotional : null,
                RecentOpenings = historyItems,
                StrategyOpenStats = statItems,
                InvolvedVersions = versionItems,
                CurrentOpenPositions = floatingItems
            };
        }

        public async Task<PositionRecentSummaryResponse> BuildRecentSummaryAsync(
            long uid,
            DateTime? to,
            IReadOnlyList<int>? candidateWindowDays,
            CancellationToken ct = default)
        {
            var safeTo = (to ?? DateTime.UtcNow).ToUniversalTime();
            var normalizedWindows = NormalizeCandidateWindowDays(candidateWindowDays);

            var selectedWindow = normalizedWindows[^1];
            var selectedFrom = safeTo.AddDays(-selectedWindow);
            var selectedSummary = new PositionWindowSummaryRecord();
            var hasData = false;

            foreach (var windowDays in normalizedWindows)
            {
                var windowFrom = safeTo.AddDays(-windowDays);
                var summary = await _positionRepository.GetWindowSummaryByUidAsync(uid, windowFrom, safeTo, ct).ConfigureAwait(false);
                if (summary.OpenCount <= 0)
                {
                    continue;
                }

                selectedWindow = windowDays;
                selectedFrom = windowFrom;
                selectedSummary = summary;
                hasData = true;
                break;
            }

            if (!hasData)
            {
                selectedSummary = await _positionRepository.GetWindowSummaryByUidAsync(uid, selectedFrom, safeTo, ct)
                    .ConfigureAwait(false);
            }

            var openRows = await _positionRepository.GetCurrentOpenLiteByUidAsync(uid, ct).ConfigureAwait(false);
            var priceMap = ResolveLatestPrices(openRows.Select(row => (row.Exchange, row.Symbol)));

            decimal totalFloatingPnl = 0m;
            var hitCount = 0;
            var missCount = 0;

            foreach (var row in openRows)
            {
                var normalizedExchange = MarketDataKeyNormalizer.NormalizeExchange(row.Exchange);
                var normalizedSymbol = MarketDataKeyNormalizer.NormalizeSymbol(row.Symbol);
                var key = BuildSymbolKey(normalizedExchange, normalizedSymbol);
                if (!priceMap.TryGetValue(key, out var latestPrice) || !latestPrice.HasValue)
                {
                    missCount++;
                    continue;
                }

                hitCount++;
                totalFloatingPnl += CalculateFloatingPnl(row.Side, row.EntryPrice, row.Qty, latestPrice.Value);
            }

            _logger.LogInformation(
                "近期总结查询完成: uid={Uid} hasData={HasData} windowDays={WindowDays} openCount={OpenCount} closedCount={ClosedCount} currentOpen={CurrentOpen}",
                uid,
                hasData,
                selectedWindow,
                selectedSummary.OpenCount,
                selectedSummary.ClosedCount,
                openRows.Count);

            return new PositionRecentSummaryResponse
            {
                QueryAt = DateTime.UtcNow,
                From = selectedFrom,
                To = safeTo,
                HasData = hasData,
                WindowDays = selectedWindow,
                CandidateWindowDays = normalizedWindows,
                OpenCount = selectedSummary.OpenCount,
                ClosedCount = selectedSummary.ClosedCount,
                WinCount = selectedSummary.WinCount,
                WinRate = selectedSummary.ClosedCount > 0
                    ? selectedSummary.WinCount / (decimal)selectedSummary.ClosedCount
                    : null,
                RealizedPnl = selectedSummary.RealizedPnl,
                CurrentOpenCount = openRows.Count,
                CurrentFloatingPnl = totalFloatingPnl,
                FloatingPriceHitCount = hitCount,
                FloatingPriceMissCount = missCount
            };
        }

        public async Task<PositionRecentActivityResponse> BuildRecentActivityAsync(
            long uid,
            DateTime? to,
            int days,
            int limit,
            CancellationToken ct = default)
        {
            var safeLimit = Math.Clamp(limit, 1, 50);
            var safeDays = Math.Clamp(days, 1, 30);
            var safeTo = (to ?? DateTime.UtcNow).ToUniversalTime();
            var safeFrom = safeTo.AddDays(-safeDays);

            var positionEventsTask = _positionRepository.GetRecentEventsByUidAsync(uid, safeFrom, safeTo, safeLimit * 2, ct);
            var notificationRowsTask = _notificationRepository.QueryInboxAsync(uid, cursor: null, limit: Math.Clamp(safeLimit * 3, 10, 100), unreadOnly: false, ct);

            await Task.WhenAll(positionEventsTask, notificationRowsTask).ConfigureAwait(false);

            var mergedItems = new List<PositionRecentActivityItem>(safeLimit * 3);
            mergedItems.AddRange(positionEventsTask.Result.Select(MapPositionEvent));

            var warningCount = 0;
            foreach (var row in notificationRowsTask.Result)
            {
                var eventAt = EnsureUtc(row.CreatedAt);
                if (eventAt < safeFrom || eventAt > safeTo || !IsWarningSeverity(row.Severity))
                {
                    continue;
                }

                warningCount++;
                mergedItems.Add(MapWarningEvent(row, eventAt));
            }

            var items = mergedItems
                .OrderByDescending(item => item.EventAt)
                .Take(safeLimit)
                .ToList();

            _logger.LogInformation(
                "近期操作日志查询完成: uid={Uid} from={From} to={To} rawPosition={RawPosition} rawWarn={RawWarn} merged={Merged}",
                uid,
                safeFrom,
                safeTo,
                positionEventsTask.Result.Count,
                warningCount,
                items.Count);

            return new PositionRecentActivityResponse
            {
                QueryAt = DateTime.UtcNow,
                From = safeFrom,
                To = safeTo,
                Limit = safeLimit,
                Items = items
            };
        }

        private Dictionary<string, decimal?> ResolveLatestPrices(IEnumerable<(string Exchange, string Symbol)> symbols)
        {
            var result = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (exchangeRaw, symbolRaw) in symbols)
            {
                var exchange = MarketDataKeyNormalizer.NormalizeExchange(exchangeRaw);
                var symbol = MarketDataKeyNormalizer.NormalizeSymbol(symbolRaw);
                var key = BuildSymbolKey(exchange, symbol);
                if (result.ContainsKey(key))
                {
                    continue;
                }

                decimal? latestPrice = null;
                var kline = _marketDataEngine.GetLatestKline(exchange, "1m", symbol);
                if (kline.HasValue)
                {
                    var close = kline.Value.close;
                    if (close.HasValue)
                    {
                        latestPrice = Convert.ToDecimal(close.Value);
                    }
                    else if (kline.Value.open.HasValue)
                    {
                        latestPrice = Convert.ToDecimal(kline.Value.open.Value);
                    }
                }

                result[key] = latestPrice;
            }

            return result;
        }

        private static PositionHistoryDetailItem MapToHistoryItem(PositionDetailRecord row)
        {
            return new PositionHistoryDetailItem
            {
                PositionId = row.PositionId,
                UsId = row.UsId,
                DefId = row.DefId,
                AliasName = row.AliasName,
                StrategyName = row.StrategyName,
                StrategyState = row.StrategyState,
                DefType = row.DefType,
                StrategyVersionId = row.StrategyVersionId,
                EffectiveVersionId = row.EffectiveVersionId,
                EffectiveVersionNo = row.EffectiveVersionNo,
                EffectiveVersionCreatedAt = row.EffectiveVersionCreatedAt,
                EffectiveVersionChangelog = row.EffectiveVersionChangelog,
                VersionSource = row.VersionSource,
                Exchange = row.Exchange,
                Symbol = row.Symbol,
                Side = row.Side,
                Status = row.Status,
                EntryPrice = row.EntryPrice,
                Qty = row.Qty,
                StopLossPrice = row.StopLossPrice,
                TakeProfitPrice = row.TakeProfitPrice,
                TrailingEnabled = row.TrailingEnabled,
                TrailingTriggered = row.TrailingTriggered,
                TrailingStopPrice = row.TrailingStopPrice,
                CloseReason = row.CloseReason,
                ClosePrice = row.ClosePrice,
                RealizedPnl = row.RealizedPnl,
                OpenedAt = row.OpenedAt,
                ClosedAt = row.ClosedAt
            };
        }

        private static string BuildSymbolKey(string exchange, string symbol)
        {
            return $"{exchange}|{symbol}";
        }

        private static decimal CalculateFloatingPnl(string side, decimal entryPrice, decimal qty, decimal latestPrice)
        {
            var isShort = string.Equals(side, "Short", StringComparison.OrdinalIgnoreCase);
            return isShort
                ? (entryPrice - latestPrice) * qty
                : (latestPrice - entryPrice) * qty;
        }

        private static List<int> NormalizeCandidateWindowDays(IReadOnlyList<int>? candidateWindowDays)
        {
            var defaults = new List<int> { 1, 3, 7, 30 };
            if (candidateWindowDays == null || candidateWindowDays.Count == 0)
            {
                return defaults;
            }

            var normalized = candidateWindowDays
                .Where(day => day is > 0 and <= 30)
                .Distinct()
                .OrderBy(day => day)
                .ToList();

            return normalized.Count > 0 ? normalized : defaults;
        }

        private static PositionRecentActivityItem MapPositionEvent(PositionRecentEventRecord row)
        {
            var isClose = string.Equals(row.EventType, "Close", StringComparison.OrdinalIgnoreCase);
            var sideText = ToSideText(row.Side);
            var actionText = isClose ? "平仓" : "开仓";
            var symbolText = NormalizeSymbolText(row.Symbol);
            var title = $"{symbolText} {sideText}{actionText}";
            var priceText = row.EventPrice.HasValue ? row.EventPrice.Value.ToString("F4", CultureInfo.InvariantCulture) : "--";
            var qtyText = row.Qty.ToString("0.########", CultureInfo.InvariantCulture);

            string description;
            if (isClose)
            {
                var pnlText = row.RealizedPnl.HasValue ? FormatSignedNumber(row.RealizedPnl.Value) : "--";
                var reasonText = string.IsNullOrWhiteSpace(row.CloseReason) ? "未记录原因" : row.CloseReason!.Trim();
                description = $"数量 {qtyText}，价格 {priceText}，已实现盈亏 {pnlText}，原因 {reasonText}";
            }
            else
            {
                description = $"数量 {qtyText}，价格 {priceText}";
            }

            return new PositionRecentActivityItem
            {
                EventType = isClose ? "close" : "open",
                EventAt = EnsureUtc(row.EventAt),
                Title = title,
                Description = description,
                Exchange = row.Exchange,
                Symbol = row.Symbol,
                Side = row.Side,
                PositionId = row.PositionId,
                RealizedPnl = row.RealizedPnl,
                Severity = null
            };
        }

        private static PositionRecentActivityItem MapWarningEvent(NotificationInboxRecord row, DateTime eventAt)
        {
            var payload = ParseWarningPayload(row.PayloadJson);
            var categoryText = NormalizeCategoryText(row.Category);
            var templateText = string.IsNullOrWhiteSpace(row.Template) ? "系统通知" : row.Template.Trim();
            var title = !string.IsNullOrWhiteSpace(payload.Title)
                ? payload.Title
                : $"{categoryText}告警：{templateText}";
            var description = !string.IsNullOrWhiteSpace(payload.Description)
                ? payload.Description
                : $"模板 {templateText}";

            return new PositionRecentActivityItem
            {
                EventType = "warn",
                EventAt = eventAt,
                Title = title,
                Description = description,
                Exchange = payload.Exchange,
                Symbol = payload.Symbol,
                Side = payload.Side,
                PositionId = payload.PositionId,
                RealizedPnl = null,
                Severity = NormalizeSeverityText(row.Severity)
            };
        }

        private static bool IsWarningSeverity(string? severity)
        {
            return string.Equals(severity, "Warn", StringComparison.OrdinalIgnoreCase)
                || string.Equals(severity, "Critical", StringComparison.OrdinalIgnoreCase);
        }

        private static DateTime EnsureUtc(DateTime value)
        {
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };
        }

        private static string NormalizeSymbolText(string? symbol)
        {
            return string.IsNullOrWhiteSpace(symbol) ? "未知币种" : symbol.Trim().ToUpperInvariant();
        }

        private static string ToSideText(string? side)
        {
            if (string.Equals(side, "Short", StringComparison.OrdinalIgnoreCase))
            {
                return "空单";
            }

            return "多单";
        }

        private static string FormatSignedNumber(decimal value)
        {
            return value > 0
                ? $"+{value.ToString("F2", CultureInfo.InvariantCulture)}"
                : value.ToString("F2", CultureInfo.InvariantCulture);
        }

        private static string NormalizeCategoryText(string? category)
        {
            if (string.IsNullOrWhiteSpace(category))
            {
                return "系统";
            }

            return category.Trim() switch
            {
                "Trade" => "交易",
                "Risk" => "风险",
                "Strategy" => "策略",
                "Security" => "安全",
                "Subscription" => "订阅",
                "Announcement" => "公告",
                "Maintenance" => "维护",
                "Update" => "更新",
                _ => category.Trim()
            };
        }

        private static string NormalizeSeverityText(string? severity)
        {
            if (string.IsNullOrWhiteSpace(severity))
            {
                return "warn";
            }

            return severity.Trim().ToLowerInvariant();
        }

        private static WarningPayloadInfo ParseWarningPayload(string? payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson))
            {
                return WarningPayloadInfo.Empty;
            }

            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return WarningPayloadInfo.Empty;
                }

                var root = doc.RootElement;
                var title = ReadFirstString(root, "title", "message");
                var description = ReadFirstString(root, "description", "detail", "reason", "error");
                var symbol = ReadFirstString(root, "symbol");
                var exchange = ReadFirstString(root, "exchange");
                var side = ReadFirstString(root, "side");
                var positionId = ReadLong(root, "positionId", "position_id");

                return new WarningPayloadInfo
                {
                    Title = title,
                    Description = description,
                    Symbol = symbol,
                    Exchange = exchange,
                    Side = side,
                    PositionId = positionId
                };
            }
            catch
            {
                return WarningPayloadInfo.Empty;
            }
        }

        private static string? ReadFirstString(JsonElement root, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!root.TryGetProperty(key, out var value))
                {
                    continue;
                }

                if (value.ValueKind == JsonValueKind.String)
                {
                    var text = value.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text.Trim();
                    }
                }
                else
                {
                    var text = value.GetRawText();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text.Trim();
                    }
                }
            }

            return null;
        }

        private static long? ReadLong(JsonElement root, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!root.TryGetProperty(key, out var value))
                {
                    continue;
                }

                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
                {
                    return number;
                }

                if (value.ValueKind == JsonValueKind.String
                    && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }
            }

            return null;
        }

        private sealed class WarningPayloadInfo
        {
            public static readonly WarningPayloadInfo Empty = new();
            public string? Title { get; init; }
            public string? Description { get; init; }
            public string? Symbol { get; init; }
            public string? Exchange { get; init; }
            public string? Side { get; init; }
            public long? PositionId { get; init; }
        }

        private static List<long> ParseStrategyIds(string? csv)
        {
            var result = new List<long>();
            if (string.IsNullOrWhiteSpace(csv))
            {
                return result;
            }

            var parts = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                if (long.TryParse(part, out var id))
                {
                    result.Add(id);
                }
            }

            return result;
        }
    }
}
