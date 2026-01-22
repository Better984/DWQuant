using System.Data.Common;

namespace ServerTest.Infrastructure.Db
{
    public interface IUnitOfWork : IAsyncDisposable
    {
        DbConnection Connection { get; }
        DbTransaction Transaction { get; }
        Task CommitAsync(CancellationToken ct = default);
        Task RollbackAsync(CancellationToken ct = default);
    }
}
