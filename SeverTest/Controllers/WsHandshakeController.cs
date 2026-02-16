using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ServerTest.Models;
using ServerTest.Modules.Accounts.Application;
using ServerTest.Protocol;
using ServerTest.Services;

namespace ServerTest.Controllers
{
    /// <summary>
    /// WebSocket 预校验接口：在前端真正发起 WS 握手前，用于校验 token 等基础条件，
    /// 并以统一协议格式返回失败原因，便于前端弹窗展示。
    /// </summary>
    [ApiController]
    [Route("api/ws/handshake")]
    public sealed class WsHandshakeController : BaseController
    {
        private readonly AuthTokenService _tokenService;

        public WsHandshakeController(
            ILogger<WsHandshakeController> logger,
            AuthTokenService tokenService)
            : base(logger)
        {
            _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
        }

        /// <summary>
        /// 校验当前请求是否具备建立 WebSocket 连接的基础条件：
        /// - 是否携带有效的 access token
        /// - token 是否有效且未过期
        /// - token 中是否包含用户标识
        /// 
        /// 返回值使用统一的 ProtocolEnvelope 格式：
        /// - code == 0 表示通过校验
        /// - code != 0 表示失败，msg 为可读错误信息
        /// </summary>
        /// <remarks>
        /// 前端通过 HttpClient.postProtocol 调用本接口，失败时会拿到清晰的错误文案，
        /// 用于弹窗提示用户具体原因，而不是只能看到浏览器的 403 WebSocket 握手错误。
        /// </remarks>
        [HttpPost("check")]
        public async Task<IActionResult> CheckAsync([FromBody] ProtocolRequest<object?> request)
        {
            var traceId = HttpContext.TraceIdentifier;

            var token = GetToken(HttpContext);
            if (string.IsNullOrWhiteSpace(token))
            {
                var err = ProtocolEnvelopeFactory.Error(
                    request?.ReqId,
                    ProtocolErrorCodes.Unauthorized,
                    "未提供访问令牌，请重新登录后重试",
                    null,
                    traceId);
                return StatusCode(StatusCodes.Status401Unauthorized, err);
            }

            var validation = await _tokenService.ValidateTokenAsync(token).ConfigureAwait(false);
            if (!validation.IsValid)
            {
                var err = ProtocolEnvelopeFactory.Error(
                    request?.ReqId,
                    ProtocolErrorCodes.TokenInvalid,
                    "访问令牌无效或已过期，请重新登录",
                    null,
                    traceId);
                return StatusCode(StatusCodes.Status401Unauthorized, err);
            }

            if (string.IsNullOrWhiteSpace(validation.UserId))
            {
                var err = ProtocolEnvelopeFactory.Error(
                    request?.ReqId,
                    ProtocolErrorCodes.Forbidden,
                    "令牌中缺少用户标识，无法建立连接",
                    null,
                    traceId);
                return StatusCode(StatusCodes.Status403Forbidden, err);
            }

            var ok = ProtocolEnvelopeFactory.Ok(
                ProtocolEnvelopeFactory.BuildAckType("ws.handshake.check"),
                request?.ReqId,
                new
                {
                    userId = validation.UserId
                },
                "可以建立 WebSocket 连接",
                traceId);

            return Ok(ok);
        }

        private static string? GetToken(HttpContext context)
        {
            // 与 Program.cs 中 GetWebSocketToken 的逻辑保持一致：
            // 先看 access_token 查询参数，再看 Authorization: Bearer xxx 头。
            var token = context.Request.Query["access_token"].ToString();
            if (!string.IsNullOrWhiteSpace(token))
            {
                return token;
            }

            var authorization = context.Request.Headers.Authorization.ToString();
            const string prefix = "Bearer ";
            if (!string.IsNullOrWhiteSpace(authorization) &&
                authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return authorization[prefix.Length..].Trim();
            }

            return null;
        }
    }
}

