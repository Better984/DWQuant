using System.Text.Json.Serialization;

namespace ServerTest.Modules.Notifications.Domain
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum NotificationCategory
    {
        Trade = 0,
        Risk = 1,
        Strategy = 2,
        Security = 3,
        Subscription = 4,
        Announcement = 5,
        Maintenance = 6,
        Update = 7
    }
}
