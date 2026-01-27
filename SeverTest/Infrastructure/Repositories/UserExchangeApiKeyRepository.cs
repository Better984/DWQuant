using Microsoft.Extensions.Logging;
using ServerTest.Infrastructure.Db;

namespace ServerTest.Infrastructure.Repositories
{
    public sealed class UserExchangeApiKeyRecord
    {
        public long Id { get; set; }
        public long Uid { get; set; }
        public string ExchangeType { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string ApiSecret { get; set; } = string.Empty;
        public string? ApiPassword { get; set; }
    }

    public sealed class UserExchangeApiKeyRepository
    {
        private readonly IDbManager _db;
        private readonly ILogger<UserExchangeApiKeyRepository> _logger;

        public UserExchangeApiKeyRepository(IDbManager db, ILogger<UserExchangeApiKeyRepository> logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<UserExchangeApiKeyRecord?> GetByIdAsync(long id, long uid, CancellationToken ct = default)
        {
            var sql = @"
SELECT id, uid, exchange_type AS ExchangeType, api_key AS ApiKey, api_secret AS ApiSecret, api_password AS ApiPassword
FROM user_exchange_api_keys
WHERE id = @id AND uid = @uid
LIMIT 1;";

            return _db.QuerySingleOrDefaultAsync<UserExchangeApiKeyRecord>(sql, new { id, uid }, null, ct);
        }

        public Task<UserExchangeApiKeyRecord?> GetLatestByUidAsync(long uid, string exchangeType, CancellationToken ct = default)
        {
            var sql = @"
SELECT id, uid, exchange_type AS ExchangeType, api_key AS ApiKey, api_secret AS ApiSecret, api_password AS ApiPassword
FROM user_exchange_api_keys
WHERE uid = @uid AND exchange_type = @exchangeType
ORDER BY updated_at DESC, id DESC
LIMIT 1;";

            return _db.QuerySingleOrDefaultAsync<UserExchangeApiKeyRecord>(sql, new { uid, exchangeType }, null, ct);
        }
    }
}
