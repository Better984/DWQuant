using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using ServerTest.Models;
using ServerTest.Models.Exchange;
using ServerTest.Services;

namespace ServerTest.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public sealed class UserExchangeApiKeysController : BaseController
    {
        private const int MaxKeysPerExchange = 5;

        private static readonly HashSet<string> SupportedExchanges = new(StringComparer.OrdinalIgnoreCase)
        {
            "binance",
            "okx",
            "bitget",
            "bybit",
            "gate",
        };

        private static readonly HashSet<string> ExchangesRequiringPassphrase = new(StringComparer.OrdinalIgnoreCase)
        {
            "okx",
            "bitget",
        };

        private readonly DatabaseService _db;
        private readonly AuthTokenService _tokenService;

        public UserExchangeApiKeysController(
            ILogger<UserExchangeApiKeysController> logger,
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
SELECT id, exchange_type, label, api_key, api_secret, api_password, created_at, updated_at
FROM user_exchange_api_keys
WHERE uid = @uid
ORDER BY updated_at DESC, id DESC
", connection);
                cmd.Parameters.AddWithValue("@uid", uid.Value);

                using var reader = await cmd.ExecuteReaderAsync();
                var results = new List<UserExchangeApiKeyDto>();
                while (await reader.ReadAsync())
                {
                    var apiKey = reader.GetString("api_key");
                    var apiSecret = reader.GetString("api_secret");
                    var apiPasswordValue = reader["api_password"];

                    results.Add(new UserExchangeApiKeyDto
                    {
                        Id = reader.GetInt64("id"),
                        ExchangeType = reader.GetString("exchange_type"),
                        Label = reader.GetString("label"),
                        ApiKeyMasked = MaskSecret(apiKey),
                        ApiSecretMasked = MaskSecret(apiSecret),
                        HasPassword = apiPasswordValue != DBNull.Value && !string.IsNullOrWhiteSpace(apiPasswordValue?.ToString()),
                        CreatedAt = reader.GetDateTime("created_at"),
                        UpdatedAt = reader.GetDateTime("updated_at")
                    });
                }

                return Ok(ApiResponse<List<UserExchangeApiKeyDto>>.Ok(results));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "获取交易所API绑定列表失败: uid={Uid}", uid.Value);
                return StatusCode(500, ApiResponse<object>.Error("获取交易所API列表失败，请稍后重试"));
            }
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateUserExchangeApiKeyRequest request)
        {
            var uid = await GetUserIdAsync();
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            var exchangeType = NormalizeExchangeType(request.ExchangeType);
            if (string.IsNullOrWhiteSpace(exchangeType) || !SupportedExchanges.Contains(exchangeType))
            {
                return BadRequest(ApiResponse<object>.Error("不支持的交易所类型"));
            }

            var label = request.Label?.Trim() ?? string.Empty;
            var apiKey = request.ApiKey?.Trim() ?? string.Empty;
            var apiSecret = request.ApiSecret?.Trim() ?? string.Empty;
            var apiPassword = request.ApiPassword?.Trim();

            if (string.IsNullOrWhiteSpace(label))
            {
                return BadRequest(ApiResponse<object>.Error("请输入备注"));
            }

            if (label.Length > 64)
            {
                return BadRequest(ApiResponse<object>.Error("备注长度不能超过64字符"));
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return BadRequest(ApiResponse<object>.Error("请输入API Key"));
            }

            if (apiKey.Length > 255)
            {
                return BadRequest(ApiResponse<object>.Error("API Key长度不能超过255字符"));
            }

            if (string.IsNullOrWhiteSpace(apiSecret))
            {
                return BadRequest(ApiResponse<object>.Error("请输入API Secret"));
            }

            if (apiSecret.Length > 255)
            {
                return BadRequest(ApiResponse<object>.Error("API Secret长度不能超过255字符"));
            }

            if (RequiresPassphrase(exchangeType) && string.IsNullOrWhiteSpace(apiPassword))
            {
                return BadRequest(ApiResponse<object>.Error("该交易所需要填写Passphrase"));
            }

            if (!string.IsNullOrWhiteSpace(apiPassword) && apiPassword.Length > 255)
            {
                return BadRequest(ApiResponse<object>.Error("Passphrase长度不能超过255字符"));
            }

            try
            {
                using var connection = await _db.GetConnectionAsync();
                var countCmd = new MySqlCommand(@"
SELECT COUNT(*)
FROM user_exchange_api_keys
WHERE uid = @uid AND exchange_type = @exchange_type
", connection);
                countCmd.Parameters.AddWithValue("@uid", uid.Value);
                countCmd.Parameters.AddWithValue("@exchange_type", exchangeType);
                var count = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

                if (count >= MaxKeysPerExchange)
                {
                    return BadRequest(ApiResponse<object>.Error($"每个交易所最多绑定{MaxKeysPerExchange}个API"));
                }

                var existsCmd = new MySqlCommand(@"
SELECT 1
FROM user_exchange_api_keys
WHERE uid = @uid AND exchange_type = @exchange_type AND api_key = @api_key
LIMIT 1
", connection);
                existsCmd.Parameters.AddWithValue("@uid", uid.Value);
                existsCmd.Parameters.AddWithValue("@exchange_type", exchangeType);
                existsCmd.Parameters.AddWithValue("@api_key", apiKey);
                var exists = await existsCmd.ExecuteScalarAsync();
                if (exists != null)
                {
                    return BadRequest(ApiResponse<object>.Error("该API Key已绑定"));
                }

                var insertCmd = new MySqlCommand(@"
INSERT INTO user_exchange_api_keys (uid, exchange_type, label, api_key, api_secret, api_password)
VALUES (@uid, @exchange_type, @label, @api_key, @api_secret, @api_password)
", connection);
                insertCmd.Parameters.AddWithValue("@uid", uid.Value);
                insertCmd.Parameters.AddWithValue("@exchange_type", exchangeType);
                insertCmd.Parameters.AddWithValue("@label", label);
                insertCmd.Parameters.AddWithValue("@api_key", apiKey);
                insertCmd.Parameters.AddWithValue("@api_secret", apiSecret);
                insertCmd.Parameters.AddWithValue("@api_password", string.IsNullOrWhiteSpace(apiPassword) ? DBNull.Value : apiPassword);
                await insertCmd.ExecuteNonQueryAsync();

                return Ok(ApiResponse<object?>.Ok(null, "绑定成功"));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "绑定交易所API失败: uid={Uid} exchange={Exchange}", uid.Value, exchangeType);
                return StatusCode(500, ApiResponse<object>.Error("绑定失败，请稍后重试"));
            }
        }

        [HttpDelete("{id:long}")]
        public async Task<IActionResult> Delete(long id)
        {
            var uid = await GetUserIdAsync();
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            if (id <= 0)
            {
                return BadRequest(ApiResponse<object>.Error("无效的绑定记录"));
            }

            try
            {
                using var connection = await _db.GetConnectionAsync();
                var cmd = new MySqlCommand(@"
DELETE FROM user_exchange_api_keys
WHERE id = @id AND uid = @uid
", connection);
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@uid", uid.Value);
                var rows = await cmd.ExecuteNonQueryAsync();

                if (rows <= 0)
                {
                    return NotFound(ApiResponse<object>.Error("未找到绑定记录"));
                }

                return Ok(ApiResponse<object?>.Ok(null, "解绑成功"));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "解绑交易所API失败: uid={Uid} id={Id}", uid.Value, id);
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

        private static string NormalizeExchangeType(string? exchangeType)
        {
            return exchangeType?.Trim().ToLowerInvariant() ?? string.Empty;
        }

        private static bool RequiresPassphrase(string exchangeType)
        {
            return ExchangesRequiringPassphrase.Contains(exchangeType);
        }

        private static string MaskSecret(string value)
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
