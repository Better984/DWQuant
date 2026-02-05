using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace ServerTest.Infrastructure.Config
{
    /// <summary>
    /// 从数据库加载服务器配置并合并到 IConfiguration
    /// </summary>
    public sealed class ServerConfigConfigurationSource : IConfigurationSource
    {
        public ServerConfigConfigurationSource(string connectionString)
        {
            ConnectionString = connectionString ?? string.Empty;
        }

        public string ConnectionString { get; }

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            return new ServerConfigConfigurationProvider(ConnectionString);
        }
    }

    public sealed class ServerConfigConfigurationProvider : ConfigurationProvider
    {
        private readonly string _connectionString;

        public ServerConfigConfigurationProvider(string connectionString)
        {
            _connectionString = connectionString ?? string.Empty;
        }

        public override void Load()
        {
            if (string.IsNullOrWhiteSpace(_connectionString))
            {
                return;
            }

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                connection.Open();
                using var command = new MySqlCommand("SELECT config_key, value_text FROM server_config", connection);
                using var reader = command.ExecuteReader();
                var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                while (reader.Read())
                {
                    if (reader.IsDBNull(0))
                    {
                        continue;
                    }

                    var key = reader.GetString(0);
                    var value = reader.IsDBNull(1) ? null : reader.GetString(1);
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    data[key] = value;
                }

                Data = data;
            }
            catch
            {
                // 数据库不可用时保持默认配置
            }
        }
    }
}
