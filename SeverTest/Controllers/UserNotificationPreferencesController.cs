using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ServerTest.Models;
using ServerTest.Notifications.Application;
using ServerTest.Notifications.Contracts;
using ServerTest.Services;

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

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var uid = await GetUserIdAsync().ConfigureAwait(false);
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("Unauthorized"));
            }

            var result = await _service.GetRulesAsync(uid.Value, HttpContext.RequestAborted).ConfigureAwait(false);
            return Ok(ApiResponse<NotificationPreferenceDto>.Ok(result));
        }

        [HttpPut]
        public async Task<IActionResult> Update([FromBody] UpdatePreferencesRequest request)
        {
            if (request == null || request.Rules == null)
            {
                return BadRequest(ApiResponse<object>.Error("Invalid request"));
            }

            var uid = await GetUserIdAsync().ConfigureAwait(false);
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("Unauthorized"));
            }

            var (ok, error) = await _service.UpdateRulesAsync(uid.Value, request.Rules, HttpContext.RequestAborted).ConfigureAwait(false);
            if (!ok)
            {
                return BadRequest(ApiResponse<object>.Error(error ?? "Invalid rules"));
            }

            return Ok(ApiResponse<object?>.Ok(null, "Updated"));
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
