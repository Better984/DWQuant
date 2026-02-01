using Dapper;
using System.Collections.Concurrent;
using System.Reflection;

namespace ServerTest.Infrastructure.Repositories
{
    public sealed class PatchUpdateBuilder
    {
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> CachedProperties = new();

        private readonly string _tableName;
        private readonly string _keyColumn;
        private readonly string _softDeleteColumn;
        private readonly HashSet<string> _allowedColumns;

        public PatchUpdateBuilder(string tableName, string keyColumn, IReadOnlyCollection<string> allowedColumns, string softDeleteColumn = "deleted_at")
        {
            _tableName = tableName;
            _keyColumn = keyColumn;
            _softDeleteColumn = softDeleteColumn;
            _allowedColumns = new HashSet<string>(allowedColumns, StringComparer.OrdinalIgnoreCase);
        }

        public (string Sql, DynamicParameters Params) Build(IReadOnlyDictionary<string, object?> patch, object id)
        {
            if (patch == null || patch.Count == 0)
            {
                throw new ArgumentException("Patch cannot be empty.", nameof(patch));
            }

            var parameters = new DynamicParameters();
            var setParts = new List<string>();

            foreach (var pair in patch)
            {
                if (!_allowedColumns.Contains(pair.Key))
                {
                    throw new InvalidOperationException($"Column '{pair.Key}' is not allowed for patch update.");
                }

                setParts.Add($"{pair.Key} = @{pair.Key}");
                parameters.Add(pair.Key, pair.Value);
            }

            setParts.Add("updated_at = NOW()");
            parameters.Add("id", id);

            var sql = $"UPDATE {_tableName} SET {string.Join(", ", setParts)} WHERE {_keyColumn} = @id AND {_softDeleteColumn} IS NULL;";
            return (sql, parameters);
        }

        public (string Sql, DynamicParameters Params) BuildFromObject(object dto, object id)
        {
            if (dto == null)
            {
                throw new ArgumentNullException(nameof(dto));
            }

            var dict = ToDictionary(dto);
            return Build(dict, id);
        }

        private static IReadOnlyDictionary<string, object?> ToDictionary(object dto)
        {
            var type = dto.GetType();
            var props = CachedProperties.GetOrAdd(type, t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance));
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            foreach (var prop in props)
            {
                if (!prop.CanRead)
                {
                    continue;
                }

                dict[prop.Name] = prop.GetValue(dto);
            }

            return dict;
        }
    }
}
