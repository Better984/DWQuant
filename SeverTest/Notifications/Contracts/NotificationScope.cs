using System.Text.Json.Serialization;

namespace ServerTest.Notifications.Contracts
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum NotificationScope
    {
        User = 0,
        System = 1
    }
}
