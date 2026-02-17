using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Infrastructure.Config;
using ServerTest.Infrastructure.Db;
using ServerTest.Models;
using ServerTest.Modules.Planet.Domain;
using ServerTest.Modules.StrategyManagement.Application;
using ServerTest.Options;

namespace ServerTest.Modules.Planet.Infrastructure
{
    public sealed class PlanetRepository
    {
        private const int DefaultPageSize = 20;
        private const int MaxPageSize = 50;
        private const int MaxTitleLength = 128;
        private const int MaxContentLength = 5000;
        private const int MaxCommentLength = 1000;
        private const int MaxImageCount = 9;
        private const int MaxStrategyBindCount = 10;
        private const int StrategyPositionHistoryLimit = 30;
        private const int CommentInitialLimit = 5;
        private const int CommentMoreLimit = 10;
        private const int CommentMaxLimit = 50;
        private const int MaxLikersPerPost = 20;
        private const int StrategyCurveDays = 30;
        private const string OwnerDeleteCommentConfigKey = "Planet:PostOwnerCanDeleteOthersComments";

        private static readonly HashSet<string> AllowedScopes = new(StringComparer.OrdinalIgnoreCase)
        {
            "square",
            "mine",
            "favorite"
        };

        private static readonly HashSet<string> AllowedPostStatuses = new(StringComparer.OrdinalIgnoreCase)
        {
            "normal",
            "hidden",
            "deleted"
        };

        private static readonly HashSet<string> AllowedReactions = new(StringComparer.OrdinalIgnoreCase)
        {
            "like",
            "dislike",
            "none"
        };

        private readonly IDbManager _db;
        private readonly ILogger<PlanetRepository> _logger;
        private readonly ServerConfigStore _configStore;
        private readonly int _superAdminRole;
        private readonly StrategyPerformanceCacheService _strategyPerformanceCacheService;

        public PlanetRepository(
            IDbManager db,
            ServerConfigStore configStore,
            IOptions<BusinessRulesOptions> businessRules,
            StrategyPerformanceCacheService strategyPerformanceCacheService,
            ILogger<PlanetRepository> logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
            _superAdminRole = businessRules?.Value?.SuperAdminRole ?? 255;
            _strategyPerformanceCacheService = strategyPerformanceCacheService ?? throw new ArgumentNullException(nameof(strategyPerformanceCacheService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IActionResult> CreatePostAsync(long uid, PlanetPostCreateRequest request, CancellationToken ct)
        {
            var payload = request ?? new PlanetPostCreateRequest();
            var normalizeResult = NormalizePostInput(payload.Title, payload.Content, payload.Status, payload.Visibility, payload.ImageUrls, payload.StrategyUsIds);
            if (!normalizeResult.Ok || normalizeResult.Input == null)
            {
                return new BadRequestObjectResult(ApiResponse<object>.Error(normalizeResult.Error ?? "参数错误"));
            }

            var ownership = await EnsureStrategiesOwnedAsync(uid, normalizeResult.Input.StrategyUsIds, null, ct).ConfigureAwait(false);
            if (!ownership.Ok)
            {
                return new BadRequestObjectResult(ApiResponse<object>.Error(ownership.Error ?? "策略归属校验失败"));
            }
            var isAdmin = await IsAdminAsync(uid, null, ct).ConfigureAwait(false);
            var isHidden = string.Equals(normalizeResult.Input.Status, "hidden", StringComparison.OrdinalIgnoreCase);

            await using var uow = await _db.BeginUnitOfWorkAsync(ct).ConfigureAwait(false);
            try
            {
                const string insertPostSql = @"
INSERT INTO planet_post
(
    uid,
    title,
    content,
    status,
    visibility,
    hidden_by_uid,
    hidden_by_admin,
    hidden_at,
    created_at,
    updated_at
)
VALUES
(
    @uid,
    @title,
    @content,
    @status,
    @visibility,
    @hiddenByUid,
    @hiddenByAdmin,
    @hiddenAt,
    UTC_TIMESTAMP(3),
    UTC_TIMESTAMP(3)
);
SELECT LAST_INSERT_ID();";

                var postId = await _db.ExecuteScalarAsync<long>(
                    insertPostSql,
                    new
                    {
                        uid,
                        title = normalizeResult.Input.Title,
                        content = normalizeResult.Input.Content,
                        status = normalizeResult.Input.Status,
                        visibility = ToLegacyVisibility(normalizeResult.Input.Status),
                        hiddenByUid = isHidden ? (long?)uid : null,
                        hiddenByAdmin = isHidden && isAdmin ? 1 : 0,
                        hiddenAt = isHidden ? (DateTime?)DateTime.UtcNow : null
                    },
                    uow,
                    ct).ConfigureAwait(false);

                if (postId <= 0)
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    return new ObjectResult(ApiResponse<object>.Error("发帖失败，请稍后重试")) { StatusCode = 500 };
                }

                await ReplacePostImagesAsync(postId, normalizeResult.Input.ImageUrls, uow, ct).ConfigureAwait(false);
                await ReplacePostStrategiesAsync(postId, normalizeResult.Input.StrategyUsIds, uow, ct).ConfigureAwait(false);
                await RefreshPostStatsAsync(postId, uow, ct).ConfigureAwait(false);

                await uow.CommitAsync(ct).ConfigureAwait(false);
                return new OkObjectResult(ApiResponse<PlanetPostCreateResponse>.Ok(new PlanetPostCreateResponse
                {
                    PostId = postId
                }, "发布成功"));
            }
            catch (Exception ex)
            {
                await SafeRollbackAsync(uow, ct).ConfigureAwait(false);
                _logger.LogError(ex, "创建星球帖子失败: uid={Uid}", uid);
                return new ObjectResult(ApiResponse<object>.Error("发帖失败，请稍后重试")) { StatusCode = 500 };
            }
        }

        public async Task<IActionResult> UpdatePostAsync(long uid, PlanetPostUpdateRequest request, CancellationToken ct)
        {
            if (request == null || request.PostId <= 0)
            {
                return new BadRequestObjectResult(ApiResponse<object>.Error("无效的帖子ID"));
            }

            await using var uow = await _db.BeginUnitOfWorkAsync(ct).ConfigureAwait(false);
            try
            {
                var ownerRow = await QueryPostOwnerAsync(request.PostId, uow, ct).ConfigureAwait(false);
                if (ownerRow == null || string.Equals(NormalizeStoredStatus(ownerRow.Status), "deleted", StringComparison.OrdinalIgnoreCase))
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    return new NotFoundObjectResult(ApiResponse<object>.Error("帖子不存在或已删除"));
                }

                var isAdmin = await IsAdminAsync(uid, uow, ct).ConfigureAwait(false);
                if (!CanManagePost(ownerRow.Uid, uid, isAdmin))
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    return new ObjectResult(ApiResponse<object>.Error("无权限修改该帖子")) { StatusCode = 403 };
                }
                var fallbackStatus = string.IsNullOrWhiteSpace(request.Status) && string.IsNullOrWhiteSpace(request.Visibility)
                    ? NormalizeStoredStatus(ownerRow.Status)
                    : request.Status;
                var normalizeResult = NormalizePostInput(
                    request.Title,
                    request.Content,
                    fallbackStatus,
                    request.Visibility,
                    request.ImageUrls,
                    request.StrategyUsIds);
                if (!normalizeResult.Ok || normalizeResult.Input == null)
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    return new BadRequestObjectResult(ApiResponse<object>.Error(normalizeResult.Error ?? "参数错误"));
                }
                var currentStatus = NormalizeStoredStatus(ownerRow.Status);
                if (string.Equals(currentStatus, "hidden", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(normalizeResult.Input.Status, "normal", StringComparison.OrdinalIgnoreCase)
                    && ownerRow.HiddenByAdmin > 0
                    && !isAdmin)
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    return new ObjectResult(ApiResponse<object>.Error("该帖子由管理员隐藏，仅管理员可恢复")) { StatusCode = 403 };
                }

                var ownership = await EnsureStrategiesOwnedAsync(uid, normalizeResult.Input.StrategyUsIds, uow, ct).ConfigureAwait(false);
                if (!ownership.Ok)
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    return new BadRequestObjectResult(ApiResponse<object>.Error(ownership.Error ?? "策略归属校验失败"));
                }
                var targetStatus = normalizeResult.Input.Status;
                var isTargetHidden = string.Equals(targetStatus, "hidden", StringComparison.OrdinalIgnoreCase);
                long? nextHiddenByUid;
                int nextHiddenByAdmin;
                DateTime? nextHiddenAt;
                if (isTargetHidden)
                {
                    if (string.Equals(currentStatus, "hidden", StringComparison.OrdinalIgnoreCase))
                    {
                        nextHiddenByUid = ownerRow.HiddenByUid ?? uid;
                        nextHiddenByAdmin = ownerRow.HiddenByAdmin > 0 ? 1 : (isAdmin ? 1 : 0);
                        nextHiddenAt = ownerRow.HiddenAt ?? DateTime.UtcNow;
                    }
                    else
                    {
                        nextHiddenByUid = uid;
                        nextHiddenByAdmin = isAdmin ? 1 : 0;
                        nextHiddenAt = DateTime.UtcNow;
                    }
                }
                else
                {
                    nextHiddenByUid = null;
                    nextHiddenByAdmin = 0;
                    nextHiddenAt = null;
                }

                const string updateSql = @"
UPDATE planet_post
SET title = @title,
    content = @content,
    status = @status,
    visibility = @visibility,
    hidden_by_uid = @hiddenByUid,
    hidden_by_admin = @hiddenByAdmin,
    hidden_at = @hiddenAt,
    updated_at = UTC_TIMESTAMP(3)
WHERE post_id = @postId
  AND status <> 'deleted';";

                var affected = await _db.ExecuteAsync(
                    updateSql,
                    new
                    {
                        title = normalizeResult.Input.Title,
                        content = normalizeResult.Input.Content,
                        status = targetStatus,
                        visibility = ToLegacyVisibility(targetStatus),
                        hiddenByUid = nextHiddenByUid,
                        hiddenByAdmin = nextHiddenByAdmin,
                        hiddenAt = nextHiddenAt,
                        postId = request.PostId
                    },
                    uow,
                    ct).ConfigureAwait(false);

                if (affected <= 0)
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    return new NotFoundObjectResult(ApiResponse<object>.Error("帖子不存在或已删除"));
                }

                await ReplacePostImagesAsync(request.PostId, normalizeResult.Input.ImageUrls, uow, ct).ConfigureAwait(false);
                await ReplacePostStrategiesAsync(request.PostId, normalizeResult.Input.StrategyUsIds, uow, ct).ConfigureAwait(false);
                await RefreshPostStatsAsync(request.PostId, uow, ct).ConfigureAwait(false);

                await uow.CommitAsync(ct).ConfigureAwait(false);
                return new OkObjectResult(ApiResponse<object?>.Ok(null, "帖子已更新"));
            }
            catch (Exception ex)
            {
                await SafeRollbackAsync(uow, ct).ConfigureAwait(false);
                _logger.LogError(ex, "更新星球帖子失败: uid={Uid}, postId={PostId}", uid, request.PostId);
                return new ObjectResult(ApiResponse<object>.Error("更新帖子失败，请稍后重试")) { StatusCode = 500 };
            }
        }

        public Task<IActionResult> DeletePostAsync(long uid, PlanetPostDeleteRequest request, CancellationToken ct)
        {
            if (request == null || request.PostId <= 0)
            {
                return Task.FromResult<IActionResult>(new BadRequestObjectResult(ApiResponse<object>.Error("无效的帖子ID")));
            }

            return SetPostStatusInternalAsync(uid, request.PostId, "deleted", ct);
        }

        public Task<IActionResult> SetPostStatusAsync(long uid, PlanetPostStatusUpdateRequest request, CancellationToken ct)
        {
            if (request == null || request.PostId <= 0)
            {
                return Task.FromResult<IActionResult>(new BadRequestObjectResult(ApiResponse<object>.Error("无效的帖子ID")));
            }
            if (string.IsNullOrWhiteSpace(request.Status) && string.IsNullOrWhiteSpace(request.Visibility))
            {
                return Task.FromResult<IActionResult>(new BadRequestObjectResult(ApiResponse<object>.Error("缺少状态参数")));
            }

            if (!TryResolvePostStatus(request.Status, request.Visibility, out var normalizedStatus))
            {
                return Task.FromResult<IActionResult>(new BadRequestObjectResult(ApiResponse<object>.Error("帖子状态仅支持 normal/hidden/deleted（兼容 public/hidden）")));
            }

            return SetPostStatusInternalAsync(uid, request.PostId, normalizedStatus, ct);
        }

        public async Task<IActionResult> ListPostsAsync(long uid, PlanetPostListRequest request, CancellationToken ct)
        {
            var payload = request ?? new PlanetPostListRequest();
            var page = payload.Page.GetValueOrDefault(1);
            if (page <= 0)
            {
                page = 1;
            }

            var pageSize = payload.PageSize.GetValueOrDefault(DefaultPageSize);
            if (pageSize <= 0)
            {
                pageSize = DefaultPageSize;
            }
            pageSize = Math.Min(pageSize, MaxPageSize);

            var scope = NormalizeScope(payload.Scope);
            if (!AllowedScopes.Contains(scope))
            {
                return new BadRequestObjectResult(ApiResponse<object>.Error("无效的列表范围，支持 square/mine/favorite"));
            }

            var offset = (page - 1) * pageSize;
            var isAdmin = await IsAdminAsync(uid, null, ct).ConfigureAwait(false);
            var whereSql = BuildListWhereSql(scope);
            var countSql = $"SELECT COUNT(1) FROM planet_post p {whereSql};";

            var total = await _db.ExecuteScalarAsync<long>(
                countSql,
                new { uid },
                null,
                ct).ConfigureAwait(false);

            var listSql = $@"
SELECT
    p.post_id AS PostId,
    p.uid AS Uid,
    COALESCE(NULLIF(a.nickname, ''), a.email) AS AuthorName,
    a.avatar_url AS AuthorAvatarUrl,
    p.title AS Title,
    p.content AS Content,
    p.status AS Status,
    p.like_count AS LikeCount,
    p.dislike_count AS DislikeCount,
    p.favorite_count AS FavoriteCount,
    p.comment_count AS CommentCount,
    pr.reaction_type AS UserReaction,
    CASE WHEN pf.id IS NULL THEN 0 ELSE 1 END AS IsFavorited,
    CASE WHEN (@isAdmin = 1 OR p.uid = @uid) THEN 1 ELSE 0 END AS CanManage,
    p.created_at AS CreatedAt,
    p.updated_at AS UpdatedAt
FROM planet_post p
LEFT JOIN account a ON a.uid = p.uid
LEFT JOIN planet_post_reaction pr ON pr.post_id = p.post_id AND pr.uid = @uid
LEFT JOIN planet_post_favorite pf ON pf.post_id = p.post_id AND pf.uid = @uid
{whereSql}
ORDER BY p.created_at DESC
LIMIT @pageSize OFFSET @offset;";

            var rows = await _db.QueryAsync<PostCardRow>(
                listSql,
                new { uid, pageSize, offset, isAdmin = isAdmin ? 1 : 0 },
                null,
                ct).ConfigureAwait(false);

            var cards = rows.Select(ToCardDto).ToList();
            await FillCardImagesAsync(cards, ct).ConfigureAwait(false);
            await FillCardStrategiesAsync(cards, ct).ConfigureAwait(false);
            await FillCardStrategyCurvesAsync(cards, ct).ConfigureAwait(false);

            return new OkObjectResult(ApiResponse<PlanetPostListResponse>.Ok(new PlanetPostListResponse
            {
                Page = page,
                PageSize = pageSize,
                Total = total,
                Items = cards
            }));
        }

        public async Task<IActionResult> GetPostDetailAsync(long uid, PlanetPostDetailRequest request, CancellationToken ct)
        {
            if (request == null || request.PostId <= 0)
            {
                return new BadRequestObjectResult(ApiResponse<object>.Error("无效的帖子ID"));
            }

            const string postSql = @"
SELECT
    p.post_id AS PostId,
    p.uid AS Uid,
    COALESCE(NULLIF(a.nickname, ''), a.email) AS AuthorName,
    a.avatar_url AS AuthorAvatarUrl,
    p.title AS Title,
    p.content AS Content,
    p.status AS Status,
    p.like_count AS LikeCount,
    p.dislike_count AS DislikeCount,
    p.favorite_count AS FavoriteCount,
    p.comment_count AS CommentCount,
    pr.reaction_type AS UserReaction,
    CASE WHEN pf.id IS NULL THEN 0 ELSE 1 END AS IsFavorited,
    CASE WHEN p.uid = @uid THEN 1 ELSE 0 END AS CanManage,
    p.created_at AS CreatedAt,
    p.updated_at AS UpdatedAt
FROM planet_post p
LEFT JOIN account a ON a.uid = p.uid
LEFT JOIN planet_post_reaction pr ON pr.post_id = p.post_id AND pr.uid = @uid
LEFT JOIN planet_post_favorite pf ON pf.post_id = p.post_id AND pf.uid = @uid
WHERE p.post_id = @postId
LIMIT 1;";

            var postRow = await _db.QuerySingleOrDefaultAsync<PostCardRow>(
                postSql,
                new { uid, postId = request.PostId },
                null,
                ct).ConfigureAwait(false);

            if (postRow == null || string.Equals(NormalizeStoredStatus(postRow.Status), "deleted", StringComparison.OrdinalIgnoreCase))
            {
                return new NotFoundObjectResult(ApiResponse<object>.Error("帖子不存在或已删除"));
            }

            var isAdmin = await IsAdminAsync(uid, null, ct).ConfigureAwait(false);
            if (!CanViewPost(postRow.Uid, postRow.Status, uid, isAdmin))
            {
                return new ObjectResult(ApiResponse<object>.Error("该帖子已隐藏")) { StatusCode = 403 };
            }

            var detail = new PlanetPostDetailDto
            {
                Post = ToCardDto(postRow),
                CanManage = CanManagePost(postRow.Uid, uid, isAdmin)
            };
            detail.Post.CanManage = detail.CanManage;

            await FillCardImagesAsync(new List<PlanetPostCardDto> { detail.Post }, ct).ConfigureAwait(false);
            await FillCardStrategiesAsync(new List<PlanetPostCardDto> { detail.Post }, ct).ConfigureAwait(false);
            await FillCardStrategyCurvesAsync(new List<PlanetPostCardDto> { detail.Post }, ct).ConfigureAwait(false);

            const string strategySql = @"
SELECT
    ps.us_id AS UsId,
    us.def_id AS DefId,
    COALESCE(NULLIF(us.alias_name, ''), sd.name, CONCAT('策略#', ps.us_id)) AS AliasName,
    COALESCE(sd.name, CONCAT('策略#', ps.us_id)) AS DefName,
    COALESCE(us.description, '') AS Description,
    COALESCE(us.state, 'unknown') AS State,
    COALESCE(sv.version_no, 0) AS VersionNo,
    sv.config_json AS ConfigJson
FROM planet_post_strategy ps
LEFT JOIN user_strategy us ON us.us_id = ps.us_id
LEFT JOIN strategy_def sd ON sd.def_id = us.def_id
LEFT JOIN strategy_version sv ON sv.version_id = us.pinned_version_id
WHERE ps.post_id = @postId
ORDER BY ps.id ASC;";

            var strategyRows = await _db.QueryAsync<PostStrategyDetailRow>(
                strategySql,
                new { postId = request.PostId },
                null,
                ct).ConfigureAwait(false);

            foreach (var row in strategyRows)
            {
                var strategyDto = new PlanetStrategyDetailDto
                {
                    UsId = row.UsId,
                    DefId = row.DefId,
                    AliasName = row.AliasName ?? string.Empty,
                    DefName = row.DefName ?? string.Empty,
                    Description = row.Description ?? string.Empty,
                    State = row.State ?? string.Empty,
                    VersionNo = row.VersionNo,
                    ConfigJson = ParseJsonElement(row.ConfigJson)
                };

                const string positionSql = @"
SELECT
    position_id AS PositionId,
    us_id AS UsId,
    exchange AS Exchange,
    symbol AS Symbol,
    side AS Side,
    entry_price AS EntryPrice,
    qty AS Qty,
    status AS Status,
    opened_at AS OpenedAt,
    closed_at AS ClosedAt,
    realized_pnl AS RealizedPnl
FROM strategy_position
WHERE us_id = @usId
ORDER BY opened_at DESC
LIMIT @take;";

                var positionRows = await _db.QueryAsync<PositionHistoryRow>(
                    positionSql,
                    new { usId = row.UsId, take = StrategyPositionHistoryLimit },
                    null,
                    ct).ConfigureAwait(false);

                strategyDto.PositionHistory = positionRows.Select(item => new PlanetPositionLiteDto
                {
                    PositionId = item.PositionId,
                    Exchange = item.Exchange ?? string.Empty,
                    Symbol = item.Symbol ?? string.Empty,
                    Side = item.Side ?? string.Empty,
                    EntryPrice = item.EntryPrice,
                    Qty = item.Qty,
                    Status = item.Status ?? string.Empty,
                    OpenedAt = item.OpenedAt,
                    ClosedAt = item.ClosedAt,
                    RealizedPnl = item.RealizedPnl
                }).ToList();

                detail.StrategyDetails.Add(strategyDto);
            }

            const string commentSql = @"
SELECT
    c.comment_id AS CommentId,
    c.post_id AS PostId,
    c.uid AS Uid,
    COALESCE(NULLIF(a.nickname, ''), a.email) AS AuthorName,
    a.avatar_url AS AuthorAvatarUrl,
    c.content AS Content,
    c.status AS Status,
    c.created_at AS CreatedAt,
    c.updated_at AS UpdatedAt
FROM planet_post_comment c
LEFT JOIN account a ON a.uid = c.uid
WHERE c.post_id = @postId
  AND c.status = 'active'
ORDER BY c.created_at DESC
LIMIT @take;";

            var commentRows = await _db.QueryAsync<PostCommentRow>(
                commentSql,
                new { postId = request.PostId, take = CommentInitialLimit },
                null,
                ct).ConfigureAwait(false);

            var allowOwnerDelete = _configStore.GetBool(OwnerDeleteCommentConfigKey, false);
            detail.Comments = commentRows
                .Select(item => ToCommentDto(item, uid, isAdmin, postRow.Uid, allowOwnerDelete))
                .ToList();

            return new OkObjectResult(ApiResponse<PlanetPostDetailDto>.Ok(detail));
        }

        public async Task<IActionResult> ReactPostAsync(long uid, PlanetPostReactionRequest request, CancellationToken ct)
        {
            if (request == null || request.PostId <= 0)
            {
                return new BadRequestObjectResult(ApiResponse<object>.Error("无效的帖子ID"));
            }

            var reaction = NormalizeReaction(request.ReactionType);
            if (!AllowedReactions.Contains(reaction))
            {
                return new BadRequestObjectResult(ApiResponse<object>.Error("无效的互动类型，支持 like/dislike/none"));
            }

            await using var uow = await _db.BeginUnitOfWorkAsync(ct).ConfigureAwait(false);
            try
            {
                var ownerRow = await QueryPostOwnerAsync(request.PostId, uow, ct).ConfigureAwait(false);
                if (ownerRow == null || string.Equals(NormalizeStoredStatus(ownerRow.Status), "deleted", StringComparison.OrdinalIgnoreCase))
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    return new NotFoundObjectResult(ApiResponse<object>.Error("帖子不存在或已删除"));
                }

                var isAdmin = await IsAdminAsync(uid, uow, ct).ConfigureAwait(false);
                if (!CanViewPost(ownerRow.Uid, ownerRow.Status, uid, isAdmin))
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    return new ObjectResult(ApiResponse<object>.Error("该帖子已隐藏")) { StatusCode = 403 };
                }

                if (string.Equals(reaction, "none", StringComparison.OrdinalIgnoreCase))
                {
                    const string deleteSql = @"
DELETE FROM planet_post_reaction
WHERE post_id = @postId
  AND uid = @uid;";
                    await _db.ExecuteAsync(deleteSql, new { postId = request.PostId, uid }, uow, ct).ConfigureAwait(false);
                }
                else
                {
                    const string upsertSql = @"
INSERT INTO planet_post_reaction
(
    post_id,
    uid,
    reaction_type,
    created_at,
    updated_at
)
VALUES
(
    @postId,
    @uid,
    @reactionType,
    UTC_TIMESTAMP(3),
    UTC_TIMESTAMP(3)
)
ON DUPLICATE KEY UPDATE
    reaction_type = VALUES(reaction_type),
    updated_at = UTC_TIMESTAMP(3);";

                    await _db.ExecuteAsync(
                        upsertSql,
                        new { postId = request.PostId, uid, reactionType = reaction },
                        uow,
                        ct).ConfigureAwait(false);
                }

                await RefreshPostStatsAsync(request.PostId, uow, ct).ConfigureAwait(false);
                await uow.CommitAsync(ct).ConfigureAwait(false);

                var message = reaction switch
                {
                    "like" => "已点赞",
                    "dislike" => "已点踩",
                    _ => "已取消互动"
                };
                return new OkObjectResult(ApiResponse<object?>.Ok(null, message));
            }
            catch (Exception ex)
            {
                await SafeRollbackAsync(uow, ct).ConfigureAwait(false);
                _logger.LogError(ex, "帖子互动失败: uid={Uid}, postId={PostId}, reaction={Reaction}", uid, request.PostId, reaction);
                return new ObjectResult(ApiResponse<object>.Error("互动失败，请稍后重试")) { StatusCode = 500 };
            }
        }

        public async Task<IActionResult> ToggleFavoriteAsync(long uid, PlanetPostFavoriteRequest request, CancellationToken ct)
        {
            if (request == null || request.PostId <= 0)
            {
                return new BadRequestObjectResult(ApiResponse<object>.Error("无效的帖子ID"));
            }

            await using var uow = await _db.BeginUnitOfWorkAsync(ct).ConfigureAwait(false);
            try
            {
                var ownerRow = await QueryPostOwnerAsync(request.PostId, uow, ct).ConfigureAwait(false);
                if (ownerRow == null || string.Equals(NormalizeStoredStatus(ownerRow.Status), "deleted", StringComparison.OrdinalIgnoreCase))
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    return new NotFoundObjectResult(ApiResponse<object>.Error("帖子不存在或已删除"));
                }

                var isAdmin = await IsAdminAsync(uid, uow, ct).ConfigureAwait(false);
                if (!CanViewPost(ownerRow.Uid, ownerRow.Status, uid, isAdmin))
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    return new ObjectResult(ApiResponse<object>.Error("该帖子已隐藏")) { StatusCode = 403 };
                }

                if (request.IsFavorite)
                {
                    const string insertSql = @"
INSERT IGNORE INTO planet_post_favorite
(
    post_id,
    uid,
    created_at
)
VALUES
(
    @postId,
    @uid,
    UTC_TIMESTAMP(3)
);";
                    await _db.ExecuteAsync(insertSql, new { postId = request.PostId, uid }, uow, ct).ConfigureAwait(false);
                }
                else
                {
                    const string deleteSql = @"
DELETE FROM planet_post_favorite
WHERE post_id = @postId
  AND uid = @uid;";
                    await _db.ExecuteAsync(deleteSql, new { postId = request.PostId, uid }, uow, ct).ConfigureAwait(false);
                }

                await RefreshPostStatsAsync(request.PostId, uow, ct).ConfigureAwait(false);
                await uow.CommitAsync(ct).ConfigureAwait(false);

                return new OkObjectResult(ApiResponse<object?>.Ok(null, request.IsFavorite ? "已收藏" : "已取消收藏"));
            }
            catch (Exception ex)
            {
                await SafeRollbackAsync(uow, ct).ConfigureAwait(false);
                _logger.LogError(ex, "帖子收藏操作失败: uid={Uid}, postId={PostId}, isFavorite={IsFavorite}", uid, request.PostId, request.IsFavorite);
                return new ObjectResult(ApiResponse<object>.Error("收藏操作失败，请稍后重试")) { StatusCode = 500 };
            }
        }

        public async Task<IActionResult> AddCommentAsync(long uid, PlanetPostCommentCreateRequest request, CancellationToken ct)
        {
            if (request == null || request.PostId <= 0)
            {
                return new BadRequestObjectResult(ApiResponse<object>.Error("无效的帖子ID"));
            }

            var content = request.Content?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(content))
            {
                return new BadRequestObjectResult(ApiResponse<object>.Error("评论内容不能为空"));
            }

            if (content.Length > MaxCommentLength)
            {
                return new BadRequestObjectResult(ApiResponse<object>.Error($"评论长度不能超过 {MaxCommentLength} 字符"));
            }

            await using var uow = await _db.BeginUnitOfWorkAsync(ct).ConfigureAwait(false);
            try
            {
                var ownerRow = await QueryPostOwnerAsync(request.PostId, uow, ct).ConfigureAwait(false);
                if (ownerRow == null || string.Equals(NormalizeStoredStatus(ownerRow.Status), "deleted", StringComparison.OrdinalIgnoreCase))
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    return new NotFoundObjectResult(ApiResponse<object>.Error("帖子不存在或已删除"));
                }

                var isAdmin = await IsAdminAsync(uid, uow, ct).ConfigureAwait(false);
                if (!CanViewPost(ownerRow.Uid, ownerRow.Status, uid, isAdmin))
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    return new ObjectResult(ApiResponse<object>.Error("该帖子已隐藏")) { StatusCode = 403 };
                }

                const string existsSql = @"
SELECT comment_id AS CommentId
FROM planet_post_comment
WHERE post_id = @postId
  AND uid = @uid
LIMIT 1;";

                var existed = await _db.QuerySingleOrDefaultAsync<CommentIdentityRow>(
                    existsSql,
                    new { postId = request.PostId, uid },
                    uow,
                    ct).ConfigureAwait(false);

                if (existed != null)
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    return new BadRequestObjectResult(ApiResponse<object>.Error("每个用户在同一帖子下仅允许评论一次"));
                }

                const string insertSql = @"
INSERT INTO planet_post_comment
(
    post_id,
    uid,
    content,
    status,
    created_at,
    updated_at
)
VALUES
(
    @postId,
    @uid,
    @content,
    'active',
    UTC_TIMESTAMP(3),
    UTC_TIMESTAMP(3)
);
SELECT LAST_INSERT_ID();";

                var commentId = await _db.ExecuteScalarAsync<long>(
                    insertSql,
                    new { postId = request.PostId, uid, content },
                    uow,
                    ct).ConfigureAwait(false);

                await RefreshPostStatsAsync(request.PostId, uow, ct).ConfigureAwait(false);
                await uow.CommitAsync(ct).ConfigureAwait(false);

                const string querySql = @"
SELECT
    c.comment_id AS CommentId,
    c.uid AS Uid,
    COALESCE(NULLIF(a.nickname, ''), a.email) AS AuthorName,
    a.avatar_url AS AuthorAvatarUrl,
    c.content AS Content,
    c.created_at AS CreatedAt,
    c.updated_at AS UpdatedAt
FROM planet_post_comment c
LEFT JOIN account a ON a.uid = c.uid
WHERE c.comment_id = @commentId
LIMIT 1;";

                var comment = await _db.QuerySingleOrDefaultAsync<PostCommentRow>(
                    querySql,
                    new { commentId },
                    null,
                    ct).ConfigureAwait(false);

                if (comment == null)
                {
                    return new OkObjectResult(ApiResponse<object?>.Ok(null, "评论成功"));
                }

                var allowOwnerDelete = _configStore.GetBool(OwnerDeleteCommentConfigKey, false);
                var dto = ToCommentDto(comment, uid, isAdmin, ownerRow.Uid, allowOwnerDelete);
                return new OkObjectResult(ApiResponse<PlanetCommentDto>.Ok(dto, "评论成功"));
            }
            catch (Exception ex)
            {
                await SafeRollbackAsync(uow, ct).ConfigureAwait(false);
                _logger.LogError(ex, "发表评论失败: uid={Uid}, postId={PostId}", uid, request.PostId);
                return new ObjectResult(ApiResponse<object>.Error("评论失败，请稍后重试")) { StatusCode = 500 };
            }
        }

        public async Task<IActionResult> ListCommentsAsync(long uid, PlanetPostCommentListRequest request, CancellationToken ct)
        {
            if (request == null || request.PostId <= 0)
            {
                return new BadRequestObjectResult(ApiResponse<object>.Error("无效的帖子ID"));
            }

            var offset = Math.Max(request.Offset.GetValueOrDefault(0), 0);
            var limit = request.Limit.GetValueOrDefault(offset == 0 ? CommentInitialLimit : CommentMoreLimit);
            if (limit <= 0)
            {
                limit = offset == 0 ? CommentInitialLimit : CommentMoreLimit;
            }
            limit = Math.Min(limit, CommentMaxLimit);

            var ownerRow = await QueryPostOwnerAsync(request.PostId, null, ct).ConfigureAwait(false);
            if (ownerRow == null || string.Equals(NormalizeStoredStatus(ownerRow.Status), "deleted", StringComparison.OrdinalIgnoreCase))
            {
                return new NotFoundObjectResult(ApiResponse<object>.Error("帖子不存在或已删除"));
            }

            var isAdmin = await IsAdminAsync(uid, null, ct).ConfigureAwait(false);
            if (!CanViewPost(ownerRow.Uid, ownerRow.Status, uid, isAdmin))
            {
                return new ObjectResult(ApiResponse<object>.Error("该帖子已隐藏")) { StatusCode = 403 };
            }

            const string totalSql = @"
SELECT COUNT(1)
FROM planet_post_comment
WHERE post_id = @postId
  AND status = 'active';";

            var total = await _db.ExecuteScalarAsync<long>(totalSql, new { postId = request.PostId }, null, ct).ConfigureAwait(false);

            const string listSql = @"
SELECT
    c.comment_id AS CommentId,
    c.post_id AS PostId,
    c.uid AS Uid,
    COALESCE(NULLIF(a.nickname, ''), a.email) AS AuthorName,
    a.avatar_url AS AuthorAvatarUrl,
    c.content AS Content,
    c.status AS Status,
    c.created_at AS CreatedAt,
    c.updated_at AS UpdatedAt
FROM planet_post_comment c
LEFT JOIN account a ON a.uid = c.uid
WHERE c.post_id = @postId
  AND c.status = 'active'
ORDER BY c.created_at DESC
LIMIT @limit OFFSET @offset;";

            var rows = await _db.QueryAsync<PostCommentRow>(
                listSql,
                new { postId = request.PostId, limit, offset },
                null,
                ct).ConfigureAwait(false);

            var allowOwnerDelete = _configStore.GetBool(OwnerDeleteCommentConfigKey, false);
            var items = rows
                .Select(item => ToCommentDto(item, uid, isAdmin, ownerRow.Uid, allowOwnerDelete))
                .ToList();

            return new OkObjectResult(ApiResponse<PlanetPostCommentListResponse>.Ok(new PlanetPostCommentListResponse
            {
                PostId = request.PostId,
                Total = total,
                Offset = offset,
                Limit = limit,
                HasMore = offset + items.Count < total,
                Items = items
            }));
        }

        public async Task<IActionResult> DeleteCommentAsync(long uid, PlanetPostCommentDeleteRequest request, CancellationToken ct)
        {
            if (request == null || request.CommentId <= 0)
            {
                return new BadRequestObjectResult(ApiResponse<object>.Error("无效的评论ID"));
            }

            await using var uow = await _db.BeginUnitOfWorkAsync(ct).ConfigureAwait(false);
            try
            {
                const string commentSql = @"
SELECT
    comment_id AS CommentId,
    post_id AS PostId,
    uid AS Uid,
    status AS Status
FROM planet_post_comment
WHERE comment_id = @commentId
LIMIT 1
FOR UPDATE;";

                var commentRow = await _db.QuerySingleOrDefaultAsync<CommentIdentityRow>(
                    commentSql,
                    new { commentId = request.CommentId },
                    uow,
                    ct).ConfigureAwait(false);

                if (commentRow == null || !string.Equals(commentRow.Status, "active", StringComparison.OrdinalIgnoreCase))
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    return new NotFoundObjectResult(ApiResponse<object>.Error("评论不存在或已删除"));
                }

                var ownerRow = await QueryPostOwnerAsync(commentRow.PostId, uow, ct).ConfigureAwait(false);
                if (ownerRow == null || string.Equals(NormalizeStoredStatus(ownerRow.Status), "deleted", StringComparison.OrdinalIgnoreCase))
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    return new NotFoundObjectResult(ApiResponse<object>.Error("帖子不存在或已删除"));
                }

                var isAdmin = await IsAdminAsync(uid, uow, ct).ConfigureAwait(false);
                var allowOwnerDelete = _configStore.GetBool(OwnerDeleteCommentConfigKey, false);
                if (!CanDeleteComment(commentRow.Uid, ownerRow.Uid, uid, isAdmin, allowOwnerDelete))
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    return new ObjectResult(ApiResponse<object>.Error("无权限删除该评论")) { StatusCode = 403 };
                }

                const string deleteSql = @"
UPDATE planet_post_comment
SET status = 'deleted',
    updated_at = UTC_TIMESTAMP(3)
WHERE comment_id = @commentId
  AND status = 'active';";

                var affected = await _db.ExecuteAsync(deleteSql, new { commentId = request.CommentId }, uow, ct).ConfigureAwait(false);
                if (affected <= 0)
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    return new NotFoundObjectResult(ApiResponse<object>.Error("评论不存在或已删除"));
                }

                await RefreshPostStatsAsync(commentRow.PostId, uow, ct).ConfigureAwait(false);
                await uow.CommitAsync(ct).ConfigureAwait(false);
                return new OkObjectResult(ApiResponse<object?>.Ok(null, "评论已删除"));
            }
            catch (Exception ex)
            {
                await SafeRollbackAsync(uow, ct).ConfigureAwait(false);
                _logger.LogError(ex, "删除评论失败: uid={Uid}, commentId={CommentId}", uid, request.CommentId);
                return new ObjectResult(ApiResponse<object>.Error("删除评论失败，请稍后重试")) { StatusCode = 500 };
            }
        }

        public async Task<IActionResult> GetOwnerStatsAsync(long uid, PlanetOwnerStatsRequest request, CancellationToken ct)
        {
            var payload = request ?? new PlanetOwnerStatsRequest();
            var page = payload.Page.GetValueOrDefault(1);
            if (page <= 0)
            {
                page = 1;
            }

            var pageSize = payload.PageSize.GetValueOrDefault(DefaultPageSize);
            if (pageSize <= 0)
            {
                pageSize = DefaultPageSize;
            }
            pageSize = Math.Min(pageSize, MaxPageSize);
            var offset = (page - 1) * pageSize;

            const string countSql = @"
SELECT COUNT(1)
FROM planet_post
WHERE uid = @uid
  AND status IN ('normal', 'active', 'hidden');";

            var total = await _db.ExecuteScalarAsync<long>(countSql, new { uid }, null, ct).ConfigureAwait(false);

            const string listSql = @"
SELECT
    post_id AS PostId,
    title AS Title,
    status AS Status,
    like_count AS LikeCount,
    dislike_count AS DislikeCount,
    favorite_count AS FavoriteCount,
    comment_count AS CommentCount,
    updated_at AS UpdatedAt
FROM planet_post
WHERE uid = @uid
  AND status IN ('normal', 'active', 'hidden')
ORDER BY updated_at DESC
LIMIT @pageSize OFFSET @offset;";

            var postRows = await _db.QueryAsync<OwnerStatsRow>(
                listSql,
                new { uid, pageSize, offset },
                null,
                ct).ConfigureAwait(false);

            var items = postRows.Select(item => new PlanetOwnerPostStatsDto
            {
                PostId = item.PostId,
                Title = item.Title ?? string.Empty,
                Status = NormalizeStoredStatus(item.Status),
                LikeCount = item.LikeCount,
                DislikeCount = item.DislikeCount,
                FavoriteCount = item.FavoriteCount,
                CommentCount = item.CommentCount,
                UpdatedAt = item.UpdatedAt
            }).ToList();

            var postIds = items.Select(item => item.PostId).ToList();
            if (postIds.Count > 0)
            {
                const string likerSql = @"
SELECT
    pr.post_id AS PostId,
    pr.uid AS Uid,
    COALESCE(NULLIF(a.nickname, ''), a.email) AS DisplayName,
    a.avatar_url AS AvatarUrl,
    pr.created_at AS ReactedAt
FROM planet_post_reaction pr
LEFT JOIN account a ON a.uid = pr.uid
WHERE pr.post_id IN @postIds
  AND pr.reaction_type = 'like'
ORDER BY pr.created_at DESC;";

                var likerRows = await _db.QueryAsync<LikerRow>(likerSql, new { postIds }, null, ct).ConfigureAwait(false);
                var likerLookup = likerRows
                    .GroupBy(item => item.PostId)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Take(MaxLikersPerPost).Select(item => new PlanetLikerDto
                        {
                            Uid = item.Uid,
                            DisplayName = item.DisplayName ?? string.Empty,
                            AvatarUrl = item.AvatarUrl,
                            ReactedAt = item.ReactedAt
                        }).ToList());

                foreach (var item in items)
                {
                    if (likerLookup.TryGetValue(item.PostId, out var likers))
                    {
                        item.Likers = likers;
                    }
                }
            }

            return new OkObjectResult(ApiResponse<PlanetOwnerStatsResponse>.Ok(new PlanetOwnerStatsResponse
            {
                Page = page,
                PageSize = pageSize,
                Total = total,
                Items = items
            }));
        }

        private async Task FillCardImagesAsync(List<PlanetPostCardDto> cards, CancellationToken ct)
        {
            if (cards.Count == 0)
            {
                return;
            }

            var postIds = cards.Select(item => item.PostId).ToList();
            const string imageSql = @"
SELECT
    post_id AS PostId,
    image_url AS ImageUrl,
    sort_order AS SortOrder
FROM planet_post_image
WHERE post_id IN @postIds
ORDER BY post_id ASC, sort_order ASC, id ASC;";

            var rows = await _db.QueryAsync<PostImageRow>(imageSql, new { postIds }, null, ct).ConfigureAwait(false);
            var lookup = rows
                .GroupBy(item => item.PostId)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(item => item.ImageUrl ?? string.Empty).Where(item => !string.IsNullOrWhiteSpace(item)).ToList());

            foreach (var card in cards)
            {
                if (lookup.TryGetValue(card.PostId, out var images))
                {
                    card.ImageUrls = images;
                }
            }
        }

        private async Task FillCardStrategiesAsync(List<PlanetPostCardDto> cards, CancellationToken ct)
        {
            if (cards.Count == 0)
            {
                return;
            }

            var postIds = cards.Select(item => item.PostId).ToList();
            const string strategySql = @"
SELECT
    ps.post_id AS PostId,
    ps.us_id AS UsId,
    COALESCE(NULLIF(us.alias_name, ''), sd.name, CONCAT('策略#', ps.us_id)) AS AliasName,
    COALESCE(sd.name, CONCAT('策略#', ps.us_id)) AS DefName,
    COALESCE(us.state, 'unknown') AS State,
    COALESCE(sv.version_no, 0) AS VersionNo
FROM planet_post_strategy ps
LEFT JOIN user_strategy us ON us.us_id = ps.us_id
LEFT JOIN strategy_def sd ON sd.def_id = us.def_id
LEFT JOIN strategy_version sv ON sv.version_id = us.pinned_version_id
WHERE ps.post_id IN @postIds
ORDER BY ps.post_id ASC, ps.id ASC;";

            var rows = await _db.QueryAsync<PostStrategyLiteRow>(strategySql, new { postIds }, null, ct).ConfigureAwait(false);
            var lookup = rows
                .GroupBy(item => item.PostId)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(item => new PlanetBoundStrategyLiteDto
                    {
                        UsId = item.UsId,
                        AliasName = item.AliasName ?? string.Empty,
                        DefName = item.DefName ?? string.Empty,
                        State = item.State ?? string.Empty,
                        VersionNo = item.VersionNo
                    }).ToList());

            foreach (var card in cards)
            {
                if (lookup.TryGetValue(card.PostId, out var strategies))
                {
                    card.Strategies = strategies;
                }
            }
        }

        private async Task FillCardStrategyCurvesAsync(List<PlanetPostCardDto> cards, CancellationToken ct)
        {
            if (cards.Count == 0)
            {
                return;
            }

            var usIds = cards
                .SelectMany(card => card.Strategies)
                .Select(strategy => strategy.UsId)
                .Distinct()
                .ToList();

            if (usIds.Count == 0)
            {
                return;
            }
            var snapshots = await _strategyPerformanceCacheService
                .GetCurveSnapshotsAsync(usIds, StrategyCurveDays, ct)
                .ConfigureAwait(false);

            foreach (var card in cards)
            {
                foreach (var strategy in card.Strategies)
                {
                    if (snapshots.TryGetValue(strategy.UsId, out var snapshot))
                    {
                        strategy.PnlSeries30d = snapshot.Series;
                        strategy.CurveSource = snapshot.CurveSource;
                        strategy.IsBacktestCurve = snapshot.IsBacktest;
                    }
                    else
                    {
                        strategy.PnlSeries30d = Enumerable.Repeat(0m, StrategyCurveDays).ToList();
                        strategy.CurveSource = "live";
                        strategy.IsBacktestCurve = false;
                    }
                }
            }
        }

        private async Task ReplacePostImagesAsync(long postId, IReadOnlyList<string> imageUrls, IUnitOfWork uow, CancellationToken ct)
        {
            const string deleteSql = "DELETE FROM planet_post_image WHERE post_id = @postId;";
            await _db.ExecuteAsync(deleteSql, new { postId }, uow, ct).ConfigureAwait(false);

            if (imageUrls.Count == 0)
            {
                return;
            }

            const string insertSql = @"
INSERT INTO planet_post_image
(
    post_id,
    image_url,
    sort_order,
    created_at
)
VALUES
(
    @postId,
    @imageUrl,
    @sortOrder,
    UTC_TIMESTAMP(3)
);";

            for (var i = 0; i < imageUrls.Count; i++)
            {
                await _db.ExecuteAsync(
                    insertSql,
                    new
                    {
                        postId,
                        imageUrl = imageUrls[i],
                        sortOrder = i
                    },
                    uow,
                    ct).ConfigureAwait(false);
            }
        }

        private async Task ReplacePostStrategiesAsync(long postId, IReadOnlyList<long> strategyUsIds, IUnitOfWork uow, CancellationToken ct)
        {
            const string deleteSql = "DELETE FROM planet_post_strategy WHERE post_id = @postId;";
            await _db.ExecuteAsync(deleteSql, new { postId }, uow, ct).ConfigureAwait(false);

            if (strategyUsIds.Count == 0)
            {
                return;
            }

            const string insertSql = @"
INSERT INTO planet_post_strategy
(
    post_id,
    us_id,
    created_at
)
VALUES
(
    @postId,
    @usId,
    UTC_TIMESTAMP(3)
);";

            foreach (var usId in strategyUsIds)
            {
                await _db.ExecuteAsync(insertSql, new { postId, usId }, uow, ct).ConfigureAwait(false);
            }
        }

        private async Task RefreshPostStatsAsync(long postId, IUnitOfWork? uow, CancellationToken ct)
        {
            const string sql = @"
UPDATE planet_post
SET like_count = (
        SELECT COUNT(1)
        FROM planet_post_reaction
        WHERE post_id = @postId
          AND reaction_type = 'like'
    ),
    dislike_count = (
        SELECT COUNT(1)
        FROM planet_post_reaction
        WHERE post_id = @postId
          AND reaction_type = 'dislike'
    ),
    favorite_count = (
        SELECT COUNT(1)
        FROM planet_post_favorite
        WHERE post_id = @postId
    ),
    comment_count = (
        SELECT COUNT(1)
        FROM planet_post_comment
        WHERE post_id = @postId
          AND status = 'active'
    ),
    updated_at = UTC_TIMESTAMP(3)
WHERE post_id = @postId;";

            await _db.ExecuteAsync(sql, new { postId }, uow, ct).ConfigureAwait(false);
        }

        private async Task<PostOwnerRow?> QueryPostOwnerAsync(long postId, IUnitOfWork? uow, CancellationToken ct)
        {
            const string sql = @"
SELECT
    post_id AS PostId,
    uid AS Uid,
    status AS Status,
    hidden_by_uid AS HiddenByUid,
    hidden_by_admin AS HiddenByAdmin,
    hidden_at AS HiddenAt
FROM planet_post
WHERE post_id = @postId
LIMIT 1;";

            return await _db.QuerySingleOrDefaultAsync<PostOwnerRow>(sql, new { postId }, uow, ct).ConfigureAwait(false);
        }

        private async Task<(bool Ok, string? Error)> EnsureStrategiesOwnedAsync(long uid, IReadOnlyList<long> strategyUsIds, IUnitOfWork? uow, CancellationToken ct)
        {
            if (strategyUsIds.Count == 0)
            {
                return (true, null);
            }

            const string sql = @"
SELECT us_id AS UsId
FROM user_strategy
WHERE uid = @uid
  AND us_id IN @strategyUsIds;";

            var ownedRows = await _db.QueryAsync<OwnedStrategyRow>(sql, new { uid, strategyUsIds }, uow, ct).ConfigureAwait(false);
            var ownedSet = ownedRows.Select(item => item.UsId).ToHashSet();
            var missing = strategyUsIds.Where(item => !ownedSet.Contains(item)).ToList();
            if (missing.Count > 0)
            {
                return (false, $"存在非本人策略，无法绑定：{string.Join(",", missing)}");
            }

            return (true, null);
        }

        private async Task<bool> IsAdminAsync(long uid, IUnitOfWork? uow, CancellationToken ct)
        {
            const string sql = @"
SELECT role
FROM account
WHERE uid = @uid
  AND deleted_at IS NULL
LIMIT 1;";

            var role = await _db.QuerySingleOrDefaultAsync<int?>(sql, new { uid }, uow, ct).ConfigureAwait(false);
            return role.HasValue && role.Value == _superAdminRole;
        }

        private async Task<IActionResult> SetPostStatusInternalAsync(long uid, long postId, string targetStatus, CancellationToken ct)
        {
            await using var uow = await _db.BeginUnitOfWorkAsync(ct).ConfigureAwait(false);
            try
            {
                var ownerRow = await QueryPostOwnerAsync(postId, uow, ct).ConfigureAwait(false);
                if (ownerRow == null || string.Equals(NormalizeStoredStatus(ownerRow.Status), "deleted", StringComparison.OrdinalIgnoreCase))
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    return new NotFoundObjectResult(ApiResponse<object>.Error("帖子不存在或已删除"));
                }

                var isAdmin = await IsAdminAsync(uid, uow, ct).ConfigureAwait(false);
                if (!CanManagePost(ownerRow.Uid, uid, isAdmin))
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    return new ObjectResult(ApiResponse<object>.Error("无权限管理该帖子")) { StatusCode = 403 };
                }

                var currentStatus = NormalizeStoredStatus(ownerRow.Status);
                if (string.Equals(currentStatus, targetStatus, StringComparison.OrdinalIgnoreCase))
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    return new OkObjectResult(ApiResponse<object?>.Ok(null, "帖子状态未变化"));
                }
                if (string.Equals(currentStatus, "hidden", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(targetStatus, "normal", StringComparison.OrdinalIgnoreCase)
                    && ownerRow.HiddenByAdmin > 0
                    && !isAdmin)
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    return new ObjectResult(ApiResponse<object>.Error("该帖子由管理员隐藏，仅管理员可恢复")) { StatusCode = 403 };
                }

                long? nextHiddenByUid;
                int nextHiddenByAdmin;
                DateTime? nextHiddenAt;
                if (string.Equals(targetStatus, "hidden", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(currentStatus, "hidden", StringComparison.OrdinalIgnoreCase))
                    {
                        nextHiddenByUid = ownerRow.HiddenByUid ?? uid;
                        nextHiddenByAdmin = ownerRow.HiddenByAdmin > 0 ? 1 : (isAdmin ? 1 : 0);
                        nextHiddenAt = ownerRow.HiddenAt ?? DateTime.UtcNow;
                    }
                    else
                    {
                        nextHiddenByUid = uid;
                        nextHiddenByAdmin = isAdmin ? 1 : 0;
                        nextHiddenAt = DateTime.UtcNow;
                    }
                }
                else
                {
                    nextHiddenByUid = null;
                    nextHiddenByAdmin = 0;
                    nextHiddenAt = null;
                }

                const string updateSql = @"
UPDATE planet_post
SET status = @status,
    visibility = @visibility,
    hidden_by_uid = @hiddenByUid,
    hidden_by_admin = @hiddenByAdmin,
    hidden_at = @hiddenAt,
    updated_at = UTC_TIMESTAMP(3)
WHERE post_id = @postId
  AND status <> 'deleted';";

                var affected = await _db.ExecuteAsync(
                    updateSql,
                    new
                    {
                        postId,
                        status = targetStatus,
                        visibility = ToLegacyVisibility(targetStatus),
                        hiddenByUid = nextHiddenByUid,
                        hiddenByAdmin = nextHiddenByAdmin,
                        hiddenAt = nextHiddenAt
                    },
                    uow,
                    ct).ConfigureAwait(false);

                if (affected <= 0)
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    return new NotFoundObjectResult(ApiResponse<object>.Error("帖子不存在或已删除"));
                }

                await uow.CommitAsync(ct).ConfigureAwait(false);
                var message = targetStatus switch
                {
                    "normal" => "帖子已设为正常",
                    "hidden" => "帖子已隐藏",
                    "deleted" => "帖子已删除",
                    _ => "帖子状态已更新"
                };
                return new OkObjectResult(ApiResponse<object?>.Ok(null, message));
            }
            catch (Exception ex)
            {
                await SafeRollbackAsync(uow, ct).ConfigureAwait(false);
                _logger.LogError(ex, "设置帖子状态失败: uid={Uid}, postId={PostId}, status={Status}", uid, postId, targetStatus);
                return new ObjectResult(ApiResponse<object>.Error("设置帖子状态失败，请稍后重试")) { StatusCode = 500 };
            }
        }

        private static PlanetPostCardDto ToCardDto(PostCardRow row)
        {
            return new PlanetPostCardDto
            {
                PostId = row.PostId,
                Uid = row.Uid,
                AuthorName = row.AuthorName ?? string.Empty,
                AuthorAvatarUrl = row.AuthorAvatarUrl,
                Title = row.Title ?? string.Empty,
                Content = row.Content ?? string.Empty,
                Status = NormalizeStoredStatus(row.Status),
                LikeCount = row.LikeCount,
                DislikeCount = row.DislikeCount,
                FavoriteCount = row.FavoriteCount,
                CommentCount = row.CommentCount,
                UserReaction = row.UserReaction,
                IsFavorited = row.IsFavorited > 0,
                CanManage = row.CanManage > 0,
                CreatedAt = row.CreatedAt,
                UpdatedAt = row.UpdatedAt
            };
        }

        private static PlanetCommentDto ToCommentDto(PostCommentRow row, long currentUid, bool isAdmin, long postOwnerUid, bool allowOwnerDelete)
        {
            return new PlanetCommentDto
            {
                CommentId = row.CommentId,
                Uid = row.Uid,
                AuthorName = row.AuthorName ?? string.Empty,
                AuthorAvatarUrl = row.AuthorAvatarUrl,
                Content = row.Content ?? string.Empty,
                CanDelete = CanDeleteComment(row.Uid, postOwnerUid, currentUid, isAdmin, allowOwnerDelete),
                CreatedAt = row.CreatedAt,
                UpdatedAt = row.UpdatedAt
            };
        }

        private static bool CanManagePost(long ownerUid, long currentUid, bool isAdmin)
        {
            return ownerUid == currentUid || isAdmin;
        }

        private static bool CanDeleteComment(long commentUid, long postOwnerUid, long currentUid, bool isAdmin, bool allowOwnerDelete)
        {
            if (commentUid == currentUid || isAdmin)
            {
                return true;
            }

            if (allowOwnerDelete && postOwnerUid == currentUid)
            {
                return true;
            }

            return false;
        }

        private static bool CanViewPost(long ownerUid, string? status, long currentUid, bool isAdmin)
        {
            var normalizedStatus = NormalizeStoredStatus(status);
            if (string.Equals(normalizedStatus, "deleted", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.Equals(normalizedStatus, "normal", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return ownerUid == currentUid || isAdmin;
        }

        private static string BuildListWhereSql(string scope)
        {
            return scope switch
            {
                "mine" => "WHERE p.uid = @uid AND p.status IN ('normal', 'active', 'hidden')",
                "favorite" => @"
WHERE p.status IN ('normal', 'active')
  AND EXISTS (
      SELECT 1
      FROM planet_post_favorite pf_scope
      WHERE pf_scope.post_id = p.post_id
        AND pf_scope.uid = @uid
  )",
                _ => "WHERE p.status IN ('normal', 'active')"
            };
        }

        private static string NormalizeScope(string? scope)
        {
            if (string.IsNullOrWhiteSpace(scope))
            {
                return "square";
            }

            return scope.Trim().ToLowerInvariant();
        }

        private static string NormalizeReaction(string? reactionType)
        {
            if (string.IsNullOrWhiteSpace(reactionType))
            {
                return "none";
            }

            return reactionType.Trim().ToLowerInvariant();
        }

        private static string NormalizePostStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return "normal";
            }

            var normalized = status.Trim().ToLowerInvariant();
            return normalized switch
            {
                "active" => "normal",
                "public" => "normal",
                _ => normalized
            };
        }

        private static string NormalizeStoredStatus(string? status)
        {
            var normalized = NormalizePostStatus(status);
            if (AllowedPostStatuses.Contains(normalized))
            {
                return normalized;
            }

            return "normal";
        }

        private static string ToLegacyVisibility(string status)
        {
            var normalized = NormalizePostStatus(status);
            return string.Equals(normalized, "normal", StringComparison.OrdinalIgnoreCase) ? "public" : "hidden";
        }

        private static bool TryResolvePostStatus(string? status, string? visibility, out string normalizedStatus)
        {
            normalizedStatus = "normal";

            if (!string.IsNullOrWhiteSpace(status))
            {
                var normalized = NormalizePostStatus(status);
                if (!AllowedPostStatuses.Contains(normalized))
                {
                    return false;
                }

                normalizedStatus = normalized;
                return true;
            }

            if (string.IsNullOrWhiteSpace(visibility))
            {
                return true;
            }

            var normalizedVisibility = visibility.Trim().ToLowerInvariant();
            if (string.Equals(normalizedVisibility, "public", StringComparison.OrdinalIgnoreCase))
            {
                normalizedStatus = "normal";
                return true;
            }

            if (string.Equals(normalizedVisibility, "hidden", StringComparison.OrdinalIgnoreCase))
            {
                normalizedStatus = "hidden";
                return true;
            }

            return false;
        }

        private static NormalizePostResult NormalizePostInput(
            string? title,
            string? content,
            string? status,
            string? visibility,
            IReadOnlyList<string>? imageUrls,
            IReadOnlyList<long>? strategyUsIds)
        {
            var normalizedTitle = title?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedTitle))
            {
                return NormalizePostResult.Fail("帖子标题不能为空");
            }

            if (normalizedTitle.Length > MaxTitleLength)
            {
                return NormalizePostResult.Fail($"帖子标题长度不能超过 {MaxTitleLength} 字符");
            }

            var normalizedContent = content?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedContent))
            {
                return NormalizePostResult.Fail("帖子正文不能为空");
            }

            if (normalizedContent.Length > MaxContentLength)
            {
                return NormalizePostResult.Fail($"帖子正文长度不能超过 {MaxContentLength} 字符");
            }

            if (!TryResolvePostStatus(status, visibility, out var normalizedStatus))
            {
                return NormalizePostResult.Fail("帖子状态仅支持 normal/hidden/deleted（兼容 public/hidden）");
            }

            var normalizedImages = new List<string>();
            if (imageUrls != null)
            {
                foreach (var imageUrl in imageUrls)
                {
                    var trimmed = imageUrl?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(trimmed))
                    {
                        continue;
                    }

                    if (trimmed.Length > 512)
                    {
                        return NormalizePostResult.Fail("图片地址过长");
                    }

                    normalizedImages.Add(trimmed);
                }
            }

            normalizedImages = normalizedImages
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (normalizedImages.Count > MaxImageCount)
            {
                return NormalizePostResult.Fail($"单个帖子最多上传 {MaxImageCount} 张图片");
            }

            var normalizedStrategies = new List<long>();
            if (strategyUsIds != null)
            {
                normalizedStrategies = strategyUsIds
                    .Where(item => item > 0)
                    .Distinct()
                    .ToList();
            }

            if (normalizedStrategies.Count > MaxStrategyBindCount)
            {
                return NormalizePostResult.Fail($"单个帖子最多绑定 {MaxStrategyBindCount} 个策略");
            }

            return NormalizePostResult.Success(new NormalizedPostInput
            {
                Title = normalizedTitle,
                Content = normalizedContent,
                Status = normalizedStatus,
                ImageUrls = normalizedImages,
                StrategyUsIds = normalizedStrategies
            });
        }

        private static JsonElement? ParseJsonElement(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.Clone();
            }
            catch
            {
                return null;
            }
        }

        private static async Task SafeRollbackAsync(IUnitOfWork uow, CancellationToken ct)
        {
            try
            {
                await uow.RollbackAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                // 回滚失败仅记录外层日志，避免覆盖原始异常。
            }
        }

        private sealed class NormalizePostResult
        {
            public bool Ok { get; init; }
            public string? Error { get; init; }
            public NormalizedPostInput? Input { get; init; }

            public static NormalizePostResult Success(NormalizedPostInput input)
            {
                return new NormalizePostResult { Ok = true, Input = input };
            }

            public static NormalizePostResult Fail(string error)
            {
                return new NormalizePostResult { Ok = false, Error = error };
            }
        }

        private sealed class NormalizedPostInput
        {
            public string Title { get; set; } = string.Empty;
            public string Content { get; set; } = string.Empty;
            public string Status { get; set; } = "normal";
            public IReadOnlyList<string> ImageUrls { get; set; } = Array.Empty<string>();
            public IReadOnlyList<long> StrategyUsIds { get; set; } = Array.Empty<long>();
        }

        private sealed class PostOwnerRow
        {
            public long PostId { get; set; }
            public long Uid { get; set; }
            public string Status { get; set; } = string.Empty;
            public long? HiddenByUid { get; set; }
            public int HiddenByAdmin { get; set; }
            public DateTime? HiddenAt { get; set; }
        }

        private class PostCardRow
        {
            public long PostId { get; set; }
            public long Uid { get; set; }
            public string? AuthorName { get; set; }
            public string? AuthorAvatarUrl { get; set; }
            public string? Title { get; set; }
            public string? Content { get; set; }
            public string? Status { get; set; }
            public int LikeCount { get; set; }
            public int DislikeCount { get; set; }
            public int FavoriteCount { get; set; }
            public int CommentCount { get; set; }
            public string? UserReaction { get; set; }
            public int IsFavorited { get; set; }
            public int CanManage { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime UpdatedAt { get; set; }
        }

        private sealed class PostImageRow
        {
            public long PostId { get; set; }
            public string? ImageUrl { get; set; }
            public int SortOrder { get; set; }
        }

        private sealed class PostStrategyLiteRow
        {
            public long PostId { get; set; }
            public long UsId { get; set; }
            public string? AliasName { get; set; }
            public string? DefName { get; set; }
            public string? State { get; set; }
            public int VersionNo { get; set; }
        }

        private sealed class PostStrategyDetailRow
        {
            public long UsId { get; set; }
            public long? DefId { get; set; }
            public string? AliasName { get; set; }
            public string? DefName { get; set; }
            public string? Description { get; set; }
            public string? State { get; set; }
            public int VersionNo { get; set; }
            public string? ConfigJson { get; set; }
        }

        private sealed class PositionHistoryRow
        {
            public long PositionId { get; set; }
            public long UsId { get; set; }
            public string? Exchange { get; set; }
            public string? Symbol { get; set; }
            public string? Side { get; set; }
            public decimal EntryPrice { get; set; }
            public decimal Qty { get; set; }
            public string? Status { get; set; }
            public DateTime OpenedAt { get; set; }
            public DateTime? ClosedAt { get; set; }
            public decimal? RealizedPnl { get; set; }
        }

        private sealed class PostCommentRow
        {
            public long CommentId { get; set; }
            public long PostId { get; set; }
            public long Uid { get; set; }
            public string? AuthorName { get; set; }
            public string? AuthorAvatarUrl { get; set; }
            public string? Content { get; set; }
            public string? Status { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime UpdatedAt { get; set; }
        }

        private sealed class CommentIdentityRow
        {
            public long CommentId { get; set; }
            public long PostId { get; set; }
            public long Uid { get; set; }
            public string? Status { get; set; }
        }

        private sealed class OwnedStrategyRow
        {
            public long UsId { get; set; }
        }

        private sealed class OwnerStatsRow
        {
            public long PostId { get; set; }
            public string? Title { get; set; }
            public string? Status { get; set; }
            public int LikeCount { get; set; }
            public int DislikeCount { get; set; }
            public int FavoriteCount { get; set; }
            public int CommentCount { get; set; }
            public DateTime UpdatedAt { get; set; }
        }

        private sealed class LikerRow
        {
            public long PostId { get; set; }
            public long Uid { get; set; }
            public string? DisplayName { get; set; }
            public string? AvatarUrl { get; set; }
            public DateTime ReactedAt { get; set; }
        }
    }
}
