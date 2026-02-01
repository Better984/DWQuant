using Dapper;
using Microsoft.Extensions.Options;
using ServerTest.Infrastructure.Db;
using ServerTest.Notifications.Contracts;
using ServerTest.Options;

namespace ServerTest.Notifications.Infrastructure
{
    public sealed class NotificationRepository
    {
        private readonly IDbManager _db;
        private readonly NotificationOptions _options;

        public NotificationRepository(IDbManager db, IOptions<NotificationOptions> options)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _options = options?.Value ?? new NotificationOptions();
        }

        public Task<long?> FindExistingNotificationIdAsync(long userId, string dedupeKey, CancellationToken ct = default)
        {
            const string sql = @"
SELECT n.id
FROM notification n
JOIN notification_user nu ON nu.notification_id = n.id
WHERE nu.user_id = @userId AND n.dedupe_key = @dedupeKey
ORDER BY n.id DESC
LIMIT 1;";

            return _db.QuerySingleOrDefaultAsync<long?>(sql, new { userId, dedupeKey }, null, ct);
        }

        public Task<long> InsertNotificationAsync(NotificationRecord record, IUnitOfWork? uow, CancellationToken ct = default)
        {
            const string sql = @"
INSERT INTO notification
(
    scope,
    category,
    severity,
    template,
    payload_json,
    dedupe_key,
    created_at
)
VALUES
(
    @Scope,
    @Category,
    @Severity,
    @Template,
    @PayloadJson,
    @DedupeKey,
    UTC_TIMESTAMP(3)
);
SELECT LAST_INSERT_ID();";

            var param = new
            {
                Scope = record.Scope.ToString(),
                Category = record.Category.ToString(),
                Severity = record.Severity.ToString(),
                Template = record.Template,
                PayloadJson = record.PayloadJson,
                DedupeKey = record.DedupeKey
            };

            return _db.ExecuteScalarAsync<long>(sql, param, uow, ct);
        }

        public Task<int> InsertNotificationUserAsync(long notificationId, long userId, IUnitOfWork? uow, CancellationToken ct = default)
        {
            const string sql = @"
INSERT IGNORE INTO notification_user
(
    notification_id,
    user_id,
    is_read,
    created_at,
    updated_at
)
VALUES
(
    @notificationId,
    @userId,
    0,
    UTC_TIMESTAMP(3),
    UTC_TIMESTAMP(3)
);";

            return _db.ExecuteAsync(sql, new { notificationId, userId }, uow, ct);
        }

        public Task<int> InsertDeliveryAsync(long notificationId, long userId, NotificationChannel channel, IUnitOfWork? uow, CancellationToken ct = default)
        {
            const string sql = @"
INSERT INTO notification_delivery
(
    notification_id,
    user_id,
    channel,
    status,
    attempt,
    next_retry_at,
    created_at,
    updated_at
)
VALUES
(
    @notificationId,
    @userId,
    @channel,
    'pending',
    0,
    UTC_TIMESTAMP(3),
    UTC_TIMESTAMP(3),
    UTC_TIMESTAMP(3)
);";

            var param = new
            {
                notificationId,
                userId,
                channel = channel.ToChannelKey()
            };

            return _db.ExecuteAsync(sql, param, uow, ct);
        }

        public async Task<int> InsertSystemBroadcastAsync(long notificationId, bool createExternalDeliveries, CancellationToken ct = default)
        {
            var total = 0;
            long lastId = 0;
            var batchSize = Math.Max(100, _options.BroadcastBatchSize);

            while (true)
            {
                var userIds = await GetUserIdsBatchAsync(lastId, batchSize, ct).ConfigureAwait(false);
                if (userIds.Count == 0)
                {
                    break;
                }

                await InsertNotificationUsersBatchAsync(notificationId, userIds, ct).ConfigureAwait(false);
                total += userIds.Count;
                lastId = userIds[^1];
            }

            if (createExternalDeliveries)
            {
                await InsertSystemBroadcastDeliveriesAsync(notificationId, ct).ConfigureAwait(false);
            }

            return total;
        }

        public async Task<IReadOnlyList<NotificationInboxRecord>> QueryInboxAsync(long userId, long? cursor, int limit, bool unreadOnly, CancellationToken ct = default)
        {
            var take = Math.Clamp(limit, 1, 100);
            var sql = @"
SELECT
    nu.id AS notification_user_id,
    n.id AS notification_id,
    n.scope,
    n.category,
    n.severity,
    n.template,
    n.payload_json,
    nu.is_read,
    nu.created_at
FROM notification_user nu
JOIN notification n ON n.id = nu.notification_id
WHERE nu.user_id = @userId
";

            if (unreadOnly)
            {
                sql += " AND nu.is_read = 0";
            }

            if (cursor.HasValue)
            {
                sql += " AND nu.id < @cursor";
            }

            sql += " ORDER BY nu.id DESC LIMIT @take;";

            var rows = await _db.QueryAsync<NotificationInboxRecord>(sql, new
            {
                userId,
                cursor,
                take
            }, null, ct).ConfigureAwait(false);

            return rows.ToList();
        }

        public Task<long> GetUnreadCountAsync(long userId, CancellationToken ct = default)
        {
            const string sql = @"
SELECT COUNT(1)
FROM notification_user
WHERE user_id = @userId AND is_read = 0;";

            return _db.ExecuteScalarAsync<long>(sql, new { userId }, null, ct);
        }

        public Task<int> MarkReadAsync(long userId, long notificationId, CancellationToken ct = default)
        {
            const string sql = @"
UPDATE notification_user
SET is_read = 1,
    read_at = UTC_TIMESTAMP(3),
    updated_at = UTC_TIMESTAMP(3)
WHERE user_id = @userId AND notification_id = @notificationId AND is_read = 0;";

            return _db.ExecuteAsync(sql, new { userId, notificationId }, null, ct);
        }

        public async Task<IReadOnlyList<NotificationDeliveryTask>> GetPendingDeliveriesAsync(int limit, DateTime utcNow, CancellationToken ct = default)
        {
            var take = Math.Clamp(limit, 1, 200);
            const string sql = @"
SELECT id,
       notification_id,
       user_id,
       channel,
       status,
       attempt,
       next_retry_at
FROM notification_delivery
WHERE status IN ('pending', 'retry') AND next_retry_at <= @now
ORDER BY next_retry_at ASC, id ASC
LIMIT @take;";

            var rows = await _db.QueryAsync<NotificationDeliveryTask>(sql, new { now = utcNow, take }, null, ct).ConfigureAwait(false);
            return rows.ToList();
        }

        public Task<int> MarkDeliverySendingAsync(long deliveryId, int attempt, CancellationToken ct = default)
        {
            const string sql = @"
UPDATE notification_delivery
SET status = 'sending',
    attempt = @attempt,
    updated_at = UTC_TIMESTAMP(3)
WHERE id = @id AND status IN ('pending', 'retry');";

            return _db.ExecuteAsync(sql, new { id = deliveryId, attempt }, null, ct);
        }

        public Task<int> MarkDeliverySuccessAsync(long deliveryId, CancellationToken ct = default)
        {
            const string sql = @"
UPDATE notification_delivery
SET status = 'sent',
    sent_at = UTC_TIMESTAMP(3),
    updated_at = UTC_TIMESTAMP(3),
    last_error = NULL
WHERE id = @id;";

            return _db.ExecuteAsync(sql, new { id = deliveryId }, null, ct);
        }

        public Task<int> MarkDeliveryRetryAsync(long deliveryId, DateTime nextRetryAt, string? error, CancellationToken ct = default)
        {
            const string sql = @"
UPDATE notification_delivery
SET status = 'retry',
    next_retry_at = @nextRetryAt,
    last_error = @error,
    updated_at = UTC_TIMESTAMP(3)
WHERE id = @id;";

            return _db.ExecuteAsync(sql, new { id = deliveryId, nextRetryAt, error }, null, ct);
        }

        public Task<int> MarkDeliveryDeadAsync(long deliveryId, string? error, CancellationToken ct = default)
        {
            const string sql = @"
UPDATE notification_delivery
SET status = 'dead',
    last_error = @error,
    updated_at = UTC_TIMESTAMP(3)
WHERE id = @id;";

            return _db.ExecuteAsync(sql, new { id = deliveryId, error }, null, ct);
        }

        public Task<NotificationRecord?> GetNotificationAsync(long notificationId, CancellationToken ct = default)
        {
            const string sql = @"
SELECT id,
       scope,
       category,
       severity,
       template,
       payload_json,
       dedupe_key,
       created_at
FROM notification
WHERE id = @notificationId
LIMIT 1;";

            return _db.QuerySingleOrDefaultAsync<NotificationRecord>(sql, new { notificationId }, null, ct);
        }

        private async Task<IReadOnlyList<long>> GetUserIdsBatchAsync(long lastId, int batchSize, CancellationToken ct)
        {
            const string sql = @"
SELECT uid
FROM account
WHERE deleted_at IS NULL AND uid > @lastId
ORDER BY uid
LIMIT @batchSize;";

            var rows = await _db.QueryAsync<long>(sql, new { lastId, batchSize }, null, ct).ConfigureAwait(false);
            return rows.ToList();
        }

        private Task<int> InsertNotificationUsersBatchAsync(long notificationId, IReadOnlyList<long> userIds, CancellationToken ct)
        {
            if (userIds.Count == 0)
            {
                return Task.FromResult(0);
            }

            var values = new List<string>(userIds.Count);
            var param = new DynamicParameters();
            for (var i = 0; i < userIds.Count; i++)
            {
                var key = $"uid{i}";
                values.Add($"(@notificationId, @{key}, 0, UTC_TIMESTAMP(3), UTC_TIMESTAMP(3))");
                param.Add(key, userIds[i]);
            }

            param.Add("notificationId", notificationId);

            var sql = $@"
INSERT IGNORE INTO notification_user
(
    notification_id,
    user_id,
    is_read,
    created_at,
    updated_at
)
VALUES {string.Join(", ", values)};";

            return _db.ExecuteAsync(sql, param, null, ct);
        }

        private async Task<int> InsertSystemBroadcastDeliveriesAsync(long notificationId, CancellationToken ct)
        {
            var total = 0;
            long lastId = 0;
            var batchSize = Math.Max(100, _options.BroadcastBatchSize);

            while (true)
            {
                var candidates = await GetBroadcastDeliveryCandidatesAsync(lastId, batchSize, ct).ConfigureAwait(false);
                if (candidates.Count == 0)
                {
                    break;
                }

                await InsertBroadcastDeliveriesBatchAsync(notificationId, candidates, ct).ConfigureAwait(false);
                total += candidates.Count;
                lastId = candidates[^1].Uid;
            }

            return total;
        }

        private async Task<IReadOnlyList<BroadcastDeliveryCandidate>> GetBroadcastDeliveryCandidatesAsync(long lastId, int batchSize, CancellationToken ct)
        {
            const string sql = @"
SELECT a.uid,
       a.current_notification_platform AS platform
FROM account a
JOIN user_notify_channels c
    ON c.uid = a.uid
   AND c.platform = a.current_notification_platform
   AND c.is_enabled = 1
WHERE a.deleted_at IS NULL
  AND a.current_notification_platform IN ('email', 'dingtalk', 'wecom', 'telegram')
  AND a.uid > @lastId
ORDER BY a.uid
LIMIT @batchSize;";

            var rows = await _db.QueryAsync<BroadcastDeliveryCandidate>(sql, new { lastId, batchSize }, null, ct).ConfigureAwait(false);
            return rows.ToList();
        }

        private Task<int> InsertBroadcastDeliveriesBatchAsync(long notificationId, IReadOnlyList<BroadcastDeliveryCandidate> candidates, CancellationToken ct)
        {
            if (candidates.Count == 0)
            {
                return Task.FromResult(0);
            }

            var values = new List<string>(candidates.Count);
            var param = new DynamicParameters();
            for (var i = 0; i < candidates.Count; i++)
            {
                var uidKey = $"uid{i}";
                var channelKey = $"channel{i}";
                values.Add($"(@notificationId, @{uidKey}, @{channelKey}, 'pending', 0, UTC_TIMESTAMP(3), UTC_TIMESTAMP(3), UTC_TIMESTAMP(3))");
                param.Add(uidKey, candidates[i].Uid);
                param.Add(channelKey, candidates[i].Platform);
            }

            param.Add("notificationId", notificationId);

            var sql = $@"
INSERT INTO notification_delivery
(
    notification_id,
    user_id,
    channel,
    status,
    attempt,
    next_retry_at,
    created_at,
    updated_at
)
VALUES {string.Join(", ", values)};";

            return _db.ExecuteAsync(sql, param, null, ct);
        }
    }

    public sealed class NotificationRecord
    {
        public long Id { get; set; }
        public NotificationScope Scope { get; set; }
        public NotificationCategory Category { get; set; }
        public NotificationSeverity Severity { get; set; }
        public string Template { get; set; } = string.Empty;
        public string PayloadJson { get; set; } = string.Empty;
        public string? DedupeKey { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public sealed class NotificationInboxRecord
    {
        public long NotificationUserId { get; set; }
        public long NotificationId { get; set; }
        public string Scope { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string Template { get; set; } = string.Empty;
        public string PayloadJson { get; set; } = string.Empty;
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public sealed class NotificationDeliveryTask
    {
        public long Id { get; set; }
        public long NotificationId { get; set; }
        public long UserId { get; set; }
        public string Channel { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int Attempt { get; set; }
        public DateTime NextRetryAt { get; set; }
    }

    public sealed class BroadcastDeliveryCandidate
    {
        public long Uid { get; set; }
        public string Platform { get; set; } = string.Empty;
    }
}
