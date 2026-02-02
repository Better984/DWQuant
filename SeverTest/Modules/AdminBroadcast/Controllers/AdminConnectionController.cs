using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.Accounts.Application;
using ServerTest.Modules.Accounts.Infrastructure;
using ServerTest.Models;
using ServerTest.Services;
using ServerTest.Protocol;
using ServerTest.WebSockets;
using ServerTest.Options;
using System.Linq;

namespace ServerTest.Controllers
{
    [ApiController]
    [Route("api/admin/connection")]
    public sealed class AdminConnectionController : BaseController
    {
        private readonly AuthTokenService _tokenService;
        private readonly AccountRepository _accountRepository;
        private readonly IConnectionManager _connectionManager;
        private readonly BusinessRulesOptions _businessRules;

        public AdminConnectionController(
            ILogger<AdminConnectionController> logger,
            AuthTokenService tokenService,
            AccountRepository accountRepository,
            IConnectionManager connectionManager,
            IOptions<BusinessRulesOptions> businessRules)
            : base(logger)
        {
            _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
            _accountRepository = accountRepository ?? throw new ArgumentNullException(nameof(accountRepository));
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _businessRules = businessRules?.Value ?? new BusinessRulesOptions();
        }

        [ProtocolType("admin.connection.stats")]
        [HttpPost("stats")]
        public async Task<IActionResult> GetStats([FromBody] ProtocolRequest<object> request)
        {
            var uid = await GetUserIdAsync().ConfigureAwait(false);
            if (!uid.HasValue)
            {
                var reqId = request?.ReqId;
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.Unauthorized, "未授权，请重新登录", null, HttpContext.TraceIdentifier);
                return Unauthorized(error);
            }

            var role = await _accountRepository.GetRoleAsync((ulong)uid.Value, null, HttpContext.RequestAborted).ConfigureAwait(false);
            if (!role.HasValue || role.Value != _businessRules.SuperAdminRole)
            {
                var reqId = request?.ReqId;
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.Forbidden, "无权限访问", null, HttpContext.TraceIdentifier);
                return StatusCode(403, error);
            }

            try
            {
                var allConnections = _connectionManager.GetAllConnections();
                var totalConnections = allConnections.Count;
                var uniqueUsers = allConnections.Select(c => c.UserId).Distinct().Count();

                var connectionsBySystem = allConnections
                    .GroupBy(c => c.System)
                    .ToDictionary(g => g.Key, g => g.Count());

                var result = new
                {
                    totalConnections,
                    totalUsers = uniqueUsers,
                    connectionsBySystem,
                };

                var reqId = request?.ReqId;
                var responseType = ProtocolEnvelopeFactory.BuildAckType(request?.Type ?? "admin.connection.stats");
                var envelope = ProtocolEnvelopeFactory.Ok(responseType, reqId, result, "查询成功", HttpContext.TraceIdentifier);
                return Ok(envelope);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "获取连接统计失败");
                var reqId = request?.ReqId;
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.InternalError, $"查询失败: {ex.Message}", null, HttpContext.TraceIdentifier);
                return StatusCode(500, error);
            }
        }

        [ProtocolType("admin.connection.users")]
        [HttpPost("users")]
        public async Task<IActionResult> GetUsers([FromBody] ProtocolRequest<object> request)
        {
            var uid = await GetUserIdAsync().ConfigureAwait(false);
            if (!uid.HasValue)
            {
                var reqId = request?.ReqId;
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.Unauthorized, "未授权，请重新登录", null, HttpContext.TraceIdentifier);
                return Unauthorized(error);
            }

            var role = await _accountRepository.GetRoleAsync((ulong)uid.Value, null, HttpContext.RequestAborted).ConfigureAwait(false);
            if (!role.HasValue || role.Value != _businessRules.SuperAdminRole)
            {
                var reqId = request?.ReqId;
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.Forbidden, "无权限访问", null, HttpContext.TraceIdentifier);
                return StatusCode(403, error);
            }

            try
            {
                var allConnections = _connectionManager.GetAllConnections();
                var users = allConnections.Select(c => new
                {
                    userId = c.UserId,
                    system = c.System,
                    connectionId = c.ConnectionId.ToString(),
                    connectedAt = c.ConnectedAt.ToString("O"),
                    remoteIp = c.RemoteIp,
                }).ToList();

                var result = new { users };

                var reqId = request?.ReqId;
                var responseType = ProtocolEnvelopeFactory.BuildAckType(request?.Type ?? "admin.connection.users");
                var envelope = ProtocolEnvelopeFactory.Ok(responseType, reqId, result, "查询成功", HttpContext.TraceIdentifier);
                return Ok(envelope);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "获取在线用户失败");
                var reqId = request?.ReqId;
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.InternalError, $"查询失败: {ex.Message}", null, HttpContext.TraceIdentifier);
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

            const string prefix = "Bearer ";
            if (authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return authorizationHeader.Substring(prefix.Length).Trim();
            }

            return null;
        }
    }
}
