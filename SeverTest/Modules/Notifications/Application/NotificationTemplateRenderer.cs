using System.Text.Json;
using ServerTest.Modules.Notifications.Domain;

namespace ServerTest.Modules.Notifications.Application
{
    public sealed class NotificationTemplateRenderer : INotificationTemplateRenderer
    {
        public NotificationRenderResult Render(string template, string payloadJson, NotificationCategory category, NotificationSeverity severity)
        {
            var normalizedTemplate = NotificationContractHelper.NormalizeTemplate(template);
            var title = BuildTitle(normalizedTemplate, category, severity);
            var body = BuildBody(payloadJson);

            return new NotificationRenderResult
            {
                Title = title,
                Body = body
            };
        }

        private static string BuildTitle(string template, NotificationCategory category, NotificationSeverity severity)
        {
            return $"{category}-{severity}: {template}";
        }

        private static string BuildBody(string payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson))
            {
                return string.Empty;
            }

            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                return doc.RootElement.ValueKind == JsonValueKind.Object
                    ? doc.RootElement.GetRawText()
                    : payloadJson;
            }
            catch
            {
                return payloadJson;
            }
        }
    }
}
