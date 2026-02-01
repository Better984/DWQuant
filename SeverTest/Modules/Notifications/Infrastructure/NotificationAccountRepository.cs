using ServerTest.Infrastructure.Db;

namespace ServerTest.Modules.Notifications.Infrastructure
{
    /// <summary>
    /// 通知模块对账户默认通知渠道的查询封装。
    /// </summary>
    public sealed class NotificationAccountRepository
    {
        private readonly IDbManager _db;

        public NotificationAccountRepository(IDbManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public Task<string?> GetCurrentNotificationPlatformAsync(long userId, CancellationToken ct = default)
        {
            const string sql = @"
SELECT current_notification_platform
FROM account
WHERE uid = @userId AND deleted_at IS NULL
LIMIT 1;";

            return _db.QuerySingleOrDefaultAsync<string?>(sql, new { userId }, null, ct);
        }
    }
}
