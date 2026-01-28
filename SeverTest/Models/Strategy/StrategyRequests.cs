using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace ServerTest.Models.Strategy
{
    public sealed class StrategyCreateRequest
    {
        [Required]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? AliasName { get; set; }

        public JsonElement ConfigJson { get; set; }
    }

    public sealed class StrategyUpdateRequest
    {
        [Required]
        public long UsId { get; set; }

        public JsonElement ConfigJson { get; set; }

        public string? Changelog { get; set; }
    }

    public sealed class StrategyPublishRequest
    {
        [Required]
        public long UsId { get; set; }

        [Required]
        public long VersionId { get; set; }

        public string? StateAfterPublish { get; set; }

        public string? Changelog { get; set; }
    }

    public sealed class StrategyDeleteRequest
    {
        [Required]
        public long UsId { get; set; }
    }

    public sealed class StrategyInstanceStateRequest
    {
        [Required]
        public string State { get; set; } = string.Empty;
    }

    public sealed class StrategyShareCreateRequest
    {
        [Required]
        public long UsId { get; set; }

        public ShareCodePolicy? Policy { get; set; }
    }

    public sealed class StrategyCatalogPublishRequest
    {
        [Required]
        public long UsId { get; set; }
    }

    public sealed class StrategyMarketPublishRequest
    {
        [Required]
        public long UsId { get; set; }
    }

    public sealed class StrategyImportShareCodeRequest
    {
        [Required]
        public string ShareCode { get; set; } = string.Empty;

        public string? AliasName { get; set; }
    }

    public sealed class ShareCodePolicy
    {
        public bool? CanFork { get; set; }

        public bool? AllowCopy { get; set; }

        public int? MaxClaims { get; set; }

        public DateTime? ExpiredAt { get; set; }
    }
}
