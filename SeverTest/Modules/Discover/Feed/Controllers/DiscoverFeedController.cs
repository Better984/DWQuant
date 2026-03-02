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
    /// Discover 资讯接口控制器（新闻 + 快讯）。
    /// </summary>
    [ApiController]
    [Route("api/discover")]
    public sealed class DiscoverFeedController : BaseController
    {
        private readonly DiscoverFeedService _discoverFeedService;
        private readonly AuthTokenService _authTokenService;

        public DiscoverFeedController(
            ILogger<DiscoverFeedController> logger,
            DiscoverFeedService discoverFeedService,
            AuthTokenService authTokenService)
            : base(logger)
        {
            _discoverFeedService = discoverFeedService ?? throw new ArgumentNullException(nameof(discoverFeedService));
            _authTokenService = authTokenService ?? throw new ArgumentNullException(nameof(authTokenService));
        }

        public sealed class DiscoverPullRequest
        {
            public long? LatestId { get; set; }
            public long? BeforeId { get; set; }
            public int? Limit { get; set; }
        }

        [ProtocolType("discover.article.pull")]
        [HttpPost("article/pull")]
        public async Task<IActionResult> PullArticle([FromBody] ProtocolRequest<DiscoverPullRequest> request)
        {
            var auth = await ValidateUserAsync().ConfigureAwait(false);
            if (!auth.IsValid)
            {
                return auth.ErrorResult!;
            }

            return await PullCoreAsync(
                    DiscoverFeedKind.Article,
                    auth.UserId!,
                    request?.Data,
                    HttpContext.RequestAborted)
                .ConfigureAwait(false);
        }

        [ProtocolType("discover.newsflash.pull")]
        [HttpPost("newsflash/pull")]
        public async Task<IActionResult> PullNewsflash([FromBody] ProtocolRequest<DiscoverPullRequest> request)
        {
            var auth = await ValidateUserAsync().ConfigureAwait(false);
            if (!auth.IsValid)
            {
                return auth.ErrorResult!;
            }

            return await PullCoreAsync(
                    DiscoverFeedKind.Newsflash,
                    auth.UserId!,
                    request?.Data,
                    HttpContext.RequestAborted)
                .ConfigureAwait(false);
        }

        private async Task<IActionResult> PullCoreAsync(
            DiscoverFeedKind kind,
            string userId,
            DiscoverPullRequest? payload,
            CancellationToken ct)
        {
            try
            {
                var query = new DiscoverPullQuery
                {
                    LatestId = payload?.LatestId,
                    BeforeId = payload?.BeforeId,
                    Limit = payload?.Limit
                };

                var result = await _discoverFeedService
                    .PullAsync(kind, query, ct)
                    .ConfigureAwait(false);

                var items = result.Items.Select(item => new
                {
                    id = item.Id,
                    title = item.Title,
                    summary = item.Summary,
                    contentHtml = item.ContentHtml,
                    source = item.SourceName,
                    sourceLogo = item.SourceLogo,
                    pictureUrl = item.PictureUrl,
                    releaseTime = item.ReleaseTime,
                    createdAt = item.CreatedAt
                }).ToList();

                Logger.LogInformation(
                    "Discover 资讯拉取成功：uid={Uid} kind={Kind} mode={Mode} latestServerId={LatestServerId} count={Count}",
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
                Logger.LogError(ex, "Discover 资讯拉取失败：uid={Uid} kind={Kind}", userId, kind);
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
