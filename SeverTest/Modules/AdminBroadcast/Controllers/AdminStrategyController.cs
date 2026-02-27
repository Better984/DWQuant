using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.Accounts.Application;
using ServerTest.Modules.Accounts.Infrastructure;
using ServerTest.Modules.Shared.Infrastructure.Diagnostics;
using ServerTest.Modules.StrategyEngine.Infrastructure;
using ServerTest.Modules.StrategyManagement.Infrastructure;
using ServerTest.Protocol;
using ServerTest.Options;

namespace ServerTest.Controllers
{
    /// <summary>
    /// 管理员策略相关接口：运行中策略全量列表、实盘任务链路追踪等。
    /// </summary>
    [ApiController]
    [Route("api/admin/strategy")]
    public sealed class AdminStrategyController : BaseController
    {
        private readonly AuthTokenService _tokenService;
        private readonly AccountRepository _accountRepository;
        private readonly StrategyRepository _strategyRepository;
        private readonly StrategyTaskTraceLogRepository _traceLogRepository;
        private readonly StrategyEngineRunLogRepository _strategyRunLogRepository;
        private readonly BusinessRulesOptions _businessRules;

        public AdminStrategyController(
            ILogger<AdminStrategyController> logger,
            AuthTokenService tokenService,
            AccountRepository accountRepository,
            StrategyRepository strategyRepository,
            StrategyTaskTraceLogRepository traceLogRepository,
            StrategyEngineRunLogRepository strategyRunLogRepository,
            IOptions<BusinessRulesOptions> businessRules)
            : base(logger)
        {
            _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
            _accountRepository = accountRepository ?? throw new ArgumentNullException(nameof(accountRepository));
            _strategyRepository = strategyRepository ?? throw new ArgumentNullException(nameof(strategyRepository));
            _traceLogRepository = traceLogRepository ?? throw new ArgumentNullException(nameof(traceLogRepository));
            _strategyRunLogRepository = strategyRunLogRepository ?? throw new ArgumentNullException(nameof(strategyRunLogRepository));
            _businessRules = businessRules?.Value ?? new BusinessRulesOptions();
        }

        /// <summary>
        /// 获取运行中策略（running/paused_open_position/testing）分页列表，用于服务器实盘情况展示。
        /// </summary>
        [ProtocolType("admin.strategy.running.list")]
        [HttpPost("running/list")]
        public async Task<IActionResult> ListRunning([FromBody] ProtocolRequest<AdminRunningListRequest> request)
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

            var page = request?.Data?.Page ?? 1;
            var pageSize = request?.Data?.PageSize ?? 100;

            try
            {
                var (total, items) = await _strategyRepository.GetRunningStrategiesForAdminAsync(page, pageSize).ConfigureAwait(false);
                var data = new { total, items };
                var responseType = ProtocolEnvelopeFactory.BuildAckType(request?.Type ?? "admin.strategy.running.list");
                var envelope = ProtocolEnvelopeFactory.Ok(responseType, reqId, data, "查询成功", HttpContext.TraceIdentifier);
                return Ok(envelope);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "获取运行中策略列表失败");
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.InternalError, "获取运行中策略列表失败，请稍后重试", null, HttpContext.TraceIdentifier);
                return StatusCode(500, error);
            }
        }

        /// <summary>
        /// 按市场聚合运行中策略：交易所、币种、周期、运行策略数、最后一次运行时间。
        /// 数据来源：运行中策略按 config 聚合。
        /// </summary>
        [ProtocolType("admin.strategy.running.by-market")]
        [HttpPost("running/by-market")]
        public async Task<IActionResult> ListRunningByMarket([FromBody] ProtocolRequest<AdminRunningByMarketRequest> request)
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

            var machineName = request?.Data?.MachineName?.Trim();
            if (string.IsNullOrEmpty(machineName))
            {
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.InvalidRequest, "machineName 不能为空", null, HttpContext.TraceIdentifier);
                return BadRequest(error);
            }

            try
            {
                var markets = await _strategyRepository.GetRunningStrategiesByMarketAsync(HttpContext.RequestAborted).ConfigureAwait(false);

                var responseType = ProtocolEnvelopeFactory.BuildAckType(request?.Type ?? "admin.strategy.running.by-market");
                var envelope = ProtocolEnvelopeFactory.Ok(responseType, reqId, markets, "查询成功", HttpContext.TraceIdentifier);
                return Ok(envelope);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "按市场聚合运行策略失败: machineName={MachineName}", machineName);
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.InternalError, "查询失败，请稍后重试", null, HttpContext.TraceIdentifier);
                return StatusCode(500, error);
            }
        }

        /// <summary>
        /// 获取指定市场的任务执行报告：任务数、平均耗时、成功率、按阶段统计。
        /// </summary>
        [ProtocolType("admin.strategy.task.trace.market-summary")]
        [HttpPost("task-trace/market-summary")]
        public async Task<IActionResult> GetMarketTaskSummary([FromBody] ProtocolRequest<AdminTaskTraceMarketSummaryRequest> request)
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

            var machineName = request?.Data?.MachineName?.Trim();
            var exchange = request?.Data?.Exchange?.Trim() ?? string.Empty;
            var symbol = request?.Data?.Symbol?.Trim() ?? string.Empty;
            var timeframe = request?.Data?.Timeframe?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(machineName) || string.IsNullOrEmpty(exchange) || string.IsNullOrEmpty(symbol) || string.IsNullOrEmpty(timeframe))
            {
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.InvalidRequest, "machineName 与 exchange/symbol/timeframe 不能为空", null, HttpContext.TraceIdentifier);
                return BadRequest(error);
            }

            try
            {
                var summary = await _strategyRunLogRepository.GetMarketTaskSummaryAsync(
                        machineName,
                        exchange,
                        symbol,
                        timeframe,
                        500,
                        5,
                        HttpContext.RequestAborted)
                    .ConfigureAwait(false);
                var data = summary ?? new StrategyRunMarketSummary
                {
                    Exchange = exchange,
                    Symbol = symbol,
                    Timeframe = timeframe,
                    TaskCount = 0,
                    AvgDurationMs = 0,
                    SuccessRatePct = 100,
                    StageStats = new List<StrategyRunStageStat>(),
                    RecentOrders = new List<StrategyRunOrderSample>(),
                    RecentTasks = new List<StrategyRunTaskSample>()
                };
                var responseType = ProtocolEnvelopeFactory.BuildAckType(request?.Type ?? "admin.strategy.task.trace.market-summary");
                var envelope = ProtocolEnvelopeFactory.Ok(responseType, reqId, data, "查询成功", HttpContext.TraceIdentifier);
                return Ok(envelope);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "获取市场任务报告失败: machine={Machine} exchange={Exchange} symbol={Symbol} tf={Timeframe}", machineName, exchange, symbol, timeframe);
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.InternalError, "查询失败，请稍后重试", null, HttpContext.TraceIdentifier);
                return StatusCode(500, error);
            }
        }

        /// <summary>
        /// 获取策略实例最近运行画像（最近N条，默认5）。
        /// </summary>
        [ProtocolType("admin.strategy.run.metrics.recent")]
        [HttpPost("run-metrics/recent")]
        public async Task<IActionResult> GetRecentRunMetrics([FromBody] ProtocolRequest<AdminStrategyRunMetricsRecentRequest> request)
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

            var machineName = request?.Data?.MachineName?.Trim();
            var usId = request?.Data?.UsId ?? 0;
            var limit = request?.Data?.Limit ?? 5;
            if (string.IsNullOrWhiteSpace(machineName) || usId <= 0)
            {
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.InvalidRequest, "machineName 与 usId 不能为空", null, HttpContext.TraceIdentifier);
                return BadRequest(error);
            }

            try
            {
                var items = await _strategyRunLogRepository.ListRecentByStrategyAsync(machineName, usId, limit, HttpContext.RequestAborted).ConfigureAwait(false);
                var responseType = ProtocolEnvelopeFactory.BuildAckType(request?.Type ?? "admin.strategy.run.metrics.recent");
                var envelope = ProtocolEnvelopeFactory.Ok(responseType, reqId, items, "查询成功", HttpContext.TraceIdentifier);
                return Ok(envelope);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "获取策略运行画像失败: machineName={MachineName} usId={UsId}", machineName, usId);
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.InternalError, "查询失败，请稍后重试", null, HttpContext.TraceIdentifier);
                return StatusCode(500, error);
            }
        }

        /// <summary>
        /// 按市场筛选运行中策略，支持分页和搜索。
        /// </summary>
        [ProtocolType("admin.strategy.running.list-by-market")]
        [HttpPost("running/list-by-market")]
        public async Task<IActionResult> ListRunningByMarketFilter([FromBody] ProtocolRequest<AdminRunningListByMarketRequest> request)
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

            var exchange = request?.Data?.Exchange?.Trim() ?? string.Empty;
            var symbol = request?.Data?.Symbol?.Trim() ?? string.Empty;
            var timeframe = request?.Data?.Timeframe?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(exchange) || string.IsNullOrEmpty(symbol) || string.IsNullOrEmpty(timeframe))
            {
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.InvalidRequest, "exchange/symbol/timeframe 不能为空", null, HttpContext.TraceIdentifier);
                return BadRequest(error);
            }

            var page = request?.Data?.Page ?? 1;
            var pageSize = request?.Data?.PageSize ?? 100;
            var search = request?.Data?.Search?.Trim();

            try
            {
                var (total, items) = await _strategyRepository.GetRunningStrategiesForMarketAsync(exchange, symbol, timeframe, page, pageSize, search, HttpContext.RequestAborted).ConfigureAwait(false);
                var data = new { total, items };
                var responseType = ProtocolEnvelopeFactory.BuildAckType(request?.Type ?? "admin.strategy.running.list-by-market");
                var envelope = ProtocolEnvelopeFactory.Ok(responseType, reqId, data, "查询成功", HttpContext.TraceIdentifier);
                return Ok(envelope);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "按市场查询运行策略失败: exchange={Exchange} symbol={Symbol} tf={Timeframe}", exchange, symbol, timeframe);
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.InternalError, "查询失败，请稍后重试", null, HttpContext.TraceIdentifier);
                return StatusCode(500, error);
            }
        }

        /// <summary>
        /// 获取指定服务器（按 machineName）的实盘任务链路追踪分页列表，用于服务器实盘任务详情展示。
        /// </summary>
        [ProtocolType("admin.strategy.task.trace.list")]
        [HttpPost("task-trace/list")]
        public async Task<IActionResult> ListTaskTrace([FromBody] ProtocolRequest<AdminTaskTraceListRequest> request)
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

            var machineName = request?.Data?.MachineName?.Trim();
            if (string.IsNullOrEmpty(machineName))
            {
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.InvalidRequest, "machineName 不能为空", null, HttpContext.TraceIdentifier);
                return BadRequest(error);
            }

            var page = request?.Data?.Page ?? 1;
            var pageSize = request?.Data?.PageSize ?? 100;

            try
            {
                var (total, items) = await _traceLogRepository.ListAggregatedByActorInstancePrefixAsync(machineName, page, pageSize, HttpContext.RequestAborted).ConfigureAwait(false);
                var data = new { total, items };
                var responseType = ProtocolEnvelopeFactory.BuildAckType(request?.Type ?? "admin.strategy.task.trace.list");
                var envelope = ProtocolEnvelopeFactory.Ok(responseType, reqId, data, "查询成功", HttpContext.TraceIdentifier);
                return Ok(envelope);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "获取实盘任务链路追踪失败: machineName={MachineName}", machineName);
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.InternalError, "获取实盘任务链路追踪失败，请稍后重试", null, HttpContext.TraceIdentifier);
                return StatusCode(500, error);
            }
        }

        /// <summary>
        /// 获取指定服务器（按 machineName）的实盘布局：按 exchange/symbol/timeframe 聚合，每个市场一行（主记录模式）。
        /// </summary>
        [ProtocolType("admin.strategy.task.trace.layout")]
        [HttpPost("task-trace/layout")]
        public async Task<IActionResult> GetTaskTraceLayout([FromBody] ProtocolRequest<AdminTaskTraceLayoutRequest> request)
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

            var machineName = request?.Data?.MachineName?.Trim();
            if (string.IsNullOrEmpty(machineName))
            {
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.InvalidRequest, "machineName 不能为空", null, HttpContext.TraceIdentifier);
                return BadRequest(error);
            }

            try
            {
                var items = await _strategyRunLogRepository.ListLayoutByActorInstancePrefixAsync(machineName, 200, HttpContext.RequestAborted).ConfigureAwait(false);
                var responseType = ProtocolEnvelopeFactory.BuildAckType(request?.Type ?? "admin.strategy.task.trace.layout");
                var envelope = ProtocolEnvelopeFactory.Ok(responseType, reqId, items, "查询成功", HttpContext.TraceIdentifier);
                return Ok(envelope);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "获取实盘布局失败: machineName={MachineName}", machineName);
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.InternalError, "获取实盘布局失败，请稍后重试", null, HttpContext.TraceIdentifier);
                return StatusCode(500, error);
            }
        }

        /// <summary>
        /// 获取指定 trace_id 的完整链路明细，用于任务详情弹窗。
        /// </summary>
        [ProtocolType("admin.strategy.task.trace.detail")]
        [HttpPost("task-trace/detail")]
        public async Task<IActionResult> GetTaskTraceDetail([FromBody] ProtocolRequest<AdminTaskTraceDetailRequest> request)
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

            var traceId = request?.Data?.TraceId?.Trim();
            if (string.IsNullOrEmpty(traceId))
            {
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.InvalidRequest, "traceId 不能为空", null, HttpContext.TraceIdentifier);
                return BadRequest(error);
            }

            try
            {
                var items = await _traceLogRepository.GetByTraceIdAsync(traceId, HttpContext.RequestAborted).ConfigureAwait(false);
                var responseType = ProtocolEnvelopeFactory.BuildAckType(request?.Type ?? "admin.strategy.task.trace.detail");
                var envelope = ProtocolEnvelopeFactory.Ok(responseType, reqId, items, "查询成功", HttpContext.TraceIdentifier);
                return Ok(envelope);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "获取实盘任务链路明细失败: traceId={TraceId}", traceId);
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.InternalError, "获取实盘任务链路明细失败，请稍后重试", null, HttpContext.TraceIdentifier);
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
    /// 管理员运行中策略列表请求：分页参数。
    /// </summary>
    public sealed class AdminRunningListRequest
    {
        /// <summary>页码，从 1 开始，默认 1</summary>
        public int? Page { get; set; }
        /// <summary>每页条数，默认 100，最大 100</summary>
        public int? PageSize { get; set; }
    }

    /// <summary>
    /// 管理员实盘任务链路追踪列表请求。
    /// </summary>
    public sealed class AdminTaskTraceListRequest
    {
        /// <summary>机器名（用于匹配 actor_instance 前缀，格式 MachineName:ProcessId:Guid）</summary>
        public string? MachineName { get; set; }
        /// <summary>页码，从 1 开始，默认 1</summary>
        public int? Page { get; set; }
        /// <summary>每页条数，默认 100，最大 100</summary>
        public int? PageSize { get; set; }
    }

    /// <summary>
    /// 管理员实盘任务链路追踪详情请求。
    /// </summary>
    public sealed class AdminTaskTraceDetailRequest
    {
        /// <summary>trace_id（必填）</summary>
        public string? TraceId { get; set; }
    }

    /// <summary>
    /// 管理员实盘布局请求。
    /// </summary>
    public sealed class AdminTaskTraceLayoutRequest
    {
        /// <summary>机器名（必填）</summary>
        public string? MachineName { get; set; }
    }

    /// <summary>
    /// 按市场聚合运行策略请求。
    /// </summary>
    public sealed class AdminRunningByMarketRequest
    {
        /// <summary>机器名（必填，用于取本机 trace 最后执行时间）</summary>
        public string? MachineName { get; set; }
    }

    /// <summary>
    /// 市场任务报告请求。
    /// </summary>
    public sealed class AdminTaskTraceMarketSummaryRequest
    {
        public string? MachineName { get; set; }
        public string? Exchange { get; set; }
        public string? Symbol { get; set; }
        public string? Timeframe { get; set; }
    }

    /// <summary>
    /// 策略最近运行画像请求。
    /// </summary>
    public sealed class AdminStrategyRunMetricsRecentRequest
    {
        public string? MachineName { get; set; }
        public long? UsId { get; set; }
        public int? Limit { get; set; }
    }

    /// <summary>
    /// 按市场筛选运行策略请求。
    /// </summary>
    public sealed class AdminRunningListByMarketRequest
    {
        public string? Exchange { get; set; }
        public string? Symbol { get; set; }
        public string? Timeframe { get; set; }
        public int? Page { get; set; }
        public int? PageSize { get; set; }
        public string? Search { get; set; }
    }
}
