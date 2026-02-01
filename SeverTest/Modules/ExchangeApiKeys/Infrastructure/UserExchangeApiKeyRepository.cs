using Microsoft.Extensions.Logging;
using ServerTest.Infrastructure.Db;
using ServerTest.Modules.ExchangeApiKeys.Domain;
using System.Linq;

namespace ServerTest.Modules.ExchangeApiKeys.Infrastructure
{
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

        public async Task<IReadOnlyList<UserExchangeApiKeyDetail>> GetAllByUidAsync(long uid, CancellationToken ct = default)
        {
            var sql = @"
SELECT id,
       uid,
       exchange_type AS ExchangeType,
       label AS Label,
       api_key AS ApiKey,
       api_secret AS ApiSecret,
       api_password AS ApiPassword,
       created_at AS CreatedAt,
       updated_at AS UpdatedAt
FROM user_exchange_api_keys
WHERE uid = @uid
ORDER BY updated_at DESC, id DESC;";

            var result = await _db.QueryAsync<UserExchangeApiKeyDetail>(sql, new { uid }, null, ct).ConfigureAwait(false);
            return result.ToList();
        }

        public Task<int> CountByExchangeAsync(long uid, string exchangeType, CancellationToken ct = default)
        {
            var sql = @"
SELECT COUNT(*)
FROM user_exchange_api_keys
WHERE uid = @uid AND exchange_type = @exchangeType;";

            return _db.ExecuteScalarAsync<int>(sql, new { uid, exchangeType }, null, ct);
        }

        public async Task<bool> ExistsAsync(long uid, string exchangeType, string apiKey, CancellationToken ct = default)
        {
            var sql = @"
SELECT 1
FROM user_exchange_api_keys
WHERE uid = @uid AND exchange_type = @exchangeType AND api_key = @apiKey
LIMIT 1;";

            var result = await _db.QuerySingleOrDefaultAsync<int?>(sql, new { uid, exchangeType, apiKey }, null, ct).ConfigureAwait(false);
            return result.HasValue;
        }

        public Task<long> InsertAsync(
            long uid,
            string exchangeType,
            string label,
            string apiKey,
            string apiSecret,
            string? apiPassword,
            CancellationToken ct = default)
        {
            var sql = @"
INSERT INTO user_exchange_api_keys (uid, exchange_type, label, api_key, api_secret, api_password)
VALUES (@uid, @exchangeType, @label, @apiKey, @apiSecret, @apiPassword);
SELECT LAST_INSERT_ID();";

            return _db.ExecuteScalarAsync<long>(sql, new { uid, exchangeType, label, apiKey, apiSecret, apiPassword }, null, ct);
        }

        public Task<int> DeleteAsync(long id, long uid, CancellationToken ct = default)
        {
            var sql = @"
DELETE FROM user_exchange_api_keys
WHERE id = @id AND uid = @uid;";

            return _db.ExecuteAsync(sql, new { id, uid }, null, ct);
        }
    }
}
