using Microsoft.AspNetCore.Mvc;
using ServerTest.Controllers;
using Microsoft.Extensions.Logging;
using ServerTest.Models;
using ServerTest.Modules.Indicators.Application;
using ServerTest.Modules.Indicators.Domain;
using ServerTest.Protocol;

namespace ServerTest.Modules.Indicators.Controllers
{
    [ApiController]
    [Route("api/indicator")]
    public sealed class IndicatorController : BaseController
    {
        private readonly IndicatorQueryService _queryService;

        public IndicatorController(
            ILogger<IndicatorController> logger,
            IndicatorQueryService queryService)
            : base(logger)
        {
            _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
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
                    displayName = definition.DisplayName,
                    shape = definition.Shape,
                    unit = definition.Unit,
                    description = definition.Description,
                    refreshIntervalSec = definition.RefreshIntervalSec,
                    ttlSec = definition.TtlSec,
                    historyRetentionDays = definition.HistoryRetentionDays,
                    defaultScopeKey = definition.DefaultScopeKey,
                    enabled = definition.Enabled,
                    sortOrder = definition.SortOrder
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
    }
}
