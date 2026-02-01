using ServerTest.Modules.Notifications.Domain;

namespace ServerTest.Modules.Notifications.Application
{
    public interface INotificationTemplateRenderer
    {
        NotificationRenderResult Render(string template, string payloadJson, NotificationCategory category, NotificationSeverity severity);
    }
}
