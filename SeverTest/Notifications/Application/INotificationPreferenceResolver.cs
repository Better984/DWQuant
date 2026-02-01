using ServerTest.Notifications.Contracts;

namespace ServerTest.Notifications.Application
{
    public interface INotificationPreferenceResolver
    {
        Task<NotificationPreferenceDecision> ResolveAsync(long userId, NotificationCategory category, CancellationToken ct = default);
    }
}
