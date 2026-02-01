using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ServerTest.Modules.StrategyManagement.Application;
using ServerTest.Models;
using ServerTest.Models.Strategy;
using ServerTest.Services;
using ServerTest.Modules.Accounts.Application;
using ServerTest.Protocol;

namespace ServerTest.Controllers
{
    [ApiController]
    [Route("api/strategy")]
    public sealed class StrategyController : BaseController
    {
    public sealed class StrategyOfficialVersionsRequest
        {
            public long DefId { get; set; }
        }

    public sealed class StrategyVersionsRequest
        {
            public long UsId { get; set; }
        }

    public sealed class StrategyInstanceStateUpdateRequest
        {
            public long Id { get; set; }
            public string State { get; set; } = string.Empty;
            public long? ExchangeApiKeyId { get; set; }
        }

        private readonly AuthTokenService _tokenService;
        private readonly StrategyService _strategyService;

        public StrategyController(
            ILogger<StrategyController> logger,
            AuthTokenService tokenService,
            StrategyService strategyService)
            : base(logger)
        {
            _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
            _strategyService = strategyService ?? throw new ArgumentNullException(nameof(strategyService));
        }

        [ProtocolType("strategy.create")]
        [HttpPost("create")]
        public Task<IActionResult> Create([FromBody] ProtocolRequest<StrategyCreateRequest> request)
        {
            var payload = request.Data;
            if (payload == null)
            {
                return Task.FromResult<IActionResult>(BadRequest(ApiResponse<object>.Error("缺少请求数据")));
            }

            return WithUserAsync(uid => _strategyService.Create(uid, payload));
        }

        [ProtocolType("strategy.list")]
        [HttpPost("list")]
        public Task<IActionResult> List([FromBody] ProtocolRequest<object> request)
        {
            return WithUserAsync(uid => _strategyService.List(uid));
        }

        [ProtocolType("strategy.official.list")]
        [HttpPost("official/list")]
        public Task<IActionResult> ListOfficial([FromBody] ProtocolRequest<object> request)
        {
            return WithUserAsync(uid => _strategyService.ListOfficial(uid));
        }

        [ProtocolType("strategy.official.versions")]
        [HttpPost("official/versions")]
        public Task<IActionResult> OfficialVersions([FromBody] ProtocolRequest<StrategyOfficialVersionsRequest> request)
        {
            var payload = request.Data;
            if (payload == null || payload.DefId <= 0)
            {
                return Task.FromResult<IActionResult>(BadRequest(ApiResponse<object>.Error("无效的策略定义")));
            }

            return WithUserAsync(uid => _strategyService.OfficialVersions(uid, payload.DefId));
        }

        [ProtocolType("strategy.template.list")]
        [HttpPost("template/list")]
        public Task<IActionResult> ListTemplate([FromBody] ProtocolRequest<object> request)
        {
            return WithUserAsync(uid => _strategyService.ListTemplate(uid));
        }

        [ProtocolType("strategy.market.list")]
        [HttpPost("market/list")]
        public Task<IActionResult> ListMarket([FromBody] ProtocolRequest<object> request)
        {
            return WithUserAsync(uid => _strategyService.ListMarket(uid));
        }

        [ProtocolType("strategy.market.publish")]
        [HttpPost("market/publish")]
        public Task<IActionResult> PublishMarket([FromBody] ProtocolRequest<StrategyMarketPublishRequest> request)
        {
            var payload = request.Data;
            if (payload == null)
            {
                return Task.FromResult<IActionResult>(BadRequest(ApiResponse<object>.Error("缺少请求数据")));
            }

            return WithUserAsync(uid => _strategyService.PublishMarket(uid, payload));
        }

        [ProtocolType("strategy.update")]
        [HttpPost("update")]
        public Task<IActionResult> Update([FromBody] ProtocolRequest<StrategyUpdateRequest> request)
        {
            var payload = request.Data;
            if (payload == null)
            {
                return Task.FromResult<IActionResult>(BadRequest(ApiResponse<object>.Error("缺少请求数据")));
            }

            return WithUserAsync(uid => _strategyService.Update(uid, payload));
        }

        [ProtocolType("strategy.publish")]
        [HttpPost("publish")]
        public Task<IActionResult> Publish([FromBody] ProtocolRequest<StrategyPublishRequest> request)
        {
            var payload = request.Data;
            if (payload == null)
            {
                return Task.FromResult<IActionResult>(BadRequest(ApiResponse<object>.Error("缺少请求数据")));
            }

            return WithUserAsync(uid => _strategyService.Publish(uid, payload));
        }

        [ProtocolType("strategy.official.publish")]
        [HttpPost("publish/official")]
        public Task<IActionResult> PublishOfficial([FromBody] ProtocolRequest<StrategyCatalogPublishRequest> request)
        {
            var payload = request.Data;
            if (payload == null)
            {
                return Task.FromResult<IActionResult>(BadRequest(ApiResponse<object>.Error("缺少请求数据")));
            }

            return WithUserAsync(uid => _strategyService.PublishOfficial(uid, payload));
        }

        [ProtocolType("strategy.template.publish")]
        [HttpPost("publish/template")]
        public Task<IActionResult> PublishTemplate([FromBody] ProtocolRequest<StrategyCatalogPublishRequest> request)
        {
            var payload = request.Data;
            if (payload == null)
            {
                return Task.FromResult<IActionResult>(BadRequest(ApiResponse<object>.Error("缺少请求数据")));
            }

            return WithUserAsync(uid => _strategyService.PublishTemplate(uid, payload));
        }

        [ProtocolType("strategy.official.sync")]
        [HttpPost("official/sync")]
        public Task<IActionResult> SyncOfficial([FromBody] ProtocolRequest<StrategyCatalogPublishRequest> request)
        {
            var payload = request.Data;
            if (payload == null)
            {
                return Task.FromResult<IActionResult>(BadRequest(ApiResponse<object>.Error("缺少请求数据")));
            }

            return WithUserAsync(uid => _strategyService.SyncOfficial(uid, payload));
        }

        [ProtocolType("strategy.template.sync")]
        [HttpPost("template/sync")]
        public Task<IActionResult> SyncTemplate([FromBody] ProtocolRequest<StrategyCatalogPublishRequest> request)
        {
            var payload = request.Data;
            if (payload == null)
            {
                return Task.FromResult<IActionResult>(BadRequest(ApiResponse<object>.Error("缺少请求数据")));
            }

            return WithUserAsync(uid => _strategyService.SyncTemplate(uid, payload));
        }

        [ProtocolType("strategy.official.remove")]
        [HttpPost("official/remove")]
        public Task<IActionResult> RemoveOfficial([FromBody] ProtocolRequest<StrategyCatalogPublishRequest> request)
        {
            var payload = request.Data;
            if (payload == null)
            {
                return Task.FromResult<IActionResult>(BadRequest(ApiResponse<object>.Error("缺少请求数据")));
            }

            return WithUserAsync(uid => _strategyService.RemoveOfficial(uid, payload));
        }

        [ProtocolType("strategy.template.remove")]
        [HttpPost("template/remove")]
        public Task<IActionResult> RemoveTemplate([FromBody] ProtocolRequest<StrategyCatalogPublishRequest> request)
        {
            var payload = request.Data;
            if (payload == null)
            {
                return Task.FromResult<IActionResult>(BadRequest(ApiResponse<object>.Error("缺少请求数据")));
            }

            return WithUserAsync(uid => _strategyService.RemoveTemplate(uid, payload));
        }

        [ProtocolType("strategy.market.sync")]
        [HttpPost("market/sync")]
        public Task<IActionResult> SyncMarket([FromBody] ProtocolRequest<StrategyMarketPublishRequest> request)
        {
            var payload = request.Data;
            if (payload == null)
            {
                return Task.FromResult<IActionResult>(BadRequest(ApiResponse<object>.Error("缺少请求数据")));
            }

            return WithUserAsync(uid => _strategyService.SyncMarket(uid, payload));
        }

        [ProtocolType("strategy.share.create")]
        [HttpPost("share/create-code")]
        public Task<IActionResult> CreateShareCode([FromBody] ProtocolRequest<StrategyShareCreateRequest> request)
        {
            var payload = request.Data;
            if (payload == null)
            {
                return Task.FromResult<IActionResult>(BadRequest(ApiResponse<object>.Error("缺少请求数据")));
            }

            return WithUserAsync(uid => _strategyService.CreateShareCode(uid, payload));
        }

        [ProtocolType("strategy.share.import")]
        [HttpPost("import/share-code")]
        public Task<IActionResult> ImportShareCode([FromBody] ProtocolRequest<StrategyImportShareCodeRequest> request)
        {
            var payload = request.Data;
            if (payload == null)
            {
                return Task.FromResult<IActionResult>(BadRequest(ApiResponse<object>.Error("缺少请求数据")));
            }

            return WithUserAsync(uid => _strategyService.ImportShareCode(uid, payload));
        }

        [ProtocolType("strategy.versions")]
        [HttpPost("versions")]
        public Task<IActionResult> Versions([FromBody] ProtocolRequest<StrategyVersionsRequest> request)
        {
            var payload = request.Data;
            if (payload == null || payload.UsId <= 0)
            {
                return Task.FromResult<IActionResult>(BadRequest(ApiResponse<object>.Error("无效的策略实例")));
            }

            return WithUserAsync(uid => _strategyService.Versions(uid, payload.UsId));
        }

        [ProtocolType("strategy.delete")]
        [HttpPost("delete")]
        public Task<IActionResult> Delete([FromBody] ProtocolRequest<StrategyDeleteRequest> request)
        {
            var payload = request.Data;
            if (payload == null)
            {
                return Task.FromResult<IActionResult>(BadRequest(ApiResponse<object>.Error("缺少请求数据")));
            }

            return WithUserAsync(uid => _strategyService.Delete(uid, payload));
        }

        [ProtocolType("strategy.instance.state.update")]
        [HttpPost("instances/state")]
        public Task<IActionResult> UpdateInstanceState([FromBody] ProtocolRequest<StrategyInstanceStateUpdateRequest> request)
        {
            var payload = request.Data;
            if (payload == null || payload.Id <= 0)
            {
                return Task.FromResult<IActionResult>(BadRequest(ApiResponse<object>.Error("无效的策略实例")));
            }

            var ct = HttpContext.RequestAborted;
            var stateRequest = new StrategyInstanceStateRequest
            {
                State = payload.State,
                ExchangeApiKeyId = payload.ExchangeApiKeyId
            };
            return WithUserAsync(uid => _strategyService.UpdateInstanceState(uid, payload.Id, stateRequest, ct));
        }

        private async Task<IActionResult> WithUserAsync(Func<long, Task<IActionResult>> action)
        {
            var uid = await GetUserIdAsync();
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            return await action(uid.Value).ConfigureAwait(false);
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
