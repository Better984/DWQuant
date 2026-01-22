using System.Data.Common;

namespace ServerTest.Infrastructure.Db
{
    public sealed class UnitOfWork : IUnitOfWork
    {
        private readonly DbConnection _connection;
        private readonly DbTransaction _transaction;
        private bool _completed;

        public DbConnection Connection => _connection;
        public DbTransaction Transaction => _transaction;

        public UnitOfWork(DbConnection connection, DbTransaction transaction)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
        }

        public async Task CommitAsync(CancellationToken ct = default)
        {
            if (_completed)
            {
                return;
            }

            await _transaction.CommitAsync(ct).ConfigureAwait(false);
            _completed = true;
        }

        public async Task RollbackAsync(CancellationToken ct = default)
        {
            if (_completed)
            {
                return;
            }

            await _transaction.RollbackAsync(ct).ConfigureAwait(false);
            _completed = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_completed)
            {
                try
                {
                    await _transaction.RollbackAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Ignore rollback failures during dispose.
                }
            }

            await _transaction.DisposeAsync().ConfigureAwait(false);
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
