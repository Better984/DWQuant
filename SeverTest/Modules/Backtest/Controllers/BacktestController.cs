using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ServerTest.Models;
using ServerTest.Modules.Accounts.Application;
using ServerTest.Modules.Backtest.Application;
using ServerTest.Modules.Backtest.Domain;
using ServerTest.Protocol;

namespace ServerTest.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BacktestController : BaseController
    {
        private readonly BacktestService _service;
        private readonly AuthTokenService _tokenService;

        public BacktestController(
            ILogger<BacktestController> logger,
            BacktestService service,
            AuthTokenService tokenService)
            : base(logger)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
        }

        /// <summary>
        /// 回测运行入口
        /// </summary>
        [ProtocolType("backtest.run")]
        [HttpPost("run")]
        public async Task<IActionResult> Run([FromBody] ProtocolRequest<BacktestRunRequest> request)
        {
            if (request?.Data == null)
            {
                return BadRequest(ApiResponse<object>.Error("缺少请求数据"));
            }

            try
            {
                var uid = await GetUserIdAsync().ConfigureAwait(false);
                var result = await _service
                    .RunAsync(request.Data, request.ReqId, uid, HttpContext.RequestAborted)
                    .ConfigureAwait(false);
                return Ok(ApiResponse<BacktestRunResult>.Ok(result));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "回测执行失败");
                return BadRequest(ApiResponse<object>.Error($"回测执行失败: {ex.Message}"));
            }
        }

        private async Task<long?> GetUserIdAsync()
        {
            var token = GetBearerToken(Request.Headers.Authorization.ToString());
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            var validation = await _tokenService.ValidateTokenAsync(token).ConfigureAwait(false);
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
