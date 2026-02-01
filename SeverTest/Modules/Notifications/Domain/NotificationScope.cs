using System.Text.Json.Serialization;

namespace ServerTest.Modules.Notifications.Domain
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum NotificationScope
    {
        User = 0,
        System = 1
    }
}
