using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ServerTest.Infrastructure.Db;
using ServerTest.Models;
using ServerTest.Modules.Planet.Domain;

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
        private const int CommentListLimit = 200;
        private const int MaxLikersPerPost = 20;

        private static readonly HashSet<string> AllowedScopes = new(StringComparer.OrdinalIgnoreCase)
        {
            "square",
            "mine",
            "favorite"
        };

        private static readonly HashSet<string> AllowedVisibilities = new(StringComparer.OrdinalIgnoreCase)
        {
            "public",
            "hidden"
        };

        private static readonly HashSet<string> AllowedReactions = new(StringComparer.OrdinalIgnoreCase)
        {
            "like",
            "dislike",
            "none"
        };

        private readonly IDbManager _db;
        private readonly ILogger<PlanetRepository> _logger;

        public PlanetRepository(IDbManager db, ILogger<PlanetRepository> logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IActionResult> CreatePostAsync(long uid, PlanetPostCreateRequest request, CancellationToken ct)
        {
            var payload = request ?? new PlanetPostCreateRequest();
            var normalizeResult = NormalizePostInput(payload.Title, payload.Content, payload.Visibility, payload.ImageUrls, payload.StrategyUsIds);
            if (!normalizeResult.Ok || normalizeResult.Input == null)
            {
                return new BadRequestObjectResult(ApiResponse<object>.Error(normalizeResult.Error ?? "参数错误"));
            }

            var ownership = await EnsureStrategiesOwnedAsync(uid, normalizeResult.Input.StrategyUsIds, null, ct).ConfigureAwait(false);
            if (!ownership.Ok)
            {
                return new BadRequestObjectResult(ApiResponse<object>.Error(ownership.Error ?? "策略归属校验失败"));
            }

            await using var uow = await _db.BeginUnitOfWorkAsync(ct).ConfigureAwait(false);
            try
            {
                const string insertPostSql = @"
INSERT INTO planet_post
(
    uid,
    title,
    content,
    visibility,
    status,
    created_at,
    updated_at
)
VALUES
(
    @uid,
    @title,
    @content,
    @visibility,
    'active',
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
                        visibility = normalizeResult.Input.Visibility
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

            var normalizeResult = NormalizePostInput(request.Title, request.Content, request.Visibility, request.ImageUrls, request.StrategyUsIds);
            if (!normalizeResult.Ok || normalizeResult.Input == null)
            {
                return new BadRequestObjectResult(ApiResponse<object>.Error(normalizeResult.Error ?? "参数错误"));
            }

            await using var uow = await _db.BeginUnitOfWorkAsync(ct).ConfigureAwait(false);
            try
            {
                var ownerRow = await QueryPostOwnerAsync(request.PostId, uow, ct).ConfigureAwait(false);
                if (ownerRow == null || !string.Equals(ownerRow.Status, "active", StringComparison.OrdinalIgnoreCase))
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    return new NotFoundObjectResult(ApiResponse<object>.Error("帖子不存在或已删除"));
                }

                if (ownerRow.Uid != uid)
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    return new ObjectResult(ApiResponse<object>.Error("无权限修改该帖子")) { StatusCode = 403 };
                }

                var ownership = await EnsureStrategiesOwnedAsync(uid, normalizeResult.Input.StrategyUsIds, uow, ct).ConfigureAwait(false);
                if (!ownership.Ok)
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    return new BadRequestObjectResult(ApiResponse<object>.Error(ownership.Error ?? "策略归属校验失败"));
                }

                const string updateSql = @"
UPDATE planet_post
SET title = @title,
    content = @content,
    visibility = @visibility,
    updated_at = UTC_TIMESTAMP(3)
WHERE post_id = @postId
  AND uid = @uid
  AND status = 'active';";

                var affected = await _db.ExecuteAsync(
                    updateSql,
                    new
                    {
                        title = normalizeResult.Input.Title,
                        content = normalizeResult.Input.Content,
                        visibility = normalizeResult.Input.Visibility,
                        postId = request.PostId,
                        uid
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

        public async Task<IActionResult> DeletePostAsync(long uid, PlanetPostDeleteRequest request, CancellationToken ct)
        {
            if (request == null || request.PostId <= 0)
            {
                return new BadRequestObjectResult(ApiResponse<object>.Error("无效的帖子ID"));
            }

            await using var uow = await _db.BeginUnitOfWorkAsync(ct).ConfigureAwait(false);
            try
            {
                var ownerRow = await QueryPostOwnerAsync(request.PostId, uow, ct).ConfigureAwait(false);
                if (ownerRow == null || !string.Equals(ownerRow.Status, "active", StringComparison.OrdinalIgnoreCase))
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    return new NotFoundObjectResult(ApiResponse<object>.Error("帖子不存在或已删除"));
                }

                if (ownerRow.Uid != uid)
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    return new ObjectResult(ApiResponse<object>.Error("无权限删除该帖子")) { StatusCode = 403 };
                }

                const string deleteSql = @"
UPDATE planet_post
SET status = 'deleted',
    updated_at = UTC_TIMESTAMP(3)
WHERE post_id = @postId
  AND uid = @uid
  AND status = 'active';";

                await _db.ExecuteAsync(deleteSql, new { postId = request.PostId, uid }, uow, ct).ConfigureAwait(false);
                await uow.CommitAsync(ct).ConfigureAwait(false);
                return new OkObjectResult(ApiResponse<object?>.Ok(null, "帖子已删除"));
            }
            catch (Exception ex)
            {
                await SafeRollbackAsync(uow, ct).ConfigureAwait(false);
                _logger.LogError(ex, "删除星球帖子失败: uid={Uid}, postId={PostId}", uid, request.PostId);
                return new ObjectResult(ApiResponse<object>.Error("删除帖子失败，请稍后重试")) { StatusCode = 500 };
            }
        }

        public async Task<IActionResult> SetVisibilityAsync(long uid, PlanetPostVisibilityRequest request, CancellationToken ct)
        {
            if (request == null || request.PostId <= 0)
            {
                return new BadRequestObjectResult(ApiResponse<object>.Error("无效的帖子ID"));
            }

            if (!TryNormalizeVisibility(request.Visibility, out var visibility))
            {
                return new BadRequestObjectResult(ApiResponse<object>.Error("帖子可见性仅支持 public 或 hidden"));
            }

            const string sql = @"
UPDATE planet_post
SET visibility = @visibility,
    updated_at = UTC_TIMESTAMP(3)
WHERE post_id = @postId
  AND uid = @uid
  AND status = 'active';";

            var affected = await _db.ExecuteAsync(sql, new { visibility, postId = request.PostId, uid }, null, ct).ConfigureAwait(false);
            if (affected <= 0)
            {
                return new NotFoundObjectResult(ApiResponse<object>.Error("帖子不存在或已删除"));
            }

            return new OkObjectResult(ApiResponse<object?>.Ok(null, "帖子可见性已更新"));
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
    p.visibility AS Visibility,
    p.like_count AS LikeCount,
    p.dislike_count AS DislikeCount,
    p.favorite_count AS FavoriteCount,
    p.comment_count AS CommentCount,
    pr.reaction_type AS UserReaction,
    CASE WHEN pf.id IS NULL THEN 0 ELSE 1 END AS IsFavorited,
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
                new { uid, pageSize, offset },
                null,
                ct).ConfigureAwait(false);

            var cards = rows.Select(ToCardDto).ToList();
            await FillCardImagesAsync(cards, ct).ConfigureAwait(false);
            await FillCardStrategiesAsync(cards, ct).ConfigureAwait(false);

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
    p.visibility AS Visibility,
    p.status AS Status,
    p.like_count AS LikeCount,
    p.dislike_count AS DislikeCount,
    p.favorite_count AS FavoriteCount,
    p.comment_count AS CommentCount,
    pr.reaction_type AS UserReaction,
    CASE WHEN pf.id IS NULL THEN 0 ELSE 1 END AS IsFavorited,
    p.created_at AS CreatedAt,
    p.updated_at AS UpdatedAt
FROM planet_post p
LEFT JOIN account a ON a.uid = p.uid
LEFT JOIN planet_post_reaction pr ON pr.post_id = p.post_id AND pr.uid = @uid
LEFT JOIN planet_post_favorite pf ON pf.post_id = p.post_id AND pf.uid = @uid
WHERE p.post_id = @postId
LIMIT 1;";

            var postRow = await _db.QuerySingleOrDefaultAsync<PostDetailRow>(
                postSql,
                new { uid, postId = request.PostId },
                null,
                ct).ConfigureAwait(false);

            if (postRow == null || !string.Equals(postRow.Status, "active", StringComparison.OrdinalIgnoreCase))
            {
                return new NotFoundObjectResult(ApiResponse<object>.Error("帖子不存在或已删除"));
            }

            if (!CanViewPost(postRow.Uid, postRow.Visibility, uid))
            {
                return new ObjectResult(ApiResponse<object>.Error("该帖子已隐藏")) { StatusCode = 403 };
            }

            var detail = new PlanetPostDetailDto
            {
                Post = ToCardDto(postRow),
                CanManage = postRow.Uid == uid
            };

            await FillCardImagesAsync(new List<PlanetPostCardDto> { detail.Post }, ct).ConfigureAwait(false);
            await FillCardStrategiesAsync(new List<PlanetPostCardDto> { detail.Post }, ct).ConfigureAwait(false);

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
    c.uid AS Uid,
    COALESCE(NULLIF(a.nickname, ''), a.email) AS AuthorName,
    a.avatar_url AS AuthorAvatarUrl,
    c.content AS Content,
    c.created_at AS CreatedAt,
    c.updated_at AS UpdatedAt
FROM planet_post_comment c
LEFT JOIN account a ON a.uid = c.uid
WHERE c.post_id = @postId
  AND c.status = 'active'
ORDER BY c.created_at ASC
LIMIT @take;";

            var commentRows = await _db.QueryAsync<PostCommentRow>(
                commentSql,
                new { postId = request.PostId, take = CommentListLimit },
                null,
                ct).ConfigureAwait(false);

            detail.Comments = commentRows.Select(item => new PlanetCommentDto
            {
                CommentId = item.CommentId,
                Uid = item.Uid,
                AuthorName = item.AuthorName ?? string.Empty,
                AuthorAvatarUrl = item.AuthorAvatarUrl,
                Content = item.Content ?? string.Empty,
                CreatedAt = item.CreatedAt,
                UpdatedAt = item.UpdatedAt
            }).ToList();

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
                if (ownerRow == null || !string.Equals(ownerRow.Status, "active", StringComparison.OrdinalIgnoreCase))
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    return new NotFoundObjectResult(ApiResponse<object>.Error("帖子不存在或已删除"));
                }

                if (!CanViewPost(ownerRow.Uid, ownerRow.Visibility, uid))
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
                if (ownerRow == null || !string.Equals(ownerRow.Status, "active", StringComparison.OrdinalIgnoreCase))
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    return new NotFoundObjectResult(ApiResponse<object>.Error("帖子不存在或已删除"));
                }

                if (!CanViewPost(ownerRow.Uid, ownerRow.Visibility, uid))
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
                if (ownerRow == null || !string.Equals(ownerRow.Status, "active", StringComparison.OrdinalIgnoreCase))
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    return new NotFoundObjectResult(ApiResponse<object>.Error("帖子不存在或已删除"));
                }

                if (!CanViewPost(ownerRow.Uid, ownerRow.Visibility, uid))
                {
                    await uow.RollbackAsync(ct).ConfigureAwait(false);
                    return new ObjectResult(ApiResponse<object>.Error("该帖子已隐藏")) { StatusCode = 403 };
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

                return new OkObjectResult(ApiResponse<PlanetCommentDto>.Ok(new PlanetCommentDto
                {
                    CommentId = comment.CommentId,
                    Uid = comment.Uid,
                    AuthorName = comment.AuthorName ?? string.Empty,
                    AuthorAvatarUrl = comment.AuthorAvatarUrl,
                    Content = comment.Content ?? string.Empty,
                    CreatedAt = comment.CreatedAt,
                    UpdatedAt = comment.UpdatedAt
                }, "评论成功"));
            }
            catch (Exception ex)
            {
                await SafeRollbackAsync(uow, ct).ConfigureAwait(false);
                _logger.LogError(ex, "发表评论失败: uid={Uid}, postId={PostId}", uid, request.PostId);
                return new ObjectResult(ApiResponse<object>.Error("评论失败，请稍后重试")) { StatusCode = 500 };
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
  AND status = 'active';";

            var total = await _db.ExecuteScalarAsync<long>(countSql, new { uid }, null, ct).ConfigureAwait(false);

            const string listSql = @"
SELECT
    post_id AS PostId,
    title AS Title,
    visibility AS Visibility,
    like_count AS LikeCount,
    dislike_count AS DislikeCount,
    favorite_count AS FavoriteCount,
    comment_count AS CommentCount,
    updated_at AS UpdatedAt
FROM planet_post
WHERE uid = @uid
  AND status = 'active'
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
                Visibility = item.Visibility ?? "public",
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
    visibility AS Visibility,
    status AS Status
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
                Visibility = row.Visibility ?? "public",
                LikeCount = row.LikeCount,
                DislikeCount = row.DislikeCount,
                FavoriteCount = row.FavoriteCount,
                CommentCount = row.CommentCount,
                UserReaction = row.UserReaction,
                IsFavorited = row.IsFavorited > 0,
                CreatedAt = row.CreatedAt,
                UpdatedAt = row.UpdatedAt
            };
        }

        private static bool CanViewPost(long ownerUid, string? visibility, long currentUid)
        {
            if (ownerUid == currentUid)
            {
                return true;
            }

            return string.Equals(visibility, "public", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildListWhereSql(string scope)
        {
            return scope switch
            {
                "mine" => "WHERE p.status = 'active' AND p.uid = @uid",
                "favorite" => @"
WHERE p.status = 'active'
  AND EXISTS (
      SELECT 1
      FROM planet_post_favorite pf_scope
      WHERE pf_scope.post_id = p.post_id
        AND pf_scope.uid = @uid
  )
  AND (p.visibility = 'public' OR p.uid = @uid)",
                _ => "WHERE p.status = 'active' AND (p.visibility = 'public' OR p.uid = @uid)"
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

        private static bool TryNormalizeVisibility(string? visibility, out string normalizedVisibility)
        {
            normalizedVisibility = "public";
            if (string.IsNullOrWhiteSpace(visibility))
            {
                return true;
            }

            var trimmed = visibility.Trim().ToLowerInvariant();
            if (!AllowedVisibilities.Contains(trimmed))
            {
                return false;
            }

            normalizedVisibility = trimmed;
            return true;
        }

        private static NormalizePostResult NormalizePostInput(
            string? title,
            string? content,
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

            if (!TryNormalizeVisibility(visibility, out var normalizedVisibility))
            {
                return NormalizePostResult.Fail("帖子可见性仅支持 public 或 hidden");
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
                Visibility = normalizedVisibility,
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
            public string Visibility { get; set; } = "public";
            public IReadOnlyList<string> ImageUrls { get; set; } = Array.Empty<string>();
            public IReadOnlyList<long> StrategyUsIds { get; set; } = Array.Empty<long>();
        }

        private sealed class PostOwnerRow
        {
            public long PostId { get; set; }
            public long Uid { get; set; }
            public string Visibility { get; set; } = "public";
            public string Status { get; set; } = string.Empty;
        }

        private class PostCardRow
        {
            public long PostId { get; set; }
            public long Uid { get; set; }
            public string? AuthorName { get; set; }
            public string? AuthorAvatarUrl { get; set; }
            public string? Title { get; set; }
            public string? Content { get; set; }
            public string? Visibility { get; set; }
            public int LikeCount { get; set; }
            public int DislikeCount { get; set; }
            public int FavoriteCount { get; set; }
            public int CommentCount { get; set; }
            public string? UserReaction { get; set; }
            public int IsFavorited { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime UpdatedAt { get; set; }
        }

        private sealed class PostDetailRow : PostCardRow
        {
            public string? Status { get; set; }
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
            public long Uid { get; set; }
            public string? AuthorName { get; set; }
            public string? AuthorAvatarUrl { get; set; }
            public string? Content { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime UpdatedAt { get; set; }
        }

        private sealed class OwnedStrategyRow
        {
            public long UsId { get; set; }
        }

        private sealed class OwnerStatsRow
        {
            public long PostId { get; set; }
            public string? Title { get; set; }
            public string? Visibility { get; set; }
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
