using ServerTest.Infrastructure.Db;
using ServerTest.Notifications.Contracts;

namespace ServerTest.Notifications.Infrastructure
{
    public sealed class UserNotifyChannelRepository
    {
        private readonly IDbManager _db;

        public UserNotifyChannelRepository(IDbManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<bool> IsChannelBoundAsync(long userId, NotificationChannel channel, CancellationToken ct = default)
        {
            if (channel == NotificationChannel.InApp)
            {
                return true;
            }

            const string sql = @"
SELECT 1
FROM user_notify_channels
WHERE uid = @userId AND platform = @platform AND is_enabled = 1
LIMIT 1;";

            var platform = channel.ToChannelKey();
            var result = await _db.QuerySingleOrDefaultAsync<int?>(sql, new { userId, platform }, null, ct).ConfigureAwait(false);
            return result.HasValue;
        }

        public Task<UserNotifyChannel?> GetChannelAsync(long userId, NotificationChannel channel, CancellationToken ct = default)
        {
            if (channel == NotificationChannel.InApp)
            {
                return Task.FromResult<UserNotifyChannel?>(null);
            }

            const string sql = @"
SELECT id,
       uid,
       platform,
       address,
       secret,
       is_enabled,
       is_default,
       created_at,
       updated_at
FROM user_notify_channels
WHERE uid = @userId AND platform = @platform AND is_enabled = 1
ORDER BY updated_at DESC, id DESC
LIMIT 1;";

            var platform = channel.ToChannelKey();
            return _db.QuerySingleOrDefaultAsync<UserNotifyChannel>(sql, new { userId, platform }, null, ct);
        }
    }

    public sealed class UserNotifyChannel
    {
        public long Id { get; set; }
        public long Uid { get; set; }
        public string Platform { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string? Secret { get; set; }
        public bool IsEnabled { get; set; }
        public bool IsDefault { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
