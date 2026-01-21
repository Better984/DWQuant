using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ServerTest.Domain.Entities;
using ServerTest.Infrastructure.Db;

namespace ServerTest.Infrastructure.Repositories
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

        public new Task<int> SoftDeleteAsync(ulong uid, IUnitOfWork? uow = null, CancellationToken ct = default)
        {
            return base.SoftDeleteAsync(uid, uow, ct);
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
