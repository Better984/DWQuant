using ServerTest.Notifications.Contracts;

namespace ServerTest.Notifications.Application
{
    public interface INotificationTemplateRenderer
    {
        NotificationRenderResult Render(string template, string payloadJson, NotificationCategory category, NotificationSeverity severity);
    }
}
