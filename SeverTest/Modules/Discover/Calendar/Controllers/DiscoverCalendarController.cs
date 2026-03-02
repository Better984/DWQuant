using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ServerTest.Controllers;
using ServerTest.Models;
using ServerTest.Modules.Accounts.Application;
using ServerTest.Modules.Discover.Application;
using ServerTest.Modules.Discover.Domain;
using ServerTest.Protocol;

namespace ServerTest.Modules.Discover.Controllers
{
    /// <summary>
    /// Discover 日历接口控制器（央行活动 + 财经事件 + 经济数据）。
    /// </summary>
    [ApiController]
    [Route("api/discover")]
    public sealed class DiscoverCalendarController : BaseController
    {
        private readonly DiscoverCalendarService _discoverCalendarService;
        private readonly AuthTokenService _authTokenService;

        public DiscoverCalendarController(
            ILogger<DiscoverCalendarController> logger,
            DiscoverCalendarService discoverCalendarService,
            AuthTokenService authTokenService)
            : base(logger)
        {
            _discoverCalendarService = discoverCalendarService ?? throw new ArgumentNullException(nameof(discoverCalendarService));
            _authTokenService = authTokenService ?? throw new ArgumentNullException(nameof(authTokenService));
        }

        public sealed class DiscoverCalendarPullRequest
        {
            public long? LatestId { get; set; }
            public long? BeforeId { get; set; }
            public long? StartTime { get; set; }
            public long? EndTime { get; set; }
            public int? Limit { get; set; }
        }

        [ProtocolType("discover.calendar.central-bank.pull")]
        [HttpPost("calendar/central-bank/pull")]
        public async Task<IActionResult> PullCentralBankCalendar([FromBody] ProtocolRequest<DiscoverCalendarPullRequest> request)
        {
            var auth = await ValidateUserAsync().ConfigureAwait(false);
            if (!auth.IsValid)
            {
                return auth.ErrorResult!;
            }

            return await PullCoreAsync(
                    DiscoverCalendarKind.CentralBankActivities,
                    auth.UserId!,
                    request?.Data,
                    HttpContext.RequestAborted)
                .ConfigureAwait(false);
        }

        [ProtocolType("discover.calendar.financial-events.pull")]
        [HttpPost("calendar/financial-events/pull")]
        public async Task<IActionResult> PullFinancialEventsCalendar([FromBody] ProtocolRequest<DiscoverCalendarPullRequest> request)
        {
            var auth = await ValidateUserAsync().ConfigureAwait(false);
            if (!auth.IsValid)
            {
                return auth.ErrorResult!;
            }

            return await PullCoreAsync(
                    DiscoverCalendarKind.FinancialEvents,
                    auth.UserId!,
                    request?.Data,
                    HttpContext.RequestAborted)
                .ConfigureAwait(false);
        }

        [ProtocolType("discover.calendar.economic-data.pull")]
        [HttpPost("calendar/economic-data/pull")]
        public async Task<IActionResult> PullEconomicDataCalendar([FromBody] ProtocolRequest<DiscoverCalendarPullRequest> request)
        {
            var auth = await ValidateUserAsync().ConfigureAwait(false);
            if (!auth.IsValid)
            {
                return auth.ErrorResult!;
            }

            return await PullCoreAsync(
                    DiscoverCalendarKind.EconomicData,
                    auth.UserId!,
                    request?.Data,
                    HttpContext.RequestAborted)
                .ConfigureAwait(false);
        }

        private async Task<IActionResult> PullCoreAsync(
            DiscoverCalendarKind kind,
            string userId,
            DiscoverCalendarPullRequest? payload,
            CancellationToken ct)
        {
            try
            {
                var query = new DiscoverCalendarPullQuery
                {
                    LatestId = payload?.LatestId,
                    BeforeId = payload?.BeforeId,
                    StartTime = payload?.StartTime,
                    EndTime = payload?.EndTime,
                    Limit = payload?.Limit
                };

                var result = await _discoverCalendarService
                    .PullAsync(kind, query, ct)
                    .ConfigureAwait(false);

                var items = result.Items.Select(item => new
                {
                    id = item.Id,
                    calendarName = item.CalendarName,
                    countryCode = item.CountryCode,
                    countryName = item.CountryName,
                    publishTimestamp = item.PublishTimestamp,
                    importanceLevel = item.ImportanceLevel,
                    hasExactPublishTime = item.HasExactPublishTime,
                    dataEffect = item.DataEffect,
                    forecastValue = item.ForecastValue,
                    previousValue = item.PreviousValue,
                    revisedPreviousValue = item.RevisedPreviousValue,
                    publishedValue = item.PublishedValue,
                    createdAt = item.CreatedAt,
                    updatedAt = item.UpdatedAt
                }).ToList();

                Logger.LogInformation(
                    "Discover 日历拉取成功：uid={Uid} kind={Kind} mode={Mode} latestServerId={LatestServerId} count={Count}",
                    userId,
                    kind,
                    result.Mode,
                    result.LatestServerId,
                    items.Count);

                return Ok(ApiResponse<object>.Ok(new
                {
                    mode = result.Mode,
                    latestServerId = result.LatestServerId,
                    hasMore = result.HasMore,
                    items,
                    total = items.Count
                }, "查询成功"));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ApiResponse<object>.Error(ex.Message));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Discover 日历拉取失败：uid={Uid} kind={Kind}", userId, kind);
                return StatusCode(500, ApiResponse<object>.Error($"查询失败: {ex.Message}"));
            }
        }

        private async Task<(bool IsValid, string? UserId, IActionResult? ErrorResult)> ValidateUserAsync()
        {
            var token = GetBearerToken(Request.Headers.Authorization.ToString());
            if (string.IsNullOrWhiteSpace(token))
            {
                return (false, null, Unauthorized(ApiResponse<object>.Error("未授权，请先登录")));
            }

            var validation = await _authTokenService.ValidateTokenAsync(token).ConfigureAwait(false);
            if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.UserId))
            {
                return (false, null, Unauthorized(ApiResponse<object>.Error("登录状态已失效，请重新登录")));
            }

            return (true, validation.UserId, null);
        }

        private static string? GetBearerToken(string? authorizationHeader)
        {
            if (string.IsNullOrWhiteSpace(authorizationHeader))
            {
                return null;
            }

            const string prefix = "Bearer ";
            if (!authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return authorizationHeader[prefix.Length..].Trim();
        }
    }
}
