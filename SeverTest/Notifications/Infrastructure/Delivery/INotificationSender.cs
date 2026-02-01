using ServerTest.Notifications.Contracts;

namespace ServerTest.Notifications.Infrastructure.Delivery
{
    public interface INotificationSender
    {
        NotificationChannel Channel { get; }
        Task<NotificationSendResult> SendAsync(NotificationSendContext context, CancellationToken ct = default);
    }

    public sealed class NotificationSendContext
    {
        public long NotificationId { get; set; }
        public long UserId { get; set; }
        public NotificationChannel Channel { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public UserNotifyChannel? ChannelBinding { get; set; }
    }

    public sealed class NotificationSendResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
