using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using ServerTest.Models;
using ServerTest.Notifications.Application;
using ServerTest.Notifications.Contracts;
using ServerTest.Services;

namespace ServerTest.Controllers
{
    [ApiController]
    [Route("api/admin/broadcast")]
    public sealed class AdminBroadcastController : BaseController
    {
        private const int SuperAdminRole = 255;

        private readonly DatabaseService _db;
        private readonly AuthTokenService _tokenService;
        private readonly INotificationPublisher _publisher;

        public AdminBroadcastController(
            ILogger<AdminBroadcastController> logger,
            DatabaseService db,
            AuthTokenService tokenService,
            INotificationPublisher publisher)
            : base(logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
            _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        }

        [HttpPost]
        public async Task<IActionResult> Broadcast([FromBody] NotificationBroadcastRequestDto request)
        {
            if (request == null)
            {
                return BadRequest(ApiResponse<object>.Error("Invalid request"));
            }

            var uid = await GetUserIdAsync().ConfigureAwait(false);
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("Unauthorized"));
            }

            var role = await GetUserRoleAsync(uid.Value).ConfigureAwait(false);
            if (!role.HasValue || role.Value != SuperAdminRole)
            {
                return StatusCode(403, ApiResponse<object>.Error("Forbidden"));
            }

            if (!NotificationContractHelper.TryParseCategory(request.Category, out var category) || !NotificationContractHelper.IsSystemCategory(category))
            {
                return BadRequest(ApiResponse<object>.Error("Invalid category"));
            }

            if (!NotificationContractHelper.TryParseSeverity(request.Severity, out var severity))
            {
                return BadRequest(ApiResponse<object>.Error("Invalid severity"));
            }

            var template = NotificationContractHelper.NormalizeTemplate(request.Template);
            var payload = NotificationContractHelper.NormalizePayload(request.Payload);

            var result = await _publisher.PublishSystemBroadcastAsync(category, severity, template, payload, HttpContext.RequestAborted)
                .ConfigureAwait(false);

            return Ok(ApiResponse<object>.Ok(new { notificationId = result.NotificationId, recipients = result.RecipientCount }, "Broadcast queued"));
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

        private async Task<int?> GetUserRoleAsync(long uid)
        {
            try
            {
                using var connection = await _db.GetConnectionAsync().ConfigureAwait(false);
                var cmd = new MySqlCommand(@"
SELECT role
FROM account
WHERE uid = @uid AND deleted_at IS NULL
LIMIT 1
", connection);
                cmd.Parameters.AddWithValue("@uid", uid);
                var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                return result == null ? null : Convert.ToInt32(result);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to query user role: uid={Uid}", uid);
                return null;
            }
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
