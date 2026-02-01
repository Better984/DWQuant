using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ServerTest.Models;
using ServerTest.Modules.Notifications.Application;
using ServerTest.Modules.Notifications.Domain;
using ServerTest.Services;
using ServerTest.Modules.Accounts.Application;
using ServerTest.Protocol;

namespace ServerTest.Controllers
{
    [ApiController]
    [Route("api/user/notification-preference")]
    public sealed class UserNotificationPreferencesController : BaseController
    {
        private readonly NotificationPreferenceService _service;
        private readonly AuthTokenService _tokenService;

        public UserNotificationPreferencesController(
            ILogger<UserNotificationPreferencesController> logger,
            NotificationPreferenceService service,
            AuthTokenService tokenService)
            : base(logger)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
        }

        public sealed class UpdatePreferencesRequest
        {
            public Dictionary<string, NotificationPreferenceRuleDto> Rules { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        }

        [ProtocolType("notification.preference.get")]
        [HttpPost("get")]
        public async Task<IActionResult> Get([FromBody] ProtocolRequest<object> request)
        {
            var uid = await GetUserIdAsync().ConfigureAwait(false);
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            var result = await _service.GetRulesAsync(uid.Value, HttpContext.RequestAborted).ConfigureAwait(false);
            return Ok(ApiResponse<NotificationPreferenceDto>.Ok(result));
        }

        [ProtocolType("notification.preference.update")]
        [HttpPost("update")]
        public async Task<IActionResult> Update([FromBody] ProtocolRequest<UpdatePreferencesRequest> request)
        {
            var payload = request.Data;
            if (payload == null || payload.Rules == null)
            {
                return BadRequest(ApiResponse<object>.Error("请求无效"));
            }

            var uid = await GetUserIdAsync().ConfigureAwait(false);
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            var (ok, error) = await _service.UpdateRulesAsync(uid.Value, payload.Rules, HttpContext.RequestAborted).ConfigureAwait(false);
            if (!ok)
            {
                return BadRequest(ApiResponse<object>.Error(error ?? "规则配置无效"));
            }

            return Ok(ApiResponse<object?>.Ok(null, "更新成功"));
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
