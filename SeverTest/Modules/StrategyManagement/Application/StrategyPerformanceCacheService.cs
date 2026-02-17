using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ServerTest.Infrastructure.Db;
using ServerTest.Modules.Backtest.Application;
using ServerTest.Modules.Backtest.Domain;
using ServerTest.Protocol;

namespace ServerTest.Modules.StrategyManagement.Application
{
    /// <summary>
    /// 策略绩效缓存服务：
    /// - 优先读取缓存，避免重复计算；
    /// - 30天内无实盘记录时自动触发回测补齐资金曲线；
    /// - 对公开策略（帖子公开/分享码/公开市场/官方）采用更长缓存周期。
    /// </summary>
    public sealed class StrategyPerformanceCacheService
    {
        private const int DefaultWindowDays = 30;
        private const int MaxWindowDays = 90;
        private const int LivePublicTtlMinutes = 5;
        private const int LivePrivateTtlMinutes = 2;
        private const int BacktestPublicTtlMinutes = 240;
        private const int BacktestPrivateTtlMinutes = 60;
        private const int BacktestTimeoutSeconds = 180;
        private const int BacktestMaxConcurrency = 2;

        private static readonly ConcurrentDictionary<string, SemaphoreSlim> CacheKeyLocks = new(StringComparer.OrdinalIgnoreCase);
        private static readonly SemaphoreSlim BacktestConcurrency = new(BacktestMaxConcurrency, BacktestMaxConcurrency);
        private static readonly SemaphoreSlim SchemaLock = new(1, 1);
        private static volatile bool _schemaEnsured;

        private readonly IDbManager _db;
        private readonly BacktestService _backtestService;
        private readonly ILogger<StrategyPerformanceCacheService> _logger;

        public StrategyPerformanceCacheService(
            IDbManager db,
            BacktestService backtestService,
            ILogger<StrategyPerformanceCacheService> logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _backtestService = backtestService ?? throw new ArgumentNullException(nameof(backtestService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Dictionary<long, StrategyCurveSnapshot>> GetCurveSnapshotsAsync(
            IReadOnlyCollection<long> usIds,
            int windowDays,
            CancellationToken ct)
        {
            var normalizedUsIds = usIds
                .Where(id => id > 0)
                .Distinct()
                .ToList();
            if (normalizedUsIds.Count == 0)
            {
                return new Dictionary<long, StrategyCurveSnapshot>();
            }

            var normalizedDays = Math.Clamp(windowDays <= 0 ? DefaultWindowDays : windowDays, 1, MaxWindowDays);
            await EnsureSchemaAsync(ct).ConfigureAwait(false);

            var nowUtc = DateTime.UtcNow;
            var cacheRows = await QueryCacheRowsByUsIdsAsync(normalizedUsIds, normalizedDays, ct).ConfigureAwait(false);
            var cacheByUsId = cacheRows.ToDictionary(item => item.UsId, item => item);

            var result = new Dictionary<long, StrategyCurveSnapshot>();
            var pendingUsIds = new List<long>(normalizedUsIds.Count);

            foreach (var usId in normalizedUsIds)
            {
                if (cacheByUsId.TryGetValue(usId, out var cacheRow)
                    && IsCacheValid(cacheRow, nowUtc)
                    && TryDeserializeSeries(cacheRow.PnlSeriesJson, normalizedDays, out var cachedSeries))
                {
                    result[usId] = new StrategyCurveSnapshot
                    {
                        UsId = usId,
                        Series = cachedSeries,
                        CurveSource = NormalizeCurveSource(cacheRow.CurveSource),
                        IsBacktest = cacheRow.IsBacktest > 0
                    };
                    continue;
                }

                pendingUsIds.Add(usId);
            }

            if (pendingUsIds.Count == 0)
            {
                return result;
            }

            var metaRows = await QueryStrategyMetaRowsAsync(pendingUsIds, ct).ConfigureAwait(false);
            var metaByUsId = metaRows.ToDictionary(item => item.UsId, item => item);

            var liveStatsRows = await QueryLiveStatsRowsAsync(pendingUsIds, normalizedDays, ct).ConfigureAwait(false);
            var liveStatsByUsId = liveStatsRows.ToDictionary(item => item.UsId, item => item);
            var liveRebuildUsIds = new List<long>();
            var liveRebuildContext = new Dictionary<long, LiveRebuildContext>();

            foreach (var usId in pendingUsIds)
            {
                var meta = metaByUsId.TryGetValue(usId, out var metaRow)
                    ? metaRow
                    : new StrategyMetaRow
                    {
                        UsId = usId,
                        Visibility = "private",
                        ShareCode = string.Empty,
                        IsOfficial = 0,
                        HasPublicPost = 0,
                        ContentHash = string.Empty
                    };
                var cacheScope = ResolveCacheScope(meta);
                var cacheKey = BuildCacheKey(usId, normalizedDays);
                var staleCache = cacheByUsId.TryGetValue(usId, out var staleRow) ? staleRow : null;

                if (liveStatsByUsId.TryGetValue(usId, out var liveStats) && liveStats.TradeCount > 0)
                {
                    var liveFingerprint = BuildLiveFingerprint(liveStats);

                    if (staleCache != null
                        && staleCache.IsBacktest <= 0
                        && string.Equals(staleCache.LiveFingerprint, liveFingerprint, StringComparison.Ordinal)
                        && TryDeserializeSeries(staleCache.PnlSeriesJson, normalizedDays, out var cachedLiveSeries))
                    {
                        await UpsertCacheAsync(new CacheUpsertPayload
                        {
                            CacheKey = cacheKey,
                            UsId = usId,
                            WindowDays = normalizedDays,
                            CurveSource = "live",
                            IsBacktest = false,
                            PnlSeriesJson = staleCache.PnlSeriesJson ?? ProtocolJson.Serialize(cachedLiveSeries),
                            TradeLogJson = staleCache.TradeLogJson,
                            PositionLogJson = staleCache.PositionLogJson,
                            OpenCloseLogJson = staleCache.OpenCloseLogJson,
                            LiveFingerprint = liveFingerprint,
                            BacktestFingerprint = null,
                            CacheScope = cacheScope,
                            ExpiresAt = ResolveExpiresAt(nowUtc, cacheScope, isBacktest: false)
                        }, ct).ConfigureAwait(false);

                        result[usId] = new StrategyCurveSnapshot
                        {
                            UsId = usId,
                            Series = cachedLiveSeries,
                            CurveSource = "live",
                            IsBacktest = false
                        };
                        continue;
                    }

                    liveRebuildUsIds.Add(usId);
                    liveRebuildContext[usId] = new LiveRebuildContext
                    {
                        CacheScope = cacheScope,
                        LiveFingerprint = liveFingerprint
                    };
                    continue;
                }

                var fallback = await GetOrBuildBacktestFallbackAsync(
                    usId,
                    normalizedDays,
                    cacheScope,
                    meta.ContentHash ?? string.Empty,
                    staleCache,
                    ct).ConfigureAwait(false);
                result[usId] = fallback;
            }

            if (liveRebuildUsIds.Count > 0)
            {
                var livePositionRows = await QueryLivePositionRowsAsync(liveRebuildUsIds, normalizedDays, ct).ConfigureAwait(false);
                var livePositionRowsByUsId = livePositionRows
                    .GroupBy(item => item.UsId)
                    .ToDictionary(group => group.Key, group => (IReadOnlyList<LivePositionRow>)group.ToList());

                foreach (var usId in liveRebuildUsIds)
                {
                    if (!liveRebuildContext.TryGetValue(usId, out var context))
                    {
                        continue;
                    }

                    var rows = livePositionRowsByUsId.TryGetValue(usId, out var mappedRows)
                        ? mappedRows
                        : Array.Empty<LivePositionRow>();
                    var dayPnl = BuildLiveDailyPnlMap(rows);
                    var series = BuildLivePnlSeries(dayPnl, normalizedDays);

                    await UpsertCacheAsync(new CacheUpsertPayload
                    {
                        CacheKey = BuildCacheKey(usId, normalizedDays),
                        UsId = usId,
                        WindowDays = normalizedDays,
                        CurveSource = "live",
                        IsBacktest = false,
                        PnlSeriesJson = ProtocolJson.Serialize(series),
                        TradeLogJson = BuildLiveTradeLogJson(rows),
                        PositionLogJson = BuildLivePositionLogJson(rows),
                        OpenCloseLogJson = BuildLiveOpenCloseLogJson(rows),
                        LiveFingerprint = context.LiveFingerprint,
                        BacktestFingerprint = null,
                        CacheScope = context.CacheScope,
                        ExpiresAt = ResolveExpiresAt(nowUtc, context.CacheScope, isBacktest: false)
                    }, ct).ConfigureAwait(false);

                    result[usId] = new StrategyCurveSnapshot
                    {
                        UsId = usId,
                        Series = series,
                        CurveSource = "live",
                        IsBacktest = false
                    };
                }
            }

            return result;
        }

        private async Task<StrategyCurveSnapshot> GetOrBuildBacktestFallbackAsync(
            long usId,
            int windowDays,
            string cacheScope,
            string contentHash,
            StrategyPerformanceCacheRow? staleCache,
            CancellationToken ct)
        {
            var cacheKey = BuildCacheKey(usId, windowDays);
            var lockSlim = CacheKeyLocks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
            await lockSlim.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                var nowUtc = DateTime.UtcNow;
                var latestCache = await QueryCacheRowByCacheKeyAsync(cacheKey, ct).ConfigureAwait(false);
                if (latestCache != null
                    && IsCacheValid(latestCache, nowUtc)
                    && TryDeserializeSeries(latestCache.PnlSeriesJson, windowDays, out var latestSeries))
                {
                    return new StrategyCurveSnapshot
                    {
                        UsId = usId,
                        Series = latestSeries,
                        CurveSource = NormalizeCurveSource(latestCache.CurveSource),
                        IsBacktest = latestCache.IsBacktest > 0
                    };
                }

                var backtestFingerprint = BuildBacktestFingerprint(contentHash, windowDays, nowUtc.Date);
                var reusableCache = latestCache ?? staleCache;
                if (reusableCache != null
                    && reusableCache.IsBacktest > 0
                    && string.Equals(reusableCache.BacktestFingerprint, backtestFingerprint, StringComparison.OrdinalIgnoreCase)
                    && TryDeserializeSeries(reusableCache.PnlSeriesJson, windowDays, out var reusedSeries))
                {
                    await UpsertCacheAsync(new CacheUpsertPayload
                    {
                        CacheKey = cacheKey,
                        UsId = usId,
                        WindowDays = windowDays,
                        CurveSource = "backtest",
                        IsBacktest = true,
                        PnlSeriesJson = reusableCache.PnlSeriesJson ?? ProtocolJson.Serialize(reusedSeries),
                        TradeLogJson = reusableCache.TradeLogJson,
                        PositionLogJson = reusableCache.PositionLogJson,
                        OpenCloseLogJson = reusableCache.OpenCloseLogJson,
                        LiveFingerprint = null,
                        BacktestFingerprint = backtestFingerprint,
                        CacheScope = cacheScope,
                        ExpiresAt = ResolveExpiresAt(nowUtc, cacheScope, isBacktest: true)
                    }, ct).ConfigureAwait(false);

                    return new StrategyCurveSnapshot
                    {
                        UsId = usId,
                        Series = reusedSeries,
                        CurveSource = "backtest",
                        IsBacktest = true
                    };
                }

                await BacktestConcurrency.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(BacktestTimeoutSeconds));

                    var from = nowUtc.AddDays(-(windowDays - 1));
                    var request = new BacktestRunRequest
                    {
                        UsId = usId,
                        StartTime = from.ToString("yyyy-MM-dd HH:mm:ss"),
                        EndTime = nowUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                        UseStrategyRuntime = false,
                        ExecutionMode = BacktestExecutionModes.BatchOpenClose,
                        Output = new BacktestOutputOptions
                        {
                            IncludeTrades = true,
                            IncludeEquityCurve = true,
                            IncludeEvents = true,
                            EquityCurveGranularity = "1d"
                        }
                    };

                    var runResult = await _backtestService.RunAsync(
                        request,
                        $"perf-cache-{usId}-{DateTime.UtcNow:yyyyMMddHHmmssfff}",
                        null,
                        null,
                        timeoutCts.Token).ConfigureAwait(false);

                    var symbolResult = runResult.Symbols.FirstOrDefault();
                    var series = BuildBacktestPnlSeries(symbolResult?.EquityCurveRaw, windowDays);

                    await UpsertCacheAsync(new CacheUpsertPayload
                    {
                        CacheKey = cacheKey,
                        UsId = usId,
                        WindowDays = windowDays,
                        CurveSource = "backtest",
                        IsBacktest = true,
                        PnlSeriesJson = ProtocolJson.Serialize(series),
                        TradeLogJson = ProtocolJson.Serialize(symbolResult?.TradesRaw ?? new List<string>()),
                        PositionLogJson = ProtocolJson.Serialize(symbolResult?.TradesRaw ?? new List<string>()),
                        OpenCloseLogJson = ProtocolJson.Serialize(symbolResult?.EventsRaw ?? new List<string>()),
                        LiveFingerprint = null,
                        BacktestFingerprint = backtestFingerprint,
                        CacheScope = cacheScope,
                        ExpiresAt = ResolveExpiresAt(nowUtc, cacheScope, isBacktest: true)
                    }, ct).ConfigureAwait(false);

                    return new StrategyCurveSnapshot
                    {
                        UsId = usId,
                        Series = series,
                        CurveSource = "backtest",
                        IsBacktest = true
                    };
                }
                finally
                {
                    BacktestConcurrency.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "策略绩效缓存回测失败，降级到缓存/零值: usId={UsId}", usId);

                if (staleCache != null
                    && TryDeserializeSeries(staleCache.PnlSeriesJson, windowDays, out var staleSeries))
                {
                    return new StrategyCurveSnapshot
                    {
                        UsId = usId,
                        Series = staleSeries,
                        CurveSource = NormalizeCurveSource(staleCache.CurveSource),
                        IsBacktest = staleCache.IsBacktest > 0
                    };
                }

                return new StrategyCurveSnapshot
                {
                    UsId = usId,
                    Series = Enumerable.Repeat(0m, windowDays).ToList(),
                    CurveSource = "live",
                    IsBacktest = false
                };
            }
            finally
            {
                lockSlim.Release();
            }
        }

        private static string BuildCacheKey(long usId, int windowDays) => $"us:{usId}:curve:{windowDays}d";

        private static bool IsPublicScope(string scope)
        {
            return !string.Equals(scope, "private", StringComparison.OrdinalIgnoreCase);
        }

        private static DateTime ResolveExpiresAt(DateTime nowUtc, string scope, bool isBacktest)
        {
            if (isBacktest)
            {
                var backtestMinutes = IsPublicScope(scope) ? BacktestPublicTtlMinutes : BacktestPrivateTtlMinutes;
                return nowUtc.AddMinutes(backtestMinutes);
            }

            var liveMinutes = IsPublicScope(scope) ? LivePublicTtlMinutes : LivePrivateTtlMinutes;
            return nowUtc.AddMinutes(liveMinutes);
        }

        private static string ResolveCacheScope(StrategyMetaRow row)
        {
            if (row.IsOfficial > 0)
            {
                return "official";
            }

            if (string.Equals(row.Visibility, "public_sale", StringComparison.OrdinalIgnoreCase))
            {
                return "public_sale";
            }

            if (string.Equals(row.Visibility, "shared", StringComparison.OrdinalIgnoreCase))
            {
                return "shared";
            }

            if (!string.IsNullOrWhiteSpace(row.ShareCode))
            {
                return "share_code";
            }

            if (row.HasPublicPost > 0)
            {
                return "planet_public";
            }

            return "private";
        }

        private static string BuildLiveFingerprint(LiveStatRow row)
        {
            var last = row.LastTradeAt?.ToString("yyyyMMddHHmmssfff") ?? "none";
            return $"{row.TradeCount}|{row.MaxPositionId}|{last}|{decimal.Round(row.SumRealizedPnl, 8)}";
        }

        private static string BuildBacktestFingerprint(string contentHash, int windowDays, DateTime utcDate)
        {
            var hash = string.IsNullOrWhiteSpace(contentHash) ? "nohash" : contentHash.Trim().ToLowerInvariant();
            return $"{hash}|{windowDays}|{utcDate:yyyyMMdd}";
        }

        private static bool IsCacheValid(StrategyPerformanceCacheRow row, DateTime nowUtc)
        {
            if (!row.ExpiresAt.HasValue)
            {
                return false;
            }

            return row.ExpiresAt.Value > nowUtc;
        }

        private static string NormalizeCurveSource(string? source)
        {
            if (string.Equals(source, "backtest", StringComparison.OrdinalIgnoreCase))
            {
                return "backtest";
            }

            return "live";
        }

        private static bool TryDeserializeSeries(string? json, int windowDays, out List<decimal> series)
        {
            series = Enumerable.Repeat(0m, windowDays).ToList();
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                var parsed = ProtocolJson.Deserialize<List<decimal>>(json);
                if (parsed == null || parsed.Count == 0)
                {
                    return false;
                }

                series = NormalizeSeries(parsed, windowDays);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static List<decimal> NormalizeSeries(IReadOnlyList<decimal> input, int windowDays)
        {
            if (input.Count == windowDays)
            {
                return input.Select(item => decimal.Round(item, 4)).ToList();
            }

            if (input.Count > windowDays)
            {
                return input.Skip(input.Count - windowDays).Select(item => decimal.Round(item, 4)).ToList();
            }

            var result = new List<decimal>(windowDays);
            result.AddRange(Enumerable.Repeat(0m, windowDays - input.Count));
            result.AddRange(input.Select(item => decimal.Round(item, 4)));
            return result;
        }

        private static List<decimal> BuildLivePnlSeries(IReadOnlyDictionary<DateOnly, decimal> dayPnl, int windowDays)
        {
            var series = new List<decimal>(windowDays);
            var start = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-(windowDays - 1)));
            decimal running = 0m;

            for (var i = 0; i < windowDays; i++)
            {
                var day = start.AddDays(i);
                if (dayPnl.TryGetValue(day, out var pnl))
                {
                    running += pnl;
                }

                series.Add(decimal.Round(running, 4));
            }

            return series;
        }

        private static Dictionary<DateOnly, decimal> BuildLiveDailyPnlMap(IReadOnlyList<LivePositionRow> rows)
        {
            var result = new Dictionary<DateOnly, decimal>();
            foreach (var row in rows)
            {
                var tradeTime = row.ClosedAt ?? row.OpenedAt;
                var day = DateOnly.FromDateTime(tradeTime.Date);
                var pnl = row.RealizedPnl ?? 0m;
                if (result.TryGetValue(day, out var existing))
                {
                    result[day] = existing + pnl;
                }
                else
                {
                    result[day] = pnl;
                }
            }

            return result;
        }

        private static string BuildLiveTradeLogJson(IReadOnlyList<LivePositionRow> rows)
        {
            var items = rows.Select(row => new LiveTradeLogItem
            {
                PositionId = row.PositionId,
                Exchange = row.Exchange ?? string.Empty,
                Symbol = row.Symbol ?? string.Empty,
                Side = row.Side ?? string.Empty,
                EntryPrice = row.EntryPrice,
                ClosePrice = row.ClosePrice,
                Qty = row.Qty,
                RealizedPnl = row.RealizedPnl,
                OpenedAt = row.OpenedAt,
                ClosedAt = row.ClosedAt,
                Status = row.Status ?? string.Empty,
                CloseReason = row.CloseReason
            }).ToList();
            return ProtocolJson.Serialize(items);
        }

        private static string BuildLivePositionLogJson(IReadOnlyList<LivePositionRow> rows)
        {
            var items = rows.Select(row => new LivePositionLogItem
            {
                PositionId = row.PositionId,
                Exchange = row.Exchange ?? string.Empty,
                Symbol = row.Symbol ?? string.Empty,
                Side = row.Side ?? string.Empty,
                EntryPrice = row.EntryPrice,
                Qty = row.Qty,
                Status = row.Status ?? string.Empty,
                StopLossPrice = row.StopLossPrice,
                TakeProfitPrice = row.TakeProfitPrice,
                TrailingEnabled = row.TrailingEnabled > 0,
                TrailingStopPrice = row.TrailingStopPrice,
                TrailingTriggered = row.TrailingTriggered > 0,
                OpenedAt = row.OpenedAt,
                ClosedAt = row.ClosedAt,
                ClosePrice = row.ClosePrice,
                CloseReason = row.CloseReason,
                RealizedPnl = row.RealizedPnl
            }).ToList();
            return ProtocolJson.Serialize(items);
        }

        private static string BuildLiveOpenCloseLogJson(IReadOnlyList<LivePositionRow> rows)
        {
            var events = new List<LiveOpenCloseLogItem>(rows.Count * 2);
            foreach (var row in rows)
            {
                events.Add(new LiveOpenCloseLogItem
                {
                    PositionId = row.PositionId,
                    Action = "open",
                    EventAt = row.OpenedAt,
                    Exchange = row.Exchange ?? string.Empty,
                    Symbol = row.Symbol ?? string.Empty,
                    Side = row.Side ?? string.Empty,
                    Price = row.EntryPrice,
                    Qty = row.Qty,
                    Status = row.Status ?? string.Empty
                });

                if (!row.ClosedAt.HasValue)
                {
                    continue;
                }

                events.Add(new LiveOpenCloseLogItem
                {
                    PositionId = row.PositionId,
                    Action = "close",
                    EventAt = row.ClosedAt.Value,
                    Exchange = row.Exchange ?? string.Empty,
                    Symbol = row.Symbol ?? string.Empty,
                    Side = row.Side ?? string.Empty,
                    Price = row.ClosePrice ?? 0m,
                    Qty = row.Qty,
                    Status = row.Status ?? string.Empty,
                    RealizedPnl = row.RealizedPnl,
                    CloseReason = row.CloseReason
                });
            }

            var ordered = events
                .OrderBy(item => item.EventAt)
                .ThenBy(item => item.PositionId)
                .ThenBy(item => item.Action, StringComparer.Ordinal)
                .ToList();
            return ProtocolJson.Serialize(ordered);
        }

        private static List<decimal> BuildBacktestPnlSeries(IReadOnlyList<string>? equityCurveRaw, int windowDays)
        {
            if (equityCurveRaw == null || equityCurveRaw.Count == 0)
            {
                return Enumerable.Repeat(0m, windowDays).ToList();
            }

            var points = new List<BacktestEquityPoint>(equityCurveRaw.Count);
            foreach (var raw in equityCurveRaw)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                var point = ProtocolJson.Deserialize<BacktestEquityPoint>(raw);
                if (point != null)
                {
                    points.Add(point);
                }
            }

            if (points.Count == 0)
            {
                return Enumerable.Repeat(0m, windowDays).ToList();
            }

            points = points
                .OrderBy(item => item.Timestamp)
                .ToList();
            var baseEquity = points[0].Equity;
            var cumulativeByDay = new Dictionary<DateOnly, decimal>();
            foreach (var point in points)
            {
                try
                {
                    var day = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(point.Timestamp).UtcDateTime.Date);
                    cumulativeByDay[day] = decimal.Round(point.Equity - baseEquity, 4);
                }
                catch
                {
                    // 忽略异常时间戳点，避免影响主流程。
                }
            }

            var series = new List<decimal>(windowDays);
            var start = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-(windowDays - 1)));
            decimal running = 0m;
            for (var i = 0; i < windowDays; i++)
            {
                var day = start.AddDays(i);
                if (cumulativeByDay.TryGetValue(day, out var cumulative))
                {
                    running = cumulative;
                }

                series.Add(decimal.Round(running, 4));
            }

            return series;
        }

        private async Task EnsureSchemaAsync(CancellationToken ct)
        {
            if (_schemaEnsured)
            {
                return;
            }

            await SchemaLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_schemaEnsured)
                {
                    return;
                }

                const string sql = @"
CREATE TABLE IF NOT EXISTS strategy_performance_cache
(
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    cache_key VARCHAR(128) NOT NULL,
    us_id BIGINT UNSIGNED NOT NULL,
    window_days INT NOT NULL DEFAULT 30,
    curve_source VARCHAR(16) NOT NULL DEFAULT 'live',
    is_backtest TINYINT(1) NOT NULL DEFAULT 0,
    pnl_series_json LONGTEXT NOT NULL,
    trade_log_json LONGTEXT NULL,
    position_log_json LONGTEXT NULL,
    open_close_log_json LONGTEXT NULL,
    live_fingerprint VARCHAR(256) NULL,
    backtest_fingerprint VARCHAR(256) NULL,
    cache_scope VARCHAR(32) NOT NULL DEFAULT 'private',
    expires_at DATETIME(3) NULL,
    last_hit_at DATETIME(3) NULL,
    hit_count BIGINT NOT NULL DEFAULT 0,
    created_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    updated_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
    PRIMARY KEY (id),
    UNIQUE KEY uk_strategy_performance_cache_key (cache_key),
    KEY idx_strategy_performance_cache_us_days (us_id, window_days),
    KEY idx_strategy_performance_cache_scope (cache_scope),
    KEY idx_strategy_performance_cache_expires (expires_at),
    KEY idx_strategy_performance_cache_updated (updated_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";

                await _db.ExecuteAsync(sql, null, null, ct).ConfigureAwait(false);
                _schemaEnsured = true;
            }
            finally
            {
                SchemaLock.Release();
            }
        }

        private async Task<List<StrategyPerformanceCacheRow>> QueryCacheRowsByUsIdsAsync(
            IReadOnlyCollection<long> usIds,
            int windowDays,
            CancellationToken ct)
        {
            if (usIds.Count == 0)
            {
                return new List<StrategyPerformanceCacheRow>();
            }

            const string sql = @"
SELECT
    cache_key AS CacheKey,
    us_id AS UsId,
    window_days AS WindowDays,
    curve_source AS CurveSource,
    is_backtest AS IsBacktest,
    pnl_series_json AS PnlSeriesJson,
    trade_log_json AS TradeLogJson,
    position_log_json AS PositionLogJson,
    open_close_log_json AS OpenCloseLogJson,
    live_fingerprint AS LiveFingerprint,
    backtest_fingerprint AS BacktestFingerprint,
    cache_scope AS CacheScope,
    expires_at AS ExpiresAt,
    updated_at AS UpdatedAt
FROM strategy_performance_cache
WHERE us_id IN @usIds
  AND window_days = @windowDays;";

            var rows = await _db.QueryAsync<StrategyPerformanceCacheRow>(
                sql,
                new { usIds, windowDays },
                null,
                ct).ConfigureAwait(false);
            return rows.ToList();
        }

        private async Task<StrategyPerformanceCacheRow?> QueryCacheRowByCacheKeyAsync(string cacheKey, CancellationToken ct)
        {
            const string sql = @"
SELECT
    cache_key AS CacheKey,
    us_id AS UsId,
    window_days AS WindowDays,
    curve_source AS CurveSource,
    is_backtest AS IsBacktest,
    pnl_series_json AS PnlSeriesJson,
    trade_log_json AS TradeLogJson,
    position_log_json AS PositionLogJson,
    open_close_log_json AS OpenCloseLogJson,
    live_fingerprint AS LiveFingerprint,
    backtest_fingerprint AS BacktestFingerprint,
    cache_scope AS CacheScope,
    expires_at AS ExpiresAt,
    updated_at AS UpdatedAt
FROM strategy_performance_cache
WHERE cache_key = @cacheKey
LIMIT 1;";

            return await _db.QuerySingleOrDefaultAsync<StrategyPerformanceCacheRow>(
                sql,
                new { cacheKey },
                null,
                ct).ConfigureAwait(false);
        }

        private async Task<List<StrategyMetaRow>> QueryStrategyMetaRowsAsync(IReadOnlyCollection<long> usIds, CancellationToken ct)
        {
            if (usIds.Count == 0)
            {
                return new List<StrategyMetaRow>();
            }

            const string sql = @"
SELECT
    us.us_id AS UsId,
    COALESCE(us.visibility, 'private') AS Visibility,
    COALESCE(us.share_code, '') AS ShareCode,
    CASE WHEN osd.def_id IS NULL THEN 0 ELSE 1 END AS IsOfficial,
    CASE WHEN EXISTS (
        SELECT 1
        FROM planet_post_strategy pps
        INNER JOIN planet_post pp ON pp.post_id = pps.post_id
        WHERE pps.us_id = us.us_id
          AND pp.status IN ('normal', 'active')
        LIMIT 1
    ) THEN 1 ELSE 0 END AS HasPublicPost,
    COALESCE(sv.content_hash, '') AS ContentHash
FROM user_strategy us
LEFT JOIN official_strategy_def osd ON osd.source_us_id = us.us_id
LEFT JOIN strategy_version sv ON sv.version_id = us.pinned_version_id
WHERE us.us_id IN @usIds;";

            var rows = await _db.QueryAsync<StrategyMetaRow>(
                sql,
                new { usIds },
                null,
                ct).ConfigureAwait(false);
            return rows.ToList();
        }

        private async Task<List<LiveStatRow>> QueryLiveStatsRowsAsync(IReadOnlyCollection<long> usIds, int days, CancellationToken ct)
        {
            if (usIds.Count == 0)
            {
                return new List<LiveStatRow>();
            }

            const string sql = @"
SELECT
    us_id AS UsId,
    COUNT(1) AS TradeCount,
    MAX(COALESCE(closed_at, opened_at)) AS LastTradeAt,
    COALESCE(MAX(position_id), 0) AS MaxPositionId,
    COALESCE(SUM(COALESCE(realized_pnl, 0)), 0) AS SumRealizedPnl
FROM strategy_position
WHERE us_id IN @usIds
  AND (
      (closed_at IS NULL AND opened_at >= DATE_SUB(UTC_TIMESTAMP(3), INTERVAL @days DAY))
      OR (closed_at IS NOT NULL AND closed_at >= DATE_SUB(UTC_TIMESTAMP(3), INTERVAL @days DAY))
  )
GROUP BY us_id;";

            var rows = await _db.QueryAsync<LiveStatRow>(
                sql,
                new { usIds, days },
                null,
                ct).ConfigureAwait(false);
            return rows.ToList();
        }

        private async Task<List<LivePositionRow>> QueryLivePositionRowsAsync(IReadOnlyCollection<long> usIds, int days, CancellationToken ct)
        {
            if (usIds.Count == 0)
            {
                return new List<LivePositionRow>();
            }

            const string sql = @"
SELECT
    us_id AS UsId,
    position_id AS PositionId,
    exchange AS Exchange,
    symbol AS Symbol,
    side AS Side,
    entry_price AS EntryPrice,
    qty AS Qty,
    status AS Status,
    stop_loss_price AS StopLossPrice,
    take_profit_price AS TakeProfitPrice,
    trailing_enabled AS TrailingEnabled,
    trailing_stop_price AS TrailingStopPrice,
    trailing_triggered AS TrailingTriggered,
    opened_at AS OpenedAt,
    closed_at AS ClosedAt,
    close_reason AS CloseReason,
    close_price AS ClosePrice,
    realized_pnl AS RealizedPnl
FROM strategy_position
WHERE us_id IN @usIds
  AND (
      opened_at >= DATE_SUB(UTC_TIMESTAMP(3), INTERVAL @days DAY)
      OR (closed_at IS NOT NULL AND closed_at >= DATE_SUB(UTC_TIMESTAMP(3), INTERVAL @days DAY))
  )
ORDER BY us_id ASC, COALESCE(closed_at, opened_at) ASC, position_id ASC;";

            var rows = await _db.QueryAsync<LivePositionRow>(
                sql,
                new { usIds, days },
                null,
                ct).ConfigureAwait(false);
            return rows.ToList();
        }

        private async Task UpsertCacheAsync(CacheUpsertPayload payload, CancellationToken ct)
        {
            const string sql = @"
INSERT INTO strategy_performance_cache
(
    cache_key,
    us_id,
    window_days,
    curve_source,
    is_backtest,
    pnl_series_json,
    trade_log_json,
    position_log_json,
    open_close_log_json,
    live_fingerprint,
    backtest_fingerprint,
    cache_scope,
    expires_at,
    last_hit_at,
    hit_count,
    created_at,
    updated_at
)
VALUES
(
    @CacheKey,
    @UsId,
    @WindowDays,
    @CurveSource,
    @IsBacktest,
    @PnlSeriesJson,
    @TradeLogJson,
    @PositionLogJson,
    @OpenCloseLogJson,
    @LiveFingerprint,
    @BacktestFingerprint,
    @CacheScope,
    @ExpiresAt,
    UTC_TIMESTAMP(3),
    1,
    UTC_TIMESTAMP(3),
    UTC_TIMESTAMP(3)
)
ON DUPLICATE KEY UPDATE
    curve_source = VALUES(curve_source),
    is_backtest = VALUES(is_backtest),
    pnl_series_json = VALUES(pnl_series_json),
    trade_log_json = VALUES(trade_log_json),
    position_log_json = VALUES(position_log_json),
    open_close_log_json = VALUES(open_close_log_json),
    live_fingerprint = VALUES(live_fingerprint),
    backtest_fingerprint = VALUES(backtest_fingerprint),
    cache_scope = VALUES(cache_scope),
    expires_at = VALUES(expires_at),
    last_hit_at = UTC_TIMESTAMP(3),
    hit_count = hit_count + 1,
    updated_at = UTC_TIMESTAMP(3);";

            await _db.ExecuteAsync(sql, payload, null, ct).ConfigureAwait(false);
        }

        public sealed class StrategyCurveSnapshot
        {
            public long UsId { get; set; }
            public List<decimal> Series { get; set; } = new();
            public string CurveSource { get; set; } = "live";
            public bool IsBacktest { get; set; }
        }

        private sealed class CacheUpsertPayload
        {
            public string CacheKey { get; set; } = string.Empty;
            public long UsId { get; set; }
            public int WindowDays { get; set; }
            public string CurveSource { get; set; } = "live";
            public bool IsBacktest { get; set; }
            public string PnlSeriesJson { get; set; } = "[]";
            public string? TradeLogJson { get; set; }
            public string? PositionLogJson { get; set; }
            public string? OpenCloseLogJson { get; set; }
            public string? LiveFingerprint { get; set; }
            public string? BacktestFingerprint { get; set; }
            public string CacheScope { get; set; } = "private";
            public DateTime ExpiresAt { get; set; }
        }

        private sealed class StrategyPerformanceCacheRow
        {
            public string CacheKey { get; set; } = string.Empty;
            public long UsId { get; set; }
            public int WindowDays { get; set; }
            public string CurveSource { get; set; } = "live";
            public int IsBacktest { get; set; }
            public string? PnlSeriesJson { get; set; }
            public string? TradeLogJson { get; set; }
            public string? PositionLogJson { get; set; }
            public string? OpenCloseLogJson { get; set; }
            public string? LiveFingerprint { get; set; }
            public string? BacktestFingerprint { get; set; }
            public string CacheScope { get; set; } = "private";
            public DateTime? ExpiresAt { get; set; }
            public DateTime UpdatedAt { get; set; }
        }

        private sealed class StrategyMetaRow
        {
            public long UsId { get; set; }
            public string Visibility { get; set; } = "private";
            public string ShareCode { get; set; } = string.Empty;
            public int IsOfficial { get; set; }
            public int HasPublicPost { get; set; }
            public string ContentHash { get; set; } = string.Empty;
        }

        private sealed class LiveStatRow
        {
            public long UsId { get; set; }
            public long TradeCount { get; set; }
            public DateTime? LastTradeAt { get; set; }
            public long MaxPositionId { get; set; }
            public decimal SumRealizedPnl { get; set; }
        }

        private sealed class LiveRebuildContext
        {
            public string CacheScope { get; set; } = "private";
            public string LiveFingerprint { get; set; } = string.Empty;
        }

        private sealed class LivePositionRow
        {
            public long UsId { get; set; }
            public long PositionId { get; set; }
            public string? Exchange { get; set; }
            public string? Symbol { get; set; }
            public string? Side { get; set; }
            public decimal EntryPrice { get; set; }
            public decimal Qty { get; set; }
            public string? Status { get; set; }
            public decimal? StopLossPrice { get; set; }
            public decimal? TakeProfitPrice { get; set; }
            public int TrailingEnabled { get; set; }
            public decimal? TrailingStopPrice { get; set; }
            public int TrailingTriggered { get; set; }
            public DateTime OpenedAt { get; set; }
            public DateTime? ClosedAt { get; set; }
            public string? CloseReason { get; set; }
            public decimal? ClosePrice { get; set; }
            public decimal? RealizedPnl { get; set; }
        }

        private sealed class LiveTradeLogItem
        {
            public long PositionId { get; set; }
            public string Exchange { get; set; } = string.Empty;
            public string Symbol { get; set; } = string.Empty;
            public string Side { get; set; } = string.Empty;
            public decimal EntryPrice { get; set; }
            public decimal? ClosePrice { get; set; }
            public decimal Qty { get; set; }
            public decimal? RealizedPnl { get; set; }
            public DateTime OpenedAt { get; set; }
            public DateTime? ClosedAt { get; set; }
            public string Status { get; set; } = string.Empty;
            public string? CloseReason { get; set; }
        }

        private sealed class LivePositionLogItem
        {
            public long PositionId { get; set; }
            public string Exchange { get; set; } = string.Empty;
            public string Symbol { get; set; } = string.Empty;
            public string Side { get; set; } = string.Empty;
            public decimal EntryPrice { get; set; }
            public decimal Qty { get; set; }
            public string Status { get; set; } = string.Empty;
            public decimal? StopLossPrice { get; set; }
            public decimal? TakeProfitPrice { get; set; }
            public bool TrailingEnabled { get; set; }
            public decimal? TrailingStopPrice { get; set; }
            public bool TrailingTriggered { get; set; }
            public DateTime OpenedAt { get; set; }
            public DateTime? ClosedAt { get; set; }
            public decimal? ClosePrice { get; set; }
            public string? CloseReason { get; set; }
            public decimal? RealizedPnl { get; set; }
        }

        private sealed class LiveOpenCloseLogItem
        {
            public long PositionId { get; set; }
            public string Action { get; set; } = string.Empty;
            public DateTime EventAt { get; set; }
            public string Exchange { get; set; } = string.Empty;
            public string Symbol { get; set; } = string.Empty;
            public string Side { get; set; } = string.Empty;
            public decimal Price { get; set; }
            public decimal Qty { get; set; }
            public string Status { get; set; } = string.Empty;
            public decimal? RealizedPnl { get; set; }
            public string? CloseReason { get; set; }
        }
    }
}
