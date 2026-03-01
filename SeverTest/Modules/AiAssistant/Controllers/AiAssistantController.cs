using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ServerTest.Modules.Accounts.Application;
using ServerTest.Modules.AiAssistant.Application;
using ServerTest.Modules.AiAssistant.Domain;
using ServerTest.Models;
using ServerTest.Protocol;

namespace ServerTest.Controllers
{
    [ApiController]
    [Route("api/ai-assistant")]
    public sealed class AiAssistantController : BaseController
    {
        private readonly AuthTokenService _tokenService;
        private readonly AiAssistantConversationService _conversationService;

        public AiAssistantController(
            ILogger<AiAssistantController> logger,
            AuthTokenService tokenService,
            AiAssistantConversationService conversationService)
            : base(logger)
        {
            _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
            _conversationService = conversationService ?? throw new ArgumentNullException(nameof(conversationService));
        }

        [ProtocolType("ai.assistant.conversation.list")]
        [HttpPost("conversations/list")]
        public async Task<IActionResult> ListConversations([FromBody] ProtocolRequest<AiAssistantConversationListRequest> request)
        {
            var uid = await GetAuthorizedUserIdAsync(request.ReqId).ConfigureAwait(false);
            if (!uid.HasValue)
            {
                var err = ProtocolEnvelopeFactory.Error(
                    request.ReqId,
                    ProtocolErrorCodes.Unauthorized,
                    "未授权，请重新登录",
                    null,
                    HttpContext.TraceIdentifier);
                return Unauthorized(err);
            }

            try
            {
                var payload = request.Data ?? new AiAssistantConversationListRequest();
                var items = await _conversationService
                    .ListConversationsAsync(uid.Value, payload.Limit, HttpContext.RequestAborted)
                    .ConfigureAwait(false);

                return Ok(ApiResponse<object>.Ok(new { items }, "查询成功"));
            }
            catch (Exception ex)
            {
                Logger.LogError(
                    ex,
                    "AI 会话列表查询失败: uid={Uid}, reqId={ReqId}",
                    uid.Value,
                    request.ReqId);
                return StatusCode(500, ApiResponse<object>.Error("会话列表查询失败，请稍后重试"));
            }
        }

        [ProtocolType("ai.assistant.conversation.create")]
        [HttpPost("conversations/create")]
        public async Task<IActionResult> CreateConversation([FromBody] ProtocolRequest<AiAssistantConversationCreateRequest> request)
        {
            var uid = await GetAuthorizedUserIdAsync(request.ReqId).ConfigureAwait(false);
            if (!uid.HasValue)
            {
                var err = ProtocolEnvelopeFactory.Error(
                    request.ReqId,
                    ProtocolErrorCodes.Unauthorized,
                    "未授权，请重新登录",
                    null,
                    HttpContext.TraceIdentifier);
                return Unauthorized(err);
            }

            try
            {
                var payload = request.Data;
                var conversation = await _conversationService
                    .CreateConversationAsync(uid.Value, payload?.Title, HttpContext.RequestAborted)
                    .ConfigureAwait(false);

                return Ok(ApiResponse<object>.Ok(conversation, "创建成功"));
            }
            catch (Exception ex)
            {
                Logger.LogError(
                    ex,
                    "AI 会话创建失败: uid={Uid}, reqId={ReqId}",
                    uid.Value,
                    request.ReqId);
                return StatusCode(500, ApiResponse<object>.Error("会话创建失败，请稍后重试"));
            }
        }

        [ProtocolType("ai.assistant.conversation.messages")]
        [HttpPost("conversations/messages")]
        public async Task<IActionResult> GetConversationMessages([FromBody] ProtocolRequest<AiAssistantConversationMessagesRequest> request)
        {
            var payload = request.Data;
            if (payload == null || payload.ConversationId <= 0)
            {
                return BadRequest(ApiResponse<object>.Error("会话ID不能为空"));
            }

            var uid = await GetAuthorizedUserIdAsync(request.ReqId).ConfigureAwait(false);
            if (!uid.HasValue)
            {
                var err = ProtocolEnvelopeFactory.Error(
                    request.ReqId,
                    ProtocolErrorCodes.Unauthorized,
                    "未授权，请重新登录",
                    null,
                    HttpContext.TraceIdentifier);
                return Unauthorized(err);
            }

            try
            {
                var result = await _conversationService
                    .GetConversationMessagesAsync(
                        uid.Value,
                        payload.ConversationId,
                        payload.Limit,
                        HttpContext.RequestAborted)
                    .ConfigureAwait(false);

                return Ok(ApiResponse<object>.Ok(result, "查询成功"));
            }
            catch (InvalidOperationException ex)
            {
                Logger.LogWarning(
                    "AI 会话消息查询失败: uid={Uid}, reqId={ReqId}, message={Message}",
                    uid.Value,
                    request.ReqId,
                    ex.Message);
                return BadRequest(ApiResponse<object>.Error(ex.Message));
            }
            catch (Exception ex)
            {
                Logger.LogError(
                    ex,
                    "AI 会话消息查询异常: uid={Uid}, reqId={ReqId}, conversationId={ConversationId}",
                    uid.Value,
                    request.ReqId,
                    payload.ConversationId);
                return StatusCode(500, ApiResponse<object>.Error("会话消息查询失败，请稍后重试"));
            }
        }

        [ProtocolType("ai.assistant.chat")]
        [HttpPost("chat")]
        public async Task<IActionResult> Chat([FromBody] ProtocolRequest<AiAssistantChatRequest> request)
        {
            var payload = request.Data;
            if (payload == null)
            {
                return BadRequest(ApiResponse<object>.Error("缺少请求数据"));
            }

            var message = payload.Message?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(message))
            {
                return BadRequest(ApiResponse<object>.Error("消息不能为空"));
            }

            var uid = await GetAuthorizedUserIdAsync(request.ReqId).ConfigureAwait(false);
            if (!uid.HasValue)
            {
                var err = ProtocolEnvelopeFactory.Error(
                    request.ReqId,
                    ProtocolErrorCodes.Unauthorized,
                    "未授权，请重新登录",
                    null,
                    HttpContext.TraceIdentifier);
                return Unauthorized(err);
            }

            try
            {
                var result = await _conversationService
                    .ChatAsync(uid.Value, payload.ConversationId, message, HttpContext.RequestAborted)
                    .ConfigureAwait(false);

                var response = new
                {
                    conversationId = result.ConversationId,
                    conversationTitle = result.ConversationTitle,
                    reply = result.Reply,
                    strategyConfig = result.StrategyConfig
                };

                return Ok(ApiResponse<object>.Ok(response, "生成成功"));
            }
            catch (InvalidOperationException ex)
            {
                Logger.LogWarning(
                    "AI 聊天请求失败: uid={Uid}, reqId={ReqId}, message={Message}",
                    uid.Value,
                    request.ReqId,
                    ex.Message);
                return BadRequest(ApiResponse<object>.Error(ex.Message));
            }
            catch (Exception ex)
            {
                Logger.LogError(
                    ex,
                    "AI 聊天请求异常: uid={Uid}, reqId={ReqId}",
                    uid.Value,
                    request.ReqId);
                return StatusCode(500, ApiResponse<object>.Error("AI 服务暂时不可用，请稍后重试"));
            }
        }

        private async Task<long?> GetAuthorizedUserIdAsync(string? reqId)
        {
            var token = GetBearerToken(Request.Headers.Authorization.ToString());
            var validation = await _tokenService.ValidateTokenAsync(token ?? string.Empty).ConfigureAwait(false);
            if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.UserId))
            {
                Logger.LogWarning(
                    "AI 接口鉴权失败: reqId={ReqId}",
                    reqId);
                return null;
            }

            return long.TryParse(validation.UserId, out var uid) ? uid : null;
        }

        private static string? GetBearerToken(string? authorizationHeader)
        {
            if (string.IsNullOrWhiteSpace(authorizationHeader))
            {
                return null;
            }

            const string prefix = "Bearer ";
            if (authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return authorizationHeader[prefix.Length..].Trim();
            }

            return null;
        }
    }
}
