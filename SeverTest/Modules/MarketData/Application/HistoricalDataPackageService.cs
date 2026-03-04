using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Models;
using ServerTest.Modules.MarketData.Domain;
using ServerTest.Modules.MarketData.Infrastructure;
using ServerTest.Options;
using ServerTest.Services;

namespace ServerTest.Modules.MarketData.Application
{
    /// <summary>
    /// 历史K线离线包打包与上传服务。
    /// </summary>
    public sealed class HistoricalDataPackageService : BaseService
    {
        private static readonly JsonSerializerOptions ManifestJsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly HistoricalMarketDataRepository _repository;
        private readonly OSSService _ossService;
        private readonly HistoricalDataPackageOptions _options;
        private readonly SemaphoreSlim _packageLock = new(1, 1);
        private HistoricalDataPackageManifest? _latestManifest;

        public HistoricalDataPackageService(
            ILogger<HistoricalDataPackageService> logger,
            HistoricalMarketDataRepository repository,
            OSSService ossService,
            IOptions<HistoricalDataPackageOptions> options)
            : base(logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _ossService = ossService ?? throw new ArgumentNullException(nameof(ossService));
            _options = options?.Value ?? new HistoricalDataPackageOptions();
        }

        private enum RemoteIntegrityState
        {
            Skipped = 0,
            Healthy = 1,
            FirstUploadRequired = 2,
            MissingLatestManifestAlias = 3,
            InvalidLatestManifest = 4,
            MissingObjects = 5,
            CheckFailed = 6
        }

        private sealed class RemoteIntegrityCheckResult
        {
            public RemoteIntegrityState State { get; set; }
            public string Message { get; set; } = string.Empty;
            public string? LatestVersion { get; set; }
            public string? NewestVersionManifestObjectKey { get; set; }
            public List<string> MissingObjectKeys { get; set; } = new();
            public bool IsHealthy => State == RemoteIntegrityState.Healthy || State == RemoteIntegrityState.Skipped;
            public bool IsFirstUpload => State == RemoteIntegrityState.FirstUploadRequired;
        }

        private sealed class ObjectKeyScanResult
        {
            public bool IsSuccess { get; set; }
            public bool IsTruncated { get; set; }
            public List<string> ObjectKeys { get; set; } = new();
            public string? ErrorMessage { get; set; }
        }

        /// <summary>
        /// 获取当前最新离线包清单快照。
        /// </summary>
        public HistoricalDataPackageManifest? GetLatestManifestSnapshot()
        {
            var manifest = _latestManifest;
            return manifest == null ? null : CloneManifest(manifest);
        }

        /// <summary>
        /// 获取当前周期保留天数配置快照。
        /// </summary>
        public Dictionary<string, int> GetRetentionDaysSnapshot()
        {
            return NormalizeRetentionDays();
        }

        public int GetUpdateIntervalMinutes()
        {
            return _options.UpdateIntervalMinutes > 0 ? _options.UpdateIntervalMinutes : 1440;
        }

        public bool IsEnabled()
        {
            return _options.Enabled;
        }

        /// <summary>
        /// 生成并上传一次离线包。
        /// </summary>
        public async Task<bool> GenerateAndUploadAsync(CancellationToken ct = default)
        {
            if (!_options.Enabled)
            {
                Logger.LogInformation("历史K线离线包任务已禁用: HistoricalDataPackage:Enabled=false");
                return false;
            }

            if (!_ossService.IsConfigured)
            {
                Logger.LogWarning("历史K线离线包上传失败: OSS 配置不完整");
                return false;
            }

            await _packageLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var prefix = NormalizePrefix(_options.PackagePrefix);
                var integrity = await CheckRemoteIntegrityAsync(prefix, ct).ConfigureAwait(false);
                var repairAction = "none";

                if (integrity.State == RemoteIntegrityState.FirstUploadRequired)
                {
                    Logger.LogInformation("历史K线离线包检测到首次上传场景: 云端不存在历史版本");
                }
                else if (integrity.State == RemoteIntegrityState.MissingLatestManifestAlias)
                {
                    Logger.LogWarning(
                        "历史K线离线包巡检发现 latest-manifest 缺失: {Message}",
                        integrity.Message);

                    if (_options.AutoRepairOnCloudMissing
                        && _options.UploadLatestManifestAlias
                        && !string.IsNullOrWhiteSpace(integrity.NewestVersionManifestObjectKey))
                    {
                        var restored = await TryRestoreLatestManifestAliasAsync(
                            prefix,
                            integrity.NewestVersionManifestObjectKey,
                            ct).ConfigureAwait(false);
                        if (restored)
                        {
                            repairAction = "restore_latest_manifest_alias";
                            integrity = await CheckRemoteIntegrityAsync(prefix, ct).ConfigureAwait(false);
                        }
                    }
                }
                else if (!integrity.IsHealthy)
                {
                    Logger.LogWarning(
                        "历史K线离线包巡检发现云端缺失: state={State}, message={Message}, missingCount={MissingCount}",
                        integrity.State,
                        integrity.Message,
                        integrity.MissingObjectKeys.Count);

                    if (integrity.MissingObjectKeys.Count > 0)
                    {
                        Logger.LogWarning(
                            "历史K线离线包缺失对象样本: {MissingObjects}",
                            string.Join(", ", integrity.MissingObjectKeys.Take(Math.Min(10, integrity.MissingObjectKeys.Count))));
                    }

                    if (_options.AutoRepairOnCloudMissing)
                    {
                        repairAction = "regenerate_for_repair";
                    }
                }

                var startedAtUtc = DateTime.UtcNow;
                var version = startedAtUtc.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
                var exchanges = ResolveExchanges();
                var symbols = ResolveSymbols();
                var retentionPlan = NormalizeRetentionDays();
                var tasks = BuildTimeframeTasks(retentionPlan);
                var datasets = new List<HistoricalDataPackageDataset>();

                Logger.LogInformation(
                    "历史K线离线包开始生成: version={Version}, exchanges={ExchangeCount}, symbols={SymbolCount}, timeframes={TimeframeCount}",
                    version,
                    exchanges.Count,
                    symbols.Count,
                    tasks.Count);

                foreach (var exchange in exchanges)
                {
                    foreach (var symbol in symbols)
                    {
                        foreach (var task in tasks)
                        {
                            ct.ThrowIfCancellationRequested();
                            var dataset = await BuildAndUploadDatasetAsync(
                                exchange,
                                symbol,
                                task.timeframe,
                                task.days,
                                version,
                                prefix,
                                startedAtUtc,
                                ct).ConfigureAwait(false);

                            if (dataset != null)
                            {
                                datasets.Add(dataset);
                            }
                        }
                    }
                }

                var manifest = new HistoricalDataPackageManifest
                {
                    Version = version,
                    GeneratedAtUtc = startedAtUtc,
                    UpdateIntervalMinutes = GetUpdateIntervalMinutes(),
                    RetentionDaysByTimeframe = retentionPlan,
                    Datasets = datasets,
                    IsFirstUpload = integrity.IsFirstUpload,
                    CloudIntegrityState = integrity.State.ToString(),
                    CloudRepairAction = repairAction
                };

                var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestJsonOptions);
                var versionManifestObjectKey = $"{prefix}/{version}/manifest.json";
                var versionUpload = await UploadManifestAsync(versionManifestObjectKey, manifestBytes, ct).ConfigureAwait(false);
                if (!versionUpload.IsSuccess || string.IsNullOrWhiteSpace(versionUpload.Url))
                {
                    Logger.LogWarning(
                        "历史K线离线包生成完成但清单上传失败: version={Version}, datasetCount={DatasetCount}, reason={Reason}",
                        version,
                        datasets.Count,
                        versionUpload.ErrorMessage ?? "unknown");
                    return false;
                }

                manifest.ManifestUrl = versionUpload.Url;

                if (_options.UploadLatestManifestAlias)
                {
                    var latestAliasObjectKey = $"{prefix}/latest-manifest.json";
                    var latestUpload = await UploadManifestAsync(latestAliasObjectKey, manifestBytes, ct).ConfigureAwait(false);
                    if (!latestUpload.IsSuccess)
                    {
                        Logger.LogWarning(
                            "历史K线 latest-manifest 上传失败: version={Version}, reason={Reason}",
                            version,
                            latestUpload.ErrorMessage ?? "unknown");
                    }
                    else if (!string.IsNullOrWhiteSpace(latestUpload.Url))
                    {
                        manifest.ManifestUrl = latestUpload.Url;
                    }
                }

                _latestManifest = CloneManifest(manifest);

                if (_options.EnableVersionCleanup)
                {
                    await CleanupOldVersionsAsync(prefix, version, ct).ConfigureAwait(false);
                }

                Logger.LogInformation(
                    "历史K线离线包生成成功: version={Version}, datasetCount={DatasetCount}, manifest={ManifestUrl}, firstUpload={FirstUpload}, integrityState={IntegrityState}, repairAction={RepairAction}",
                    manifest.Version,
                    manifest.Datasets.Count,
                    manifest.ManifestUrl,
                    manifest.IsFirstUpload,
                    manifest.CloudIntegrityState,
                    manifest.CloudRepairAction);
                return true;
            }
            finally
            {
                _packageLock.Release();
            }
        }

        private async Task<RemoteIntegrityCheckResult> CheckRemoteIntegrityAsync(string prefix, CancellationToken ct)
        {
            if (!_options.EnableCloudIntegrityCheck)
            {
                return new RemoteIntegrityCheckResult
                {
                    State = RemoteIntegrityState.Skipped,
                    Message = "已关闭云端完整性巡检"
                };
            }

            var latestAliasObjectKey = $"{prefix}/latest-manifest.json";
            var latestRead = await _ossService.ReadTextAsync(latestAliasObjectKey, ct).ConfigureAwait(false);
            if (latestRead.IsNotFound)
            {
                var newestVersionManifest = await FindNewestVersionManifestObjectKeyAsync(prefix, ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(newestVersionManifest))
                {
                    return new RemoteIntegrityCheckResult
                    {
                        State = RemoteIntegrityState.FirstUploadRequired,
                        Message = "未发现 latest-manifest 与任何历史版本清单"
                    };
                }

                return new RemoteIntegrityCheckResult
                {
                    State = RemoteIntegrityState.MissingLatestManifestAlias,
                    Message = "latest-manifest 缺失，但发现历史版本清单",
                    NewestVersionManifestObjectKey = newestVersionManifest
                };
            }

            if (!latestRead.IsSuccess || string.IsNullOrWhiteSpace(latestRead.Content))
            {
                return new RemoteIntegrityCheckResult
                {
                    State = RemoteIntegrityState.CheckFailed,
                    Message = latestRead.ErrorMessage ?? "读取 latest-manifest 失败"
                };
            }

            HistoricalDataPackageManifest? latestManifest;
            try
            {
                latestManifest = JsonSerializer.Deserialize<HistoricalDataPackageManifest>(latestRead.Content, ManifestJsonOptions);
            }
            catch (Exception ex)
            {
                return new RemoteIntegrityCheckResult
                {
                    State = RemoteIntegrityState.InvalidLatestManifest,
                    Message = $"latest-manifest JSON 解析失败: {ex.Message}"
                };
            }

            if (latestManifest == null || string.IsNullOrWhiteSpace(latestManifest.Version))
            {
                return new RemoteIntegrityCheckResult
                {
                    State = RemoteIntegrityState.InvalidLatestManifest,
                    Message = "latest-manifest 缺少 version"
                };
            }

            var missing = new List<string>();
            var reportLimit = Math.Max(1, _options.IntegrityCheckMaxReportMissing);

            var versionManifestObjectKey = $"{prefix}/{latestManifest.Version}/manifest.json";
            if (!await _ossService.ExistsAsync(versionManifestObjectKey, ct).ConfigureAwait(false))
            {
                missing.Add(versionManifestObjectKey);
            }

            foreach (var dataset in latestManifest.Datasets)
            {
                ct.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(dataset.ObjectKey))
                {
                    missing.Add($"(empty-object-key:{dataset.Id})");
                }
                else
                {
                    var exists = await _ossService.ExistsAsync(dataset.ObjectKey, ct).ConfigureAwait(false);
                    if (!exists)
                    {
                        missing.Add(dataset.ObjectKey);
                    }
                }

                if (missing.Count >= reportLimit)
                {
                    break;
                }
            }

            if (missing.Count > 0)
            {
                return new RemoteIntegrityCheckResult
                {
                    State = RemoteIntegrityState.MissingObjects,
                    Message = "latest-manifest 引用的对象存在缺失",
                    LatestVersion = latestManifest.Version,
                    MissingObjectKeys = missing
                };
            }

            return new RemoteIntegrityCheckResult
            {
                State = RemoteIntegrityState.Healthy,
                Message = "巡检通过",
                LatestVersion = latestManifest.Version
            };
        }

        private async Task<bool> TryRestoreLatestManifestAliasAsync(
            string prefix,
            string sourceManifestObjectKey,
            CancellationToken ct)
        {
            var sourceRead = await _ossService.ReadTextAsync(sourceManifestObjectKey, ct).ConfigureAwait(false);
            if (!sourceRead.IsSuccess || string.IsNullOrWhiteSpace(sourceRead.Content))
            {
                Logger.LogWarning(
                    "历史K线离线包恢复 latest-manifest 失败: 读取历史版本清单失败 source={Source}, reason={Reason}",
                    sourceManifestObjectKey,
                    sourceRead.ErrorMessage ?? "unknown");
                return false;
            }

            var latestAliasObjectKey = $"{prefix}/latest-manifest.json";
            var payload = Encoding.UTF8.GetBytes(sourceRead.Content);
            var upload = await UploadManifestAsync(latestAliasObjectKey, payload, ct).ConfigureAwait(false);
            if (!upload.IsSuccess)
            {
                Logger.LogWarning(
                    "历史K线离线包恢复 latest-manifest 失败: 上传失败 source={Source}, reason={Reason}",
                    sourceManifestObjectKey,
                    upload.ErrorMessage ?? "unknown");
                return false;
            }

            Logger.LogInformation(
                "历史K线离线包已恢复 latest-manifest: source={Source}, latestAlias={LatestAlias}",
                sourceManifestObjectKey,
                latestAliasObjectKey);
            return true;
        }

        private async Task<string?> FindNewestVersionManifestObjectKeyAsync(string prefix, CancellationToken ct)
        {
            var scan = await ScanObjectKeysAsync(prefix, Math.Max(1, _options.CleanupMaxScanObjects), ct).ConfigureAwait(false);
            if (!scan.IsSuccess)
            {
                Logger.LogWarning(
                    "历史K线离线包扫描云端对象失败: prefix={Prefix}, reason={Reason}",
                    prefix,
                    scan.ErrorMessage ?? "unknown");
                return null;
            }

            var candidates = scan.ObjectKeys
                .Select(key =>
                {
                    if (!TryExtractVersionFromObjectKey(prefix, key, out var version))
                    {
                        return (valid: false, key, version: string.Empty);
                    }

                    var isVersionManifest = key.EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase);
                    return (valid: isVersionManifest, key, version);
                })
                .Where(item => item.valid)
                .OrderByDescending(item => item.version, StringComparer.Ordinal)
                .ThenByDescending(item => item.key, StringComparer.Ordinal)
                .ToList();

            return candidates.Count > 0 ? candidates[0].key : null;
        }

        private async Task<ObjectKeyScanResult> ScanObjectKeysAsync(
            string prefix,
            int maxScanObjects,
            CancellationToken ct)
        {
            var normalizedPrefix = $"{NormalizePrefix(prefix)}/";
            var normalizedMax = Math.Max(1, maxScanObjects);
            var keys = new List<string>(Math.Min(normalizedMax, 1024));
            string? marker = null;
            var truncated = false;

            while (keys.Count < normalizedMax)
            {
                ct.ThrowIfCancellationRequested();
                var pageSize = Math.Min(1000, normalizedMax - keys.Count);
                var page = await _ossService.ListObjectKeysAsync(
                    normalizedPrefix,
                    marker,
                    pageSize,
                    ct).ConfigureAwait(false);

                if (!page.IsSuccess)
                {
                    return new ObjectKeyScanResult
                    {
                        IsSuccess = false,
                        ErrorMessage = page.ErrorMessage,
                        ObjectKeys = keys,
                        IsTruncated = truncated
                    };
                }

                if (page.ObjectKeys.Count > 0)
                {
                    keys.AddRange(page.ObjectKeys);
                }

                if (!page.IsTruncated || string.IsNullOrWhiteSpace(page.NextMarker))
                {
                    truncated = page.IsTruncated;
                    break;
                }

                marker = page.NextMarker;
            }

            if (keys.Count >= normalizedMax)
            {
                truncated = true;
            }

            return new ObjectKeyScanResult
            {
                IsSuccess = true,
                IsTruncated = truncated,
                ObjectKeys = keys
            };
        }

        private async Task CleanupOldVersionsAsync(string prefix, string currentVersion, CancellationToken ct)
        {
            try
            {
                var scan = await ScanObjectKeysAsync(
                    prefix,
                    Math.Max(1, _options.CleanupMaxScanObjects),
                    ct).ConfigureAwait(false);

                if (!scan.IsSuccess)
                {
                    Logger.LogWarning(
                        "历史K线离线包版本清理跳过: 扫描对象失败 prefix={Prefix}, reason={Reason}",
                        prefix,
                        scan.ErrorMessage ?? "unknown");
                    return;
                }

                var versionObjects = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                foreach (var key in scan.ObjectKeys)
                {
                    if (!TryExtractVersionFromObjectKey(prefix, key, out var version))
                    {
                        continue;
                    }

                    if (!versionObjects.TryGetValue(version, out var list))
                    {
                        list = new List<string>();
                        versionObjects[version] = list;
                    }

                    list.Add(key);
                }

                if (versionObjects.Count <= 1)
                {
                    return;
                }

                var versionsDesc = versionObjects.Keys
                    .OrderByDescending(item => item, StringComparer.Ordinal)
                    .ToList();

                var keepVersions = new HashSet<string>(StringComparer.Ordinal)
                {
                    currentVersion
                };

                var keepCount = Math.Max(1, _options.KeepLatestVersionCount);
                foreach (var version in versionsDesc.Take(keepCount))
                {
                    keepVersions.Add(version);
                }

                var cutoffUtc = DateTime.UtcNow.AddDays(-Math.Max(1, _options.KeepLatestVersionDays));
                foreach (var version in versionsDesc)
                {
                    if (TryParseVersionUtc(version, out var versionUtc) && versionUtc >= cutoffUtc)
                    {
                        keepVersions.Add(version);
                    }
                }

                var deleteVersions = versionsDesc
                    .Where(version => !keepVersions.Contains(version))
                    .OrderBy(version => version, StringComparer.Ordinal)
                    .ToList();

                if (deleteVersions.Count <= 0)
                {
                    return;
                }

                var maxDelete = Math.Max(1, _options.CleanupMaxDeleteObjectsPerRun);
                var deleteKeys = deleteVersions
                    .SelectMany(version => versionObjects[version])
                    .Distinct(StringComparer.Ordinal)
                    .Take(maxDelete)
                    .ToList();

                if (deleteKeys.Count <= 0)
                {
                    return;
                }

                var deletedCount = 0;
                var failedCount = 0;
                foreach (var key in deleteKeys)
                {
                    ct.ThrowIfCancellationRequested();
                    var ok = await _ossService.DeleteAsync(key).ConfigureAwait(false);
                    if (ok)
                    {
                        deletedCount += 1;
                    }
                    else
                    {
                        failedCount += 1;
                    }
                }

                Logger.LogInformation(
                    "历史K线离线包版本清理完成: deleted={Deleted}, failed={Failed}, deleteVersionCount={DeleteVersionCount}, scanTruncated={ScanTruncated}, maxDeletePerRun={MaxDelete}",
                    deletedCount,
                    failedCount,
                    deleteVersions.Count,
                    scan.IsTruncated,
                    maxDelete);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "历史K线离线包版本清理异常");
            }
        }

        private async Task<HistoricalDataPackageDataset?> BuildAndUploadDatasetAsync(
            string exchange,
            string symbol,
            string timeframe,
            int retentionDays,
            string version,
            string prefix,
            DateTime generatedAtUtc,
            CancellationToken ct)
        {
            try
            {
                var endMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var startMs = endMs - retentionDays * 86_400_000L;
                var tableName = BuildTableName(exchange, symbol, timeframe);
                var count = BuildQueryCount(startMs, endMs, timeframe);
                var rows = await _repository.QueryRangeAsync(tableName, startMs, endMs, count, ct).ConfigureAwait(false);
                if (rows.Count <= 0)
                {
                    Logger.LogInformation(
                        "历史K线离线包分片无数据: {Exchange} {Symbol} {Timeframe} retentionDays={Days}",
                        exchange,
                        symbol,
                        timeframe,
                        retentionDays);
                    return null;
                }

                var rawBytes = BuildDatasetJsonBytes(exchange, symbol, timeframe, generatedAtUtc, retentionDays, rows);
                var gzipBytes = CompressGzip(rawBytes);
                var sha256 = Convert.ToHexString(SHA256.HashData(gzipBytes)).ToLowerInvariant();
                var objectKey = BuildDatasetObjectKey(prefix, version, exchange, symbol, timeframe);

                using var uploadStream = new MemoryStream(gzipBytes, writable: false);
                var uploadResult = await _ossService.UploadAsync(uploadStream, objectKey, "application/gzip").ConfigureAwait(false);
                if (!uploadResult.IsSuccess || string.IsNullOrWhiteSpace(uploadResult.Url))
                {
                    Logger.LogWarning(
                        "历史K线离线包分片上传失败: {Exchange} {Symbol} {Timeframe}, reason={Reason}",
                        exchange,
                        symbol,
                        timeframe,
                        uploadResult.ErrorMessage ?? "unknown");
                    return null;
                }

                return new HistoricalDataPackageDataset
                {
                    Id = BuildDatasetId(exchange, symbol, timeframe),
                    Exchange = exchange,
                    Symbol = symbol,
                    Timeframe = timeframe,
                    RetentionDays = retentionDays,
                    StartTime = rows[0].OpenTime,
                    EndTime = rows[^1].OpenTime,
                    Count = rows.Count,
                    RawBytes = rawBytes.LongLength,
                    CompressedBytes = gzipBytes.LongLength,
                    Sha256 = sha256,
                    ObjectKey = objectKey,
                    Url = uploadResult.Url
                };
            }
            catch (Exception ex)
            {
                Logger.LogWarning(
                    ex,
                    "历史K线离线包分片构建失败: {Exchange} {Symbol} {Timeframe}",
                    exchange,
                    symbol,
                    timeframe);
                return null;
            }
        }

        private async Task<OSSUploadResult> UploadManifestAsync(
            string objectKey,
            byte[] payload,
            CancellationToken ct)
        {
            using var stream = new MemoryStream(payload, writable: false);
            ct.ThrowIfCancellationRequested();
            return await _ossService.UploadAsync(stream, objectKey, "application/json").ConfigureAwait(false);
        }

        private static bool TryExtractVersionFromObjectKey(string prefix, string objectKey, out string version)
        {
            version = string.Empty;
            if (string.IsNullOrWhiteSpace(objectKey))
            {
                return false;
            }

            var normalizedPrefix = $"{NormalizePrefix(prefix)}/";
            if (!objectKey.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var remain = objectKey.Substring(normalizedPrefix.Length);
            var splitIndex = remain.IndexOf('/');
            if (splitIndex <= 0)
            {
                return false;
            }

            var candidate = remain[..splitIndex];
            if (!TryParseVersionUtc(candidate, out _))
            {
                return false;
            }

            version = candidate;
            return true;
        }

        private static bool TryParseVersionUtc(string version, out DateTime utc)
        {
            return DateTime.TryParseExact(
                version,
                "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out utc);
        }

        private static string BuildDatasetId(string exchange, string symbol, string timeframe)
        {
            var symbolKey = MarketDataKeyNormalizer.NormalizeSymbol(symbol);
            return $"{MarketDataKeyNormalizer.NormalizeExchange(exchange)}|{symbolKey}|{MarketDataKeyNormalizer.NormalizeTimeframe(timeframe)}";
        }

        private static string BuildDatasetObjectKey(string prefix, string version, string exchange, string symbol, string timeframe)
        {
            var symbolKey = MarketDataKeyNormalizer.NormalizeSymbol(symbol)
                .Replace("/", string.Empty, StringComparison.Ordinal)
                .ToLowerInvariant();
            var exchangeKey = MarketDataKeyNormalizer.NormalizeExchange(exchange);
            var timeframeKey = MarketDataKeyNormalizer.NormalizeTimeframe(timeframe);
            return $"{prefix}/{version}/datasets/{exchangeKey}_{symbolKey}_{timeframeKey}.json.gz";
        }

        private int BuildQueryCount(long startMs, long endMs, string timeframe)
        {
            var timeframeMs = MarketDataConfig.TimeframeToMs(timeframe);
            var expected = (int)Math.Ceiling(Math.Max(1, endMs - startMs) / (double)timeframeMs) + 1;
            var buffer = Math.Max(0, _options.ExtraBarsBuffer);
            var total = expected + buffer;
            return Math.Min(Math.Max(1, total), 5_000_000);
        }

        private static byte[] BuildDatasetJsonBytes(
            string exchange,
            string symbol,
            string timeframe,
            DateTime generatedAtUtc,
            int retentionDays,
            IReadOnlyList<HistoricalMarketDataKlineRow> rows)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("exchange", exchange);
                writer.WriteString("symbol", MarketDataKeyNormalizer.NormalizeSymbol(symbol));
                writer.WriteString("timeframe", MarketDataKeyNormalizer.NormalizeTimeframe(timeframe));
                writer.WriteString("generatedAtUtc", generatedAtUtc);
                writer.WriteNumber("retentionDays", retentionDays);
                writer.WriteNumber("startTime", rows[0].OpenTime);
                writer.WriteNumber("endTime", rows[^1].OpenTime);
                writer.WriteNumber("count", rows.Count);

                writer.WritePropertyName("bars");
                writer.WriteStartArray();
                foreach (var row in rows)
                {
                    writer.WriteStartArray();
                    writer.WriteNumberValue(row.OpenTime);
                    writer.WriteNumberValue((double)(row.Open ?? 0m));
                    writer.WriteNumberValue((double)(row.High ?? 0m));
                    writer.WriteNumberValue((double)(row.Low ?? 0m));
                    writer.WriteNumberValue((double)(row.Close ?? 0m));
                    writer.WriteNumberValue((double)(row.Volume ?? 0m));
                    writer.WriteEndArray();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
                writer.Flush();
            }

            return stream.ToArray();
        }

        private static byte[] CompressGzip(byte[] input)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                gzip.Write(input, 0, input.Length);
            }

            return output.ToArray();
        }

        private static string BuildTableName(string exchange, string symbol, string timeframe)
        {
            var exchangeKey = MarketDataKeyNormalizer.NormalizeExchange(exchange);
            var symbolKey = MarketDataKeyNormalizer.NormalizeSymbol(symbol);
            var timeframeKey = MarketDataKeyNormalizer.NormalizeTimeframe(timeframe);
            var symbolPart = symbolKey.Replace("/", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
            return $"{exchangeKey}_futures_{symbolPart}_{timeframeKey}";
        }

        private static string NormalizePrefix(string prefix)
        {
            var trimmed = (prefix ?? string.Empty).Trim().Trim('/');
            return string.IsNullOrWhiteSpace(trimmed) ? "marketdata/kline-offline" : trimmed;
        }

        private List<string> ResolveExchanges()
        {
            var exchanges = (_options.Exchanges ?? Array.Empty<string>())
                .Select(MarketDataKeyNormalizer.NormalizeExchange)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (exchanges.Count == 0)
            {
                exchanges.Add("binance");
            }

            return exchanges;
        }

        private List<string> ResolveSymbols()
        {
            var fromConfig = (_options.Symbols ?? Array.Empty<string>())
                .Select(MarketDataKeyNormalizer.NormalizeSymbol)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (fromConfig.Count > 0)
            {
                return fromConfig;
            }

            return Enum.GetValues<MarketDataConfig.SymbolEnum>()
                .Select(MarketDataConfig.SymbolToString)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private Dictionary<string, int> NormalizeRetentionDays()
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var supported = Enum.GetValues<MarketDataConfig.TimeframeEnum>()
                .Select(MarketDataConfig.TimeframeToString)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var source = _options.RetentionDaysByTimeframe ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in source)
            {
                var timeframe = MarketDataKeyNormalizer.NormalizeTimeframe(pair.Key);
                if (string.IsNullOrWhiteSpace(timeframe) || !supported.Contains(timeframe))
                {
                    continue;
                }

                result[timeframe] = Math.Max(0, pair.Value);
            }

            return result;
        }

        private static List<(string timeframe, int days)> BuildTimeframeTasks(Dictionary<string, int> retentionPlan)
        {
            return retentionPlan
                .Where(pair => pair.Value > 0)
                .Select(pair => (timeframe: pair.Key, days: pair.Value))
                .OrderBy(pair => MarketDataConfig.TimeframeToMs(pair.timeframe))
                .ToList();
        }

        private static HistoricalDataPackageManifest CloneManifest(HistoricalDataPackageManifest manifest)
        {
            var json = JsonSerializer.Serialize(manifest, ManifestJsonOptions);
            return JsonSerializer.Deserialize<HistoricalDataPackageManifest>(json, ManifestJsonOptions)
                ?? new HistoricalDataPackageManifest();
        }
    }
}
