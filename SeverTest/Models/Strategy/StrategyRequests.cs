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
}
