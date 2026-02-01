using System.Text.Json.Serialization;

namespace ServerTest.Modules.Notifications.Domain
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum NotificationSeverity
    {
        Info = 0,
        Warn = 1,
        Critical = 2
    }
}
