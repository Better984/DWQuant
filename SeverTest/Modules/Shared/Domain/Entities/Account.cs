namespace ServerTest.Domain.Entities
{
    public sealed class Account
    {
        public ulong Uid { get; set; }
        public string Email { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public DateTime? PasswordUpdatedAt { get; set; }
        public string? Nickname { get; set; }
        public string? AvatarUrl { get; set; }
        public string? Signature { get; set; }
        public byte Status { get; set; }
        public byte Role { get; set; }
        public DateTime? VipExpiredAt { get; set; }
        public string? CurrentNotificationPlatform { get; set; }
        public string? StrategyIds { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public DateTime? RegisterAt { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? DeletedAt { get; set; }
    }
}
