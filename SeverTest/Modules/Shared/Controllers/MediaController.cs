using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ServerTest.Modules.Accounts.Infrastructure;
using ServerTest.Modules.Accounts.Application;
using ServerTest.Models;
using ServerTest.Services;
using ServerTest.Protocol;

namespace ServerTest.Controllers
{
    /// <summary>
    /// 媒体文件上传控制器
    /// 支持头像上传，后续可扩展图片/视频等媒体类型
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public sealed class MediaController : BaseController
    {
        public sealed class AvatarUploadRequest : IProtocolRequest
        {
            public string Type { get; set; } = string.Empty;
            public string ReqId { get; set; } = string.Empty;
            public long Ts { get; set; }
            public IFormFile? File { get; set; }
        }

        private readonly OSSService _ossService;
        private readonly AccountRepository _accountRepository;
        private readonly AuthTokenService _tokenService;
        private readonly long _maxImageSizeBytes;
        private readonly long _maxVideoSizeBytes;
        private readonly HashSet<string> _allowedImageTypes;
        private readonly HashSet<string> _allowedVideoTypes;

        public MediaController(
            ILogger<MediaController> logger,
            OSSService ossService,
            AccountRepository accountRepository,
            AuthTokenService tokenService,
            IConfiguration configuration)
            : base(logger)
        {
            _ossService = ossService;
            _accountRepository = accountRepository ?? throw new ArgumentNullException(nameof(accountRepository));
            _tokenService = tokenService;

            // 读取上传限制配置
            _maxImageSizeBytes = configuration.GetValue<long>("Upload:MaxImageSizeBytes", 524288); // 默认 0.5MB
            _maxVideoSizeBytes = configuration.GetValue<long>("Upload:MaxVideoSizeBytes", 52428800); // 默认 50MB

            var imageTypes = configuration.GetValue<string>("Upload:AllowedImageTypes", "image/jpeg,image/png,image/gif,image/webp");
            var videoTypes = configuration.GetValue<string>("Upload:AllowedVideoTypes", "video/mp4,video/webm");

            _allowedImageTypes = new HashSet<string>(
                (imageTypes ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase);

            _allowedVideoTypes = new HashSet<string>(
                (videoTypes ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 上传头像
        /// POST /api/media/avatar
        /// </summary>
        [ProtocolType("media.avatar.upload")]
        [HttpPost("avatar")]
        [RequestSizeLimit(52428800)] // 50MB 上限（包含表单数据）
        public async Task<IActionResult> UploadAvatar([FromForm] AvatarUploadRequest request)
        {
            var file = request.File;
            var uid = await GetUserIdAsync();
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            // 检查 OSS 配置
            if (!_ossService.IsConfigured)
            {
                Logger.LogWarning("OSS 配置不完整，无法上传头像");
                return StatusCode(503, ApiResponse<object>.Error("文件存储服务暂不可用"));
            }

            // 校验文件
            if (file == null || file.Length == 0)
            {
                return BadRequest(ApiResponse<object>.Error("请选择要上传的文件"));
            }

            // 校验文件类型
            var contentType = file.ContentType?.ToLowerInvariant() ?? string.Empty;
            if (!_allowedImageTypes.Contains(contentType))
            {
                return BadRequest(ApiResponse<object>.Error("不支持的图片格式，仅支持 JPG、PNG、GIF、WebP"));
            }

            // 校验文件大小
            if (file.Length > _maxImageSizeBytes)
            {
                var maxSizeMB = _maxImageSizeBytes / 1024.0 / 1024.0;
                return BadRequest(ApiResponse<object>.Error($"图片大小不能超过 {maxSizeMB:F1}MB"));
            }

            try
            {
                // 获取文件扩展名
                var extension = GetFileExtension(file.FileName, contentType);

                // 生成 OSS 对象键
                var objectKey = _ossService.GenerateAvatarKey(uid.Value, extension);

                // 上传到 OSS
                using var stream = file.OpenReadStream();
                var uploadResult = await _ossService.UploadAsync(stream, objectKey, contentType);

                if (!uploadResult.IsSuccess)
                {
                    Logger.LogWarning("头像上传失败: uid={Uid}, error={Error}", uid.Value, uploadResult.ErrorMessage);
                    return StatusCode(500, ApiResponse<object>.Error(uploadResult.ErrorMessage ?? "上传失败，请稍后重试"));
                }

                // 更新数据库中的头像 URL
                var avatarUrl = uploadResult.Url!;
                var dbUpdated = await UpdateAvatarUrlAsync(uid.Value, avatarUrl);

                if (!dbUpdated)
                {
                    Logger.LogWarning("头像 URL 保存失败: uid={Uid}", uid.Value);
                    // 文件已上传，但数据库更新失败，仍返回成功并记录日志
                }

                Logger.LogInformation("头像上传成功: uid={Uid}, url={Url}", uid.Value, avatarUrl);

                return Ok(ApiResponse<AvatarUploadResponse>.Ok(new AvatarUploadResponse
                {
                    AvatarUrl = avatarUrl
                }, "头像上传成功"));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "头像上传异常: uid={Uid}", uid.Value);
                return StatusCode(500, ApiResponse<object>.Error("上传失败，请稍后重试"));
            }
        }

        /// <summary>
        /// 获取当前用户头像
        /// GET /api/media/avatar
        /// </summary>
        [ProtocolType("media.avatar.get")]
        [HttpPost("avatar/get")]
        public async Task<IActionResult> GetAvatar([FromBody] ProtocolRequest<object> request)
        {
            var uid = await GetUserIdAsync();
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            try
            {
                var avatarUrl = await _accountRepository.GetAvatarUrlAsync((ulong)uid.Value, null, HttpContext.RequestAborted)
                    .ConfigureAwait(false);

                return Ok(ApiResponse<AvatarResponse>.Ok(new AvatarResponse
                {
                    AvatarUrl = avatarUrl
                }));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "获取头像失败: uid={Uid}", uid.Value);
                return StatusCode(500, ApiResponse<object>.Error("获取头像失败，请稍后重试"));
            }
        }

        /// <summary>
        /// 更新数据库中的头像 URL
        /// </summary>
        private async Task<bool> UpdateAvatarUrlAsync(long uid, string avatarUrl)
        {
            try
            {
                var rows = await _accountRepository.UpdateAvatarUrlAsync((ulong)uid, avatarUrl, null, HttpContext.RequestAborted)
                    .ConfigureAwait(false);
                return rows > 0;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "更新头像 URL 失败: uid={Uid}", uid);
                return false;
            }
        }

        /// <summary>
        /// 获取文件扩展名
        /// </summary>
        private static string GetFileExtension(string? fileName, string contentType)
        {
            // 优先从文件名获取扩展名
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                var ext = Path.GetExtension(fileName);
                if (!string.IsNullOrWhiteSpace(ext))
                {
                    return ext.ToLowerInvariant();
                }
            }

            // 根据 Content-Type 推断扩展名
            return contentType switch
            {
                "image/jpeg" => ".jpg",
                "image/png" => ".png",
                "image/gif" => ".gif",
                "image/webp" => ".webp",
                "video/mp4" => ".mp4",
                "video/webm" => ".webm",
                _ => ".bin"
            };
        }

        /// <summary>
        /// 从请求中获取用户 ID
        /// </summary>
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

        /// <summary>
        /// 从 Authorization 头中提取 Bearer Token
        /// </summary>
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

    /// <summary>
    /// 头像上传响应
    /// </summary>
    public sealed class AvatarUploadResponse
    {
        public string AvatarUrl { get; set; } = string.Empty;
    }

    /// <summary>
    /// 头像获取响应
    /// </summary>
    public sealed class AvatarResponse
    {
        public string? AvatarUrl { get; set; }
    }
}
