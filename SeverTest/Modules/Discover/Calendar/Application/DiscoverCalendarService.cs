using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.Discover.Domain;
using ServerTest.Modules.Discover.Infrastructure;
using ServerTest.Modules.Indicators.Infrastructure;
using ServerTest.Options;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace ServerTest.Modules.Discover.Application
{
    /// <summary>
    /// Discover 日历服务：
    /// 1) 定时从 CoinGlass 聚合源拉取三类日历并去重入库；
    /// 2) 维护内存热缓存（每模块最多 N 条）；
    /// 3) 对前端提供首次/增量/历史/时间区间拉取能力。
    /// </summary>
    public sealed class DiscoverCalendarService
    {
        private static readonly Regex MultiWhitespaceRegex = new("\\s+", RegexOptions.Compiled);
        private const int InitWarmupMinItemsFloor = 20;

        private readonly DiscoverCalendarRepository _repository;
        private readonly DiscoverCalendarMemoryCache _memoryCache;
        private readonly CoinGlassClient _coinGlassClient;
        private readonly IOptionsMonitor<DiscoverCalendarOptions> _calendarOptionsMonitor;
        private readonly IOptionsMonitor<CoinGlassOptions> _coinGlassOptionsMonitor;
        private readonly ILogger<DiscoverCalendarService> _logger;
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private int _coinGlassDisabledWarned;
        private int _coinGlassApiKeyWarned;
        private volatile bool _initialized;

        public DiscoverCalendarService(
            DiscoverCalendarRepository repository,
            DiscoverCalendarMemoryCache memoryCache,
            CoinGlassClient coinGlassClient,
            IOptionsMonitor<DiscoverCalendarOptions> calendarOptionsMonitor,
            IOptionsMonitor<CoinGlassOptions> coinGlassOptionsMonitor,
            ILogger<DiscoverCalendarService> logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            _coinGlassClient = coinGlassClient ?? throw new ArgumentNullException(nameof(coinGlassClient));
            _calendarOptionsMonitor = calendarOptionsMonitor ?? throw new ArgumentNullException(nameof(calendarOptionsMonitor));
            _coinGlassOptionsMonitor = coinGlassOptionsMonitor ?? throw new ArgumentNullException(nameof(coinGlassOptionsMonitor));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 初始化 Discover 日历模块（可重复调用，只有第一次生效）。
        /// </summary>
        public async Task EnsureInitializedAsync(CancellationToken ct = default)
        {
            if (_initialized)
            {
                return;
            }

            await _initLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_initialized)
                {
                    return;
                }

                var options = _calendarOptionsMonitor.CurrentValue;
                if (!options.Enabled)
                {
                    _logger.LogDebug("Discover 日历模块已禁用，初始化暂不执行");
                    return;
                }

                if (options.AutoCreateSchema)
                {
                    await _repository.EnsureSchemaAsync(ct).ConfigureAwait(false);
                }

                var maxItems = Math.Max(1, options.MemoryCacheMaxItems);
                var initMinItems = Math.Min(
                    maxItems,
                    Math.Max(InitWarmupMinItemsFloor, Math.Max(1, options.InitialLatestCount)));

                await ReloadCacheAsync(DiscoverCalendarKind.CentralBankActivities, maxItems, ct).ConfigureAwait(false);
                await ReloadCacheAsync(DiscoverCalendarKind.FinancialEvents, maxItems, ct).ConfigureAwait(false);
                await ReloadCacheAsync(DiscoverCalendarKind.EconomicData, maxItems, ct).ConfigureAwait(false);

                await EnsureWarmupForKindAsync(
                        DiscoverCalendarKind.CentralBankActivities,
                        options.CentralBankActivitiesPath,
                        options.Language,
                        maxItems,
                        initMinItems,
                        options.ProviderPerPage,
                        options.InitBackfillMaxPages,
                        ct)
                    .ConfigureAwait(false);

                await EnsureWarmupForKindAsync(
                        DiscoverCalendarKind.FinancialEvents,
                        options.FinancialEventsPath,
                        options.Language,
                        maxItems,
                        initMinItems,
                        options.ProviderPerPage,
                        options.InitBackfillMaxPages,
                        ct)
                    .ConfigureAwait(false);

                await EnsureWarmupForKindAsync(
                        DiscoverCalendarKind.EconomicData,
                        options.EconomicDataPath,
                        options.Language,
                        maxItems,
                        initMinItems,
                        options.ProviderPerPage,
                        options.InitBackfillMaxPages,
                        ct)
                    .ConfigureAwait(false);

                _initialized = true;
                _logger.LogInformation(
                    "Discover 日历模块初始化完成：memoryMaxItems={MemoryMaxItems} initMinItems={InitMinItems}",
                    maxItems,
                    initMinItems);
            }
            finally
            {
                _initLock.Release();
            }
        }

        /// <summary>
        /// 执行一次上游刷新（央行活动 + 财经事件 + 经济数据）。
        /// </summary>
        public async Task RefreshOnceAsync(CancellationToken ct = default)
        {
            var calendarOptions = _calendarOptionsMonitor.CurrentValue;
            if (!calendarOptions.Enabled)
            {
                return;
            }

            await EnsureInitializedAsync(ct).ConfigureAwait(false);
            if (!CanPullFromProvider())
            {
                return;
            }

            var maxItems = Math.Max(1, calendarOptions.MemoryCacheMaxItems);
            var perPage = Math.Max(1, calendarOptions.ProviderPerPage);
            var language = string.IsNullOrWhiteSpace(calendarOptions.Language)
                ? "zh"
                : calendarOptions.Language.Trim().ToLowerInvariant();

            await RefreshKindAsync(
                    DiscoverCalendarKind.CentralBankActivities,
                    calendarOptions.CentralBankActivitiesPath,
                    language,
                    page: 1,
                    perPage,
                    maxItems,
                    ct)
                .ConfigureAwait(false);

            await RefreshKindAsync(
                    DiscoverCalendarKind.FinancialEvents,
                    calendarOptions.FinancialEventsPath,
                    language,
                    page: 1,
                    perPage,
                    maxItems,
                    ct)
                .ConfigureAwait(false);

            await RefreshKindAsync(
                    DiscoverCalendarKind.EconomicData,
                    calendarOptions.EconomicDataPath,
                    language,
                    page: 1,
                    perPage,
                    maxItems,
                    ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// 前端拉取日历数据：
        /// - 无 latestId/beforeId/startTime/endTime：返回最新列表（默认 20）。
        /// - 传 latestId：返回更新增量（按 ID 升序）。
        /// - 传 beforeId：返回更早历史（按 ID 降序）。
        /// - 传 startTime/endTime：返回时间区间（按发布时间降序）。
        /// </summary>
        public async Task<DiscoverCalendarPullResult> PullAsync(
            DiscoverCalendarKind kind,
            DiscoverCalendarPullQuery query,
            CancellationToken ct = default)
        {
            if (!_calendarOptionsMonitor.CurrentValue.Enabled)
            {
                throw new InvalidOperationException("Discover 日历模块未启用");
            }

            query ??= new DiscoverCalendarPullQuery();
            await EnsureInitializedAsync(ct).ConfigureAwait(false);

            ValidatePullQuery(query);
            var options = _calendarOptionsMonitor.CurrentValue;
            var limit = ResolveLimit(query, options);
            var fetchLimit = limit + 1;

            string mode;
            IReadOnlyList<DiscoverCalendarItem> rows;

            if (query.BeforeId.HasValue)
            {
                mode = "history";
                rows = await _repository
                    .GetBeforeIdAsync(kind, query.BeforeId.Value, fetchLimit, ct)
                    .ConfigureAwait(false);
            }
            else if (query.LatestId.HasValue)
            {
                mode = "incremental";
                rows = await _repository
                    .GetAfterIdAsync(kind, query.LatestId.Value, fetchLimit, ct)
                    .ConfigureAwait(false);
            }
            else if (query.StartTime.HasValue || query.EndTime.HasValue)
            {
                mode = "range";
                rows = await _repository
                    .GetByPublishRangeAsync(kind, query.StartTime, query.EndTime, fetchLimit, ct)
                    .ConfigureAwait(false);
            }
            else
            {
                mode = "latest";
                rows = _memoryCache.GetLatestDesc(kind, fetchLimit);
                if (rows.Count == 0)
                {
                    rows = await _repository
                        .GetLatestAsync(kind, fetchLimit, ct)
                        .ConfigureAwait(false);
                }
            }

            var hasMore = rows.Count > limit;
            var items = hasMore
                ? rows.Take(limit).ToList()
                : rows.ToList();

            var latestServerId = await _repository.GetMaxIdAsync(kind, ct).ConfigureAwait(false);
            return new DiscoverCalendarPullResult
            {
                Mode = mode,
                LatestServerId = latestServerId,
                HasMore = hasMore,
                Items = items
            };
        }

        private async Task RefreshKindAsync(
            DiscoverCalendarKind kind,
            string path,
            string language,
            int page,
            int perPage,
            int memoryMaxItems,
            CancellationToken ct)
        {
            var providerItems = await PullProviderItemsAsync(
                    kind,
                    path,
                    language,
                    page,
                    perPage,
                    ct)
                .ConfigureAwait(false);
            if (providerItems.Count <= 0)
            {
                return;
            }

            var affected = await _repository
                .UpsertBatchAsync(kind, providerItems, ct)
                .ConfigureAwait(false);

            await ReloadCacheAsync(kind, memoryMaxItems, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Discover 日历刷新完成：{Kind} 拉取={Pulled} 影响行数={Affected}",
                kind,
                providerItems.Count,
                affected);
        }

        private async Task EnsureWarmupForKindAsync(
            DiscoverCalendarKind kind,
            string path,
            string language,
            int memoryMaxItems,
            int initMinItems,
            int providerPerPage,
            int initBackfillMaxPages,
            CancellationToken ct)
        {
            var cached = _memoryCache.GetLatestDesc(kind, memoryMaxItems);
            var targetCount = Math.Min(memoryMaxItems, Math.Max(1, initMinItems));
            var latestIdBeforeWarmup = await _repository.GetMaxIdAsync(kind, ct).ConfigureAwait(false);
            var isDbEmpty = latestIdBeforeWarmup <= 0;

            if (!CanPullFromProvider())
            {
                _logger.LogInformation(
                    "Discover 日历初始化仅使用本地库：{Kind} cacheCount={CacheCount} targetCount={TargetCount}",
                    kind,
                    cached.Count,
                    targetCount);
                return;
            }

            if (isDbEmpty)
            {
                await BackfillWhenEmptyAsync(
                        kind,
                        path,
                        language,
                        memoryMaxItems,
                        providerPerPage,
                        initBackfillMaxPages,
                        ct)
                    .ConfigureAwait(false);
                return;
            }

            await RefreshKindAsync(
                    kind,
                    path,
                    language,
                    page: 1,
                    providerPerPage,
                    memoryMaxItems,
                    ct)
                .ConfigureAwait(false);

            cached = _memoryCache.GetLatestDesc(kind, memoryMaxItems);
            if (cached.Count < targetCount)
            {
                await BackfillSparseAsync(
                        kind,
                        path,
                        language,
                        memoryMaxItems,
                        targetCount,
                        providerPerPage,
                        initBackfillMaxPages,
                        ct)
                    .ConfigureAwait(false);
            }
        }

        private async Task BackfillWhenEmptyAsync(
            DiscoverCalendarKind kind,
            string path,
            string language,
            int memoryMaxItems,
            int providerPerPage,
            int maxPages,
            CancellationToken ct)
        {
            var perPage = Math.Max(1, Math.Min(providerPerPage, 1000));
            var safeMaxPages = Math.Max(1, maxPages);
            var allItems = new List<DiscoverCalendarItem>(perPage);

            for (var page = 1; page <= safeMaxPages; page++)
            {
                var pageItems = await PullProviderItemsAsync(
                        kind,
                        path,
                        language,
                        page,
                        perPage,
                        ct)
                    .ConfigureAwait(false);

                if (pageItems.Count <= 0)
                {
                    break;
                }

                allItems.AddRange(pageItems);
                _logger.LogInformation(
                    "Discover 日历空表初始化拉取：{Kind} page={Page}/{MaxPages} pulled={Pulled}",
                    kind,
                    page,
                    safeMaxPages,
                    pageItems.Count);

                if (pageItems.Count < perPage)
                {
                    break;
                }
            }

            if (allItems.Count <= 0)
            {
                _logger.LogWarning("Discover 日历空表初始化未获取到上游数据：{Kind}", kind);
                return;
            }

            var deduped = allItems
                .GroupBy(item => item.DedupeKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .OrderBy(item => item.PublishTimestamp)
                .ThenBy(item => item.CalendarName, StringComparer.Ordinal)
                .ToList();

            var affected = await _repository
                .UpsertBatchAsync(kind, deduped, ct)
                .ConfigureAwait(false);

            await ReloadCacheAsync(kind, memoryMaxItems, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Discover 日历空表初始化补齐完成：{Kind} pulled={Pulled} deduped={Deduped} affected={Affected} cacheCount={CacheCount}",
                kind,
                allItems.Count,
                deduped.Count,
                affected,
                _memoryCache.GetLatestDesc(kind, memoryMaxItems).Count);
        }

        private async Task BackfillSparseAsync(
            DiscoverCalendarKind kind,
            string path,
            string language,
            int memoryMaxItems,
            int targetCount,
            int providerPerPage,
            int maxPages,
            CancellationToken ct)
        {
            var perPage = Math.Max(1, Math.Min(providerPerPage, 1000));
            var safeMaxPages = Math.Max(1, maxPages);

            var cached = _memoryCache.GetLatestDesc(kind, memoryMaxItems);
            for (var page = 2; page <= safeMaxPages && cached.Count < targetCount; page++)
            {
                var pageItems = await PullProviderItemsAsync(
                        kind,
                        path,
                        language,
                        page,
                        perPage,
                        ct)
                    .ConfigureAwait(false);
                if (pageItems.Count <= 0)
                {
                    break;
                }

                var affected = await _repository
                    .UpsertBatchAsync(kind, pageItems, ct)
                    .ConfigureAwait(false);

                await ReloadCacheAsync(kind, memoryMaxItems, ct).ConfigureAwait(false);
                cached = _memoryCache.GetLatestDesc(kind, memoryMaxItems);

                _logger.LogInformation(
                    "Discover 日历补齐：{Kind} page={Page}/{MaxPages} pulled={Pulled} affected={Affected} cacheCount={CacheCount} targetCount={TargetCount}",
                    kind,
                    page,
                    safeMaxPages,
                    pageItems.Count,
                    affected,
                    cached.Count,
                    targetCount);

                if (pageItems.Count < perPage)
                {
                    break;
                }
            }
        }

        private async Task<IReadOnlyList<DiscoverCalendarItem>> PullProviderItemsAsync(
            DiscoverCalendarKind kind,
            string path,
            string language,
            int page,
            int perPage,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                _logger.LogWarning("[coinglass][Discover-日历] 拉取跳过：{Kind} 接口路径为空", kind);
                return Array.Empty<DiscoverCalendarItem>();
            }

            var query = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["language"] = string.IsNullOrWhiteSpace(language) ? "zh" : language.Trim().ToLowerInvariant(),
                ["page"] = Math.Max(1, page).ToString(CultureInfo.InvariantCulture),
                ["per_page"] = Math.Max(1, Math.Min(1000, perPage)).ToString(CultureInfo.InvariantCulture)
            };

            using var document = await _coinGlassClient
                .GetJsonAsync(path, query, ct, "Discover-日历")
                .ConfigureAwait(false);

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var items = ParseProviderItems(kind, document.RootElement, now);
            _logger.LogInformation("[coinglass][Discover-日历] 拉取成功: kind={Kind}, 解析条数={Count}", kind, items.Count);
            return items;
        }

        private async Task ReloadCacheAsync(DiscoverCalendarKind kind, int maxItems, CancellationToken ct)
        {
            var latest = await _repository
                .GetLatestAsync(kind, Math.Max(1, maxItems), ct)
                .ConfigureAwait(false);

            _memoryCache.Replace(kind, latest);
        }

        private bool CanPullFromProvider()
        {
            var options = _coinGlassOptionsMonitor.CurrentValue;
            if (!options.Enabled)
            {
                if (Interlocked.Exchange(ref _coinGlassDisabledWarned, 1) == 0)
                {
                    _logger.LogWarning("[coinglass][Discover-日历] 刷新已跳过：CoinGlass.Enabled=false");
                }
                return false;
            }

            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                if (Interlocked.Exchange(ref _coinGlassApiKeyWarned, 1) == 0)
                {
                    _logger.LogWarning("[coinglass][Discover-日历] 刷新已跳过：CoinGlass.ApiKey 为空");
                }
                return false;
            }

            Interlocked.Exchange(ref _coinGlassDisabledWarned, 0);
            Interlocked.Exchange(ref _coinGlassApiKeyWarned, 0);
            return true;
        }

        private static IReadOnlyList<DiscoverCalendarItem> ParseProviderItems(
            DiscoverCalendarKind kind,
            JsonElement root,
            long now)
        {
            var dataArray = TryResolveDataArray(root);
            if (!dataArray.HasValue || dataArray.Value.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<DiscoverCalendarItem>();
            }

            var result = new List<DiscoverCalendarItem>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var element in dataArray.Value.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var calendarName = ReadStringFromFields(element, "calendar_name", "title", "name");
                if (string.IsNullOrWhiteSpace(calendarName))
                {
                    continue;
                }

                var publish = ReadLongFromFields(element, "publish_timestamp", "release_time", "time", "ts");
                if (!publish.HasValue || publish.Value <= 0)
                {
                    continue;
                }

                var countryCode = ReadStringFromFields(element, "country_code", "countryCode") ?? string.Empty;
                var countryName = ReadStringFromFields(element, "country_name", "countryName") ?? string.Empty;
                var importanceLevel = ReadIntFromFields(element, "importance_level", "importanceLevel") ?? 0;
                var hasExactPublishTime = (ReadIntFromFields(element, "has_exact_publish_time", "hasExactPublishTime") ?? 0) == 1;
                var dataEffect = ReadStringFromFields(element, "data_effect", "dataEffect");
                var forecastValue = ReadStringFromFields(element, "forecast_value", "forecastValue");
                var previousValue = ReadStringFromFields(element, "previous_value", "previousValue");
                var revisedPreviousValue = ReadStringFromFields(element, "revised_previous_value", "revisedPreviousValue");
                var publishedValue = ReadStringFromFields(element, "published_value", "publishedValue");

                if (kind != DiscoverCalendarKind.EconomicData)
                {
                    dataEffect = null;
                    forecastValue = null;
                    previousValue = null;
                    revisedPreviousValue = null;
                    publishedValue = null;
                }

                var normalizedPublish = NormalizeTimestamp(publish.Value);
                var dedupeKey = BuildDedupeKey(calendarName, normalizedPublish, countryCode, countryName);
                if (!seenKeys.Add(dedupeKey))
                {
                    continue;
                }

                result.Add(new DiscoverCalendarItem
                {
                    DedupeKey = dedupeKey,
                    CalendarName = calendarName.Trim(),
                    CountryCode = countryCode.Trim(),
                    CountryName = countryName.Trim(),
                    PublishTimestamp = normalizedPublish,
                    ImportanceLevel = importanceLevel,
                    HasExactPublishTime = hasExactPublishTime,
                    DataEffect = dataEffect,
                    ForecastValue = forecastValue,
                    PreviousValue = previousValue,
                    RevisedPreviousValue = revisedPreviousValue,
                    PublishedValue = publishedValue,
                    RawPayloadJson = element.GetRawText(),
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }

            // 入库前按发布时间升序，尽量保证 ID 越大越新。
            return result
                .OrderBy(item => item.PublishTimestamp)
                .ThenBy(item => item.CalendarName, StringComparer.Ordinal)
                .ToList();
        }

        private static JsonElement? TryResolveDataArray(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Array)
            {
                return root;
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var name in new[] { "data", "list", "rows" })
            {
                if (root.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array)
                {
                    return array;
                }
            }

            return null;
        }

        private static string? ReadStringFromFields(JsonElement element, params string[] fields)
        {
            foreach (var field in fields)
            {
                if (!element.TryGetProperty(field, out var value))
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
            }

            return null;
        }

        private static long? ReadLongFromFields(JsonElement element, params string[] fields)
        {
            foreach (var field in fields)
            {
                if (!element.TryGetProperty(field, out var value))
                {
                    continue;
                }

                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
                {
                    return number;
                }

                if (value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var text = value.GetString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }

                if (DateTimeOffset.TryParse(text, out var dateTime))
                {
                    return dateTime.ToUnixTimeMilliseconds();
                }
            }

            return null;
        }

        private static int? ReadIntFromFields(JsonElement element, params string[] fields)
        {
            foreach (var field in fields)
            {
                if (!element.TryGetProperty(field, out var value))
                {
                    continue;
                }

                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
                {
                    return number;
                }

                if (value.ValueKind == JsonValueKind.True)
                {
                    return 1;
                }

                if (value.ValueKind == JsonValueKind.False)
                {
                    return 0;
                }

                if (value.ValueKind == JsonValueKind.String)
                {
                    var text = value.GetString();
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    {
                        return parsed;
                    }
                }
            }

            return null;
        }

        private static long NormalizeTimestamp(long raw)
        {
            return raw < 100_000_000_000L ? raw * 1000L : raw;
        }

        private static string BuildDedupeKey(string calendarName, long publishTimestamp, string countryCode, string countryName)
        {
            var normalizedCalendarName = NormalizeForHash(calendarName);
            var normalizedCountryCode = NormalizeForHash(countryCode);
            var normalizedCountryName = NormalizeForHash(countryName);
            var raw = $"{normalizedCalendarName}|{publishTimestamp}|{normalizedCountryCode}|{normalizedCountryName}";
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private static string NormalizeForHash(string value)
        {
            var trimmed = (value ?? string.Empty).Trim();
            return MultiWhitespaceRegex.Replace(trimmed, " ");
        }

        private static void ValidatePullQuery(DiscoverCalendarPullQuery query)
        {
            if (query.LatestId.HasValue && query.LatestId.Value <= 0)
            {
                throw new ArgumentException("latestId 必须大于 0");
            }

            if (query.BeforeId.HasValue && query.BeforeId.Value <= 0)
            {
                throw new ArgumentException("beforeId 必须大于 0");
            }

            if (query.StartTime.HasValue && query.StartTime.Value <= 0)
            {
                throw new ArgumentException("startTime 必须大于 0");
            }

            if (query.EndTime.HasValue && query.EndTime.Value <= 0)
            {
                throw new ArgumentException("endTime 必须大于 0");
            }

            if (query.StartTime.HasValue && query.EndTime.HasValue && query.StartTime.Value > query.EndTime.Value)
            {
                throw new ArgumentException("startTime 不能大于 endTime");
            }

            if (query.LatestId.HasValue && query.BeforeId.HasValue)
            {
                throw new ArgumentException("latestId 与 beforeId 不能同时传入");
            }
        }

        private static int ResolveLimit(DiscoverCalendarPullQuery query, DiscoverCalendarOptions options)
        {
            var maxPullLimit = Math.Max(1, options.MaxPullLimit);
            if (query.Limit.HasValue && query.Limit.Value > 0)
            {
                return Math.Min(query.Limit.Value, maxPullLimit);
            }

            if (!query.LatestId.HasValue &&
                !query.BeforeId.HasValue &&
                !query.StartTime.HasValue &&
                !query.EndTime.HasValue)
            {
                return Math.Min(Math.Max(1, options.InitialLatestCount), maxPullLimit);
            }

            return maxPullLimit;
        }
    }
}
