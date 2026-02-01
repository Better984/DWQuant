using Microsoft.Extensions.Logging;
using ServerTest.Domain.Entities;
using ServerTest.Infrastructure.Db;
using ServerTest.Infrastructure.Repositories;

namespace ServerTest.Modules.Accounts.Infrastructure
{
    public sealed class AccountRepository : RepositoryBase<Account, ulong>
    {
        protected override string TableName => "account";
        protected override string KeyColumn => "uid";
        protected override IReadOnlyCollection<string> AllowedPatchColumns =>
            new[] { "nickname", "avatar_url", "signature" };

        public AccountRepository(IDbManager db, ILogger<AccountRepository> logger)
            : base(db, logger)
        {
        }

        public Task<Account?> GetByUidAsync(ulong uid, IUnitOfWork? uow = null, CancellationToken ct = default)
        {
            if (uid == 0)
            {
                throw new ArgumentException("Uid is required.", nameof(uid));
            }

            return GetByIdAsync(uid, uow, ct);
        }

        public Task<Account?> GetByEmailAsync(string email, IUnitOfWork? uow = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                throw new ArgumentException("Email is required.", nameof(email));
            }

            var sql = @"
SELECT *
FROM account
WHERE email = @email AND deleted_at IS NULL
LIMIT 1;";

            return Db.QuerySingleOrDefaultAsync<Account>(sql, new { email }, uow, ct);
        }

        public async Task<bool> ExistsByEmailAsync(string email, IUnitOfWork? uow = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                throw new ArgumentException("Email is required.", nameof(email));
            }

            var sql = @"
SELECT 1
FROM account
WHERE email = @email AND deleted_at IS NULL
LIMIT 1;";

            var result = await Db.QuerySingleOrDefaultAsync<int?>(sql, new { email }, uow, ct).ConfigureAwait(false);
            return result.HasValue;
        }

        public Task<long> CreateAccountAsync(
            string email,
            string passwordHash,
            string nickname,
            string? avatarUrl,
            string signature,
            IUnitOfWork? uow = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                throw new ArgumentException("Email is required.", nameof(email));
            }

            if (string.IsNullOrWhiteSpace(passwordHash))
            {
                throw new ArgumentException("Password hash is required.", nameof(passwordHash));
            }

            if (string.IsNullOrWhiteSpace(nickname))
            {
                throw new ArgumentException("Nickname is required.", nameof(nickname));
            }

            if (string.IsNullOrWhiteSpace(signature))
            {
                throw new ArgumentException("Signature is required.", nameof(signature));
            }

            var sql = @"
INSERT INTO account (email, password_hash, nickname, avatar_url, signature, status, role, register_at, created_at, updated_at)
VALUES (@email, @passwordHash, @nickname, @avatarUrl, @signature, 0, 0, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
SELECT LAST_INSERT_ID();";

            return Db.ExecuteScalarAsync<long>(sql, new { email, passwordHash, nickname, avatarUrl, signature }, uow, ct);
        }

        public Task<long> InsertAccountAsync(Account entity, IUnitOfWork? uow = null, CancellationToken ct = default)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            return InsertAsync(entity, uow, ct);
        }

        public Task<int> PatchProfileAsync(ulong uid, string? nickname, string? avatarUrl, string? signature, IUnitOfWork? uow = null, CancellationToken ct = default)
        {
            if (uid == 0)
            {
                throw new ArgumentException("Uid is required.", nameof(uid));
            }

            var patch = new Dictionary<string, object?>();

            if (nickname != null)
            {
                patch["nickname"] = nickname;
            }

            if (avatarUrl != null)
            {
                patch["avatar_url"] = avatarUrl;
            }

            if (signature != null)
            {
                patch["signature"] = signature;
            }

            if (patch.Count == 0)
            {
                throw new ArgumentException("At least one field must be provided.", nameof(patch));
            }

            return UpdateAsync(uid, patch, uow, ct);
        }

        public Task<int> UpdateLastLoginAsync(ulong uid, IUnitOfWork? uow = null, CancellationToken ct = default)
        {
            if (uid == 0)
            {
                throw new ArgumentException("Uid is required.", nameof(uid));
            }

            var sql = @"
UPDATE account
SET last_login_at = NOW()
WHERE uid = @uid AND deleted_at IS NULL;";

            return Db.ExecuteAsync(sql, new { uid }, uow, ct);
        }

        public Task<int?> GetRoleAsync(ulong uid, IUnitOfWork? uow = null, CancellationToken ct = default)
        {
            if (uid == 0)
            {
                throw new ArgumentException("Uid is required.", nameof(uid));
            }

            var sql = @"
SELECT role
FROM account
WHERE uid = @uid AND deleted_at IS NULL
LIMIT 1;";

            return Db.QuerySingleOrDefaultAsync<int?>(sql, new { uid }, uow, ct);
        }

        public Task<string?> GetAvatarUrlAsync(ulong uid, IUnitOfWork? uow = null, CancellationToken ct = default)
        {
            if (uid == 0)
            {
                throw new ArgumentException("Uid is required.", nameof(uid));
            }

            var sql = @"
SELECT avatar_url
FROM account
WHERE uid = @uid AND deleted_at IS NULL
LIMIT 1;";

            return Db.QuerySingleOrDefaultAsync<string?>(sql, new { uid }, uow, ct);
        }

        public Task<int> UpdateAvatarUrlAsync(ulong uid, string avatarUrl, IUnitOfWork? uow = null, CancellationToken ct = default)
        {
            if (uid == 0)
            {
                throw new ArgumentException("Uid is required.", nameof(uid));
            }

            if (string.IsNullOrWhiteSpace(avatarUrl))
            {
                throw new ArgumentException("AvatarUrl is required.", nameof(avatarUrl));
            }

            return PatchProfileAsync(uid, null, avatarUrl, null, uow, ct);
        }

        public Task<int> ChangePasswordAsync(ulong uid, string newHash, IUnitOfWork? uow = null, CancellationToken ct = default)
        {
            if (uid == 0)
            {
                throw new ArgumentException("Uid is required.", nameof(uid));
            }

            if (string.IsNullOrWhiteSpace(newHash))
            {
                throw new ArgumentException("Password hash is required.", nameof(newHash));
            }

            var sql = @"
UPDATE account
SET password_hash = @newHash,
    password_updated_at = NOW()
WHERE uid = @uid AND deleted_at IS NULL;";

            return Db.ExecuteAsync(sql, new { uid, newHash }, uow, ct);
        }

        public Task<int> MarkDeletedAsync(ulong uid, IUnitOfWork? uow = null, CancellationToken ct = default)
        {
            if (uid == 0)
            {
                throw new ArgumentException("Uid is required.", nameof(uid));
            }

            var sql = @"
UPDATE account
SET status = 2,
    deleted_at = NOW()
WHERE uid = @uid AND deleted_at IS NULL;";

            return Db.ExecuteAsync(sql, new { uid }, uow, ct);
        }

        public new Task<int> SoftDeleteAsync(ulong uid, IUnitOfWork? uow = null, CancellationToken ct = default)
        {
            return base.SoftDeleteAsync(uid, uow, ct);
        }

        public Task<int> AdminUpdateAsync(ulong uid, IReadOnlyDictionary<string, object?> patch, IUnitOfWork? uow = null, CancellationToken ct = default)
        {
            if (uid == 0)
            {
                throw new ArgumentException("Uid is required.", nameof(uid));
            }

            if (patch == null || patch.Count == 0)
            {
                throw new ArgumentException("Patch dictionary cannot be empty.", nameof(patch));
            }

            // 允许管理员更新的字段
            var allowedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "role", "status", "nickname", "avatar_url", "signature",
                "vip_expired_at", "current_notification_platform", "strategy_ids", "updated_at"
            };

            var setParts = new List<string>();
            var parameters = new Dictionary<string, object?> { { "uid", uid } };

            foreach (var kvp in patch)
            {
                if (!allowedFields.Contains(kvp.Key))
                {
                    throw new ArgumentException($"Field '{kvp.Key}' is not allowed for admin update.");
                }

                var paramName = $"p_{kvp.Key.Replace("_", "", StringComparison.Ordinal)}";
                setParts.Add($"{kvp.Key} = @{paramName}");
                parameters[paramName] = kvp.Value;
            }

            var sql = $@"
UPDATE account
SET {string.Join(", ", setParts)}
WHERE uid = @uid AND deleted_at IS NULL;";

            return Db.ExecuteAsync(sql, parameters, uow, ct);
        }

        protected override (string Sql, object Param) BuildInsertCommand(Account entity)
        {
            var sql = @"
INSERT INTO account
(
    email,
    password_hash,
    nickname,
    avatar_url,
    signature,
    status,
    role,
    vip_expired_at,
    current_notification_platform,
    strategy_ids,
    last_login_at,
    register_at,
    created_at,
    updated_at
)
VALUES
(
    @Email,
    @PasswordHash,
    @Nickname,
    @AvatarUrl,
    @Signature,
    @Status,
    @Role,
    @VipExpiredAt,
    @CurrentNotificationPlatform,
    @StrategyIds,
    @LastLoginAt,
    @RegisterAt,
    @CreatedAt,
    @UpdatedAt
);
SELECT LAST_INSERT_ID();";

            return (sql, entity);
        }
    }
}
