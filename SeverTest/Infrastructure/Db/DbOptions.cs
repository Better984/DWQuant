namespace ServerTest.Infrastructure.Db
{
    public sealed class DbOptions
    {
        public string ConnectionString { get; set; } = string.Empty;
        public int SlowQueryThresholdMs { get; set; } = 200;
        public int CommandTimeoutSeconds { get; set; } = 30;
    }
}
