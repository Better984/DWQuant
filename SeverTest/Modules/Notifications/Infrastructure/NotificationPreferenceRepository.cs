using ServerTest.Infrastructure.Db;

namespace ServerTest.Modules.Notifications.Infrastructure
{
    public sealed class NotificationPreferenceRepository
    {
        private readonly IDbManager _db;

        public NotificationPreferenceRepository(IDbManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public Task<NotificationPreferenceRecord?> GetAsync(long userId, CancellationToken ct = default)
        {
            const string sql = @"
SELECT user_id,
       rules_json,
       created_at,
       updated_at
FROM user_notification_preference
WHERE user_id = @userId
LIMIT 1;";

            return _db.QuerySingleOrDefaultAsync<NotificationPreferenceRecord>(sql, new { userId }, null, ct);
        }

        public Task<int> UpsertAsync(long userId, string rulesJson, CancellationToken ct = default)
        {
            const string sql = @"
INSERT INTO user_notification_preference
(
    user_id,
    rules_json,
    created_at,
    updated_at
)
VALUES
(
    @userId,
    @rulesJson,
    UTC_TIMESTAMP(3),
    UTC_TIMESTAMP(3)
)
ON DUPLICATE KEY UPDATE
    rules_json = @rulesJson,
    updated_at = UTC_TIMESTAMP(3);";

            return _db.ExecuteAsync(sql, new { userId, rulesJson }, null, ct);
        }
    }

    public sealed class NotificationPreferenceRecord
    {
        public long UserId { get; set; }
        public string RulesJson { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
