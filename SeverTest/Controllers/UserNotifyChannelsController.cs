using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using ServerTest.Models;
using ServerTest.Models.Notify;
using ServerTest.Services;

namespace ServerTest.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public sealed class UserNotifyChannelsController : BaseController
    {
        private static readonly HashSet<string> SupportedPlatforms = new(StringComparer.OrdinalIgnoreCase)
        {
            "dingtalk",
            "wecom",
            "email",
            "telegram",
        };

        private static readonly HashSet<string> PlatformsRequireSecret = new(StringComparer.OrdinalIgnoreCase)
        {
            "telegram",
        };

        private readonly DatabaseService _db;
        private readonly AuthTokenService _tokenService;

        public UserNotifyChannelsController(
            ILogger<UserNotifyChannelsController> logger,
            DatabaseService db,
            AuthTokenService tokenService)
            : base(logger)
        {
            _db = db;
            _tokenService = tokenService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var uid = await GetUserIdAsync();
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            try
            {
                using var connection = await _db.GetConnectionAsync();
                var cmd = new MySqlCommand(@"
SELECT id, platform, address, secret, is_enabled, is_default, created_at, updated_at
FROM user_notify_channels
WHERE uid = @uid
ORDER BY updated_at DESC, id DESC
", connection);
                cmd.Parameters.AddWithValue("@uid", uid.Value);

                using var reader = await cmd.ExecuteReaderAsync();
                var results = new List<UserNotifyChannelDto>();
                while (await reader.ReadAsync())
                {
                    var platform = reader.GetString("platform");
                    var address = reader.GetString("address");
                    var secretValue = reader["secret"];

                    results.Add(new UserNotifyChannelDto
                    {
                        Id = reader.GetInt64("id"),
                        Platform = platform,
                        AddressMasked = MaskAddress(platform, address),
                        HasSecret = secretValue != DBNull.Value && !string.IsNullOrWhiteSpace(secretValue?.ToString()),
                        IsEnabled = reader.GetBoolean("is_enabled"),
                        IsDefault = reader.GetBoolean("is_default"),
                        CreatedAt = reader.GetDateTime("created_at"),
                        UpdatedAt = reader.GetDateTime("updated_at"),
                    });
                }

                return Ok(ApiResponse<List<UserNotifyChannelDto>>.Ok(results));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "获取通知渠道失败: uid={Uid}", uid.Value);
                return StatusCode(500, ApiResponse<object>.Error("获取通知渠道失败，请稍后重试"));
            }
        }

        [HttpPost]
        public async Task<IActionResult> Upsert([FromBody] UpsertUserNotifyChannelRequest request)
        {
            var uid = await GetUserIdAsync();
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            var platform = NormalizePlatform(request.Platform);
            if (string.IsNullOrWhiteSpace(platform) || !SupportedPlatforms.Contains(platform))
            {
                return BadRequest(ApiResponse<object>.Error("不支持的通知平台"));
            }

            var address = request.Address?.Trim() ?? string.Empty;
            var secret = request.Secret?.Trim();
            var isEnabled = request.IsEnabled ?? true;
            var isDefault = request.IsDefault ?? false;

            if (string.IsNullOrWhiteSpace(address))
            {
                return BadRequest(ApiResponse<object>.Error("请输入通知地址"));
            }

            if (address.Length > 512)
            {
                return BadRequest(ApiResponse<object>.Error("通知地址长度不能超过512字符"));
            }

            if (RequiresSecret(platform) && string.IsNullOrWhiteSpace(secret))
            {
                return BadRequest(ApiResponse<object>.Error("该平台需要填写密钥"));
            }

            if (!string.IsNullOrWhiteSpace(secret) && secret.Length > 255)
            {
                return BadRequest(ApiResponse<object>.Error("密钥长度不能超过255字符"));
            }

            try
            {
                using var connection = await _db.GetConnectionAsync();
                var selectCmd = new MySqlCommand(@"
SELECT id
FROM user_notify_channels
WHERE uid = @uid AND platform = @platform
ORDER BY updated_at DESC, id DESC
", connection);
                selectCmd.Parameters.AddWithValue("@uid", uid.Value);
                selectCmd.Parameters.AddWithValue("@platform", platform);

                var ids = new List<long>();
                using (var reader = await selectCmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        ids.Add(reader.GetInt64("id"));
                    }
                }

                if (ids.Count == 0)
                {
                    var insertCmd = new MySqlCommand(@"
INSERT INTO user_notify_channels (uid, platform, address, secret, is_enabled, is_default)
VALUES (@uid, @platform, @address, @secret, @is_enabled, @is_default)
", connection);
                    insertCmd.Parameters.AddWithValue("@uid", uid.Value);
                    insertCmd.Parameters.AddWithValue("@platform", platform);
                    insertCmd.Parameters.AddWithValue("@address", address);
                    insertCmd.Parameters.AddWithValue("@secret", string.IsNullOrWhiteSpace(secret) ? DBNull.Value : secret);
                    insertCmd.Parameters.AddWithValue("@is_enabled", isEnabled ? 1 : 0);
                    insertCmd.Parameters.AddWithValue("@is_default", isDefault ? 1 : 0);
                    await insertCmd.ExecuteNonQueryAsync();
                }
                else
                {
                    var updateCmd = new MySqlCommand(@"
UPDATE user_notify_channels
SET address = @address,
    secret = @secret,
    is_enabled = @is_enabled,
    is_default = @is_default,
    updated_at = CURRENT_TIMESTAMP(3)
WHERE id = @id AND uid = @uid
", connection);
                    updateCmd.Parameters.AddWithValue("@id", ids[0]);
                    updateCmd.Parameters.AddWithValue("@uid", uid.Value);
                    updateCmd.Parameters.AddWithValue("@address", address);
                    updateCmd.Parameters.AddWithValue("@secret", string.IsNullOrWhiteSpace(secret) ? DBNull.Value : secret);
                    updateCmd.Parameters.AddWithValue("@is_enabled", isEnabled ? 1 : 0);
                    updateCmd.Parameters.AddWithValue("@is_default", isDefault ? 1 : 0);
                    await updateCmd.ExecuteNonQueryAsync();

                    if (ids.Count > 1)
                    {
                        var deleteCmd = new MySqlCommand(@"
DELETE FROM user_notify_channels
WHERE uid = @uid AND platform = @platform AND id <> @keep_id
", connection);
                        deleteCmd.Parameters.AddWithValue("@uid", uid.Value);
                        deleteCmd.Parameters.AddWithValue("@platform", platform);
                        deleteCmd.Parameters.AddWithValue("@keep_id", ids[0]);
                        await deleteCmd.ExecuteNonQueryAsync();
                    }
                }

                return Ok(ApiResponse<object?>.Ok(null, "绑定成功"));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "绑定通知渠道失败: uid={Uid} platform={Platform}", uid.Value, platform);
                return StatusCode(500, ApiResponse<object>.Error("绑定失败，请稍后重试"));
            }
        }

        [HttpDelete("{platform}")]
        public async Task<IActionResult> Delete(string platform)
        {
            var uid = await GetUserIdAsync();
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            platform = NormalizePlatform(platform);
            if (string.IsNullOrWhiteSpace(platform) || !SupportedPlatforms.Contains(platform))
            {
                return BadRequest(ApiResponse<object>.Error("不支持的通知平台"));
            }

            try
            {
                using var connection = await _db.GetConnectionAsync();
                var cmd = new MySqlCommand(@"
DELETE FROM user_notify_channels
WHERE uid = @uid AND platform = @platform
", connection);
                cmd.Parameters.AddWithValue("@uid", uid.Value);
                cmd.Parameters.AddWithValue("@platform", platform);
                var rows = await cmd.ExecuteNonQueryAsync();

                if (rows <= 0)
                {
                    return NotFound(ApiResponse<object>.Error("未找到绑定记录"));
                }

                return Ok(ApiResponse<object?>.Ok(null, "解绑成功"));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "解绑通知渠道失败: uid={Uid} platform={Platform}", uid.Value, platform);
                return StatusCode(500, ApiResponse<object>.Error("解绑失败，请稍后重试"));
            }
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

        private static string NormalizePlatform(string? platform)
        {
            return platform?.Trim().ToLowerInvariant() ?? string.Empty;
        }

        private static bool RequiresSecret(string platform)
        {
            return PlatformsRequireSecret.Contains(platform);
        }

        private static string MaskAddress(string platform, string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return string.Empty;
            }

            if (string.Equals(platform, "email", StringComparison.OrdinalIgnoreCase))
            {
                var atIndex = address.IndexOf('@');
                if (atIndex > 1)
                {
                    return $"{address.Substring(0, 1)}****{address.Substring(atIndex)}";
                }
            }

            return MaskGeneric(address);
        }

        private static string MaskGeneric(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            if (value.Length <= 8)
            {
                return new string('*', value.Length);
            }

            return $"{value.Substring(0, 4)}****{value.Substring(value.Length - 4)}";
        }
    }
}
