using System.Text.Json;

namespace ServerTest.Notifications.Contracts
{
    public static class NotificationContractHelper
    {
        public static readonly IReadOnlyList<NotificationCategory> UserCategories = new[]
        {
            NotificationCategory.Trade,
            NotificationCategory.Risk,
            NotificationCategory.Strategy,
            NotificationCategory.Security,
            NotificationCategory.Subscription
        };

        public static readonly IReadOnlyList<NotificationCategory> SystemCategories = new[]
        {
            NotificationCategory.Announcement,
            NotificationCategory.Maintenance,
            NotificationCategory.Update
        };

        public static bool TryParseCategory(string? value, out NotificationCategory category)
        {
            category = default;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return Enum.TryParse(value.Trim(), ignoreCase: true, out category);
        }

        public static bool TryParseSeverity(string? value, out NotificationSeverity severity)
        {
            severity = default;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return Enum.TryParse(value.Trim(), ignoreCase: true, out severity);
        }

        public static bool TryParseScope(string? value, out NotificationScope scope)
        {
            scope = default;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return Enum.TryParse(value.Trim(), ignoreCase: true, out scope);
        }

        public static bool TryParseChannel(string? value, out NotificationChannel channel)
        {
            channel = default;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return Enum.TryParse(value.Trim(), ignoreCase: true, out channel);
        }

        public static string ToChannelKey(this NotificationChannel channel)
        {
            return channel switch
            {
                NotificationChannel.Email => "email",
                NotificationChannel.DingTalk => "dingtalk",
                NotificationChannel.WeCom => "wecom",
                NotificationChannel.Telegram => "telegram",
                _ => "inapp"
            };
        }

        public static NotificationChannel? TryMapChannelKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Trim().ToLowerInvariant() switch
            {
                "email" => NotificationChannel.Email,
                "dingtalk" => NotificationChannel.DingTalk,
                "wecom" => NotificationChannel.WeCom,
                "telegram" => NotificationChannel.Telegram,
                "inapp" => NotificationChannel.InApp,
                _ => null
            };
        }

        public static bool IsUserCategory(NotificationCategory category)
        {
            return UserCategories.Contains(category);
        }

        public static bool IsSystemCategory(NotificationCategory category)
        {
            return SystemCategories.Contains(category);
        }

        public static string NormalizeTemplate(string? template)
        {
            return string.IsNullOrWhiteSpace(template) ? "default" : template.Trim();
        }

        public static string NormalizePayload(JsonElement? payload)
        {
            if (!payload.HasValue)
            {
                return "{}";
            }

            return payload.Value.ValueKind == JsonValueKind.Undefined
                ? "{}"
                : payload.Value.GetRawText();
        }
    }

    public sealed class NotificationInboxItemDto
    {
        public long NotificationId { get; set; }
        public NotificationScope Scope { get; set; }
        public NotificationCategory Category { get; set; }
        public NotificationSeverity Severity { get; set; }
        public string Template { get; set; } = string.Empty;
        public string PayloadJson { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }
        public long Cursor { get; set; }
    }

    public sealed class NotificationInboxPageDto
    {
        public List<NotificationInboxItemDto> Items { get; set; } = new();
        public long? NextCursor { get; set; }
    }

    public sealed class NotificationUnreadCountDto
    {
        public long UnreadCount { get; set; }
    }

    public sealed class NotificationPreferenceRuleDto
    {
        public bool Enabled { get; set; }
        public NotificationChannel Channel { get; set; }
    }

    public sealed class NotificationPreferenceDto
    {
        public Dictionary<string, NotificationPreferenceRuleDto> Rules { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class NotificationBroadcastRequestDto
    {
        public string Category { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string Template { get; set; } = string.Empty;
        public JsonElement? Payload { get; set; }
        public string Target { get; set; } = "AllUsers";
    }

    public sealed class NotificationPublishRequest
    {
        public long UserId { get; set; }
        public NotificationCategory Category { get; set; }
        public NotificationSeverity Severity { get; set; } = NotificationSeverity.Info;
        public string Template { get; set; } = "default";
        public string PayloadJson { get; set; } = "{}";
        public string? DedupeKey { get; set; }
    }

    public sealed class NotificationPublishResult
    {
        public long NotificationId { get; set; }
        public bool Created { get; set; }
        public bool Skipped { get; set; }
        public string? Reason { get; set; }
    }

    public sealed class NotificationBroadcastResult
    {
        public long NotificationId { get; set; }
        public int RecipientCount { get; set; }
    }

    public sealed class NotificationPreferenceRule
    {
        public bool Enabled { get; set; }
        public NotificationChannel Channel { get; set; }
    }

    public sealed class NotificationPreferenceDecision
    {
        public bool Enabled { get; set; }
        public NotificationChannel Channel { get; set; }
        public string? Reason { get; set; }
    }

    public sealed class NotificationRenderResult
    {
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
    }
}
