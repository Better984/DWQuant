using Microsoft.Extensions.Logging;
using ServerTest.Domain.Entities;
using ServerTest.Infrastructure.Db;
using ServerTest.Modules.Accounts.Infrastructure;

namespace ServerTest.Modules.Accounts.Application
{
    public sealed class AccountService
    {
        private readonly AccountRepository _repository;
        private readonly IUnitOfWorkFactory _uowFactory;
        private readonly ILogger<AccountService> _logger;

        public AccountService(AccountRepository repository, IUnitOfWorkFactory uowFactory, ILogger<AccountService> logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _uowFactory = uowFactory ?? throw new ArgumentNullException(nameof(uowFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<ulong> RegisterAsync(string email, string passwordHash, string? nickname = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                throw new ArgumentException("Email is required.", nameof(email));
            }

            if (string.IsNullOrWhiteSpace(passwordHash))
            {
                throw new ArgumentException("Password hash is required.", nameof(passwordHash));
            }

            await using var uow = await _uowFactory.BeginAsync(ct).ConfigureAwait(false);

            var existing = await _repository.GetByEmailAsync(email, uow, ct).ConfigureAwait(false);
            if (existing != null)
            {
                throw new InvalidOperationException("Email already exists.");
            }

            var account = new Account
            {
                Email = email,
                PasswordHash = passwordHash,
                Nickname = nickname,
                Status = 0,
                Role = 0,
                RegisterAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var uid = await _repository.InsertAccountAsync(account, uow, ct).ConfigureAwait(false);
            await uow.CommitAsync(ct).ConfigureAwait(false);
            return (ulong)uid;
        }

        public async Task<Account?> LoginAsync(string email, bool passwordOk, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                throw new ArgumentException("Email is required.", nameof(email));
            }

            var account = await _repository.GetByEmailAsync(email, null, ct).ConfigureAwait(false);
            if (account == null || !passwordOk)
            {
                return null;
            }

            await _repository.UpdateLastLoginAsync(account.Uid, null, ct).ConfigureAwait(false);
            return account;
        }

        public async Task<bool> UpdateProfileAsync(ulong uid, string? nickname, string? avatarUrl, string? signature, CancellationToken ct = default)
        {
            if (uid == 0)
            {
                throw new ArgumentException("Uid is required.", nameof(uid));
            }

            var affected = await _repository.PatchProfileAsync(uid, nickname, avatarUrl, signature, null, ct).ConfigureAwait(false);
            return affected > 0;
        }

        public async Task<bool> ChangePasswordAsync(ulong uid, bool oldPasswordOk, string newHash, CancellationToken ct = default)
        {
            if (uid == 0)
            {
                throw new ArgumentException("Uid is required.", nameof(uid));
            }

            if (string.IsNullOrWhiteSpace(newHash))
            {
                throw new ArgumentException("New password hash is required.", nameof(newHash));
            }

            if (!oldPasswordOk)
            {
                return false;
            }

            await using var uow = await _uowFactory.BeginAsync(ct).ConfigureAwait(false);
            var affected = await _repository.ChangePasswordAsync(uid, newHash, uow, ct).ConfigureAwait(false);
            await uow.CommitAsync(ct).ConfigureAwait(false);
            return affected > 0;
        }

        public Task<bool> SoftDeleteAsync(ulong uid, CancellationToken ct = default)
        {
            if (uid == 0)
            {
                throw new ArgumentException("Uid is required.", nameof(uid));
            }

            return SoftDeleteInternalAsync(uid, ct);
        }

        private async Task<bool> SoftDeleteInternalAsync(ulong uid, CancellationToken ct)
        {
            var affected = await _repository.SoftDeleteAsync(uid, null, ct).ConfigureAwait(false);
            return affected > 0;
        }
    }
}
