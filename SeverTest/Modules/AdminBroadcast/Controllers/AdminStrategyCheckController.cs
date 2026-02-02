using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.Accounts.Application;
using ServerTest.Modules.Accounts.Infrastructure;
using ServerTest.Modules.StrategyEngine.Infrastructure;
using ServerTest.Models;
using ServerTest.Services;
using ServerTest.Protocol;
using ServerTest.Options;

namespace ServerTest.Controllers
{
    /// <summary>
    /// 测试用：策略检查日志管理控制器（后续会删除）
    /// </summary>
    [ApiController]
    [Route("api/admin/strategy-check")]
    public sealed class AdminStrategyCheckController : BaseController
    {
        private readonly AuthTokenService _tokenService;
        private readonly AccountRepository _accountRepository;
        private readonly TestStrategyCheckLogRepository _checkLogRepository;
        private readonly BusinessRulesOptions _businessRules;

        public AdminStrategyCheckController(
            ILogger<AdminStrategyCheckController> logger,
            AuthTokenService tokenService,
            AccountRepository accountRepository,
            TestStrategyCheckLogRepository checkLogRepository,
            IOptions<BusinessRulesOptions> businessRules)
            : base(logger)
        {
            _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
            _accountRepository = accountRepository ?? throw new ArgumentNullException(nameof(accountRepository));
            _checkLogRepository = checkLogRepository ?? throw new ArgumentNullException(nameof(checkLogRepository));
            _businessRules = businessRules?.Value ?? new BusinessRulesOptions();
        }

        /// <summary>
        /// 测试用：获取策略检查日志列表（后续会删除）
        /// </summary>
        [ProtocolType("admin.strategy.check.list")]
        [HttpPost("list")]
        public async Task<IActionResult> GetCheckLogs([FromBody] ProtocolRequest<StrategyCheckLogRequest> request)
        {
            var reqId = request?.ReqId;
            var uid = await GetUserIdAsync().ConfigureAwait(false);
            if (!uid.HasValue)
            {
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.Unauthorized, "未授权，请重新登录", null, HttpContext.TraceIdentifier);
                return Unauthorized(error);
            }

            var role = await _accountRepository.GetRoleAsync((ulong)uid.Value, null, HttpContext.RequestAborted).ConfigureAwait(false);
            if (!role.HasValue || role.Value != _businessRules.SuperAdminRole)
            {
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.Forbidden, "无权限访问", null, HttpContext.TraceIdentifier);
                return StatusCode(403, error);
            }

            var payload = request.Data;
            if (payload == null || payload.UsId <= 0)
            {
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.InvalidRequest, "无效的策略实例ID", null, HttpContext.TraceIdentifier);
                return BadRequest(error);
            }

            try
            {
                var logs = await _checkLogRepository.GetByUsIdAsync(
                    payload.UsId,
                    payload.Limit ?? 1000,
                    HttpContext.RequestAborted).ConfigureAwait(false);

                var responseType = ProtocolEnvelopeFactory.BuildAckType(request?.Type ?? "admin.strategy.check.list");
                var envelope = ProtocolEnvelopeFactory.Ok(responseType, reqId, logs, "查询成功", HttpContext.TraceIdentifier);
                return Ok(envelope);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "获取策略检查日志失败: usId={UsId}", payload.UsId);
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.InternalError, $"查询失败: {ex.Message}", null, HttpContext.TraceIdentifier);
                return StatusCode(500, error);
            }
        }

        /// <summary>
        /// 测试用：清空策略检查日志（后续会删除）
        /// </summary>
        [ProtocolType("admin.strategy.check.clear")]
        [HttpPost("clear")]
        public async Task<IActionResult> ClearCheckLogs([FromBody] ProtocolRequest<StrategyCheckLogRequest> request)
        {
            var reqId = request?.ReqId;
            var uid = await GetUserIdAsync().ConfigureAwait(false);
            if (!uid.HasValue)
            {
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.Unauthorized, "未授权，请重新登录", null, HttpContext.TraceIdentifier);
                return Unauthorized(error);
            }

            var role = await _accountRepository.GetRoleAsync((ulong)uid.Value, null, HttpContext.RequestAborted).ConfigureAwait(false);
            if (!role.HasValue || role.Value != _businessRules.SuperAdminRole)
            {
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.Forbidden, "无权限访问", null, HttpContext.TraceIdentifier);
                return StatusCode(403, error);
            }

            var payload = request.Data;
            if (payload == null || payload.UsId <= 0)
            {
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.InvalidRequest, "无效的策略实例ID", null, HttpContext.TraceIdentifier);
                return BadRequest(error);
            }

            try
            {
                var deletedCount = await _checkLogRepository.DeleteByUsIdAsync(
                    payload.UsId,
                    HttpContext.RequestAborted).ConfigureAwait(false);

                var responseType = ProtocolEnvelopeFactory.BuildAckType(request?.Type ?? "admin.strategy.check.clear");
                var envelope = ProtocolEnvelopeFactory.Ok(responseType, reqId, new { deletedCount }, $"已清空 {deletedCount} 条检查记录", HttpContext.TraceIdentifier);
                return Ok(envelope);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "清空策略检查日志失败: usId={UsId}", payload.UsId);
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.InternalError, $"清空失败: {ex.Message}", null, HttpContext.TraceIdentifier);
                return StatusCode(500, error);
            }
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

            if (!authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return authorizationHeader.Substring(7).Trim();
        }
    }

    /// <summary>
    /// 测试用：策略检查日志请求模型（后续会删除）
    /// </summary>
    public sealed class StrategyCheckLogRequest
    {
        public long UsId { get; set; }
        public int? Limit { get; set; }
    }
}
