namespace ServerTest.Services
{
    /// <summary>
    /// 提供进程级唯一实例标识，用于跨模块链路追踪与分布式定位。
    /// </summary>
    public static class ProcessInstanceIdProvider
    {
        public static string InstanceId { get; } =
            $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
    }
}
