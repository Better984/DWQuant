namespace ServerTest.Infrastructure.Db
{
    public interface IDbManager
    {
        Task<IEnumerable<T>> QueryAsync<T>(string sql, object? param = null, IUnitOfWork? uow = null, CancellationToken ct = default);
        Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object? param = null, IUnitOfWork? uow = null, CancellationToken ct = default);
        Task<int> ExecuteAsync(string sql, object? param = null, IUnitOfWork? uow = null, CancellationToken ct = default);
        Task<T> ExecuteScalarAsync<T>(string sql, object? param = null, IUnitOfWork? uow = null, CancellationToken ct = default);
        Task<IUnitOfWork> BeginUnitOfWorkAsync(CancellationToken ct = default);
    }
}
