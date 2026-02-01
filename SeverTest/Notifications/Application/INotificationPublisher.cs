using ServerTest.Notifications.Contracts;

namespace ServerTest.Notifications.Application
{
    public interface INotificationPublisher
    {
        Task<NotificationPublishResult> PublishToUserAsync(NotificationPublishRequest request, CancellationToken ct = default);
        Task<NotificationBroadcastResult> PublishSystemBroadcastAsync(NotificationCategory category, NotificationSeverity severity, string template, string payloadJson, CancellationToken ct = default);
    }
}
