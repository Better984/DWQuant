namespace ServerTest.Models.Notify
{
    public sealed class UserNotifyChannelDto
    {
        public long Id { get; set; }
        public string Platform { get; set; } = string.Empty;
        public string AddressMasked { get; set; } = string.Empty;
        public bool HasSecret { get; set; }
        public bool IsEnabled { get; set; }
        public bool IsDefault { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public sealed class UpsertUserNotifyChannelRequest
    {
        public string Platform { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string? Secret { get; set; }
        public bool? IsEnabled { get; set; }
        public bool? IsDefault { get; set; }
    }
}
