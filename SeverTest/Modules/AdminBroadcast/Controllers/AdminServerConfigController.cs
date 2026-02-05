using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.Accounts.Infrastructure;
using ServerTest.Models;
using ServerTest.Options;
using ServerTest.Protocol;
using ServerTest.Services;
using ServerTest.Modules.Accounts.Application;

namespace ServerTest.Controllers
{
    [ApiController]
    [Route("api/admin/server-config")]
    public sealed class AdminServerConfigController : BaseController
    {
        private readonly AuthTokenService _tokenService;
        private readonly AccountRepository _accountRepository;
        private readonly ServerConfigService _configService;
        private readonly BusinessRulesOptions _businessRules;

        public AdminServerConfigController(
            ILogger<AdminServerConfigController> logger,
            AuthTokenService tokenService,
            AccountRepository accountRepository,
            ServerConfigService configService,
            IOptions<BusinessRulesOptions> businessRules)
            : base(logger)
        {
            _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
            _accountRepository = accountRepository ?? throw new ArgumentNullException(nameof(accountRepository));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _businessRules = businessRules?.Value ?? new BusinessRulesOptions();
        }

        [ProtocolType("admin.server-config.list")]
        [HttpPost("list")]
        public async Task<IActionResult> List([FromBody] ProtocolRequest<object> request)
        {
            var uid = await GetUserIdAsync().ConfigureAwait(false);
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            var role = await _accountRepository.GetRoleAsync((ulong)uid.Value, null, HttpContext.RequestAborted).ConfigureAwait(false);
            if (!role.HasValue || role.Value != _businessRules.SuperAdminRole)
            {
                return StatusCode(403, ApiResponse<object>.Error("无权限访问"));
            }

            try
            {
                var items = await _configService.ListAsync(HttpContext.RequestAborted).ConfigureAwait(false);
                return Ok(ApiResponse<object>.Ok(new { items }, "查询成功"));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "获取服务器配置失败");
                return StatusCode(500, ApiResponse<object>.Error("查询失败，请稍后重试"));
            }
        }

        [ProtocolType("admin.server-config.update")]
        [HttpPost("update")]
        public async Task<IActionResult> Update([FromBody] ProtocolRequest<ServerConfigUpdateRequest> request)
        {
            var payload = request.Data;
            if (payload == null || string.IsNullOrWhiteSpace(payload.Key))
            {
                return BadRequest(ApiResponse<object>.Error("缺少配置键"));
            }

            var uid = await GetUserIdAsync().ConfigureAwait(false);
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            var role = await _accountRepository.GetRoleAsync((ulong)uid.Value, null, HttpContext.RequestAborted).ConfigureAwait(false);
            if (!role.HasValue || role.Value != _businessRules.SuperAdminRole)
            {
                return StatusCode(403, ApiResponse<object>.Error("无权限访问"));
            }

            var (success, error) = await _configService.UpdateValueAsync(payload.Key, payload.Value ?? string.Empty, uid.Value, HttpContext.RequestAborted)
                .ConfigureAwait(false);
            if (!success)
            {
                return BadRequest(ApiResponse<object>.Error(error));
            }

            return Ok(ApiResponse<object>.Ok(new { payload.Key }, "更新成功"));
        }

        private async Task<long?> GetUserIdAsync()
        {
            var token = GetBearerToken(Request.Headers.Authorization.ToString());
            var validation = await _tokenService.ValidateTokenAsync(token ?? string.Empty).ConfigureAwait(false);
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

    public sealed class ServerConfigUpdateRequest
    {
        public string Key { get; set; } = string.Empty;
        public string? Value { get; set; }
    }
}
