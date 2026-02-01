using System.Text.Json.Serialization;

namespace ServerTest.Modules.Notifications.Domain
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum NotificationChannel
    {
        InApp = 0,
        Email = 1,
        DingTalk = 2,
        WeCom = 3,
        Telegram = 4
    }
}
