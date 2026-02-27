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
        private readonly BacktestTaskService _taskService;
        private readonly AuthTokenService _tokenService;

        public BacktestController(
            ILogger<BacktestController> logger,
            BacktestService service,
            BacktestTaskService taskService,
            AuthTokenService tokenService)
            : base(logger)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _taskService = taskService ?? throw new ArgumentNullException(nameof(taskService));
            _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
        }

        /// <summary>
        /// 回测运行入口（同步执行，兼容旧接口）
        /// </summary>
        [ProtocolType("backtest.run")]
        [HttpPost("run")]
        public async Task<IActionResult> Run([FromBody] ProtocolRequest<BacktestRunRequest> request)
        {
            if (request?.Data == null)
                return BadRequest(ApiResponse<object>.Error("缺少请求数据"));

            try
            {
                var uid = await GetUserIdAsync().ConfigureAwait(false);
                var result = await _service
                    .RunAsync(request.Data, request.ReqId, uid, null, HttpContext.RequestAborted)
                    .ConfigureAwait(false);
                return Ok(ApiResponse<BacktestRunResult>.Ok(result));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "回测执行失败");
                return BadRequest(ApiResponse<object>.Error($"回测执行失败: {ex.Message}"));
            }
        }

        /// <summary>
        /// 提交回测任务（异步队列，返回 taskId）
        /// </summary>
        [ProtocolType("backtest.submit")]
        [HttpPost("submit")]
        public async Task<IActionResult> Submit([FromBody] ProtocolRequest<BacktestRunRequest> request)
        {
            if (request?.Data == null)
                return BadRequest(ApiResponse<object>.Error("缺少请求数据"));

            try
            {
                var uid = await GetUserIdAsync().ConfigureAwait(false);
                if (!uid.HasValue || uid.Value <= 0)
                    return Unauthorized(ApiResponse<object>.Error("未登录"));

                var summary = await _taskService
                    .SubmitAsync(request.Data, uid.Value, request.ReqId, HttpContext.RequestAborted)
                    .ConfigureAwait(false);
                return Ok(ApiResponse<BacktestTaskSummary>.Ok(summary));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse<object>.Error(ex.Message));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "回测任务提交失败");
                return BadRequest(ApiResponse<object>.Error($"回测任务提交失败: {ex.Message}"));
            }
        }

        /// <summary>
        /// 查询回测任务状态（用于 HTTP 轮询）
        /// </summary>
        [HttpGet("task/{taskId}/status")]
        public async Task<IActionResult> GetTaskStatus(long taskId)
        {
            try
            {
                var uid = await GetUserIdAsync().ConfigureAwait(false);
                if (!uid.HasValue || uid.Value <= 0)
                    return Unauthorized(ApiResponse<object>.Error("未登录"));

                var summary = await _taskService
                    .GetTaskStatusAsync(taskId, uid.Value, HttpContext.RequestAborted)
                    .ConfigureAwait(false);

                if (summary == null)
                    return NotFound(ApiResponse<object>.Error("任务不存在"));

                return Ok(ApiResponse<BacktestTaskSummary>.Ok(summary));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "查询回测任务状态失败: taskId={TaskId}", taskId);
                return BadRequest(ApiResponse<object>.Error($"查询失败: {ex.Message}"));
            }
        }

        /// <summary>
        /// 获取回测任务完整结果（含 result_json）
        /// </summary>
        [HttpGet("task/{taskId}/result")]
        public async Task<IActionResult> GetTaskResult(long taskId)
        {
            try
            {
                var uid = await GetUserIdAsync().ConfigureAwait(false);
                if (!uid.HasValue || uid.Value <= 0)
                    return Unauthorized(ApiResponse<object>.Error("未登录"));

                var task = await _taskService
                    .GetTaskAsync(taskId, uid.Value, HttpContext.RequestAborted)
                    .ConfigureAwait(false);

                if (task == null)
                    return NotFound(ApiResponse<object>.Error("任务不存在"));

                if (task.Status != BacktestTaskStatus.Completed)
                    return BadRequest(ApiResponse<object>.Error($"任务尚未完成，当前状态: {task.Status}"));

                // 直接返回已序列化的结果 JSON
                return Content(task.ResultJson ?? "{}", "application/json");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "获取回测任务结果失败: taskId={TaskId}", taskId);
                return BadRequest(ApiResponse<object>.Error($"获取结果失败: {ex.Message}"));
            }
        }

        /// <summary>
        /// 列出用户的回测任务历史
        /// </summary>
        [HttpGet("tasks")]
        public async Task<IActionResult> ListTasks([FromQuery] int limit = 20)
        {
            try
            {
                var uid = await GetUserIdAsync().ConfigureAwait(false);
                if (!uid.HasValue || uid.Value <= 0)
                    return Unauthorized(ApiResponse<object>.Error("未登录"));

                var tasks = await _taskService
                    .ListTasksAsync(uid.Value, limit, HttpContext.RequestAborted)
                    .ConfigureAwait(false);

                return Ok(ApiResponse<object>.Ok(tasks));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "列出回测任务失败");
                return BadRequest(ApiResponse<object>.Error($"列出任务失败: {ex.Message}"));
            }
        }

        /// <summary>
        /// 列出用户当前活跃回测任务（queued/running）
        /// </summary>
        [HttpGet("tasks/active")]
        public async Task<IActionResult> ListActiveTasks([FromQuery] int limit = 20)
        {
            try
            {
                var uid = await GetUserIdAsync().ConfigureAwait(false);
                if (!uid.HasValue || uid.Value <= 0)
                    return Unauthorized(ApiResponse<object>.Error("未登录"));

                var tasks = await _taskService
                    .ListActiveTasksAsync(uid.Value, limit, HttpContext.RequestAborted)
                    .ConfigureAwait(false);

                return Ok(ApiResponse<object>.Ok(tasks));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "列出活跃回测任务失败");
                return BadRequest(ApiResponse<object>.Error($"列出活跃任务失败: {ex.Message}"));
            }
        }

        /// <summary>
        /// 取消回测任务
        /// </summary>
        [HttpPost("task/{taskId}/cancel")]
        public async Task<IActionResult> CancelTask(long taskId)
        {
            try
            {
                var uid = await GetUserIdAsync().ConfigureAwait(false);
                if (!uid.HasValue || uid.Value <= 0)
                    return Unauthorized(ApiResponse<object>.Error("未登录"));

                var cancelled = await _taskService
                    .CancelTaskAsync(taskId, uid.Value, HttpContext.RequestAborted)
                    .ConfigureAwait(false);

                if (!cancelled)
                    return BadRequest(ApiResponse<object>.Error("任务不存在或无法取消"));

                return Ok(ApiResponse<object>.Ok("已取消"));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "取消回测任务失败: taskId={TaskId}", taskId);
                return BadRequest(ApiResponse<object>.Error($"取消失败: {ex.Message}"));
            }
        }

        private async Task<long?> GetUserIdAsync()
        {
            var token = GetBearerToken(Request.Headers.Authorization.ToString());
            if (string.IsNullOrWhiteSpace(token))
                return null;

            var validation = await _tokenService.ValidateTokenAsync(token).ConfigureAwait(false);
            if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.UserId))
                return null;

            return long.TryParse(validation.UserId, out var uid) ? uid : null;
        }

        private static string? GetBearerToken(string? authorizationHeader)
        {
            if (string.IsNullOrWhiteSpace(authorizationHeader))
                return null;

            const string prefix = "Bearer ";
            if (authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return authorizationHeader.Substring(prefix.Length).Trim();

            return null;
        }
    }
}
