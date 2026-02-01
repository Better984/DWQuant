using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ServerTest.Modules.Accounts.Application;
using ServerTest.Modules.AdminBroadcast.Application;
using ServerTest.Models;
using ServerTest.Services;
using ServerTest.Modules.Notifications.Domain;
using ServerTest.Protocol;

namespace ServerTest.Controllers
{
    [ApiController]
    [Route("api/admin/broadcast")]
    public sealed class AdminBroadcastController : BaseController
    {
        private readonly AuthTokenService _tokenService;
        private readonly AdminBroadcastService _broadcastService;

        public AdminBroadcastController(
            ILogger<AdminBroadcastController> logger,
            AuthTokenService tokenService,
            AdminBroadcastService broadcastService)
            : base(logger)
        {
            _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
            _broadcastService = broadcastService ?? throw new ArgumentNullException(nameof(broadcastService));
        }

        [ProtocolType("admin.broadcast.send")]
        [HttpPost]
        public async Task<IActionResult> Broadcast([FromBody] ProtocolRequest<NotificationBroadcastRequestDto> request)
        {
            var payload = request.Data;
            if (payload == null)
            {
                return BadRequest(ApiResponse<object>.Error("请求无效"));
            }

            var uid = await GetUserIdAsync().ConfigureAwait(false);
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            var result = await _broadcastService.BroadcastAsync(uid.Value, payload, HttpContext.RequestAborted)
                .ConfigureAwait(false);
            if (!result.Success)
            {
                if (string.Equals(result.Error, "Forbidden", StringComparison.OrdinalIgnoreCase))
                {
                    return StatusCode(403, ApiResponse<object>.Error("无权限访问"));
                }

                return BadRequest(ApiResponse<object>.Error(result.Error));
            }

            return Ok(ApiResponse<object>.Ok(new { notificationId = result.Result?.NotificationId, recipients = result.Result?.RecipientCount }, "广播已入队"));
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
