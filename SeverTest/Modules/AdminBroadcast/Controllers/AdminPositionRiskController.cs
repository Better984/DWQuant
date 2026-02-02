using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Models;
using ServerTest.Modules.Accounts.Application;
using ServerTest.Modules.Accounts.Infrastructure;
using ServerTest.Modules.MarketStreaming.Application;
using ServerTest.Modules.Positions.Application;
using ServerTest.Options;
using ServerTest.Protocol;
using ServerTest.Services;
using System;
using System.Linq;

namespace ServerTest.Controllers
{
    [ApiController]
    [Route("api/admin/position-risk")]
    public sealed class AdminPositionRiskController : BaseController
    {
        private readonly AuthTokenService _tokenService;
        private readonly AccountRepository _accountRepository;
        private readonly BusinessRulesOptions _businessRules;
        private readonly PositionRiskIndexManager _riskIndexManager;

        public AdminPositionRiskController(
            ILogger<AdminPositionRiskController> logger,
            AuthTokenService tokenService,
            AccountRepository accountRepository,
            IOptions<BusinessRulesOptions> businessRules,
            PositionRiskIndexManager riskIndexManager)
            : base(logger)
        {
            _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
            _accountRepository = accountRepository ?? throw new ArgumentNullException(nameof(accountRepository));
            _businessRules = businessRules?.Value ?? new BusinessRulesOptions();
            _riskIndexManager = riskIndexManager ?? throw new ArgumentNullException(nameof(riskIndexManager));
        }

        [ProtocolType("admin.position.risk.index")]
        [HttpPost("index")]
        public async Task<IActionResult> GetIndexSnapshot([FromBody] ProtocolRequest<AdminPositionRiskIndexRequest> request)
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
                var snapshots = _riskIndexManager.BuildSnapshots();
                var payload = request?.Data;
                if (payload != null)
                {
                    var exchange = string.IsNullOrWhiteSpace(payload.Exchange)
                        ? null
                        : MarketDataKeyNormalizer.NormalizeExchange(payload.Exchange);
                    var symbol = string.IsNullOrWhiteSpace(payload.Symbol)
                        ? null
                        : MarketDataKeyNormalizer.NormalizeSymbol(payload.Symbol);

                    if (!string.IsNullOrWhiteSpace(exchange))
                    {
                        snapshots = snapshots.Where(item => string.Equals(item.Exchange, exchange, StringComparison.OrdinalIgnoreCase)).ToList();
                    }

                    if (!string.IsNullOrWhiteSpace(symbol))
                    {
                        snapshots = snapshots.Where(item => string.Equals(item.Symbol, symbol, StringComparison.OrdinalIgnoreCase)).ToList();
                    }
                }

                var reqId = request?.ReqId;
                var responseType = ProtocolEnvelopeFactory.BuildAckType(request?.Type ?? "admin.position.risk.index");
                var envelope = ProtocolEnvelopeFactory.Ok(
                    responseType,
                    reqId,
                    new
                    {
                        generatedAt = DateTime.UtcNow,
                        items = snapshots
                    },
                    "查询成功",
                    HttpContext.TraceIdentifier);
                return Ok(envelope);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "获取风控索引快照失败");
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

    public sealed class AdminPositionRiskIndexRequest
    {
        public string? Exchange { get; set; }
        public string? Symbol { get; set; }
    }
}
