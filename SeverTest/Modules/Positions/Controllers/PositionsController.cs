using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ServerTest.Modules.Positions.Infrastructure;
using ServerTest.Modules.Positions.Application;
using ServerTest.Modules.Positions.Domain;
using ServerTest.Models;
using ServerTest.Models.Trading;
using ServerTest.Services;
using ServerTest.Modules.Accounts.Application;
using ServerTest.Protocol;

namespace ServerTest.Controllers
{
    [ApiController]
    [Route("api/positions")]
    public sealed class PositionsController : BaseController
    {
        private readonly StrategyPositionRepository _positionRepository;
        private readonly AuthTokenService _tokenService;
        private readonly StrategyPositionCloseService _closeService;

        public PositionsController(
            ILogger<PositionsController> logger,
            StrategyPositionRepository positionRepository,
            AuthTokenService tokenService,
            StrategyPositionCloseService closeService)
            : base(logger)
        {
            _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
            _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
            _closeService = closeService ?? throw new ArgumentNullException(nameof(closeService));
        }

        [ProtocolType("position.list")]
        [HttpPost("list")]
        public async Task<IActionResult> GetByUser([FromBody] ProtocolRequest<PositionQueryRequest> request)
        {
            var payload = request.Data;
            if (payload == null)
            {
                return BadRequest(ApiResponse<object>.Error("缺少请求数据"));
            }

            var uid = await GetUserIdAsync();
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            var (from, to, parseError) = ParseRange(payload.From, payload.To);
            if (parseError != null)
            {
                return BadRequest(ApiResponse<object>.Error(parseError));
            }

            var items = await _positionRepository.GetByUidAsync(uid.Value, from, to, payload.Status, HttpContext.RequestAborted);
            var response = new PositionListResponse
            {
                Items = items.Select(ToDto).ToList()
            };

            return Ok(ApiResponse<PositionListResponse>.Ok(response));
        }

        [ProtocolType("position.list.by_strategy")]
        [HttpPost("by-strategy")]
        public async Task<IActionResult> GetByStrategy([FromBody] ProtocolRequest<PositionStrategyQueryRequest> request)
        {
            var payload = request.Data;
            if (payload == null)
            {
                return BadRequest(ApiResponse<object>.Error("缺少请求数据"));
            }

            var uid = await GetUserIdAsync();
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            if (payload.UsId <= 0)
            {
                return BadRequest(ApiResponse<object>.Error("无效的策略实例ID"));
            }

            var (from, to, parseError) = ParseRange(payload.From, payload.To);
            if (parseError != null)
            {
                return BadRequest(ApiResponse<object>.Error(parseError));
            }

            var items = await _positionRepository.GetByUsIdAsync(uid.Value, payload.UsId, from, to, payload.Status, HttpContext.RequestAborted);
            var response = new PositionListResponse
            {
                Items = items.Select(ToDto).ToList()
            };

            return Ok(ApiResponse<PositionListResponse>.Ok(response));
        }

        [ProtocolType("position.close.by_strategy")]
        [HttpPost("close-by-strategy")]
        public async Task<IActionResult> CloseByStrategy([FromBody] ProtocolRequest<PositionCloseByStrategyRequest> request)
        {
            var payload = request.Data;
            if (payload == null)
            {
                return BadRequest(ApiResponse<object>.Error("缺少请求数据"));
            }

            var uid = await GetUserIdAsync();
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            if (payload.UsId <= 0)
            {
                return BadRequest(ApiResponse<object>.Error("无效的策略实例ID"));
            }

            var result = await _closeService
                .CloseByStrategyAsync(uid.Value, payload.UsId, HttpContext.RequestAborted)
                .ConfigureAwait(false);

            return Ok(ApiResponse<StrategyClosePositionsResult>.Ok(result));
        }

        [ProtocolType("position.close.by_id")]
        [HttpPost("close-by-id")]
        public async Task<IActionResult> CloseById([FromBody] ProtocolRequest<PositionCloseByIdRequest> request)
        {
            var payload = request.Data;
            if (payload == null)
            {
                return BadRequest(ApiResponse<object>.Error("缺少请求数据"));
            }

            var uid = await GetUserIdAsync();
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            if (payload.PositionId <= 0)
            {
                return BadRequest(ApiResponse<object>.Error("无效的仓位ID"));
            }

            var result = await _closeService
                .CloseByPositionAsync(uid.Value, payload.PositionId, HttpContext.RequestAborted)
                .ConfigureAwait(false);

            if (!result.Success)
            {
                return BadRequest(ApiResponse<PositionCloseResult>.Error(result.Error ?? "平仓失败"));
            }

            return Ok(ApiResponse<PositionCloseResult>.Ok(result, "已发起平仓"));
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

        private static (DateTime? From, DateTime? To, string? Error) ParseRange(string? from, string? to)
        {
            DateTime? fromValue = null;
            DateTime? toValue = null;

            if (!string.IsNullOrWhiteSpace(from))
            {
                if (!DateTime.TryParse(from, out var parsed))
                {
                    return (null, null, "开始时间格式错误，请使用 yyyy-MM-dd HH:mm:ss");
                }

                fromValue = parsed;
            }

            if (!string.IsNullOrWhiteSpace(to))
            {
                if (!DateTime.TryParse(to, out var parsed))
                {
                    return (null, null, "结束时间格式错误，请使用 yyyy-MM-dd HH:mm:ss");
                }

                toValue = parsed;
            }

            return (fromValue, toValue, null);
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

        private static PositionListItem ToDto(ServerTest.Domain.Entities.StrategyPosition position)
        {
            return new PositionListItem
            {
                PositionId = position.PositionId,
                Uid = position.Uid,
                UsId = position.UsId,
                ExchangeApiKeyId = position.ExchangeApiKeyId,
                Exchange = position.Exchange,
                Symbol = position.Symbol,
                Side = position.Side,
                Status = position.Status,
                EntryPrice = position.EntryPrice,
                Qty = position.Qty,
                StopLossPrice = position.StopLossPrice,
                TakeProfitPrice = position.TakeProfitPrice,
                TrailingEnabled = position.TrailingEnabled,
                TrailingTriggered = position.TrailingTriggered,
                TrailingStopPrice = position.TrailingStopPrice,
                CloseReason = position.CloseReason,
                OpenedAt = position.OpenedAt,
                ClosedAt = position.ClosedAt
            };
        }
    }

    public sealed class PositionQueryRequest
    {
        public string? From { get; set; }
        public string? To { get; set; }
        public string? Status { get; set; } = "all";
    }

    public sealed class PositionStrategyQueryRequest
    {
        public long UsId { get; set; }
        public string? From { get; set; }
        public string? To { get; set; }
        public string? Status { get; set; } = "all";
    }

    public sealed class PositionCloseByStrategyRequest
    {
        public long UsId { get; set; }
    }

    public sealed class PositionCloseByIdRequest
    {
        public long PositionId { get; set; }
    }
}
