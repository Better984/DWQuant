using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ServerTest.Models;
using ServerTest.Modules.Accounts.Application;
using ServerTest.Modules.Planet.Application;
using ServerTest.Modules.Planet.Domain;
using ServerTest.Protocol;
using ServerTest.Services;

namespace ServerTest.Controllers
{
    [ApiController]
    [Route("api/planet")]
    public sealed class PlanetController : BaseController
    {
        public sealed class PlanetImageUploadRequest : IProtocolRequest
        {
            public string Type { get; set; } = string.Empty;
            public string ReqId { get; set; } = string.Empty;
            public long Ts { get; set; }
            public IFormFile? File { get; set; }
        }

        private readonly PlanetService _planetService;
        private readonly AuthTokenService _tokenService;
        private readonly OSSService _ossService;
        private readonly long _maxImageSizeBytes;
        private readonly HashSet<string> _allowedImageTypes;

        public PlanetController(
            ILogger<PlanetController> logger,
            PlanetService planetService,
            AuthTokenService tokenService,
            OSSService ossService,
            IConfiguration configuration)
            : base(logger)
        {
            _planetService = planetService ?? throw new ArgumentNullException(nameof(planetService));
            _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
            _ossService = ossService ?? throw new ArgumentNullException(nameof(ossService));

            _maxImageSizeBytes = configuration.GetValue<long>("Upload:MaxImageSizeBytes", 524288);
            var imageTypes = configuration.GetValue<string>("Upload:AllowedImageTypes", "image/jpeg,image/png,image/gif,image/webp");
            _allowedImageTypes = new HashSet<string>(
                (imageTypes ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase);
        }

        [ProtocolType("planet.post.create")]
        [HttpPost("posts/create")]
        public Task<IActionResult> CreatePost([FromBody] ProtocolRequest<PlanetPostCreateRequest> request)
        {
            var payload = request.Data;
            if (payload == null)
            {
                return Task.FromResult<IActionResult>(BadRequest(ApiResponse<object>.Error("缺少请求数据")));
            }

            return WithUserAsync(request, uid => _planetService.CreatePostAsync(uid, payload, HttpContext.RequestAborted));
        }

        [ProtocolType("planet.post.update")]
        [HttpPost("posts/update")]
        public Task<IActionResult> UpdatePost([FromBody] ProtocolRequest<PlanetPostUpdateRequest> request)
        {
            var payload = request.Data;
            if (payload == null)
            {
                return Task.FromResult<IActionResult>(BadRequest(ApiResponse<object>.Error("缺少请求数据")));
            }

            return WithUserAsync(request, uid => _planetService.UpdatePostAsync(uid, payload, HttpContext.RequestAborted));
        }

        [ProtocolType("planet.post.delete")]
        [HttpPost("posts/delete")]
        public Task<IActionResult> DeletePost([FromBody] ProtocolRequest<PlanetPostDeleteRequest> request)
        {
            var payload = request.Data;
            if (payload == null)
            {
                return Task.FromResult<IActionResult>(BadRequest(ApiResponse<object>.Error("缺少请求数据")));
            }

            return WithUserAsync(request, uid => _planetService.DeletePostAsync(uid, payload, HttpContext.RequestAborted));
        }

        [ProtocolType("planet.post.visibility")]
        [HttpPost("posts/visibility")]
        public Task<IActionResult> SetVisibility([FromBody] ProtocolRequest<PlanetPostVisibilityRequest> request)
        {
            var payload = request.Data;
            if (payload == null)
            {
                return Task.FromResult<IActionResult>(BadRequest(ApiResponse<object>.Error("缺少请求数据")));
            }

            return WithUserAsync(request, uid => _planetService.SetVisibilityAsync(uid, payload, HttpContext.RequestAborted));
        }

        [ProtocolType("planet.post.list")]
        [HttpPost("posts/list")]
        public Task<IActionResult> ListPosts([FromBody] ProtocolRequest<PlanetPostListRequest> request)
        {
            var payload = request.Data ?? new PlanetPostListRequest();
            return WithUserAsync(request, uid => _planetService.ListPostsAsync(uid, payload, HttpContext.RequestAborted));
        }

        [ProtocolType("planet.post.detail")]
        [HttpPost("posts/detail")]
        public Task<IActionResult> GetPostDetail([FromBody] ProtocolRequest<PlanetPostDetailRequest> request)
        {
            var payload = request.Data;
            if (payload == null)
            {
                return Task.FromResult<IActionResult>(BadRequest(ApiResponse<object>.Error("缺少请求数据")));
            }

            return WithUserAsync(request, uid => _planetService.GetPostDetailAsync(uid, payload, HttpContext.RequestAborted));
        }

        [ProtocolType("planet.post.react")]
        [HttpPost("posts/react")]
        public Task<IActionResult> React([FromBody] ProtocolRequest<PlanetPostReactionRequest> request)
        {
            var payload = request.Data;
            if (payload == null)
            {
                return Task.FromResult<IActionResult>(BadRequest(ApiResponse<object>.Error("缺少请求数据")));
            }

            return WithUserAsync(request, uid => _planetService.ReactPostAsync(uid, payload, HttpContext.RequestAborted));
        }

        [ProtocolType("planet.post.favorite")]
        [HttpPost("posts/favorite")]
        public Task<IActionResult> Favorite([FromBody] ProtocolRequest<PlanetPostFavoriteRequest> request)
        {
            var payload = request.Data;
            if (payload == null)
            {
                return Task.FromResult<IActionResult>(BadRequest(ApiResponse<object>.Error("缺少请求数据")));
            }

            return WithUserAsync(request, uid => _planetService.ToggleFavoriteAsync(uid, payload, HttpContext.RequestAborted));
        }

        [ProtocolType("planet.post.comment.create")]
        [HttpPost("posts/comment/create")]
        public Task<IActionResult> CreateComment([FromBody] ProtocolRequest<PlanetPostCommentCreateRequest> request)
        {
            var payload = request.Data;
            if (payload == null)
            {
                return Task.FromResult<IActionResult>(BadRequest(ApiResponse<object>.Error("缺少请求数据")));
            }

            return WithUserAsync(request, uid => _planetService.AddCommentAsync(uid, payload, HttpContext.RequestAborted));
        }

        [ProtocolType("planet.post.owner.stats")]
        [HttpPost("posts/owner/stats")]
        public Task<IActionResult> OwnerStats([FromBody] ProtocolRequest<PlanetOwnerStatsRequest> request)
        {
            var payload = request.Data ?? new PlanetOwnerStatsRequest();
            return WithUserAsync(request, uid => _planetService.GetOwnerStatsAsync(uid, payload, HttpContext.RequestAborted));
        }

        [ProtocolType("planet.image.upload")]
        [HttpPost("image/upload")]
        [RequestSizeLimit(52428800)]
        public async Task<IActionResult> UploadImage([FromForm] PlanetImageUploadRequest request)
        {
            var uid = await GetUserIdAsync().ConfigureAwait(false);
            if (!uid.HasValue)
            {
                return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
            }

            var file = request.File;
            if (file == null || file.Length <= 0)
            {
                return BadRequest(ApiResponse<object>.Error("请选择要上传的图片"));
            }

            if (!_ossService.IsConfigured)
            {
                return StatusCode(503, ApiResponse<object>.Error("文件存储服务暂不可用"));
            }

            var contentType = file.ContentType?.ToLowerInvariant() ?? string.Empty;
            if (!_allowedImageTypes.Contains(contentType))
            {
                return BadRequest(ApiResponse<object>.Error("不支持的图片格式，仅支持 JPG、PNG、GIF、WebP"));
            }

            if (file.Length > _maxImageSizeBytes)
            {
                var maxSizeMB = _maxImageSizeBytes / 1024.0 / 1024.0;
                return BadRequest(ApiResponse<object>.Error($"图片大小不能超过 {maxSizeMB:F1}MB"));
            }

            try
            {
                var extension = ResolveImageExtension(file.FileName, contentType);
                var objectKey = _ossService.GenerateMediaKey(uid.Value, "planet", extension);
                using var stream = file.OpenReadStream();
                var uploadResult = await _ossService.UploadAsync(stream, objectKey, contentType).ConfigureAwait(false);
                if (!uploadResult.IsSuccess || string.IsNullOrWhiteSpace(uploadResult.Url))
                {
                    return StatusCode(500, ApiResponse<object>.Error(uploadResult.ErrorMessage ?? "图片上传失败"));
                }

                return Ok(ApiResponse<PlanetImageUploadResponse>.Ok(new PlanetImageUploadResponse
                {
                    ImageUrl = uploadResult.Url,
                    ObjectKey = uploadResult.ObjectKey ?? objectKey
                }, "上传成功"));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "星球图片上传失败: uid={Uid}", uid.Value);
                return StatusCode(500, ApiResponse<object>.Error("图片上传失败，请稍后重试"));
            }
        }

        private static string ResolveImageExtension(string? fileName, string contentType)
        {
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                var ext = Path.GetExtension(fileName);
                if (!string.IsNullOrWhiteSpace(ext))
                {
                    return ext.ToLowerInvariant();
                }
            }

            return contentType switch
            {
                "image/jpeg" => ".jpg",
                "image/png" => ".png",
                "image/gif" => ".gif",
                "image/webp" => ".webp",
                _ => ".jpg"
            };
        }

        private async Task<IActionResult> WithUserAsync(IProtocolRequest? request, Func<long, Task<IActionResult>> action)
        {
            var uid = await GetUserIdAsync().ConfigureAwait(false);
            if (!uid.HasValue)
            {
                var reqId = request?.ReqId;
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.Unauthorized, "未授权，请重新登录", null, HttpContext.TraceIdentifier);
                return Unauthorized(error);
            }

            return await action(uid.Value).ConfigureAwait(false);
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
                return authorizationHeader[prefix.Length..].Trim();
            }

            return null;
        }
    }
}
