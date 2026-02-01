using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.Accounts.Infrastructure;
using ServerTest.Modules.Accounts.Application;
using ServerTest.Models;
using ServerTest.Options;
using ServerTest.Services;
using ServerTest.Protocol;

namespace ServerTest.Controllers
{
    [ApiController]
    [Route("api/trading")]
    public sealed class TradingSettingsController : BaseController
    {
        private static readonly SemaphoreSlim ConfigFileLock = new(1, 1);

        private readonly AccountRepository _accountRepository;
        private readonly AuthTokenService _tokenService;
        private readonly IOptionsMonitor<TradingOptions> _tradingOptions;
        private readonly BusinessRulesOptions _businessRules;
        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _environment;

        public TradingSettingsController(
            ILogger<TradingSettingsController> logger,
            AccountRepository accountRepository,
            AuthTokenService tokenService,
            IOptionsMonitor<TradingOptions> tradingOptions,
            IOptions<BusinessRulesOptions> businessRules,
            IConfiguration configuration,
            IHostEnvironment environment)
            : base(logger)
        {
            _accountRepository = accountRepository ?? throw new ArgumentNullException(nameof(accountRepository));
            _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
            _tradingOptions = tradingOptions ?? throw new ArgumentNullException(nameof(tradingOptions));
            _businessRules = businessRules?.Value ?? new BusinessRulesOptions();
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        }

        public sealed class SandboxToggleRequest
        {
            public bool EnableSandboxMode { get; set; }
        }

        [ProtocolType("trading.sandbox.get")]
        [HttpPost("sandbox/get")]
        public async Task<IActionResult> GetSandboxMode([FromBody] ProtocolRequest<object> request)
        {
            var uid = await GetUserIdAsync();
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            var role = await GetUserRoleAsync(uid.Value);
            if (!role.HasValue || role.Value != _businessRules.SuperAdminRole)
            {
                return StatusCode(403, ApiResponse<object>.Error("无权限访问"));
            }

            var enabled = _tradingOptions.CurrentValue.EnableSandboxMode;
            return Ok(ApiResponse<object>.Ok(new { enableSandboxMode = enabled }));
        }

        [ProtocolType("trading.sandbox.set")]
        [HttpPost("sandbox/set")]
        public async Task<IActionResult> SetSandboxMode([FromBody] ProtocolRequest<SandboxToggleRequest> request)
        {
            var payload = request.Data;
            if (payload == null)
            {
                return BadRequest(ApiResponse<object>.Error("请求无效"));
            }

            var uid = await GetUserIdAsync();
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            var role = await GetUserRoleAsync(uid.Value);
            if (!role.HasValue || role.Value != _businessRules.SuperAdminRole)
            {
                return StatusCode(403, ApiResponse<object>.Error("无权限访问"));
            }

            var updated = await TryUpdateSandboxConfigAsync(payload.EnableSandboxMode, HttpContext.RequestAborted);
            if (!updated)
            {
                return StatusCode(500, ApiResponse<object>.Error("更新配置失败"));
            }

            Logger.LogInformation("交易沙盒模式已更新: enable={Enable} uid={Uid}", payload.EnableSandboxMode, uid.Value);
            return Ok(ApiResponse<object>.Ok(new { enableSandboxMode = payload.EnableSandboxMode }, "更新成功"));
        }

        private async Task<bool> TryUpdateSandboxConfigAsync(bool enableSandboxMode, CancellationToken ct)
        {
            var settingsPath = ResolveSettingsPath();
            if (string.IsNullOrWhiteSpace(settingsPath))
            {
                Logger.LogError("未找到 appsettings.json: 环境 {Environment}", _environment.EnvironmentName);
                return false;
            }

            await ConfigFileLock.WaitAsync(ct);
            try
            {
                var root = await LoadConfigAsync(settingsPath, ct).ConfigureAwait(false);
                var tradingNode = root["Trading"] as JsonObject ?? new JsonObject();
                tradingNode["EnableSandboxMode"] = enableSandboxMode;
                root["Trading"] = tradingNode;

                var updatedJson = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                await WriteAtomicallyAsync(settingsPath, updatedJson, ct).ConfigureAwait(false);

                if (_configuration is IConfigurationRoot configurationRoot)
                {
                    configurationRoot.Reload();
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "更新交易沙盒配置失败");
                return false;
            }
            finally
            {
                ConfigFileLock.Release();
            }
        }

        private string? ResolveSettingsPath()
        {
            var contentRoot = _environment.ContentRootPath;
            var envPath = Path.Combine(contentRoot, $"appsettings.{_environment.EnvironmentName}.json");
            if (System.IO.File.Exists(envPath))
            {
                return envPath;
            }

            var basePath = Path.Combine(contentRoot, "appsettings.json");
            return System.IO.File.Exists(basePath) ? basePath : null;
        }

        private static async Task<JsonObject> LoadConfigAsync(string path, CancellationToken ct)
        {
            if (!System.IO.File.Exists(path))
            {
                return new JsonObject();
            }

            var json = await System.IO.File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new JsonObject();
            }

            return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
        }

        private static async Task WriteAtomicallyAsync(string path, string content, CancellationToken ct)
        {
            var directory = Path.GetDirectoryName(path) ?? string.Empty;
            var tempPath = Path.Combine(directory, $"{Path.GetFileName(path)}.tmp");
            await System.IO.File.WriteAllTextAsync(tempPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct)
                .ConfigureAwait(false);
            System.IO.File.Copy(tempPath, path, overwrite: true);
            System.IO.File.Delete(tempPath);
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

        private async Task<int?> GetUserRoleAsync(long uid)
        {
            try
            {
                return await _accountRepository.GetRoleAsync((ulong)uid, null, HttpContext.RequestAborted)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "查询用户角色失败: uid={Uid}", uid);
                return null;
            }
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
