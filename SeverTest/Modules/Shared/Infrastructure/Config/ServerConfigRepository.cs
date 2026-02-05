using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using ServerTest.Infrastructure.Db;
using ServerTest.Models.Config;

namespace ServerTest.Infrastructure.Config
{
    /// <summary>
    /// 服务器配置数据访问
    /// </summary>
    public sealed class ServerConfigRepository
    {
        private readonly IDbManager _db;

        public ServerConfigRepository(IDbManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public Task EnsureSchemaAsync(CancellationToken ct = default)
        {
            const string sql = @"
CREATE TABLE IF NOT EXISTS server_config (
  config_key VARCHAR(128) NOT NULL,
  category VARCHAR(64) NOT NULL,
  value_type VARCHAR(16) NOT NULL,
  value_text LONGTEXT NOT NULL,
  description VARCHAR(255) NOT NULL,
  is_realtime TINYINT(1) NOT NULL DEFAULT 0,
  updated_by BIGINT NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (config_key)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
";
            return _db.ExecuteAsync(sql, null, null, ct);
        }

        public async Task<IReadOnlyList<ServerConfigItem>> GetAllAsync(CancellationToken ct = default)
        {
            const string sql = @"
SELECT
  config_key AS `Key`,
  category AS Category,
  value_type AS ValueType,
  value_text AS Value,
  description AS Description,
  is_realtime AS IsRealtime,
  updated_at AS UpdatedAt
FROM server_config
ORDER BY category, config_key
";
            var list = await _db.QueryAsync<ServerConfigItem>(sql, null, null, ct).ConfigureAwait(false);
            return list.ToList();
        }

        public Task<ServerConfigItem?> GetAsync(string key, CancellationToken ct = default)
        {
            const string sql = @"
SELECT
  config_key AS `Key`,
  category AS Category,
  value_type AS ValueType,
  value_text AS Value,
  description AS Description,
  is_realtime AS IsRealtime,
  updated_at AS UpdatedAt
FROM server_config
WHERE config_key = @key
LIMIT 1
";
            return _db.QuerySingleOrDefaultAsync<ServerConfigItem>(sql, new { key }, null, ct);
        }

        public Task<int> UpsertAsync(ServerConfigItem item, long? updatedBy, CancellationToken ct = default)
        {
            const string sql = @"
INSERT INTO server_config
  (config_key, category, value_type, value_text, description, is_realtime, updated_by, created_at, updated_at)
VALUES
  (@key, @category, @valueType, @value, @description, @isRealtime, @updatedBy, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
ON DUPLICATE KEY UPDATE
  category = VALUES(category),
  value_type = VALUES(value_type),
  value_text = VALUES(value_text),
  description = VALUES(description),
  is_realtime = VALUES(is_realtime),
  updated_by = VALUES(updated_by),
  updated_at = CURRENT_TIMESTAMP
";
            var param = new
            {
                key = item.Key,
                category = item.Category,
                valueType = item.ValueType,
                value = item.Value,
                description = item.Description,
                isRealtime = item.IsRealtime ? 1 : 0,
                updatedBy
            };
            return _db.ExecuteAsync(sql, param, null, ct);
        }

        public Task<int> UpdateValueAsync(string key, string value, long? updatedBy, CancellationToken ct = default)
        {
            const string sql = @"
UPDATE server_config
SET value_text = @value,
    updated_by = @updatedBy,
    updated_at = CURRENT_TIMESTAMP
WHERE config_key = @key
";
            return _db.ExecuteAsync(sql, new { key, value, updatedBy }, null, ct);
        }
    }
}
