using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ServerTest.Application.Services;
using ServerTest.WebSockets.Contracts;

namespace ServerTest.WebSockets.Handlers
{
    public sealed class AccountProfileUpdateHandler : IWsMessageHandler
    {
        private readonly AccountService _accountService;
        private readonly ILogger<AccountProfileUpdateHandler> _logger;

        public AccountProfileUpdateHandler(AccountService accountService, ILogger<AccountProfileUpdateHandler> logger)
        {
            _accountService = accountService ?? throw new ArgumentNullException(nameof(accountService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string Type => "account.profile.update";

        public async Task<WsMessageEnvelope> HandleAsync(WebSocketConnection connection, WsMessageEnvelope envelope, CancellationToken ct)
        {
            if (!ulong.TryParse(connection.UserId, out var uid))
            {
                return WsMessageEnvelope.Error(envelope.ReqId, "forbidden", "Invalid user id");
            }

            var payload = ParsePayload<AccountProfileUpdateRequest>(envelope.Payload);
            if (payload == null || payload.Nickname == null || payload.Signature == null)
            {
                return WsMessageEnvelope.Error(envelope.ReqId, "bad_request", "nickname and signature are required");
            }

            var updated = await _accountService.UpdateProfileAsync(uid, payload.Nickname, null, payload.Signature, ct).ConfigureAwait(false);
            if (!updated)
            {
                _logger.LogWarning("用户 {UserId} 的 profile 更新失败", connection.UserId);
                return WsMessageEnvelope.Error(envelope.ReqId, "not_found", "Account not found");
            }

            var responsePayload = new
            {
                success = true,
                nickname = payload.Nickname,
                signature = payload.Signature
            };

            return WsMessageEnvelope.Create("account.profile.updated", envelope.ReqId, responsePayload, null);
        }

        private static T? ParsePayload<T>(object? payload)
        {
            if (payload == null)
            {
                return default;
            }

            if (payload is JObject obj)
            {
                return obj.ToObject<T>();
            }

            if (payload is JToken token)
            {
                return token.ToObject<T>();
            }

            return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(payload));
        }
    }
}
