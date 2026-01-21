using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

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
