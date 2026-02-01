using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlConnector;
using ServerTest.Models;
using ServerTest.Options;
using ServerTest.Services;

namespace ServerTest.Controllers
{
    [ApiController]
    [Route("api/trading")]
    public sealed class TradingSettingsController : BaseController
    {
        private const int SuperAdminRole = 255;
        private static readonly SemaphoreSlim ConfigFileLock = new(1, 1);

        private readonly DatabaseService _db;
        private readonly AuthTokenService _tokenService;
        private readonly IOptionsMonitor<TradingOptions> _tradingOptions;
        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _environment;

        public TradingSettingsController(
            ILogger<TradingSettingsController> logger,
            DatabaseService db,
            AuthTokenService tokenService,
            IOptionsMonitor<TradingOptions> tradingOptions,
            IConfiguration configuration,
            IHostEnvironment environment)
            : base(logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
            _tradingOptions = tradingOptions ?? throw new ArgumentNullException(nameof(tradingOptions));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        }

        public sealed class SandboxToggleRequest
        {
            public bool EnableSandboxMode { get; set; }
        }

        [HttpGet("sandbox")]
        public async Task<IActionResult> GetSandboxMode()
        {
            var uid = await GetUserIdAsync();
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("Unauthorized"));
            }

            var role = await GetUserRoleAsync(uid.Value);
            if (!role.HasValue || role.Value != SuperAdminRole)
            {
                return StatusCode(403, ApiResponse<object>.Error("Forbidden"));
            }

            var enabled = _tradingOptions.CurrentValue.EnableSandboxMode;
            return Ok(ApiResponse<object>.Ok(new { enableSandboxMode = enabled }));
        }

        [HttpPost("sandbox")]
        public async Task<IActionResult> SetSandboxMode([FromBody] SandboxToggleRequest request)
        {
            if (request == null)
            {
                return BadRequest(ApiResponse<object>.Error("Invalid request"));
            }

            var uid = await GetUserIdAsync();
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("Unauthorized"));
            }

            var role = await GetUserRoleAsync(uid.Value);
            if (!role.HasValue || role.Value != SuperAdminRole)
            {
                return StatusCode(403, ApiResponse<object>.Error("Forbidden"));
            }

            var updated = await TryUpdateSandboxConfigAsync(request.EnableSandboxMode, HttpContext.RequestAborted);
            if (!updated)
            {
                return StatusCode(500, ApiResponse<object>.Error("Failed to update configuration"));
            }

            Logger.LogInformation("Trading sandbox mode updated: enable={Enable} uid={Uid}", request.EnableSandboxMode, uid.Value);
            return Ok(ApiResponse<object>.Ok(new { enableSandboxMode = request.EnableSandboxMode }, "Updated"));
        }

        private async Task<bool> TryUpdateSandboxConfigAsync(bool enableSandboxMode, CancellationToken ct)
        {
            var settingsPath = ResolveSettingsPath();
            if (string.IsNullOrWhiteSpace(settingsPath))
            {
                Logger.LogError("appsettings.json not found for environment {Environment}", _environment.EnvironmentName);
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
                Logger.LogError(ex, "Failed to update trading sandbox configuration");
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
                using var connection = await _db.GetConnectionAsync();
                var cmd = new MySqlCommand(@"
SELECT role
FROM account
WHERE uid = @uid AND deleted_at IS NULL
LIMIT 1
", connection);
                cmd.Parameters.AddWithValue("@uid", uid);
                var result = await cmd.ExecuteScalarAsync();
                return result == null ? null : Convert.ToInt32(result);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to query user role: uid={Uid}", uid);
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
