using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ServerTest.Infrastructure.Repositories;
using ServerTest.Models;
using ServerTest.Models.Trading;
using ServerTest.Services;

namespace ServerTest.Controllers
{
    [ApiController]
    [Route("api/positions")]
    public sealed class PositionsController : BaseController
    {
        private readonly StrategyPositionRepository _positionRepository;
        private readonly AuthTokenService _tokenService;

        public PositionsController(
            ILogger<PositionsController> logger,
            StrategyPositionRepository positionRepository,
            AuthTokenService tokenService)
            : base(logger)
        {
            _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
            _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
        }

        [HttpGet]
        public async Task<IActionResult> GetByUser([FromQuery] PositionQueryRequest request)
        {
            var uid = await GetUserIdAsync();
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            var (from, to, parseError) = ParseRange(request.From, request.To);
            if (parseError != null)
            {
                return BadRequest(ApiResponse<object>.Error(parseError));
            }

            var items = await _positionRepository.GetByUidAsync(uid.Value, from, to, request.Status, HttpContext.RequestAborted);
            var response = new PositionListResponse
            {
                Items = items.Select(ToDto).ToList()
            };

            return Ok(ApiResponse<PositionListResponse>.Ok(response));
        }

        [HttpGet("by-strategy")]
        public async Task<IActionResult> GetByStrategy([FromQuery] PositionStrategyQueryRequest request)
        {
            var uid = await GetUserIdAsync();
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            if (request.UsId <= 0)
            {
                return BadRequest(ApiResponse<object>.Error("无效的策略实例ID"));
            }

            var (from, to, parseError) = ParseRange(request.From, request.To);
            if (parseError != null)
            {
                return BadRequest(ApiResponse<object>.Error(parseError));
            }

            var items = await _positionRepository.GetByUsIdAsync(uid.Value, request.UsId, from, to, request.Status, HttpContext.RequestAborted);
            var response = new PositionListResponse
            {
                Items = items.Select(ToDto).ToList()
            };

            return Ok(ApiResponse<PositionListResponse>.Ok(response));
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
}
