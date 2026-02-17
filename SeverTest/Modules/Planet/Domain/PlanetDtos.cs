using System.Text.Json;

namespace ServerTest.Modules.Planet.Domain
{
    public sealed class PlanetPostCreateRequest
    {
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Visibility { get; set; } = "public";
        public List<string>? ImageUrls { get; set; }
        public List<long>? StrategyUsIds { get; set; }
    }

    public sealed class PlanetPostUpdateRequest
    {
        public long PostId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Visibility { get; set; } = "public";
        public List<string>? ImageUrls { get; set; }
        public List<long>? StrategyUsIds { get; set; }
    }

    public sealed class PlanetPostDeleteRequest
    {
        public long PostId { get; set; }
    }

    public sealed class PlanetPostVisibilityRequest
    {
        public long PostId { get; set; }
        public string Visibility { get; set; } = "public";
    }

    public sealed class PlanetPostListRequest
    {
        public int? Page { get; set; }
        public int? PageSize { get; set; }
        public string? Scope { get; set; }
    }

    public sealed class PlanetPostDetailRequest
    {
        public long PostId { get; set; }
    }

    public sealed class PlanetPostReactionRequest
    {
        public long PostId { get; set; }
        public string ReactionType { get; set; } = "none";
    }

    public sealed class PlanetPostFavoriteRequest
    {
        public long PostId { get; set; }
        public bool IsFavorite { get; set; }
    }

    public sealed class PlanetPostCommentCreateRequest
    {
        public long PostId { get; set; }
        public string Content { get; set; } = string.Empty;
    }

    public sealed class PlanetOwnerStatsRequest
    {
        public int? Page { get; set; }
        public int? PageSize { get; set; }
    }

    public sealed class PlanetPostCreateResponse
    {
        public long PostId { get; set; }
    }

    public sealed class PlanetPostListResponse
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public long Total { get; set; }
        public List<PlanetPostCardDto> Items { get; set; } = new();
    }

    public sealed class PlanetPostCardDto
    {
        public long PostId { get; set; }
        public long Uid { get; set; }
        public string AuthorName { get; set; } = string.Empty;
        public string? AuthorAvatarUrl { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Visibility { get; set; } = "public";
        public int LikeCount { get; set; }
        public int DislikeCount { get; set; }
        public int FavoriteCount { get; set; }
        public int CommentCount { get; set; }
        public string? UserReaction { get; set; }
        public bool IsFavorited { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public List<string> ImageUrls { get; set; } = new();
        public List<PlanetBoundStrategyLiteDto> Strategies { get; set; } = new();
    }

    public sealed class PlanetPostDetailDto
    {
        public PlanetPostCardDto Post { get; set; } = new();
        public List<PlanetCommentDto> Comments { get; set; } = new();
        public List<PlanetStrategyDetailDto> StrategyDetails { get; set; } = new();
        public bool CanManage { get; set; }
    }

    public sealed class PlanetBoundStrategyLiteDto
    {
        public long UsId { get; set; }
        public string AliasName { get; set; } = string.Empty;
        public string DefName { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public int VersionNo { get; set; }
    }

    public sealed class PlanetStrategyDetailDto
    {
        public long UsId { get; set; }
        public long? DefId { get; set; }
        public string AliasName { get; set; } = string.Empty;
        public string DefName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public int VersionNo { get; set; }
        public JsonElement? ConfigJson { get; set; }
        public List<PlanetPositionLiteDto> PositionHistory { get; set; } = new();
    }

    public sealed class PlanetPositionLiteDto
    {
        public long PositionId { get; set; }
        public string Exchange { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public string Side { get; set; } = string.Empty;
        public decimal EntryPrice { get; set; }
        public decimal Qty { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime OpenedAt { get; set; }
        public DateTime? ClosedAt { get; set; }
        public decimal? RealizedPnl { get; set; }
    }

    public sealed class PlanetCommentDto
    {
        public long CommentId { get; set; }
        public long Uid { get; set; }
        public string AuthorName { get; set; } = string.Empty;
        public string? AuthorAvatarUrl { get; set; }
        public string Content { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public sealed class PlanetOwnerStatsResponse
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public long Total { get; set; }
        public List<PlanetOwnerPostStatsDto> Items { get; set; } = new();
    }

    public sealed class PlanetOwnerPostStatsDto
    {
        public long PostId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Visibility { get; set; } = "public";
        public int LikeCount { get; set; }
        public int DislikeCount { get; set; }
        public int FavoriteCount { get; set; }
        public int CommentCount { get; set; }
        public DateTime UpdatedAt { get; set; }
        public List<PlanetLikerDto> Likers { get; set; } = new();
    }

    public sealed class PlanetLikerDto
    {
        public long Uid { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public DateTime ReactedAt { get; set; }
    }

    public sealed class PlanetImageUploadResponse
    {
        public string ImageUrl { get; set; } = string.Empty;
        public string ObjectKey { get; set; } = string.Empty;
    }
}
