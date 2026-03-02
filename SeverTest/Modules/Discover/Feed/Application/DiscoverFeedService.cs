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
    /// Discover 新闻/快讯服务：
    /// 1) 定时从 CoinGlass 聚合源拉取数据并去重入库；
    /// 2) 维护内存热缓存（每模块最多 N 条）；
    /// 3) 对前端提供首次/增量/下拉历史拉取能力。
    /// </summary>
    public sealed class DiscoverFeedService
    {
        private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex MultiWhitespaceRegex = new("\\s+", RegexOptions.Compiled);
        private static readonly Regex CjkRegex = new("[\\u4e00-\\u9fff]", RegexOptions.Compiled);
        private const int InitWarmupMinItemsFloor = 20;

        private readonly DiscoverFeedRepository _repository;
        private readonly DiscoverFeedMemoryCache _memoryCache;
        private readonly CoinGlassClient _coinGlassClient;
        private readonly IOptionsMonitor<DiscoverFeedOptions> _discoverOptionsMonitor;
        private readonly IOptionsMonitor<CoinGlassOptions> _coinGlassOptionsMonitor;
        private readonly ILogger<DiscoverFeedService> _logger;
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private int _coinGlassDisabledWarned;
        private int _coinGlassApiKeyWarned;
        private volatile bool _initialized;

        public DiscoverFeedService(
            DiscoverFeedRepository repository,
            DiscoverFeedMemoryCache memoryCache,
            CoinGlassClient coinGlassClient,
            IOptionsMonitor<DiscoverFeedOptions> discoverOptionsMonitor,
            IOptionsMonitor<CoinGlassOptions> coinGlassOptionsMonitor,
            ILogger<DiscoverFeedService> logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            _coinGlassClient = coinGlassClient ?? throw new ArgumentNullException(nameof(coinGlassClient));
            _discoverOptionsMonitor = discoverOptionsMonitor ?? throw new ArgumentNullException(nameof(discoverOptionsMonitor));
            _coinGlassOptionsMonitor = coinGlassOptionsMonitor ?? throw new ArgumentNullException(nameof(coinGlassOptionsMonitor));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 初始化 Discover 模块（可重复调用，只有第一次生效）。
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

                var options = _discoverOptionsMonitor.CurrentValue;
                if (!options.Enabled)
                {
                    _logger.LogDebug("Discover 资讯模块已禁用，初始化暂不执行");
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

                await ReloadCacheAsync(DiscoverFeedKind.Article, maxItems, ct).ConfigureAwait(false);
                await ReloadCacheAsync(DiscoverFeedKind.Newsflash, maxItems, ct).ConfigureAwait(false);

                // 初始化阶段：启动即拉新；若本地不足则继续尝试补齐，尽量保证“足够新、足够多”。
                await EnsureWarmupForKindAsync(
                        DiscoverFeedKind.Article,
                        options.ArticleListPath,
                        options.ArticleLanguage,
                        options.PreferChineseContent,
                        maxItems,
                        initMinItems,
                        options.ProviderPerPage,
                        options.InitBackfillMaxPages,
                        ct)
                    .ConfigureAwait(false);

                await EnsureWarmupForKindAsync(
                        DiscoverFeedKind.Newsflash,
                        options.NewsflashListPath,
                        options.NewsflashLanguage,
                        false,
                        maxItems,
                        initMinItems,
                        options.ProviderPerPage,
                        options.InitBackfillMaxPages,
                        ct)
                    .ConfigureAwait(false);

                _initialized = true;
                _logger.LogInformation(
                    "Discover 资讯模块初始化完成：memoryMaxItems={MemoryMaxItems} initMinItems={InitMinItems}\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n",
                    maxItems,
                    initMinItems);
            }
            finally
            {
                _initLock.Release();
            }
        }

        /// <summary>
        /// 执行一次上游刷新（新闻 + 快讯）。
        /// </summary>
        public async Task RefreshOnceAsync(CancellationToken ct = default)
        {
            var discoverOptions = _discoverOptionsMonitor.CurrentValue;
            if (!discoverOptions.Enabled)
            {
                return;
            }

            await EnsureInitializedAsync(ct).ConfigureAwait(false);

            if (!CanPullFromProvider())
            {
                return;
            }

            var maxItems = Math.Max(1, discoverOptions.MemoryCacheMaxItems);
            var perPage = Math.Max(1, discoverOptions.ProviderPerPage);

            await RefreshKindAsync(
                    DiscoverFeedKind.Article,
                    discoverOptions.ArticleListPath,
                    discoverOptions.ArticleLanguage,
                    page: 1,
                    perPage,
                    discoverOptions.PreferChineseContent,
                    maxItems,
                    ct)
                .ConfigureAwait(false);

            await RefreshKindAsync(
                    DiscoverFeedKind.Newsflash,
                    discoverOptions.NewsflashListPath,
                    discoverOptions.NewsflashLanguage,
                    page: 1,
                    perPage,
                    preferChineseOnly: false,
                    maxItems,
                    ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// 前端拉取资讯数据：
        /// - 无 latestId/beforeId：返回最新列表（默认 20）。
        /// - 传 latestId：返回更新增量（按 ID 升序）。
        /// - 传 beforeId：返回更早历史（按 ID 降序）。
        /// </summary>
        public async Task<DiscoverPullResult> PullAsync(
            DiscoverFeedKind kind,
            DiscoverPullQuery query,
            CancellationToken ct = default)
        {
            if (!_discoverOptionsMonitor.CurrentValue.Enabled)
            {
                throw new InvalidOperationException("Discover 资讯模块未启用");
            }

            query ??= new DiscoverPullQuery();
            await EnsureInitializedAsync(ct).ConfigureAwait(false);

            ValidatePullQuery(query);
            var options = _discoverOptionsMonitor.CurrentValue;
            var limit = ResolveLimit(query, options);
            var fetchLimit = limit + 1;

            string mode;
            IReadOnlyList<DiscoverFeedItem> rows;

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

            rows = ApplyDisplayFilter(kind, rows, options);

            var hasMore = rows.Count > limit;
            var items = hasMore
                ? rows.Take(limit).ToList()
                : rows.ToList();

            var latestServerId = await ResolveLatestServerIdAsync(kind, ct).ConfigureAwait(false);
            return new DiscoverPullResult
            {
                Mode = mode,
                LatestServerId = latestServerId,
                HasMore = hasMore,
                Items = items
            };
        }

        private async Task RefreshKindAsync(
            DiscoverFeedKind kind,
            string path,
            string language,
            int page,
            int perPage,
            bool preferChineseOnly,
            int memoryMaxItems,
            CancellationToken ct)
        {
            var (pulled, inserted) = await PullProviderAndStoreAsync(
                    kind,
                    path,
                    language,
                    page,
                    perPage,
                    preferChineseOnly,
                    ct)
                .ConfigureAwait(false);
            if (pulled <= 0)
            {
                return;
            }

            var bounds = _memoryCache.GetIdBounds(kind);
            if (inserted > 0 || bounds.LatestId <= 0)
            {
                await ReloadCacheAsync(kind, memoryMaxItems, ct).ConfigureAwait(false);
            }

            _logger.LogInformation(
                "Discover 刷新完成：{Kind} 拉取={Pulled} 新增={Inserted}",
                kind,
                pulled,
                inserted);
        }

        private async Task EnsureWarmupForKindAsync(
            DiscoverFeedKind kind,
            string path,
            string language,
            bool preferChineseOnly,
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
                    "Discover 初始化仅使用本地库：{Kind} cacheCount={CacheCount} targetCount={TargetCount}",
                    kind,
                    cached.Count,
                    targetCount);
                return;
            }

            var totalInserted = 0;
            var perPage = Math.Max(1, Math.Min(providerPerPage, 1000));
            var maxPages = Math.Max(1, initBackfillMaxPages);

            for (var page = 1; page <= maxPages; page++)
            {
                // 非空表：达到目标条数后可停止。
                // 空表：尽量多拉，直到没有新增或达到页数上限。
                if (!isDbEmpty && page > 1 && cached.Count >= targetCount)
                {
                    break;
                }

                try
                {
                    var (pulled, inserted) = await PullProviderAndStoreAsync(
                            kind,
                            path,
                            language,
                            page,
                            perPage,
                            preferChineseOnly,
                            ct)
                        .ConfigureAwait(false);
                    totalInserted += inserted;

                    if (inserted > 0 || cached.Count <= 0)
                    {
                        await ReloadCacheAsync(kind, memoryMaxItems, ct).ConfigureAwait(false);
                        cached = _memoryCache.GetLatestDesc(kind, memoryMaxItems);
                    }

                    _logger.LogInformation(
                        "Discover 初始化拉新：{Kind} page={Page}/{MaxPages} pulled={Pulled} inserted={Inserted} cacheCount={CacheCount} targetCount={TargetCount} language={Language} chineseOnly={ChineseOnly} dbEmpty={DbEmpty}",
                        kind,
                        page,
                        maxPages,
                        pulled,
                        inserted,
                        cached.Count,
                        targetCount,
                        language,
                        preferChineseOnly,
                        isDbEmpty);

                    if (!isDbEmpty && cached.Count >= targetCount)
                    {
                        break;
                    }

                    if (inserted <= 0)
                    {
                        // 上游当前没有更多新增数据，结束补齐尝试。
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Discover 初始化拉新失败：{Kind} page={Page}/{MaxPages}",
                        kind,
                        page,
                        maxPages);
                    break;
                }
            }

            if (cached.Count < targetCount)
            {
                _logger.LogWarning(
                    "Discover 初始化完成但数量不足：{Kind} cacheCount={CacheCount} targetCount={TargetCount} totalInserted={TotalInserted}",
                    kind,
                    cached.Count,
                    targetCount,
                    totalInserted);
            }

            if (isDbEmpty)
            {
                _logger.LogInformation(
                    "Discover 空表初始化补齐完成：{Kind} inserted={Inserted} cacheCount={CacheCount} maxPages={MaxPages}",
                    kind,
                    totalInserted,
                    cached.Count,
                    maxPages);
            }
        }

        private async Task<(int Pulled, int Inserted)> PullProviderAndStoreAsync(
            DiscoverFeedKind kind,
            string path,
            string language,
            int page,
            int perPage,
            bool preferChineseOnly,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                _logger.LogWarning("[coinglass][Discover-Feed] 拉取跳过：{Kind} 接口路径为空", kind);
                return (0, 0);
            }

            var query = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["language"] = string.IsNullOrWhiteSpace(language) ? "zh" : language.Trim().ToLowerInvariant(),
                ["page"] = Math.Max(1, page).ToString(CultureInfo.InvariantCulture),
                ["per_page"] = Math.Max(1, Math.Min(1000, perPage)).ToString(CultureInfo.InvariantCulture)
            };

            using var document = await _coinGlassClient
                .GetJsonAsync(path, query, ct, "Discover-Feed")
                .ConfigureAwait(false);

            var createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var providerItems = ParseProviderItems(
                kind,
                document.RootElement,
                createdAt,
                preferChineseOnly);
            if (providerItems.Count == 0)
            {
                _logger.LogDebug("[coinglass][Discover-Feed] 拉取完成：{Kind} 上游无可入库数据", kind);
                return (0, 0);
            }

            _logger.LogInformation("[coinglass][Discover-Feed] 拉取成功: kind={Kind}, 解析条数={Count}", kind, providerItems.Count);

            var inserted = await _repository
                .InsertIgnoreBatchAsync(kind, providerItems, ct)
                .ConfigureAwait(false);

            _logger.LogDebug("[coinglass][Discover-Feed] 入库完成: kind={Kind}, 本次入库={Inserted}", kind, inserted);
            return (providerItems.Count, inserted);
        }

        private async Task ReloadCacheAsync(DiscoverFeedKind kind, int maxItems, CancellationToken ct)
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
                    _logger.LogWarning("[coinglass][Discover-Feed] 刷新已跳过：CoinGlass.Enabled=false");
                }
                return false;
            }

            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                if (Interlocked.Exchange(ref _coinGlassApiKeyWarned, 1) == 0)
                {
                    _logger.LogWarning("[coinglass][Discover-Feed] 刷新已跳过：CoinGlass.ApiKey 为空");
                }
                return false;
            }

            Interlocked.Exchange(ref _coinGlassDisabledWarned, 0);
            Interlocked.Exchange(ref _coinGlassApiKeyWarned, 0);
            return true;
        }

        private static IReadOnlyList<DiscoverFeedItem> ParseProviderItems(
            DiscoverFeedKind kind,
            JsonElement root,
            long createdAt,
            bool preferChineseOnly)
        {
            var dataArray = TryResolveDataArray(root);
            if (!dataArray.HasValue || dataArray.Value.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<DiscoverFeedItem>();
            }

            var result = new List<DiscoverFeedItem>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var element in dataArray.Value.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var title = kind == DiscoverFeedKind.Article
                    ? ReadStringFromFields(element, "article_title", "title")
                    : ReadStringFromFields(element, "newsflash_title", "title");
                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                var releaseTime = kind == DiscoverFeedKind.Article
                    ? ReadLongFromFields(element, "article_release_time", "release_time", "time", "ts")
                    : ReadLongFromFields(element, "newsflash_release_time", "release_time", "time", "ts");
                if (!releaseTime.HasValue || releaseTime.Value <= 0)
                {
                    continue;
                }

                var sourceName = ReadStringFromFields(element, "source_name", "source", "sourceName");
                sourceName = string.IsNullOrWhiteSpace(sourceName) ? "unknown" : sourceName;

                var summaryRaw = kind == DiscoverFeedKind.Article
                    ? ReadStringFromFields(element, "article_description", "summary", "description")
                    : ReadStringFromFields(element, "summary", "description");
                var contentHtml = kind == DiscoverFeedKind.Article
                    ? ReadStringFromFields(element, "article_content", "content", "body")
                    : ReadStringFromFields(element, "newsflash_content", "content", "body");

                var summaryText = BuildSummary(summaryRaw, contentHtml);
                var sourceLogo = ReadStringFromFields(element, "source_website_logo", "source_logo", "sourceLogo");
                var pictureUrl = kind == DiscoverFeedKind.Article
                    ? ReadStringFromFields(element, "article_picture", "picture", "picture_url")
                    : ReadStringFromFields(element, "newsflash_picture", "picture", "picture_url");

                if (preferChineseOnly &&
                    !ContainsChinese(title) &&
                    !ContainsChinese(summaryText) &&
                    !ContainsChinese(contentHtml))
                {
                    continue;
                }

                var normalizedReleaseTime = NormalizeTimestamp(releaseTime.Value);
                var dedupeKey = BuildDedupeKey(title, normalizedReleaseTime, sourceName);
                if (!seenKeys.Add(dedupeKey))
                {
                    continue;
                }

                result.Add(new DiscoverFeedItem
                {
                    DedupeKey = dedupeKey,
                    Title = title.Trim(),
                    Summary = summaryText,
                    ContentHtml = contentHtml ?? string.Empty,
                    SourceName = sourceName.Trim(),
                    SourceLogo = sourceLogo,
                    PictureUrl = pictureUrl,
                    ReleaseTime = normalizedReleaseTime,
                    RawPayloadJson = element.GetRawText(),
                    CreatedAt = createdAt
                });
            }

            // 入库前按发布时间升序，保证 ID 增长方向与“越新越大”一致。
            return result
                .OrderBy(item => item.ReleaseTime)
                .ThenBy(item => item.Title, StringComparer.Ordinal)
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

        private static string BuildSummary(string? summaryRaw, string? contentHtml)
        {
            if (!string.IsNullOrWhiteSpace(summaryRaw))
            {
                return TruncatePlainText(summaryRaw, 280);
            }

            if (string.IsNullOrWhiteSpace(contentHtml))
            {
                return string.Empty;
            }

            return TruncatePlainText(contentHtml, 280);
        }

        private static string TruncatePlainText(string input, int maxLength)
        {
            var text = HtmlTagRegex.Replace(input, " ");
            text = MultiWhitespaceRegex.Replace(text, " ").Trim();
            if (text.Length <= maxLength)
            {
                return text;
            }

            return text[..maxLength];
        }

        private static long NormalizeTimestamp(long raw)
        {
            return raw < 100_000_000_000L ? raw * 1000L : raw;
        }

        private static string BuildDedupeKey(string title, long releaseTime, string source)
        {
            var normalizedTitle = NormalizeForHash(title);
            var normalizedSource = NormalizeForHash(source);
            var raw = $"{normalizedTitle}|{releaseTime}|{normalizedSource}";
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private static string NormalizeForHash(string value)
        {
            var trimmed = (value ?? string.Empty).Trim();
            return MultiWhitespaceRegex.Replace(trimmed, " ");
        }

        private static bool ContainsChinese(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            return CjkRegex.IsMatch(text);
        }

        private static void ValidatePullQuery(DiscoverPullQuery query)
        {
            if (query.LatestId.HasValue && query.LatestId.Value <= 0)
            {
                throw new ArgumentException("latestId 必须大于 0");
            }

            if (query.BeforeId.HasValue && query.BeforeId.Value <= 0)
            {
                throw new ArgumentException("beforeId 必须大于 0");
            }

            if (query.LatestId.HasValue && query.BeforeId.HasValue)
            {
                throw new ArgumentException("latestId 与 beforeId 不能同时传入");
            }
        }

        private static int ResolveLimit(DiscoverPullQuery query, DiscoverFeedOptions options)
        {
            var maxPullLimit = Math.Max(1, options.MaxPullLimit);
            if (query.Limit.HasValue && query.Limit.Value > 0)
            {
                return Math.Min(query.Limit.Value, maxPullLimit);
            }

            if (!query.LatestId.HasValue && !query.BeforeId.HasValue)
            {
                return Math.Min(Math.Max(1, options.InitialLatestCount), maxPullLimit);
            }

            return maxPullLimit;
        }

        private async Task<long> ResolveLatestServerIdAsync(DiscoverFeedKind kind, CancellationToken ct)
        {
            var bounds = _memoryCache.GetIdBounds(kind);
            if (bounds.LatestId > 0)
            {
                return bounds.LatestId;
            }

            return await _repository.GetMaxIdAsync(kind, ct).ConfigureAwait(false);
        }

        private static IReadOnlyList<DiscoverFeedItem> ApplyDisplayFilter(
            DiscoverFeedKind kind,
            IReadOnlyList<DiscoverFeedItem> rows,
            DiscoverFeedOptions options)
        {
            if (kind != DiscoverFeedKind.Article || !options.PreferChineseContent || rows.Count == 0)
            {
                return rows;
            }

            return rows
                .Where(item =>
                    ContainsChinese(item.Title) ||
                    ContainsChinese(item.Summary) ||
                    ContainsChinese(item.ContentHtml))
                .ToList();
        }
    }
}
