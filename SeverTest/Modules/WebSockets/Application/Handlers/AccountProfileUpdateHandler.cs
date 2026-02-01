using Microsoft.Extensions.Logging;
using ServerTest.Modules.Accounts.Application;
using ServerTest.WebSockets.Contracts;
using ServerTest.Protocol;

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

        public async Task<ProtocolEnvelope<object>> HandleAsync(WebSocketConnection connection, ProtocolEnvelope<object> envelope, CancellationToken ct)
        {
            if (!ulong.TryParse(connection.UserId, out var uid))
            {
                return ProtocolEnvelopeFactory.Error(envelope.ReqId, ProtocolErrorCodes.Forbidden, "无效的用户ID");
            }

            var payload = ProtocolJson.DeserializePayload<AccountProfileUpdateRequest>(envelope.Data);
            if (payload == null || payload.Nickname == null || payload.Signature == null)
            {
                return ProtocolEnvelopeFactory.Error(envelope.ReqId, ProtocolErrorCodes.InvalidRequest, "昵称和签名不能为空");
            }

            var updated = await _accountService.UpdateProfileAsync(uid, payload.Nickname, null, payload.Signature, ct).ConfigureAwait(false);
            if (!updated)
            {
                _logger.LogWarning("用户 {UserId} 的资料更新失败", connection.UserId);
                return ProtocolEnvelopeFactory.Error(envelope.ReqId, ProtocolErrorCodes.NotFound, "用户不存在");
            }

            var responsePayload = new
            {
                success = true,
                nickname = payload.Nickname,
                signature = payload.Signature
            };

            return ProtocolEnvelopeFactory.Ok<object>("account.profile.updated", envelope.ReqId, responsePayload);
        }

    }
}
