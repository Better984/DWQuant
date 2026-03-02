using Microsoft.AspNetCore.Mvc;
using ServerTest.Controllers;
using Microsoft.Extensions.Logging;
using ServerTest.Models;
using ServerTest.Modules.Indicators.Application;
using ServerTest.Modules.Indicators.Domain;
using ServerTest.Modules.Indicators.Infrastructure;
using ServerTest.Protocol;
using System.Text.Json;

namespace ServerTest.Modules.Indicators.Controllers
{
    [ApiController]
    [Route("api/indicator")]
    public sealed class IndicatorController : BaseController
    {
        private readonly IndicatorQueryService _queryService;
        private readonly CoinGlassRealtimeCache _realtimeCache;

        public IndicatorController(
            ILogger<IndicatorController> logger,
            IndicatorQueryService queryService,
            CoinGlassRealtimeCache realtimeCache)
            : base(logger)
        {
            _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
            _realtimeCache = realtimeCache ?? throw new ArgumentNullException(nameof(realtimeCache));
        }

        public sealed class IndicatorMetaListRequest
        {
            public bool IncludeDisabled { get; set; }
        }

        public sealed class IndicatorLatestRequest
        {
            public string Code { get; set; } = string.Empty;
            public Dictionary<string, string>? Scope { get; set; }
            public bool AllowStale { get; set; } = true;
            public bool ForceRefresh { get; set; }
        }

        public sealed class IndicatorBatchLatestRequest
        {
            public List<string>? Codes { get; set; }
            public Dictionary<string, string>? Scope { get; set; }
            public bool AllowStale { get; set; } = true;
        }

        public sealed class IndicatorHistoryRequest
        {
            public string Code { get; set; } = string.Empty;
            public Dictionary<string, string>? Scope { get; set; }
            public string? StartTime { get; set; }
            public string? EndTime { get; set; }
            public int Limit { get; set; } = 200;
        }

        public sealed class IndicatorRealtimeChannelRequest
        {
            public string Channel { get; set; } = string.Empty;
            public bool AllowStale { get; set; } = true;
        }

        [ProtocolType("indicator.meta.list")]
        [HttpPost("meta/list")]
        public async Task<IActionResult> MetaList([FromBody] ProtocolRequest<IndicatorMetaListRequest> request)
        {
            try
            {
                var payload = request.Data ?? new IndicatorMetaListRequest();
                var definitions = await _queryService
                    .GetDefinitionsAsync(payload.IncludeDisabled, HttpContext.RequestAborted)
                    .ConfigureAwait(false);

                var result = definitions.Select(definition => new
                {
                    code = definition.Code,
                    provider = definition.Provider,
                    sourceType = ResolveSourceType(definition),
                    displayName = definition.DisplayName,
                    shape = definition.Shape,
                    unit = definition.Unit,
                    description = definition.Description,
                    refreshIntervalSec = definition.RefreshIntervalSec,
                    ttlSec = definition.TtlSec,
                    historyRetentionDays = definition.HistoryRetentionDays,
                    defaultScopeKey = definition.DefaultScopeKey,
                    enabled = definition.Enabled,
                    sortOrder = definition.SortOrder,
                    fields = BuildMetaFields(definition)
                }).ToList();

                return Ok(ApiResponse<object>.Ok(new { items = result, total = result.Count }, "查询成功"));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "查询指标元数据失败");
                return StatusCode(500, ApiResponse<object>.Error($"查询失败: {ex.Message}"));
            }
        }

        [ProtocolType("indicator.latest.get")]
        [HttpPost("latest/get")]
        public async Task<IActionResult> LatestGet([FromBody] ProtocolRequest<IndicatorLatestRequest> request)
        {
            var payload = request.Data;
            if (payload == null || string.IsNullOrWhiteSpace(payload.Code))
            {
                return BadRequest(ApiResponse<object>.Error("code 不能为空"));
            }

            try
            {
                var result = await _queryService
                    .GetLatestAsync(
                        payload.Code,
                        payload.Scope,
                        payload.AllowStale,
                        payload.ForceRefresh,
                        HttpContext.RequestAborted)
                    .ConfigureAwait(false);

                return Ok(ApiResponse<object>.Ok(ToLatestDto(result), "查询成功"));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ApiResponse<object>.Error(ex.Message));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "查询指标最新值失败: code={Code}", payload.Code);
                return StatusCode(500, ApiResponse<object>.Error($"查询失败: {ex.Message}"));
            }
        }

        [ProtocolType("indicator.batch.latest")]
        [HttpPost("batch/latest")]
        public async Task<IActionResult> BatchLatest([FromBody] ProtocolRequest<IndicatorBatchLatestRequest> request)
        {
            var payload = request.Data;
            if (payload?.Codes == null || payload.Codes.Count == 0)
            {
                return BadRequest(ApiResponse<object>.Error("codes 不能为空"));
            }

            try
            {
                var results = await _queryService
                    .GetBatchLatestAsync(payload.Codes, payload.Scope, payload.AllowStale, HttpContext.RequestAborted)
                    .ConfigureAwait(false);

                var items = results.Select(ToLatestDto).ToList();
                return Ok(ApiResponse<object>.Ok(new { items, total = items.Count }, "查询成功"));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "批量查询指标失败");
                return StatusCode(500, ApiResponse<object>.Error($"查询失败: {ex.Message}"));
            }
        }

        [ProtocolType("indicator.history.get")]
        [HttpPost("history/get")]
        public async Task<IActionResult> HistoryGet([FromBody] ProtocolRequest<IndicatorHistoryRequest> request)
        {
            var payload = request.Data;
            if (payload == null || string.IsNullOrWhiteSpace(payload.Code))
            {
                return BadRequest(ApiResponse<object>.Error("code 不能为空"));
            }

            if (payload.Limit <= 0)
            {
                return BadRequest(ApiResponse<object>.Error("limit 必须大于 0"));
            }

            if (!TryParseTime(payload.StartTime, out var startMs))
            {
                return BadRequest(ApiResponse<object>.Error("startTime 格式错误，建议使用 ISO8601 或 yyyy-MM-dd HH:mm:ss"));
            }

            if (!TryParseTime(payload.EndTime, out var endMs))
            {
                return BadRequest(ApiResponse<object>.Error("endTime 格式错误，建议使用 ISO8601 或 yyyy-MM-dd HH:mm:ss"));
            }

            try
            {
                var history = await _queryService
                    .GetHistoryAsync(
                        payload.Code,
                        payload.Scope,
                        startMs,
                        endMs,
                        payload.Limit,
                        HttpContext.RequestAborted)
                    .ConfigureAwait(false);

                var points = history.Points.Select(point => new
                {
                    sourceTs = point.SourceTs,
                    payload = IndicatorQueryService.ParsePayload(point.PayloadJson)
                }).ToList();

                return Ok(ApiResponse<object>.Ok(new
                {
                    code = history.Definition.Code,
                    displayName = history.Definition.DisplayName,
                    unit = history.Definition.Unit,
                    shape = history.Definition.Shape,
                    scopeKey = history.ScopeKey,
                    points,
                    total = points.Count
                }, "查询成功"));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ApiResponse<object>.Error(ex.Message));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "查询指标历史失败: code={Code}", payload.Code);
                return StatusCode(500, ApiResponse<object>.Error($"查询失败: {ex.Message}"));
            }
        }

        [ProtocolType("indicator.realtime.channel.get")]
        [HttpPost("realtime/channel/get")]
        public async Task<IActionResult> RealtimeChannelGet([FromBody] ProtocolRequest<IndicatorRealtimeChannelRequest> request)
        {
            var payload = request.Data;
            if (payload == null || string.IsNullOrWhiteSpace(payload.Channel))
            {
                return BadRequest(ApiResponse<object>.Error("channel 不能为空"));
            }

            try
            {
                var snapshot = await _realtimeCache.GetAsync(payload.Channel, HttpContext.RequestAborted).ConfigureAwait(false);
                if (snapshot == null)
                {
                    return NotFound(ApiResponse<object>.Error("频道暂无数据"));
                }

                var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var stale = nowMs > snapshot.ExpireAt;
                if (!payload.AllowStale && stale)
                {
                    return NotFound(ApiResponse<object>.Error("频道缓存已过期，请稍后重试"));
                }

                return Ok(ApiResponse<object>.Ok(new
                {
                    channel = snapshot.Channel,
                    source = snapshot.Source,
                    receivedAt = snapshot.ReceivedAt,
                    expireAt = snapshot.ExpireAt,
                    stale,
                    payload = IndicatorQueryService.ParsePayload(snapshot.PayloadJson)
                }, "查询成功"));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "查询实时频道缓存失败: channel={Channel}", payload.Channel);
                return StatusCode(500, ApiResponse<object>.Error($"查询失败: {ex.Message}"));
            }
        }

        private static object ToLatestDto(IndicatorQueryResult result)
        {
            return new
            {
                code = result.Definition.Code,
                provider = result.Definition.Provider,
                displayName = result.Definition.DisplayName,
                shape = result.Definition.Shape,
                unit = result.Definition.Unit,
                description = result.Definition.Description,
                scopeKey = result.Snapshot.ScopeKey,
                sourceTs = result.Snapshot.SourceTs,
                fetchedAt = result.Snapshot.FetchedAt,
                expireAt = result.Snapshot.ExpireAt,
                stale = result.Stale,
                origin = result.Origin,
                payload = IndicatorQueryService.ParsePayload(result.Snapshot.PayloadJson)
            };
        }

        private static bool TryParseTime(string? text, out long? unixMs)
        {
            unixMs = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            if (long.TryParse(text, out var numeric))
            {
                unixMs = numeric < 100_000_000_000L ? numeric * 1000L : numeric;
                return true;
            }

            if (DateTimeOffset.TryParse(text, out var dateTimeOffset))
            {
                unixMs = dateTimeOffset.ToUnixTimeMilliseconds();
                return true;
            }

            return false;
        }

        private static string ResolveSourceType(IndicatorDefinition definition)
        {
            if (definition == null)
            {
                return "unknown";
            }

            if (TryParseConfig(definition.ConfigJson, out var root))
            {
                if (TryReadString(root, out var sourceType, "sourceType") &&
                    !string.IsNullOrWhiteSpace(sourceType))
                {
                    return sourceType;
                }

                if (TryReadObject(root, out var sourceObj, "source"))
                {
                    if (TryReadString(sourceObj, out sourceType, "type") &&
                        !string.IsNullOrWhiteSpace(sourceType))
                    {
                        return sourceType;
                    }

                    if (TryReadString(sourceObj, out sourceType, "mode") &&
                        !string.IsNullOrWhiteSpace(sourceType))
                    {
                        return sourceType;
                    }
                }
            }

            return "http_pull";
        }

        private static IReadOnlyList<object> BuildMetaFields(IndicatorDefinition definition)
        {
            var fields = ParseFieldsFromConfig(definition.ConfigJson);
            if (fields.Count == 0)
            {
                return new[]
                {
                    new
                    {
                        path = "value",
                        displayName = "主值",
                        dataType = "number",
                        unit = definition.Unit,
                        conditionSupported = true,
                        description = "指标主值。"
                    }
                };
            }

            return fields
                .Select(field => (object)new
                {
                    path = field.Path,
                    displayName = string.IsNullOrWhiteSpace(field.DisplayName) ? field.Path : field.DisplayName,
                    dataType = string.IsNullOrWhiteSpace(field.DataType) ? "number" : field.DataType,
                    unit = field.Unit,
                    conditionSupported = field.ConditionSupported,
                    description = field.Description
                })
                .ToList();
        }

        private static List<IndicatorMetaField> ParseFieldsFromConfig(string? configJson)
        {
            var output = new List<IndicatorMetaField>();
            if (!TryParseConfig(configJson, out var root))
            {
                return output;
            }

            if (!TryReadArray(root, out var fieldsArray, "fields"))
            {
                return output;
            }

            foreach (var item in fieldsArray.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!TryReadString(item, out var path, "path", "field") ||
                    string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                TryReadString(item, out var displayName, "displayName", "name", "title");
                TryReadString(item, out var dataType, "dataType", "type");
                TryReadString(item, out var unit, "unit");
                TryReadString(item, out var description, "description", "desc");
                var conditionSupported = TryReadBool(item, "conditionSupported", "strategySupported", "condition")
                    ?? true;

                output.Add(new IndicatorMetaField
                {
                    Path = path.Trim(),
                    DisplayName = displayName,
                    DataType = dataType,
                    Unit = unit,
                    ConditionSupported = conditionSupported,
                    Description = description
                });
            }

            return output;
        }

        private static bool TryParseConfig(string? configJson, out JsonElement root)
        {
            root = default;
            if (string.IsNullOrWhiteSpace(configJson))
            {
                return false;
            }

            try
            {
                using var document = JsonDocument.Parse(configJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                root = document.RootElement.Clone();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadObject(JsonElement element, out JsonElement value, params string[] names)
        {
            foreach (var name in names)
            {
                if (element.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Object)
                {
                    return true;
                }
            }

            value = default;
            return false;
        }

        private static bool TryReadArray(JsonElement element, out JsonElement value, params string[] names)
        {
            foreach (var name in names)
            {
                if (element.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Array)
                {
                    return true;
                }
            }

            value = default;
            return false;
        }

        private static bool TryReadString(JsonElement element, out string? value, params string[] names)
        {
            foreach (var name in names)
            {
                if (!element.TryGetProperty(name, out var item))
                {
                    continue;
                }

                switch (item.ValueKind)
                {
                    case JsonValueKind.String:
                        value = item.GetString();
                        return true;
                    case JsonValueKind.Number:
                    case JsonValueKind.True:
                    case JsonValueKind.False:
                        value = item.ToString();
                        return true;
                }
            }

            value = null;
            return false;
        }

        private static bool? TryReadBool(JsonElement element, params string[] names)
        {
            foreach (var name in names)
            {
                if (!element.TryGetProperty(name, out var item))
                {
                    continue;
                }

                if (item.ValueKind == JsonValueKind.True)
                {
                    return true;
                }

                if (item.ValueKind == JsonValueKind.False)
                {
                    return false;
                }

                if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var numeric))
                {
                    return numeric != 0;
                }

                if (item.ValueKind == JsonValueKind.String)
                {
                    var text = item.GetString();
                    if (bool.TryParse(text, out var parsed))
                    {
                        return parsed;
                    }

                    if (int.TryParse(text, out var parsedInt))
                    {
                        return parsedInt != 0;
                    }
                }
            }

            return null;
        }

        private sealed class IndicatorMetaField
        {
            public string Path { get; set; } = string.Empty;
            public string? DisplayName { get; set; }
            public string? DataType { get; set; }
            public string? Unit { get; set; }
            public bool ConditionSupported { get; set; } = true;
            public string? Description { get; set; }
        }
    }
}
