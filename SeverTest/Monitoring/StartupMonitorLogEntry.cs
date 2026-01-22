using Microsoft.Extensions.Logging;

namespace ServerTest.Monitoring
{
    public sealed class StartupMonitorLogEntry
    {
        public StartupMonitorLogEntry(DateTime timestamp, LogLevel level, string category, string message)
        {
            Timestamp = timestamp;
            Level = level;
            Category = category;
            Message = message;
        }

        public DateTime Timestamp { get; }
        public LogLevel Level { get; }
        public string Category { get; }
        public string Message { get; }
    }
}
