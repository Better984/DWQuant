using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.Accounts.Application;
using ServerTest.Modules.Accounts.Infrastructure;
using ServerTest.Modules.StrategyManagement.Infrastructure;
using ServerTest.Modules.ExchangeApiKeys.Infrastructure;
using ServerTest.Modules.Notifications.Infrastructure;
using ServerTest.Modules.Positions.Infrastructure;
using ServerTest.Models;
using ServerTest.Services;
using ServerTest.Protocol;
using ServerTest.Infrastructure.Db;
using ServerTest.Options;
using System.Linq;

namespace ServerTest.Controllers
{
    [ApiController]
    [Route("api/admin/user")]
    public sealed class AdminUserController : BaseController
    {
        private readonly AuthTokenService _tokenService;
        private readonly AccountRepository _accountRepository;
        private readonly StrategyRepository _strategyRepository;
        private readonly UserExchangeApiKeyRepository _apiKeyRepository;
        private readonly UserNotifyChannelRepository _notifyChannelRepository;
        private readonly StrategyPositionRepository _positionRepository;
        private readonly IDbManager _db;
        private readonly BusinessRulesOptions _businessRules;

        public AdminUserController(
            ILogger<AdminUserController> logger,
            AuthTokenService tokenService,
            AccountRepository accountRepository,
            StrategyRepository strategyRepository,
            UserExchangeApiKeyRepository apiKeyRepository,
            UserNotifyChannelRepository notifyChannelRepository,
            StrategyPositionRepository positionRepository,
            IDbManager db,
            IOptions<BusinessRulesOptions> businessRules)
            : base(logger)
        {
            _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
            _accountRepository = accountRepository ?? throw new ArgumentNullException(nameof(accountRepository));
            _strategyRepository = strategyRepository ?? throw new ArgumentNullException(nameof(strategyRepository));
            _apiKeyRepository = apiKeyRepository ?? throw new ArgumentNullException(nameof(apiKeyRepository));
            _notifyChannelRepository = notifyChannelRepository ?? throw new ArgumentNullException(nameof(notifyChannelRepository));
            _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _businessRules = businessRules?.Value ?? new BusinessRulesOptions();
        }

        [ProtocolType("admin.user.universal-search")]
        [HttpPost("universal-search")]
        public async Task<IActionResult> UniversalSearch([FromBody] ProtocolRequest<UniversalSearchRequest> request)
        {
            var payload = request.Data;
            var reqId = request?.ReqId;
            if (payload == null || string.IsNullOrWhiteSpace(payload.Query))
            {
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.MissingField, "查询关键词不能为空", null, HttpContext.TraceIdentifier);
                return BadRequest(error);
            }

            var uid = await GetUserIdAsync().ConfigureAwait(false);
            if (!uid.HasValue)
            {
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.Unauthorized, "未授权，请重新登录", null, HttpContext.TraceIdentifier);
                return Unauthorized(error);
            }

            var role = await _accountRepository.GetRoleAsync((ulong)uid.Value, null, HttpContext.RequestAborted).ConfigureAwait(false);
            if (!role.HasValue || role.Value != _businessRules.SuperAdminRole)
            {
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.Forbidden, "无权限访问", null, HttpContext.TraceIdentifier);
                return StatusCode(403, error);
            }

            try
            {
                var query = payload.Query.Trim();
                ulong? targetUid = null;

                // 1. 尝试通过多种方式查找用户
                // 1.1 尝试作为UID
                if (ulong.TryParse(query, out var uidValue))
                {
                    var account = await _accountRepository.GetByUidAsync(uidValue, null, HttpContext.RequestAborted).ConfigureAwait(false);
                    if (account != null)
                    {
                        targetUid = account.Uid;
                    }
                }

                // 1.2 尝试作为邮箱
                if (!targetUid.HasValue && query.Contains("@"))
                {
                    var account = await _accountRepository.GetByEmailAsync(query, null, HttpContext.RequestAborted).ConfigureAwait(false);
                    if (account != null)
                    {
                        targetUid = account.Uid;
                    }
                }

                // 1.3 尝试通过策略ID (us_id) 查找
                if (!targetUid.HasValue && long.TryParse(query, out var usId))
                {
                    var sql = @"
SELECT uid FROM user_strategy WHERE us_id = @usId LIMIT 1;";
                    var foundUid = await _db.QuerySingleOrDefaultAsync<ulong?>(sql, new { usId }, null, HttpContext.RequestAborted).ConfigureAwait(false);
                    if (foundUid.HasValue)
                    {
                        targetUid = foundUid.Value;
                    }
                }

                // 1.4 尝试通过策略定义ID (def_id) 查找创建者
                if (!targetUid.HasValue && long.TryParse(query, out var defId))
                {
                    var sql = @"
SELECT creator_uid FROM strategy_def WHERE def_id = @defId LIMIT 1;";
                    var foundUid = await _db.QuerySingleOrDefaultAsync<ulong?>(sql, new { defId }, null, HttpContext.RequestAborted).ConfigureAwait(false);
                    if (foundUid.HasValue)
                    {
                        targetUid = foundUid.Value;
                    }
                }

                // 1.5 尝试通过分享码查找
                if (!targetUid.HasValue)
                {
                    var sql = @"
SELECT created_by_uid FROM share_code WHERE share_code = @shareCode LIMIT 1;";
                    var foundUid = await _db.QuerySingleOrDefaultAsync<ulong?>(sql, new { shareCode = query }, null, HttpContext.RequestAborted).ConfigureAwait(false);
                    if (foundUid.HasValue)
                    {
                        targetUid = foundUid.Value;
                    }
                }

                // 1.6 尝试通过API Key ID查找
                if (!targetUid.HasValue && long.TryParse(query, out var apiKeyId))
                {
                    var sql = @"
SELECT uid FROM user_exchange_api_keys WHERE id = @apiKeyId LIMIT 1;";
                    var foundUid = await _db.QuerySingleOrDefaultAsync<ulong?>(sql, new { apiKeyId }, null, HttpContext.RequestAborted).ConfigureAwait(false);
                    if (foundUid.HasValue)
                    {
                        targetUid = foundUid.Value;
                    }
                }

                // 1.7 尝试通过策略名称查找
                if (!targetUid.HasValue)
                {
                    var sql = @"
SELECT creator_uid FROM strategy_def WHERE name LIKE @name LIMIT 1;";
                    var foundUid = await _db.QuerySingleOrDefaultAsync<ulong?>(sql, new { name = $"%{query}%" }, null, HttpContext.RequestAborted).ConfigureAwait(false);
                    if (foundUid.HasValue)
                    {
                        targetUid = foundUid.Value;
                    }
                }

                if (!targetUid.HasValue)
                {
                    var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.NotFound, "未找到相关用户", null, HttpContext.TraceIdentifier);
                    return NotFound(error);
                }

                // 2. 获取用户基础信息
                var targetAccount = await _accountRepository.GetByUidAsync(targetUid.Value, null, HttpContext.RequestAborted).ConfigureAwait(false);
                if (targetAccount == null)
                {
                    var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.NotFound, "用户不存在", null, HttpContext.TraceIdentifier);
                    return NotFound(error);
                }

                // 3. 向下检索用户的所有行为
                var result = new
                {
                    account = new
                    {
                        uid = targetAccount.Uid,
                        email = targetAccount.Email,
                        nickname = targetAccount.Nickname,
                        avatarUrl = targetAccount.AvatarUrl,
                        signature = targetAccount.Signature,
                        status = targetAccount.Status,
                        role = targetAccount.Role,
                        vipExpiredAt = targetAccount.VipExpiredAt,
                        currentNotificationPlatform = targetAccount.CurrentNotificationPlatform,
                        strategyIds = targetAccount.StrategyIds,
                        lastLoginAt = targetAccount.LastLoginAt,
                        registerAt = targetAccount.RegisterAt,
                        createdAt = targetAccount.CreatedAt,
                        updatedAt = targetAccount.UpdatedAt,
                        passwordUpdatedAt = targetAccount.PasswordUpdatedAt,
                        deletedAt = targetAccount.DeletedAt,
                    },
                    strategies = await GetUserStrategiesAsync(targetUid.Value).ConfigureAwait(false),
                    shareCodesCreated = await GetShareCodesCreatedAsync(targetUid.Value).ConfigureAwait(false),
                    shareEvents = await GetShareEventsAsync(targetUid.Value).ConfigureAwait(false),
                    importLogs = await GetImportLogsAsync(targetUid.Value).ConfigureAwait(false),
                    exchangeApiKeys = await GetExchangeApiKeysAsync(targetUid.Value).ConfigureAwait(false),
                    notifyChannels = await GetNotifyChannelsAsync(targetUid.Value).ConfigureAwait(false),
                    positions = await GetPositionsAsync(targetUid.Value).ConfigureAwait(false),
                };

                var responseType = ProtocolEnvelopeFactory.BuildAckType(request?.Type ?? "admin.user.universal-search");
                var envelope = ProtocolEnvelopeFactory.Ok(responseType, reqId, result, "查询成功", HttpContext.TraceIdentifier);
                return Ok(envelope);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "万向查询失败: Query={Query}", payload.Query);
                var error = ProtocolEnvelopeFactory.Error(reqId, ProtocolErrorCodes.InternalError, $"查询失败: {ex.Message}", null, HttpContext.TraceIdentifier);
                return StatusCode(500, error);
            }
        }

        private async Task<List<object>> GetUserStrategiesAsync(ulong uid)
        {
            var sql = @"
SELECT 
    us.us_id AS UsId,
    us.def_id AS DefId,
    us.alias_name AS AliasName,
    us.state AS State,
    us.visibility AS Visibility,
    us.share_code AS ShareCode,
    us.exchange_api_key_id AS ExchangeApiKeyId,
    us.source_type AS SourceType,
    us.created_at AS CreatedAt,
    us.updated_at AS UpdatedAt
FROM user_strategy us
WHERE us.uid = @uid
ORDER BY us.updated_at DESC;";

            var strategies = await _db.QueryAsync<dynamic>(sql, new { uid }, null, HttpContext.RequestAborted).ConfigureAwait(false);
            return strategies.Select(s => new
            {
                usId = (long)s.UsId,
                defId = (long)s.DefId,
                aliasName = (string)s.AliasName,
                state = (string)s.State,
                visibility = (string)s.Visibility,
                shareCode = s.ShareCode?.ToString(),
                exchangeApiKeyId = s.ExchangeApiKeyId != null ? (long?)s.ExchangeApiKeyId : null,
                sourceType = (string)s.SourceType,
                createdAt = ((DateTime)s.CreatedAt).ToString("O"),
                updatedAt = ((DateTime)s.UpdatedAt).ToString("O"),
            }).Cast<object>().ToList();
        }

        private async Task<List<object>> GetShareCodesCreatedAsync(ulong uid)
        {
            var sql = @"
SELECT 
    share_code AS ShareCode,
    def_id AS DefId,
    created_by_uid AS CreatedByUid,
    is_active AS IsActive,
    expired_at AS ExpiredAt,
    created_at AS CreatedAt
FROM share_code
WHERE created_by_uid = @uid
ORDER BY created_at DESC;";

            var codes = await _db.QueryAsync<dynamic>(sql, new { uid }, null, HttpContext.RequestAborted).ConfigureAwait(false);
            return codes.Select(c => new
            {
                shareCode = (string)c.ShareCode,
                defId = (long)c.DefId,
                createdByUid = (ulong)c.CreatedByUid,
                isActive = Convert.ToBoolean(c.IsActive),
                expiredAt = c.ExpiredAt != null ? ((DateTime)c.ExpiredAt).ToString("O") : null,
                createdAt = ((DateTime)c.CreatedAt).ToString("O"),
            }).Cast<object>().ToList();
        }

        private async Task<List<object>> GetShareEventsAsync(ulong uid)
        {
            var sql = @"
SELECT 
    event_id AS EventId,
    share_code AS ShareCode,
    def_id AS DefId,
    from_uid AS FromUid,
    to_uid AS ToUid,
    from_instance_id AS FromInstanceId,
    to_instance_id AS ToInstanceId,
    event_type AS EventType,
    created_at AS CreatedAt
FROM share_event
WHERE from_uid = @uid OR to_uid = @uid
ORDER BY created_at DESC
LIMIT 100;";

            var events = await _db.QueryAsync<dynamic>(sql, new { uid }, null, HttpContext.RequestAborted).ConfigureAwait(false);
            return events.Select(e => new
            {
                eventId = (long)e.EventId,
                shareCode = (string)e.ShareCode,
                defId = (long)e.DefId,
                fromUid = (ulong)e.FromUid,
                toUid = (ulong)e.ToUid,
                fromInstanceId = (long)e.FromInstanceId,
                toInstanceId = (long)e.ToInstanceId,
                eventType = (string)e.EventType,
                createdAt = ((DateTime)e.CreatedAt).ToString("O"),
            }).Cast<object>().ToList();
        }

        private async Task<List<object>> GetImportLogsAsync(ulong uid)
        {
            var sql = @"
SELECT 
    id AS Id,
    uid AS Uid,
    us_id AS UsId,
    source_type AS SourceType,
    source_ref AS SourceRef,
    created_at AS CreatedAt
FROM strategy_import_log
WHERE uid = @uid
ORDER BY created_at DESC;";

            var logs = await _db.QueryAsync<dynamic>(sql, new { uid }, null, HttpContext.RequestAborted).ConfigureAwait(false);
            return logs.Select(l => new
            {
                id = (long)l.Id,
                uid = (ulong)l.Uid,
                usId = (long)l.UsId,
                sourceType = (string)l.SourceType,
                sourceRef = l.SourceRef?.ToString(),
                createdAt = ((DateTime)l.CreatedAt).ToString("O"),
            }).Cast<object>().ToList();
        }

        private async Task<List<object>> GetExchangeApiKeysAsync(ulong uid)
        {
            var keys = await _apiKeyRepository.GetAllByUidAsync((long)uid, HttpContext.RequestAborted).ConfigureAwait(false);
            return keys.Select(k => new
            {
                id = k.Id,
                uid = k.Uid,
                exchangeType = k.ExchangeType,
                label = k.Label,
                createdAt = k.CreatedAt.ToString("O"),
                updatedAt = k.UpdatedAt.ToString("O"),
            }).Cast<object>().ToList();
        }

        private async Task<List<object>> GetNotifyChannelsAsync(ulong uid)
        {
            var channels = await _notifyChannelRepository.GetAllByUidAsync((long)uid, HttpContext.RequestAborted).ConfigureAwait(false);
            return channels.Select(c => new
            {
                id = c.Id,
                uid = c.Uid,
                platform = c.Platform,
                address = c.Address,
                isEnabled = c.IsEnabled,
                isDefault = c.IsDefault,
                createdAt = c.CreatedAt.ToString("O"),
                updatedAt = c.UpdatedAt.ToString("O"),
            }).Cast<object>().ToList();
        }

        private async Task<List<object>> GetPositionsAsync(ulong uid)
        {
            var positions = await _positionRepository.GetByUidAsync((long)uid, null, null, null, HttpContext.RequestAborted).ConfigureAwait(false);
            return positions.Select(p => new
            {
                positionId = p.PositionId,
                uid = p.Uid,
                usId = p.UsId,
                exchangeApiKeyId = p.ExchangeApiKeyId,
                exchange = p.Exchange,
                symbol = p.Symbol,
                side = p.Side,
                entryPrice = p.EntryPrice,
                qty = p.Qty,
                status = p.Status,
                openedAt = p.OpenedAt.ToString("O"),
                closedAt = p.ClosedAt?.ToString("O"),
            }).Cast<object>().ToList();
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

        [ProtocolType("admin.user.update")]
        [HttpPost("update")]
        public async Task<IActionResult> UpdateUser([FromBody] ProtocolRequest<UpdateUserRequest> request)
        {
            var payload = request.Data;
            if (payload == null || payload.Uid == 0)
            {
                return BadRequest(ApiResponse<object>.Error("请求无效：用户ID不能为空"));
            }

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
                var targetUid = (ulong)payload.Uid;
                var patch = new Dictionary<string, object?>();

                // 更新角色
                if (payload.Role.HasValue)
                {
                    patch["role"] = payload.Role.Value;
                }

                // 更新状态
                if (payload.Status.HasValue)
                {
                    patch["status"] = payload.Status.Value;
                }

                // 更新昵称
                if (payload.Nickname != null)
                {
                    patch["nickname"] = payload.Nickname;
                }

                // 更新头像
                if (payload.AvatarUrl != null)
                {
                    patch["avatar_url"] = payload.AvatarUrl;
                }

                // 更新签名
                if (payload.Signature != null)
                {
                    patch["signature"] = payload.Signature;
                }

                // 更新VIP到期时间
                if (payload.VipExpiredAt != null)
                {
                    patch["vip_expired_at"] = payload.VipExpiredAt;
                }

                // 更新当前通知平台
                if (payload.CurrentNotificationPlatform != null)
                {
                    patch["current_notification_platform"] = payload.CurrentNotificationPlatform;
                }

                // 更新策略ID列表
                if (payload.StrategyIds != null)
                {
                    patch["strategy_ids"] = payload.StrategyIds;
                }

                if (patch.Count == 0)
                {
                    return BadRequest(ApiResponse<object>.Error("至少需要提供一个更新字段"));
                }

                // 更新 updated_at
                patch["updated_at"] = DateTime.UtcNow;

                var affected = await _accountRepository.AdminUpdateAsync(targetUid, patch, null, HttpContext.RequestAborted).ConfigureAwait(false);
                if (affected == 0)
                {
                    return NotFound(ApiResponse<object>.Error("用户不存在或更新失败"));
                }

                // 重新获取更新后的用户信息
                var updatedAccount = await _accountRepository.GetByUidAsync(targetUid, null, HttpContext.RequestAborted).ConfigureAwait(false);
                var result = updatedAccount != null ? new
                {
                    uid = updatedAccount.Uid,
                    email = updatedAccount.Email,
                    nickname = updatedAccount.Nickname,
                    avatarUrl = updatedAccount.AvatarUrl,
                    signature = updatedAccount.Signature,
                    status = updatedAccount.Status,
                    role = updatedAccount.Role,
                    vipExpiredAt = updatedAccount.VipExpiredAt,
                    currentNotificationPlatform = updatedAccount.CurrentNotificationPlatform,
                    strategyIds = updatedAccount.StrategyIds,
                    updatedAt = updatedAccount.UpdatedAt,
                } : null;

                return Ok(ApiResponse<object>.Ok(result, "更新成功"));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "更新用户失败: Uid={Uid}", payload.Uid);
                return StatusCode(500, ApiResponse<object>.Error($"更新失败: {ex.Message}"));
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

    public sealed class UniversalSearchRequest
    {
        public string Query { get; set; } = string.Empty;
    }

    public sealed class UpdateUserRequest
    {
        public long Uid { get; set; }
        public byte? Role { get; set; }
        public byte? Status { get; set; }
        public string? Nickname { get; set; }
        public string? AvatarUrl { get; set; }
        public string? Signature { get; set; }
        public DateTime? VipExpiredAt { get; set; }
        public string? CurrentNotificationPlatform { get; set; }
        public string? StrategyIds { get; set; }
    }
}
