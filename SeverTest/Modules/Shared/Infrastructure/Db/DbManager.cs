using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlConnector;
using System.Data.Common;
using System.Diagnostics;

namespace ServerTest.Infrastructure.Db
{
    public sealed class DbManager : IDbManager
    {
        private readonly DbOptions _options;
        private readonly ILogger<DbManager> _logger;

        public DbManager(IOptions<DbOptions> options, ILogger<DbManager> logger)
        {
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IUnitOfWork> BeginUnitOfWorkAsync(CancellationToken ct = default)
        {
            var connection = CreateConnection();
            await connection.OpenAsync(ct).ConfigureAwait(false);
            var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            return new UnitOfWork(connection, transaction);
        }

        public async Task<IEnumerable<T>> QueryAsync<T>(string sql, object? param = null, IUnitOfWork? uow = null, CancellationToken ct = default)
        {
            var (connection, transaction, shouldDispose) = await ResolveConnectionAsync(uow, ct).ConfigureAwait(false);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var command = new CommandDefinition(sql, param, transaction, _options.CommandTimeoutSeconds, cancellationToken: ct);
                var result = await connection.QueryAsync<T>(command).ConfigureAwait(false);
                var list = result.AsList();
                LogMetrics(sql, stopwatch, list.Count);
                return list;
            }
            finally
            {
                await DisposeIfNeededAsync(connection, shouldDispose).ConfigureAwait(false);
            }
        }

        public async Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object? param = null, IUnitOfWork? uow = null, CancellationToken ct = default)
        {
            var (connection, transaction, shouldDispose) = await ResolveConnectionAsync(uow, ct).ConfigureAwait(false);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var command = new CommandDefinition(sql, param, transaction, _options.CommandTimeoutSeconds, cancellationToken: ct);
                var result = await connection.QuerySingleOrDefaultAsync<T>(command).ConfigureAwait(false);
                LogMetrics(sql, stopwatch, null);
                return result;
            }
            finally
            {
                await DisposeIfNeededAsync(connection, shouldDispose).ConfigureAwait(false);
            }
        }

        public async Task<int> ExecuteAsync(string sql, object? param = null, IUnitOfWork? uow = null, CancellationToken ct = default)
        {
            var (connection, transaction, shouldDispose) = await ResolveConnectionAsync(uow, ct).ConfigureAwait(false);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var command = new CommandDefinition(sql, param, transaction, _options.CommandTimeoutSeconds, cancellationToken: ct);
                var affected = await connection.ExecuteAsync(command).ConfigureAwait(false);
                LogMetrics(sql, stopwatch, affected);
                return affected;
            }
            finally
            {
                await DisposeIfNeededAsync(connection, shouldDispose).ConfigureAwait(false);
            }
        }

        public async Task<T> ExecuteScalarAsync<T>(string sql, object? param = null, IUnitOfWork? uow = null, CancellationToken ct = default)
        {
            var (connection, transaction, shouldDispose) = await ResolveConnectionAsync(uow, ct).ConfigureAwait(false);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var command = new CommandDefinition(sql, param, transaction, _options.CommandTimeoutSeconds, cancellationToken: ct);
                var result = await connection.ExecuteScalarAsync<T>(command).ConfigureAwait(false);
                LogMetrics(sql, stopwatch, null);
                return result;
            }
            finally
            {
                await DisposeIfNeededAsync(connection, shouldDispose).ConfigureAwait(false);
            }
        }

        private DbConnection CreateConnection()
        {
            if (string.IsNullOrWhiteSpace(_options.ConnectionString))
            {
                throw new InvalidOperationException("DbOptions.ConnectionString is required.");
            }

            return new MySqlConnection(_options.ConnectionString);
        }

        private async Task<(DbConnection Connection, DbTransaction? Transaction, bool ShouldDispose)> ResolveConnectionAsync(IUnitOfWork? uow, CancellationToken ct)
        {
            if (uow != null)
            {
                return (uow.Connection, uow.Transaction, false);
            }

            var connection = CreateConnection();
            await connection.OpenAsync(ct).ConfigureAwait(false);
            return (connection, null, true);
        }

        private static async Task DisposeIfNeededAsync(DbConnection connection, bool shouldDispose)
        {
            if (!shouldDispose)
            {
                return;
            }

            await connection.DisposeAsync().ConfigureAwait(false);
        }

        private void LogMetrics(string sql, Stopwatch stopwatch, int? rows)
        {
            stopwatch.Stop();
            var metrics = new SqlExecutorMetrics(sql, stopwatch.ElapsedMilliseconds, rows);

            if (metrics.ElapsedMilliseconds >= _options.SlowQueryThresholdMs)
            {
                _logger.LogWarning("慢SQL查询 ({ElapsedMs}毫秒, 行数 {Rows}): {Sql}", metrics.ElapsedMilliseconds, metrics.RowsAffected, metrics.Sql);
            }
            // else
            // {
            //     _logger.LogDebug("SQL ({ElapsedMs}ms, rows {Rows}): {Sql}", metrics.ElapsedMilliseconds, metrics.RowsAffected, metrics.Sql);
            // }
        }
    }
}
