using ServerTest.Infrastructure.Db;
using ServerTest.Modules.Notifications.Domain;
using System.Linq;

namespace ServerTest.Modules.Notifications.Infrastructure
{
    public sealed class UserNotifyChannelRepository
    {
        private readonly IDbManager _db;

        public UserNotifyChannelRepository(IDbManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<IReadOnlyList<UserNotifyChannel>> GetAllByUidAsync(long uid, CancellationToken ct = default)
        {
            const string sql = @"
SELECT id,
       uid,
       platform AS Platform,
       address AS Address,
       secret AS Secret,
       is_enabled AS IsEnabled,
       is_default AS IsDefault,
       created_at AS CreatedAt,
       updated_at AS UpdatedAt
FROM user_notify_channels
WHERE uid = @uid
ORDER BY updated_at DESC, id DESC;";

            var result = await _db.QueryAsync<UserNotifyChannel>(sql, new { uid }, null, ct).ConfigureAwait(false);
            return result.ToList();
        }

        public async Task<IReadOnlyList<long>> GetIdsByPlatformAsync(long uid, string platform, CancellationToken ct = default)
        {
            const string sql = @"
SELECT id
FROM user_notify_channels
WHERE uid = @uid AND platform = @platform
ORDER BY updated_at DESC, id DESC;";

            var result = await _db.QueryAsync<long>(sql, new { uid, platform }, null, ct).ConfigureAwait(false);
            return result.ToList();
        }

        public Task<long> InsertAsync(
            long uid,
            string platform,
            string address,
            string? secret,
            bool isEnabled,
            bool isDefault,
            CancellationToken ct = default)
        {
            const string sql = @"
INSERT INTO user_notify_channels (uid, platform, address, secret, is_enabled, is_default)
VALUES (@uid, @platform, @address, @secret, @isEnabled, @isDefault);
SELECT LAST_INSERT_ID();";

            return _db.ExecuteScalarAsync<long>(sql, new { uid, platform, address, secret, isEnabled, isDefault }, null, ct);
        }

        public Task<int> UpdateAsync(
            long id,
            long uid,
            string address,
            string? secret,
            bool isEnabled,
            bool isDefault,
            CancellationToken ct = default)
        {
            const string sql = @"
UPDATE user_notify_channels
SET address = @address,
    secret = @secret,
    is_enabled = @isEnabled,
    is_default = @isDefault,
    updated_at = CURRENT_TIMESTAMP(3)
WHERE id = @id AND uid = @uid;";

            return _db.ExecuteAsync(sql, new { id, uid, address, secret, isEnabled, isDefault }, null, ct);
        }

        public Task<int> DeleteByPlatformAsync(long uid, string platform, CancellationToken ct = default)
        {
            const string sql = @"
DELETE FROM user_notify_channels
WHERE uid = @uid AND platform = @platform;";

            return _db.ExecuteAsync(sql, new { uid, platform }, null, ct);
        }

        public Task<int> DeleteByIdsExceptAsync(long uid, string platform, long keepId, CancellationToken ct = default)
        {
            const string sql = @"
DELETE FROM user_notify_channels
WHERE uid = @uid AND platform = @platform AND id <> @keepId;";

            return _db.ExecuteAsync(sql, new { uid, platform, keepId }, null, ct);
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
