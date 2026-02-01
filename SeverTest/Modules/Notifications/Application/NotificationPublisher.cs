using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Infrastructure.Db;
using ServerTest.Modules.Notifications.Domain;
using ServerTest.Modules.Notifications.Infrastructure;
using ServerTest.Options;

namespace ServerTest.Modules.Notifications.Application
{
    public sealed class NotificationPublisher : INotificationPublisher
    {
        private readonly IDbManager _db;
        private readonly NotificationRepository _repository;
        private readonly INotificationPreferenceResolver _preferenceResolver;
        private readonly NotificationOptions _options;
        private readonly ILogger<NotificationPublisher> _logger;

        public NotificationPublisher(
            IDbManager db,
            NotificationRepository repository,
            INotificationPreferenceResolver preferenceResolver,
            IOptions<NotificationOptions> options,
            ILogger<NotificationPublisher> logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _preferenceResolver = preferenceResolver ?? throw new ArgumentNullException(nameof(preferenceResolver));
            _options = options?.Value ?? new NotificationOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<NotificationPublishResult> PublishToUserAsync(NotificationPublishRequest request, CancellationToken ct = default)
        {
            if (request == null || request.UserId <= 0)
            {
                return new NotificationPublishResult { Skipped = true, Reason = "invalid_request" };
            }

            if (!NotificationContractHelper.IsUserCategory(request.Category))
            {
                return new NotificationPublishResult { Skipped = true, Reason = "invalid_category" };
            }

            if (!string.IsNullOrWhiteSpace(request.DedupeKey))
            {
                var existing = await _repository.FindExistingNotificationIdAsync(request.UserId, request.DedupeKey!, ct).ConfigureAwait(false);
                if (existing.HasValue)
                {
                    return new NotificationPublishResult
                    {
                        NotificationId = existing.Value,
                        Created = false,
                        Skipped = true,
                        Reason = "dedupe"
                    };
                }
            }

            var preference = await _preferenceResolver.ResolveAsync(request.UserId, request.Category, ct).ConfigureAwait(false);
            if (!preference.Enabled)
            {
                return new NotificationPublishResult { Skipped = true, Reason = preference.Reason ?? "disabled" };
            }

            var template = NotificationContractHelper.NormalizeTemplate(request.Template);
            var payload = string.IsNullOrWhiteSpace(request.PayloadJson) ? "{}" : request.PayloadJson;

            await using var uow = await _db.BeginUnitOfWorkAsync(ct).ConfigureAwait(false);
            try
            {
                var notificationId = await _repository.InsertNotificationAsync(new NotificationRecord
                {
                    Scope = NotificationScope.User,
                    Category = request.Category,
                    Severity = request.Severity,
                    Template = template,
                    PayloadJson = payload,
                    DedupeKey = request.DedupeKey
                }, uow, ct).ConfigureAwait(false);

                await _repository.InsertNotificationUserAsync(notificationId, request.UserId, uow, ct).ConfigureAwait(false);

                if (preference.Channel != NotificationChannel.InApp)
                {
                    await _repository.InsertDeliveryAsync(notificationId, request.UserId, preference.Channel, uow, ct).ConfigureAwait(false);
                }

                await uow.CommitAsync(ct).ConfigureAwait(false);

                return new NotificationPublishResult
                {
                    NotificationId = notificationId,
                    Created = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Publish user notification failed: uid={Uid} category={Category}", request.UserId, request.Category);
                throw;
            }
        }

        public async Task<NotificationBroadcastResult> PublishSystemBroadcastAsync(
            NotificationCategory category,
            NotificationSeverity severity,
            string template,
            string payloadJson,
            CancellationToken ct = default)
        {
            if (!NotificationContractHelper.IsSystemCategory(category))
            {
                throw new InvalidOperationException("Invalid system broadcast category.");
            }

            var normalizedTemplate = NotificationContractHelper.NormalizeTemplate(template);
            var normalizedPayload = string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson;

            long notificationId;
            await using (var uow = await _db.BeginUnitOfWorkAsync(ct).ConfigureAwait(false))
            {
                notificationId = await _repository.InsertNotificationAsync(new NotificationRecord
                {
                    Scope = NotificationScope.System,
                    Category = category,
                    Severity = severity,
                    Template = normalizedTemplate,
                    PayloadJson = normalizedPayload
                }, uow, ct).ConfigureAwait(false);

                await uow.CommitAsync(ct).ConfigureAwait(false);
            }

            var total = await _repository.InsertSystemBroadcastAsync(notificationId, _options.EnableSystemBroadcastExternal, ct).ConfigureAwait(false);
            return new NotificationBroadcastResult
            {
                NotificationId = notificationId,
                RecipientCount = total
            };
        }
    }
}
