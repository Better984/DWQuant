using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ServerTest.Models;
using ServerTest.Modules.Notifications.Application;
using ServerTest.Modules.Notifications.Domain;
using ServerTest.Modules.Notifications.Infrastructure;
using ServerTest.Services;
using ServerTest.Modules.Accounts.Application;
using ServerTest.Protocol;

namespace ServerTest.Controllers
{
    [ApiController]
    [Route("api/notifications")]
    public sealed class UserNotificationsController : BaseController
    {
        public sealed class NotificationQueryRequest
        {
            public int? Limit { get; set; }
            public long? Cursor { get; set; }
            public bool? UnreadOnly { get; set; }
        }

        public sealed class NotificationReadRequest
        {
            public long NotificationId { get; set; }
        }

        private readonly NotificationRepository _repository;
        private readonly INotificationTemplateRenderer _renderer;
        private readonly AuthTokenService _tokenService;

        public UserNotificationsController(
            ILogger<UserNotificationsController> logger,
            NotificationRepository repository,
            INotificationTemplateRenderer renderer,
            AuthTokenService tokenService)
            : base(logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
        }

        [ProtocolType("notification.list")]
        [HttpPost("list")]
        public async Task<IActionResult> GetList([FromBody] ProtocolRequest<NotificationQueryRequest> request)
        {
            var payload = request.Data ?? new NotificationQueryRequest();
            var uid = await GetUserIdAsync().ConfigureAwait(false);
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            var rows = await _repository.QueryInboxAsync(uid.Value, payload.Cursor, payload.Limit ?? 20, payload.UnreadOnly ?? false, HttpContext.RequestAborted)
                .ConfigureAwait(false);

            var items = new List<NotificationInboxItemDto>();
            foreach (var row in rows)
            {
                if (!NotificationContractHelper.TryParseScope(row.Scope, out var scope))
                {
                    scope = NotificationScope.User;
                }

                if (!NotificationContractHelper.TryParseCategory(row.Category, out var category))
                {
                    category = NotificationCategory.Trade;
                }

                if (!NotificationContractHelper.TryParseSeverity(row.Severity, out var severity))
                {
                    severity = NotificationSeverity.Info;
                }

                var rendered = _renderer.Render(row.Template, row.PayloadJson, category, severity);

                items.Add(new NotificationInboxItemDto
                {
                    NotificationId = row.NotificationId,
                    Scope = scope,
                    Category = category,
                    Severity = severity,
                    Template = row.Template,
                    PayloadJson = row.PayloadJson,
                    Title = rendered.Title,
                    Body = rendered.Body,
                    IsRead = row.IsRead,
                    CreatedAt = row.CreatedAt,
                    Cursor = row.NotificationUserId
                });
            }

            var nextCursor = items.Count > 0 ? items[^1].Cursor : (long?)null;

            var pageDto = new NotificationInboxPageDto
            {
                Items = items,
                NextCursor = nextCursor
            };

            return Ok(ApiResponse<NotificationInboxPageDto>.Ok(pageDto));
        }

        [ProtocolType("notification.unread.count")]
        [HttpPost("unread-count")]
        public async Task<IActionResult> GetUnreadCount([FromBody] ProtocolRequest<object> request)
        {
            var uid = await GetUserIdAsync().ConfigureAwait(false);
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            var count = await _repository.GetUnreadCountAsync(uid.Value, HttpContext.RequestAborted).ConfigureAwait(false);
            return Ok(ApiResponse<NotificationUnreadCountDto>.Ok(new NotificationUnreadCountDto { UnreadCount = count }));
        }

        [ProtocolType("notification.read")]
        [HttpPost("read")]
        public async Task<IActionResult> MarkRead([FromBody] ProtocolRequest<NotificationReadRequest> request)
        {
            var payload = request.Data;
            if (payload == null || payload.NotificationId <= 0)
            {
                return BadRequest(ApiResponse<object>.Error("无效的通知ID"));
            }

            var uid = await GetUserIdAsync().ConfigureAwait(false);
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            var updated = await _repository.MarkReadAsync(uid.Value, payload.NotificationId, HttpContext.RequestAborted).ConfigureAwait(false);
            if (updated <= 0)
            {
                return NotFound(ApiResponse<object>.Error("未找到通知记录"));
            }

            return Ok(ApiResponse<object?>.Ok(null, "已标记为已读"));
        }

        private async Task<long?> GetUserIdAsync()
        {
            var token = GetBearerToken(Request.Headers.Authorization.ToString());
            var validation = await _tokenService.ValidateTokenAsync(token ?? string.Empty);
            if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.UserId))
            {
                return null;
            }

            return long.TryParse(validation.UserId, out var uid) ? uid : null;
        }

        private static string? GetBearerToken(string? authorizationHeader)
        {
            if (string.IsNullOrWhiteSpace(authorizationHeader))
            {
                return null;
            }

            const string prefix = "Bearer ";
            if (authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return authorizationHeader.Substring(prefix.Length).Trim();
            }

            return null;
        }
    }
}
