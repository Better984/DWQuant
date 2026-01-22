using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace ServerTest.Services
{
    public class DatabaseService : BaseService
    {
        private readonly string _connectionString;

        public DatabaseService(ILogger<DatabaseService> logger, IConfiguration configuration)
            : base(logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("数据库连接字符串未配置");
        }

        public async Task<MySqlConnection> GetConnectionAsync()
        {
            var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            return connection;
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                using var connection = await GetConnectionAsync();
                return connection.State == System.Data.ConnectionState.Open;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "数据库连接测试失败");
                return false;
            }
        }
    }
}
