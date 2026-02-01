namespace ServerTest.Options
{
    public sealed class NotificationOptions
    {
        public int DeliveryBatchSize { get; set; } = 50;
        public int BroadcastBatchSize { get; set; } = 500;
        public int PollIntervalSeconds { get; set; } = 10;
        public int MaxAttempts { get; set; } = 5;
        public int[] RetryDelaysSeconds { get; set; } = { 60, 300, 900, 3600, 21600 };
        public bool EnableSystemBroadcastExternal { get; set; } = false;
    }
}
