using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.ExchangeApiKeys.Infrastructure;
using ServerTest.Models;
using ServerTest.Models.Exchange;
using ServerTest.Options;
using ServerTest.Services;
using ServerTest.Modules.Accounts.Application;
using ServerTest.Protocol;

namespace ServerTest.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public sealed class UserExchangeApiKeysController : BaseController
    {
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

        private readonly UserExchangeApiKeyRepository _repository;
        private readonly AuthTokenService _tokenService;
        private readonly BusinessRulesOptions _businessRules;

        public UserExchangeApiKeysController(
            ILogger<UserExchangeApiKeysController> logger,
            UserExchangeApiKeyRepository repository,
            AuthTokenService tokenService,
            IOptions<BusinessRulesOptions> businessRules)
            : base(logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _tokenService = tokenService;
            _businessRules = businessRules?.Value ?? new BusinessRulesOptions();
        }

        [ProtocolType("exchange.api_key.list")]
        [HttpPost("list")]
        public async Task<IActionResult> GetAll([FromBody] ProtocolRequest<object> request)
        {
            var uid = await GetUserIdAsync();
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            try
            {
                var records = await _repository.GetAllByUidAsync(uid.Value, HttpContext.RequestAborted).ConfigureAwait(false);
                var results = new List<UserExchangeApiKeyDto>();
                foreach (var record in records)
                {
                    results.Add(new UserExchangeApiKeyDto
                    {
                        Id = record.Id,
                        ExchangeType = record.ExchangeType,
                        Label = record.Label,
                        ApiKeyMasked = MaskSecret(record.ApiKey),
                        ApiSecretMasked = MaskSecret(record.ApiSecret),
                        HasPassword = !string.IsNullOrWhiteSpace(record.ApiPassword),
                        CreatedAt = record.CreatedAt,
                        UpdatedAt = record.UpdatedAt
                    });
                }

                return Ok(ApiResponse<List<UserExchangeApiKeyDto>>.Ok(results));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "获取交易所 API 绑定列表失败: uid={Uid}", uid.Value);
                return StatusCode(500, ApiResponse<object>.Error("获取交易所 API 列表失败，请稍后重试"));
            }
        }

        [ProtocolType("exchange.api_key.create")]
        [HttpPost("create")]
        public async Task<IActionResult> Create([FromBody] ProtocolRequest<CreateUserExchangeApiKeyRequest> request)
        {
            var payload = request.Data;
            if (payload == null)
            {
                return BadRequest(ApiResponse<object>.Error("缺少请求数据"));
            }

            var uid = await GetUserIdAsync();
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            var exchangeType = NormalizeExchangeType(payload.ExchangeType);
            if (string.IsNullOrWhiteSpace(exchangeType) || !SupportedExchanges.Contains(exchangeType))
            {
                return BadRequest(ApiResponse<object>.Error("不支持的交易所类型"));
            }

            var label = payload.Label?.Trim() ?? string.Empty;
            var apiKey = payload.ApiKey?.Trim() ?? string.Empty;
            var apiSecret = payload.ApiSecret?.Trim() ?? string.Empty;
            var apiPassword = payload.ApiPassword?.Trim();

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
                return BadRequest(ApiResponse<object>.Error("请输入 API Key"));
            }

            if (apiKey.Length > 255)
            {
                return BadRequest(ApiResponse<object>.Error("API Key 长度不能超过255字符"));
            }

            if (string.IsNullOrWhiteSpace(apiSecret))
            {
                return BadRequest(ApiResponse<object>.Error("请输入 API Secret"));
            }

            if (apiSecret.Length > 255)
            {
                return BadRequest(ApiResponse<object>.Error("API Secret 长度不能超过255字符"));
            }

            if (RequiresPassphrase(exchangeType) && string.IsNullOrWhiteSpace(apiPassword))
            {
                return BadRequest(ApiResponse<object>.Error("该交易所需要填写 Passphrase"));
            }

            if (!string.IsNullOrWhiteSpace(apiPassword) && apiPassword.Length > 255)
            {
                return BadRequest(ApiResponse<object>.Error("Passphrase 长度不能超过255字符"));
            }

            try
            {
                var count = await _repository.CountByExchangeAsync(uid.Value, exchangeType, HttpContext.RequestAborted)
                    .ConfigureAwait(false);
                if (count >= _businessRules.MaxKeysPerExchange)
                {
                    return BadRequest(ApiResponse<object>.Error($"每个交易所最多绑定 {_businessRules.MaxKeysPerExchange} 个 API"));
                }

                var exists = await _repository.ExistsAsync(uid.Value, exchangeType, apiKey, HttpContext.RequestAborted)
                    .ConfigureAwait(false);
                if (exists)
                {
                    return BadRequest(ApiResponse<object>.Error("该 API Key 已绑定"));
                }

                await _repository.InsertAsync(uid.Value, exchangeType, label, apiKey, apiSecret, apiPassword, HttpContext.RequestAborted)
                    .ConfigureAwait(false);

                return Ok(ApiResponse<object?>.Ok(null, "绑定成功"));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "绑定交易所 API 失败: uid={Uid} exchange={Exchange}", uid.Value, exchangeType);
                return StatusCode(500, ApiResponse<object>.Error("绑定失败，请稍后重试"));
            }
        }

        public sealed class DeleteUserExchangeApiKeyRequest
        {
            public long Id { get; set; }
        }

        [ProtocolType("exchange.api_key.delete")]
        [HttpPost("delete")]
        public async Task<IActionResult> Delete([FromBody] ProtocolRequest<DeleteUserExchangeApiKeyRequest> request)
        {
            var payload = request.Data;
            if (payload == null)
            {
                return BadRequest(ApiResponse<object>.Error("缺少请求数据"));
            }

            var uid = await GetUserIdAsync();
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            if (payload.Id <= 0)
            {
                return BadRequest(ApiResponse<object>.Error("无效的绑定记录"));
            }

            try
            {
                var rows = await _repository.DeleteAsync(payload.Id, uid.Value, HttpContext.RequestAborted)
                    .ConfigureAwait(false);

                if (rows <= 0)
                {
                    return NotFound(ApiResponse<object>.Error("未找到绑定记录"));
                }

                return Ok(ApiResponse<object?>.Ok(null, "解绑成功"));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "解绑交易所 API 失败: uid={Uid} id={Id}", uid.Value, payload.Id);
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
