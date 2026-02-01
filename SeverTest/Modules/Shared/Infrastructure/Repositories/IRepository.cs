using ServerTest.Infrastructure.Db;

namespace ServerTest.Infrastructure.Repositories
{
    public interface IRepository<TEntity, TKey>
    {
        Task<TEntity?> GetByIdAsync(TKey id, IUnitOfWork? uow = null, CancellationToken ct = default);
        Task<long> InsertAsync(TEntity entity, IUnitOfWork? uow = null, CancellationToken ct = default);
        Task<int> UpdateAsync(TKey id, IReadOnlyDictionary<string, object?> patch, IUnitOfWork? uow = null, CancellationToken ct = default);
        Task<int> SoftDeleteAsync(TKey id, IUnitOfWork? uow = null, CancellationToken ct = default);
        Task<PageResult<TEntity>> QueryPageAsync(int page, int pageSize, IUnitOfWork? uow = null, CancellationToken ct = default);
    }
}
