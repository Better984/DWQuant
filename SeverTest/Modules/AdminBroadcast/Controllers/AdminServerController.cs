using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ServerTest.Modules.Accounts.Application;
using ServerTest.Modules.Accounts.Infrastructure;
using ServerTest.Modules.AdminBroadcast.Application;
using ServerTest.Models;
using ServerTest.Services;
using ServerTest.Protocol;
using ServerTest.Options;
using Microsoft.Extensions.Options;

namespace ServerTest.Controllers
{
    [ApiController]
    [Route("api/admin/server")]
    public sealed class AdminServerController : BaseController
    {
        private readonly AuthTokenService _tokenService;
        private readonly AccountRepository _accountRepository;
        private readonly ServerStatusService _serverStatusService;
        private readonly BusinessRulesOptions _businessRules;

        public AdminServerController(
            ILogger<AdminServerController> logger,
            AuthTokenService tokenService,
            AccountRepository accountRepository,
            ServerStatusService serverStatusService,
            IOptions<BusinessRulesOptions> businessRules)
            : base(logger)
        {
            _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
            _accountRepository = accountRepository ?? throw new ArgumentNullException(nameof(accountRepository));
            _serverStatusService = serverStatusService ?? throw new ArgumentNullException(nameof(serverStatusService));
            _businessRules = businessRules?.Value ?? new BusinessRulesOptions();
        }

        [ProtocolType("admin.server.list")]
        [HttpPost("list")]
        public async Task<IActionResult> GetServerList([FromBody] ProtocolRequest<object> request)
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
                var servers = await _serverStatusService.GetAllServersAsync(HttpContext.RequestAborted).ConfigureAwait(false);
                var result = servers.Select(s => new
                {
                    nodeId = s.NodeId,
                    machineName = s.MachineName,
                    isCurrentNode = s.IsCurrentNode,
                    status = s.Status,
                    connectionCount = s.ConnectionCount,
                    systems = s.Systems,
                    lastHeartbeat = s.LastHeartbeat.ToString("O"),
                    processInfo = s.ProcessInfo != null ? new
                    {
                        processId = s.ProcessInfo.ProcessId,
                        processName = s.ProcessInfo.ProcessName,
                        startTime = s.ProcessInfo.StartTime.ToString("O"),
                        cpuUsage = Math.Round(s.ProcessInfo.CpuUsage, 2),
                        memoryUsage = s.ProcessInfo.MemoryUsage,
                        threadCount = s.ProcessInfo.ThreadCount,
                    } : null,
                    systemInfo = s.SystemInfo != null ? new
                    {
                        machineName = s.SystemInfo.MachineName,
                        osVersion = s.SystemInfo.OsVersion,
                        processorCount = s.SystemInfo.ProcessorCount,
                        totalMemory = s.SystemInfo.TotalMemory,
                        is64BitProcess = s.SystemInfo.Is64BitProcess,
                        is64BitOperatingSystem = s.SystemInfo.Is64BitOperatingSystem,
                    } : null,
                    runtimeInfo = s.RuntimeInfo != null ? new
                    {
                        dotNetVersion = s.RuntimeInfo.DotNetVersion,
                        frameworkDescription = s.RuntimeInfo.FrameworkDescription,
                        clrVersion = s.RuntimeInfo.ClrVersion,
                    } : null,
                }).ToList();

                return Ok(ApiResponse<object>.Ok(new { servers = result }, "查询成功"));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "获取服务器列表失败");
                return StatusCode(500, ApiResponse<object>.Error($"查询失败: {ex.Message}"));
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
