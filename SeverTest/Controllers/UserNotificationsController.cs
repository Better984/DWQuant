using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ServerTest.Models;
using ServerTest.Notifications.Application;
using ServerTest.Notifications.Contracts;
using ServerTest.Notifications.Infrastructure;
using ServerTest.Services;

namespace ServerTest.Controllers
{
    [ApiController]
    [Route("api/notifications")]
    public sealed class UserNotificationsController : BaseController
    {
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

        [HttpGet]
        public async Task<IActionResult> GetList([FromQuery] int? limit, [FromQuery] long? cursor, [FromQuery] bool? unreadOnly)
        {
            var uid = await GetUserIdAsync().ConfigureAwait(false);
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("Unauthorized"));
            }

            var rows = await _repository.QueryInboxAsync(uid.Value, cursor, limit ?? 20, unreadOnly ?? false, HttpContext.RequestAborted)
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

            var payload = new NotificationInboxPageDto
            {
                Items = items,
                NextCursor = nextCursor
            };

            return Ok(ApiResponse<NotificationInboxPageDto>.Ok(payload));
        }

        [HttpGet("unread-count")]
        public async Task<IActionResult> GetUnreadCount()
        {
            var uid = await GetUserIdAsync().ConfigureAwait(false);
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("Unauthorized"));
            }

            var count = await _repository.GetUnreadCountAsync(uid.Value, HttpContext.RequestAborted).ConfigureAwait(false);
            return Ok(ApiResponse<NotificationUnreadCountDto>.Ok(new NotificationUnreadCountDto { UnreadCount = count }));
        }

        [HttpPost("{notificationId:long}/read")]
        public async Task<IActionResult> MarkRead(long notificationId)
        {
            var uid = await GetUserIdAsync().ConfigureAwait(false);
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("Unauthorized"));
            }

            var updated = await _repository.MarkReadAsync(uid.Value, notificationId, HttpContext.RequestAborted).ConfigureAwait(false);
            if (updated <= 0)
            {
                return NotFound(ApiResponse<object>.Error("Notification not found"));
            }

            return Ok(ApiResponse<object?>.Ok(null, "Marked"));
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
