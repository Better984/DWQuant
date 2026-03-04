using Aliyun.OSS;
using Aliyun.OSS.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;

namespace ServerTest.Services
{
    /// <summary>
    /// 阿里云 OSS 文件上传服务
    /// </summary>
    public sealed class OSSService
    {
        private readonly ILogger<OSSService> _logger;
        private readonly string _accessKeyId;
        private readonly string _accessKeySecret;
        private readonly string _endpoint;
        private readonly string _bucketName;
        private readonly OssClient _client;

        public OSSService(IConfiguration configuration, ILogger<OSSService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _accessKeyId = configuration["AliyunOSS:AccessKeyId"] ?? string.Empty;
            _accessKeySecret = configuration["AliyunOSS:AccessKeySecret"] ?? string.Empty;
            _endpoint = configuration["AliyunOSS:Endpoint"] ?? string.Empty;
            _bucketName = configuration["AliyunOSS:BucketName"] ?? string.Empty;

            if (string.IsNullOrWhiteSpace(_accessKeyId) ||
                string.IsNullOrWhiteSpace(_accessKeySecret) ||
                string.IsNullOrWhiteSpace(_endpoint) ||
                string.IsNullOrWhiteSpace(_bucketName))
            {
                _logger.LogWarning("阿里云 OSS 配置不完整，文件上传功能将不可用");
            }

            _client = new OssClient(_endpoint, _accessKeyId, _accessKeySecret);
        }

        /// <summary>
        /// 检查 OSS 配置是否完整
        /// </summary>
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(_accessKeyId) &&
            !string.IsNullOrWhiteSpace(_accessKeySecret) &&
            !string.IsNullOrWhiteSpace(_endpoint) &&
            !string.IsNullOrWhiteSpace(_bucketName);

        /// <summary>
        /// 上传文件到 OSS
        /// </summary>
        /// <param name="stream">文件流</param>
        /// <param name="objectKey">OSS 对象键（路径）</param>
        /// <param name="contentType">文件 MIME 类型</param>
        /// <returns>文件的完整 URL</returns>
        public async Task<OSSUploadResult> UploadAsync(Stream stream, string objectKey, string contentType)
        {
            if (!IsConfigured)
            {
                return OSSUploadResult.Fail("OSS 配置不完整");
            }

            if (stream == null || stream.Length == 0)
            {
                return OSSUploadResult.Fail("文件内容为空");
            }

            if (string.IsNullOrWhiteSpace(objectKey))
            {
                return OSSUploadResult.Fail("文件路径不能为空");
            }

            try
            {
                var metadata = new ObjectMetadata
                {
                    ContentType = contentType
                };

                // 重置流位置
                if (stream.CanSeek)
                {
                    stream.Position = 0;
                }

                // 使用 Task.Run 将同步操作包装为异步
                var result = await Task.Run(() => _client.PutObject(_bucketName, objectKey, stream, metadata));

                if (result.HttpStatusCode == System.Net.HttpStatusCode.OK)
                {
                    var url = GetObjectUrl(objectKey);
                    _logger.LogInformation("文件上传成功: {ObjectKey} -> {Url}", objectKey, url);
                    return OSSUploadResult.Success(url, objectKey);
                }

                _logger.LogWarning("文件上传失败: {ObjectKey}, HTTP Status: {StatusCode}", objectKey, result.HttpStatusCode);
                return OSSUploadResult.Fail($"上传失败: HTTP {result.HttpStatusCode}");
            }
            catch (OssException ex)
            {
                _logger.LogError(ex, "OSS 上传异常: {ObjectKey}, ErrorCode: {ErrorCode}", objectKey, ex.ErrorCode);
                return OSSUploadResult.Fail($"OSS 错误: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "文件上传异常: {ObjectKey}", objectKey);
                return OSSUploadResult.Fail($"上传失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 删除 OSS 对象
        /// </summary>
        /// <param name="objectKey">OSS 对象键</param>
        public async Task<bool> DeleteAsync(string objectKey)
        {
            if (!IsConfigured || string.IsNullOrWhiteSpace(objectKey))
            {
                return false;
            }

            try
            {
                await Task.Run(() => _client.DeleteObject(_bucketName, objectKey));
                _logger.LogInformation("文件删除成功: {ObjectKey}", objectKey);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "文件删除失败: {ObjectKey}", objectKey);
                return false;
            }
        }

        /// <summary>
        /// 检查对象是否存在。
        /// </summary>
        public async Task<bool> ExistsAsync(string objectKey, CancellationToken ct = default)
        {
            if (!IsConfigured || string.IsNullOrWhiteSpace(objectKey))
            {
                return false;
            }

            try
            {
                ct.ThrowIfCancellationRequested();
                return await Task.Run(() => _client.DoesObjectExist(_bucketName, objectKey), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "检查 OSS 对象存在性失败: {ObjectKey}", objectKey);
                return false;
            }
        }

        /// <summary>
        /// 读取文本对象内容。
        /// </summary>
        public async Task<OSSReadTextResult> ReadTextAsync(string objectKey, CancellationToken ct = default)
        {
            if (!IsConfigured)
            {
                return OSSReadTextResult.Fail("OSS 配置不完整");
            }

            if (string.IsNullOrWhiteSpace(objectKey))
            {
                return OSSReadTextResult.Fail("文件路径不能为空");
            }

            try
            {
                ct.ThrowIfCancellationRequested();
                if (!await ExistsAsync(objectKey, ct).ConfigureAwait(false))
                {
                    return OSSReadTextResult.NotFound(objectKey);
                }

                using var result = await Task.Run(() => _client.GetObject(_bucketName, objectKey), ct).ConfigureAwait(false);
                using var reader = new StreamReader(result.Content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                var content = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
                return OSSReadTextResult.Success(objectKey, content);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "读取 OSS 文本对象失败: {ObjectKey}", objectKey);
                return OSSReadTextResult.Fail($"读取失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 按前缀分页列举对象键。
        /// </summary>
        public async Task<OSSListObjectsResult> ListObjectKeysAsync(
            string prefix,
            string? marker = null,
            int maxKeys = 1000,
            CancellationToken ct = default)
        {
            if (!IsConfigured)
            {
                return OSSListObjectsResult.Fail("OSS 配置不完整");
            }

            if (string.IsNullOrWhiteSpace(prefix))
            {
                return OSSListObjectsResult.Fail("前缀不能为空");
            }

            var normalizedMaxKeys = Math.Clamp(maxKeys, 1, 1000);

            try
            {
                ct.ThrowIfCancellationRequested();
                var request = new ListObjectsRequest(_bucketName)
                {
                    Prefix = prefix,
                    Marker = marker,
                    MaxKeys = normalizedMaxKeys
                };

                var listing = await Task.Run(() => _client.ListObjects(request), ct).ConfigureAwait(false);
                var keys = listing.ObjectSummaries
                    .Where(item => !string.IsNullOrWhiteSpace(item.Key))
                    .Select(item => item.Key)
                    .ToList();
                return OSSListObjectsResult.Success(keys, listing.NextMarker, listing.IsTruncated);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "列举 OSS 对象失败: prefix={Prefix}", prefix);
                return OSSListObjectsResult.Fail($"列举失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 生成头像存储路径
        /// </summary>
        /// <param name="uid">用户 ID</param>
        /// <param name="fileExtension">文件扩展名（如 .jpg）</param>
        public string GenerateAvatarKey(long uid, string fileExtension)
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var random = Guid.NewGuid().ToString("N").Substring(0, 8);
            return $"avatar/{uid}/{timestamp}_{random}{fileExtension}";
        }

        /// <summary>
        /// 生成媒体存储路径（用于未来视频等扩展）
        /// </summary>
        /// <param name="uid">用户 ID</param>
        /// <param name="mediaType">媒体类型（如 video）</param>
        /// <param name="fileExtension">文件扩展名</param>
        public string GenerateMediaKey(long uid, string mediaType, string fileExtension)
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var random = Guid.NewGuid().ToString("N").Substring(0, 8);
            return $"{mediaType}/{uid}/{timestamp}_{random}{fileExtension}";
        }

        /// <summary>
        /// 获取对象的完整 URL
        /// </summary>
        private string GetObjectUrl(string objectKey)
        {
            return $"https://{_bucketName}.{_endpoint}/{objectKey}";
        }
    }

    /// <summary>
    /// OSS 上传结果
    /// </summary>
    public sealed class OSSUploadResult
    {
        public bool IsSuccess { get; private set; }
        public string? Url { get; private set; }
        public string? ObjectKey { get; private set; }
        public string? ErrorMessage { get; private set; }

        private OSSUploadResult() { }

        public static OSSUploadResult Success(string url, string objectKey)
        {
            return new OSSUploadResult
            {
                IsSuccess = true,
                Url = url,
                ObjectKey = objectKey
            };
        }

        public static OSSUploadResult Fail(string errorMessage)
        {
            return new OSSUploadResult
            {
                IsSuccess = false,
                ErrorMessage = errorMessage
            };
        }
    }

    /// <summary>
    /// OSS 文本读取结果。
    /// </summary>
    public sealed class OSSReadTextResult
    {
        public bool IsSuccess { get; private set; }
        public bool IsNotFound { get; private set; }
        public string? ObjectKey { get; private set; }
        public string? Content { get; private set; }
        public string? ErrorMessage { get; private set; }

        private OSSReadTextResult() { }

        public static OSSReadTextResult Success(string objectKey, string content)
        {
            return new OSSReadTextResult
            {
                IsSuccess = true,
                ObjectKey = objectKey,
                Content = content ?? string.Empty
            };
        }

        public static OSSReadTextResult NotFound(string objectKey)
        {
            return new OSSReadTextResult
            {
                IsSuccess = false,
                IsNotFound = true,
                ObjectKey = objectKey,
                ErrorMessage = "对象不存在"
            };
        }

        public static OSSReadTextResult Fail(string errorMessage)
        {
            return new OSSReadTextResult
            {
                IsSuccess = false,
                ErrorMessage = errorMessage
            };
        }
    }

    /// <summary>
    /// OSS 对象分页查询结果。
    /// </summary>
    public sealed class OSSListObjectsResult
    {
        public bool IsSuccess { get; private set; }
        public List<string> ObjectKeys { get; private set; } = new();
        public string? NextMarker { get; private set; }
        public bool IsTruncated { get; private set; }
        public string? ErrorMessage { get; private set; }

        private OSSListObjectsResult() { }

        public static OSSListObjectsResult Success(List<string> objectKeys, string? nextMarker, bool isTruncated)
        {
            return new OSSListObjectsResult
            {
                IsSuccess = true,
                ObjectKeys = objectKeys ?? new List<string>(),
                NextMarker = nextMarker,
                IsTruncated = isTruncated
            };
        }

        public static OSSListObjectsResult Fail(string errorMessage)
        {
            return new OSSListObjectsResult
            {
                IsSuccess = false,
                ErrorMessage = errorMessage
            };
        }
    }
}
