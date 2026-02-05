using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.Accounts.Infrastructure;
using ServerTest.Modules.StrategyEngine.Application;
using ServerTest.Models;
using ServerTest.Models.Strategy;
using ServerTest.Options;
using ServerTest.Protocol;
using ServerTest.Services;
using ServerTest.Modules.Accounts.Application;

namespace ServerTest.Controllers
{
    [ApiController]
    [Route("api/admin/runtime-template")]
    public sealed class AdminRuntimeTemplateController : BaseController
    {
        private readonly AuthTokenService _tokenService;
        private readonly AccountRepository _accountRepository;
        private readonly StrategyRuntimeTemplateService _templateService;
        private readonly BusinessRulesOptions _businessRules;

        public AdminRuntimeTemplateController(
            ILogger<AdminRuntimeTemplateController> logger,
            AuthTokenService tokenService,
            AccountRepository accountRepository,
            StrategyRuntimeTemplateService templateService,
            IOptions<BusinessRulesOptions> businessRules)
            : base(logger)
        {
            _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
            _accountRepository = accountRepository ?? throw new ArgumentNullException(nameof(accountRepository));
            _templateService = templateService ?? throw new ArgumentNullException(nameof(templateService));
            _businessRules = businessRules?.Value ?? new BusinessRulesOptions();
        }

        [ProtocolType("admin.runtime-template.list")]
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
                var templates = await _templateService.GetAllAsync(HttpContext.RequestAborted).ConfigureAwait(false);
                var timezones = _templateService.GetTimezones();
                return Ok(ApiResponse<object>.Ok(new { templates, timezones }, "查询成功"));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "获取运行时间模板失败");
                return StatusCode(500, ApiResponse<object>.Error("查询失败，请稍后重试"));
            }
        }

        [ProtocolType("admin.runtime-template.create")]
        [HttpPost("create")]
        public async Task<IActionResult> Create([FromBody] ProtocolRequest<RuntimeTemplatePayload> request)
        {
            var payload = request.Data;
            if (payload?.Template == null)
            {
                return BadRequest(ApiResponse<object>.Error("缺少模板数据"));
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

            var (success, error) = await _templateService.CreateAsync(payload.Template, HttpContext.RequestAborted).ConfigureAwait(false);
            if (!success)
            {
                return BadRequest(ApiResponse<object>.Error(error));
            }

            return Ok(ApiResponse<object>.Ok(new { payload.Template.Id }, "新增成功"));
        }

        [ProtocolType("admin.runtime-template.update")]
        [HttpPost("update")]
        public async Task<IActionResult> Update([FromBody] ProtocolRequest<RuntimeTemplatePayload> request)
        {
            var payload = request.Data;
            if (payload?.Template == null)
            {
                return BadRequest(ApiResponse<object>.Error("缺少模板数据"));
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

            var (success, error) = await _templateService.UpdateAsync(payload.Template, HttpContext.RequestAborted).ConfigureAwait(false);
            if (!success)
            {
                return BadRequest(ApiResponse<object>.Error(error));
            }

            return Ok(ApiResponse<object>.Ok(new { payload.Template.Id }, "更新成功"));
        }

        [ProtocolType("admin.runtime-template.delete")]
        [HttpPost("delete")]
        public async Task<IActionResult> Delete([FromBody] ProtocolRequest<RuntimeTemplateDeleteRequest> request)
        {
            var payload = request.Data;
            if (payload == null || string.IsNullOrWhiteSpace(payload.TemplateId))
            {
                return BadRequest(ApiResponse<object>.Error("缺少模板 ID"));
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

            var (success, error) = await _templateService.DeleteAsync(payload.TemplateId, HttpContext.RequestAborted).ConfigureAwait(false);
            if (!success)
            {
                return BadRequest(ApiResponse<object>.Error(error));
            }

            return Ok(ApiResponse<object>.Ok(new { payload.TemplateId }, "删除成功"));
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

    public sealed class RuntimeTemplatePayload
    {
        public StrategyRuntimeTemplateDefinition? Template { get; set; }
    }

    public sealed class RuntimeTemplateDeleteRequest
    {
        public string TemplateId { get; set; } = string.Empty;
    }
}
