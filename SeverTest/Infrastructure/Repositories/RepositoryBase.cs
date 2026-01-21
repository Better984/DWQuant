using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ServerTest.Infrastructure.Db;

namespace ServerTest.Infrastructure.Repositories
{
    public abstract class RepositoryBase<TEntity, TKey> : IRepository<TEntity, TKey>
    {
        protected readonly IDbManager Db;
        protected readonly ILogger Logger;

        protected abstract string TableName { get; }
        protected abstract string KeyColumn { get; }
        protected virtual string SoftDeleteColumn => "deleted_at";
        protected virtual IReadOnlyCollection<string> AllowedPatchColumns => Array.Empty<string>();

        protected RepositoryBase(IDbManager db, ILogger logger)
        {
            Db = db ?? throw new ArgumentNullException(nameof(db));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public virtual Task<TEntity?> GetByIdAsync(TKey id, IUnitOfWork? uow = null, CancellationToken ct = default)
        {
            if (id == null)
            {
                throw new ArgumentException("Id is required.", nameof(id));
            }

            var sql = $@"
SELECT *
FROM {TableName}
WHERE {KeyColumn} = @id AND {SoftDeleteColumn} IS NULL
LIMIT 1;";

            return Db.QuerySingleOrDefaultAsync<TEntity>(sql, new { id }, uow, ct);
        }

        public virtual Task<long> InsertAsync(TEntity entity, IUnitOfWork? uow = null, CancellationToken ct = default)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            var (sql, param) = BuildInsertCommand(entity);
            return Db.ExecuteScalarAsync<long>(sql, param, uow, ct);
        }

        public virtual Task<int> UpdateAsync(TKey id, IReadOnlyDictionary<string, object?> patch, IUnitOfWork? uow = null, CancellationToken ct = default)
        {
            if (id == null)
            {
                throw new ArgumentException("Id is required.", nameof(id));
            }

            if (AllowedPatchColumns.Count == 0)
            {
                throw new InvalidOperationException("Patch updates are not configured for this repository.");
            }

            var builder = new PatchUpdateBuilder(TableName, KeyColumn, AllowedPatchColumns, SoftDeleteColumn);
            var (sql, param) = builder.Build(patch, id);
            return Db.ExecuteAsync(sql, param, uow, ct);
        }

        public virtual Task<int> SoftDeleteAsync(TKey id, IUnitOfWork? uow = null, CancellationToken ct = default)
        {
            if (id == null)
            {
                throw new ArgumentException("Id is required.", nameof(id));
            }

            var sql = $@"
UPDATE {TableName}
SET {SoftDeleteColumn} = NOW()
WHERE {KeyColumn} = @id AND {SoftDeleteColumn} IS NULL;";

            return Db.ExecuteAsync(sql, new { id }, uow, ct);
        }

        public virtual async Task<PageResult<TEntity>> QueryPageAsync(int page, int pageSize, IUnitOfWork? uow = null, CancellationToken ct = default)
        {
            if (page <= 0)
            {
                throw new ArgumentException("Page must be greater than 0.", nameof(page));
            }

            if (pageSize <= 0)
            {
                throw new ArgumentException("PageSize must be greater than 0.", nameof(pageSize));
            }

            var offset = (page - 1) * pageSize;

            var countSql = $@"
SELECT COUNT(1)
FROM {TableName}
WHERE {SoftDeleteColumn} IS NULL;";

            var pageSql = $@"
SELECT *
FROM {TableName}
WHERE {SoftDeleteColumn} IS NULL
ORDER BY {KeyColumn} DESC
LIMIT @offset, @pageSize;";

            var total = await Db.ExecuteScalarAsync<int>(countSql, null, uow, ct).ConfigureAwait(false);
            var items = await Db.QueryAsync<TEntity>(pageSql, new { offset, pageSize }, uow, ct).ConfigureAwait(false);

            return new PageResult<TEntity>(items.ToList(), total, page, pageSize);
        }

        protected abstract (string Sql, object Param) BuildInsertCommand(TEntity entity);
    }

    public sealed record PageResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
}
