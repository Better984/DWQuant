using System;

namespace ServerTest.Infrastructure.Db
{
    public sealed class SqlExecutorMetrics
    {
        public string Sql { get; }
        public long ElapsedMilliseconds { get; }
        public int? RowsAffected { get; }

        public SqlExecutorMetrics(string sql, long elapsedMilliseconds, int? rowsAffected)
        {
            Sql = sql;
            ElapsedMilliseconds = elapsedMilliseconds;
            RowsAffected = rowsAffected;
        }
    }
}
