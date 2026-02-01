using ServerTest.Modules.Notifications.Domain;

namespace ServerTest.Modules.Notifications.Application
{
    public interface INotificationPreferenceResolver
    {
        Task<NotificationPreferenceDecision> ResolveAsync(long userId, NotificationCategory category, CancellationToken ct = default);
    }
}
