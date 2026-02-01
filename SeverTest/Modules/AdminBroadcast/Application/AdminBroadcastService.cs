using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerTest.Modules.Accounts.Infrastructure;
using ServerTest.Modules.Notifications.Application;
using ServerTest.Modules.Notifications.Domain;
using ServerTest.Options;

namespace ServerTest.Modules.AdminBroadcast.Application
{
    /// <summary>
    /// ??????
    /// </summary>
    public sealed class AdminBroadcastService
    {
        private readonly AccountRepository _accountRepository;
        private readonly INotificationPublisher _publisher;
        private readonly BusinessRulesOptions _businessRules;
        private readonly ILogger<AdminBroadcastService> _logger;

        public AdminBroadcastService(
            AccountRepository accountRepository,
            INotificationPublisher publisher,
            IOptions<BusinessRulesOptions> businessRules,
            ILogger<AdminBroadcastService> logger)
        {
            _accountRepository = accountRepository ?? throw new ArgumentNullException(nameof(accountRepository));
            _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
            _businessRules = businessRules?.Value ?? new BusinessRulesOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<(bool Success, string Error, NotificationBroadcastResult? Result)> BroadcastAsync(
            long uid,
            NotificationBroadcastRequestDto request,
            CancellationToken ct = default)
        {
            if (request == null)
            {
                return (false, "Invalid request", null);
            }

            var role = await _accountRepository.GetRoleAsync((ulong)uid, null, ct).ConfigureAwait(false);
            if (!role.HasValue || role.Value != _businessRules.SuperAdminRole)
            {
                return (false, "Forbidden", null);
            }

            if (!NotificationContractHelper.TryParseCategory(request.Category, out var category)
                || !NotificationContractHelper.IsSystemCategory(category))
            {
                return (false, "Invalid category", null);
            }

            if (!NotificationContractHelper.TryParseSeverity(request.Severity, out var severity))
            {
                return (false, "Invalid severity", null);
            }

            var template = NotificationContractHelper.NormalizeTemplate(request.Template);
            var payload = NotificationContractHelper.NormalizePayload(request.Payload);

            var result = await _publisher.PublishSystemBroadcastAsync(category, severity, template, payload, ct)
                .ConfigureAwait(false);

            _logger.LogInformation("???????: uid={Uid} category={Category} severity={Severity}", uid, category, severity);
            return (true, string.Empty, result);
        }
    }
}
