using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ServerTest.Modules.Notifications.Infrastructure;
using ServerTest.Models;
using ServerTest.Models.Notify;
using ServerTest.Services;
using ServerTest.Modules.Accounts.Application;
using ServerTest.Protocol;

namespace ServerTest.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public sealed class UserNotifyChannelsController : BaseController
    {
        public sealed class NotifyChannelDeleteRequest
        {
            public string Platform { get; set; } = string.Empty;
        }

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

        private readonly UserNotifyChannelRepository _repository;
        private readonly AuthTokenService _tokenService;

        public UserNotifyChannelsController(
            ILogger<UserNotifyChannelsController> logger,
            UserNotifyChannelRepository repository,
            AuthTokenService tokenService)
            : base(logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _tokenService = tokenService;
        }

        [ProtocolType("notify_channel.list")]
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
                var results = new List<UserNotifyChannelDto>();
                foreach (var record in records)
                {
                    results.Add(new UserNotifyChannelDto
                    {
                        Id = record.Id,
                        Platform = record.Platform,
                        AddressMasked = MaskAddress(record.Platform, record.Address),
                        HasSecret = !string.IsNullOrWhiteSpace(record.Secret),
                        IsEnabled = record.IsEnabled,
                        IsDefault = record.IsDefault,
                        CreatedAt = record.CreatedAt,
                        UpdatedAt = record.UpdatedAt,
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

        [ProtocolType("notify_channel.upsert")]
        [HttpPost("upsert")]
        public async Task<IActionResult> Upsert([FromBody] ProtocolRequest<UpsertUserNotifyChannelRequest> request)
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

            var platform = NormalizePlatform(payload.Platform);
            if (string.IsNullOrWhiteSpace(platform) || !SupportedPlatforms.Contains(platform))
            {
                return BadRequest(ApiResponse<object>.Error("不支持的通知平台"));
            }

            var address = payload.Address?.Trim() ?? string.Empty;
            var secret = payload.Secret?.Trim();
            var isEnabled = payload.IsEnabled ?? true;
            var isDefault = payload.IsDefault ?? false;

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
                var ids = await _repository.GetIdsByPlatformAsync(uid.Value, platform, HttpContext.RequestAborted)
                    .ConfigureAwait(false);

                if (ids.Count == 0)
                {
                    await _repository.InsertAsync(uid.Value, platform, address, secret, isEnabled, isDefault, HttpContext.RequestAborted)
                        .ConfigureAwait(false);
                }
                else
                {
                    await _repository.UpdateAsync(ids[0], uid.Value, address, secret, isEnabled, isDefault, HttpContext.RequestAborted)
                        .ConfigureAwait(false);

                    if (ids.Count > 1)
                    {
                        await _repository.DeleteByIdsExceptAsync(uid.Value, platform, ids[0], HttpContext.RequestAborted)
                            .ConfigureAwait(false);
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

        [ProtocolType("notify_channel.delete")]
        [HttpPost("delete")]
        public async Task<IActionResult> Delete([FromBody] ProtocolRequest<NotifyChannelDeleteRequest> request)
        {
            var payload = request.Data;
            if (payload == null || string.IsNullOrWhiteSpace(payload.Platform))
            {
                return BadRequest(ApiResponse<object>.Error("缺少通知平台"));
            }

            var uid = await GetUserIdAsync();
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            var platform = NormalizePlatform(payload.Platform);
            if (string.IsNullOrWhiteSpace(platform) || !SupportedPlatforms.Contains(platform))
            {
                return BadRequest(ApiResponse<object>.Error("不支持的通知平台"));
            }

            try
            {
                var rows = await _repository.DeleteByPlatformAsync(uid.Value, platform, HttpContext.RequestAborted)
                    .ConfigureAwait(false);

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
