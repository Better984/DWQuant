using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using ServerTest.Modules.Accounts.Infrastructure;
using ServerTest.Modules.Accounts.Application;
using ServerTest.Models;
using ServerTest.Models.Auth;
using ServerTest.Services;
using ServerTest.Protocol;

namespace ServerTest.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AccountRepository _accountRepository;
        private readonly JwtService _jwtService;
        private readonly AuthTokenService _tokenService;
        private readonly AccountSessionKickService _sessionKickService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            AccountRepository accountRepository,
            JwtService jwtService,
            AuthTokenService tokenService,
            AccountSessionKickService sessionKickService,
            ILogger<AuthController> logger)
        {
            _accountRepository = accountRepository ?? throw new ArgumentNullException(nameof(accountRepository));
            _jwtService = jwtService;
            _tokenService = tokenService;
            _sessionKickService = sessionKickService ?? throw new ArgumentNullException(nameof(sessionKickService));
            _logger = logger;
        }

        [AllowAnonymous]
        [ProtocolType("auth.register")]
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] ProtocolRequest<RegisterRequest> request)
        {
            try
            {
                var payload = request.Data;
                if (payload == null)
                {
                    return BadRequest(ApiResponse<object>.Error("缺少请求数据"));
                }

                _logger.LogInformation("开始注册流程：邮箱 = {Email}", payload.Email ?? "null");

                // 验证输入
                if (string.IsNullOrWhiteSpace(payload.Email))
                {
                    _logger.LogWarning("注册失败：邮箱为空");
                    return BadRequest(ApiResponse<object>.Error("请输入邮箱"));
                }

                if (string.IsNullOrWhiteSpace(payload.Password))
                {
                    _logger.LogWarning("注册失败：密码为空");
                    return BadRequest(ApiResponse<object>.Error("请输入密码"));
                }

                // 验证密码强度
                if (payload.Password.Length < 6)
                {
                    _logger.LogWarning("注册失败：密码长度不足 - 邮箱 = {Email}, 密码长度 = {Length}", payload.Email, payload.Password.Length);
                    return BadRequest(ApiResponse<object>.Error("密码长度至少为6位"));
                }

                _logger.LogInformation("验证邮箱是否已存在：{Email}", payload.Email);
                var existing = await _accountRepository.GetByEmailAsync(payload.Email).ConfigureAwait(false);
                if (existing != null)
                {
                    _logger.LogWarning("注册失败：邮箱已被注册 - {Email}", payload.Email);
                    return BadRequest(ApiResponse<object>.Error("该邮箱已被注册"));
                }

                _logger.LogInformation("生成密码哈希：{Email}", payload.Email);
                var passwordHash = BCrypt.Net.BCrypt.HashPassword(payload.Password);

                // 生成6位大写英文+数字的随机字符串
                var random = new Random();
                const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
                var randomCode = new string(Enumerable.Repeat(chars, 6)
                    .Select(s => s[random.Next(s.Length)]).ToArray());
                var nickname = "用户" + randomCode;
                const string defaultSignature = "Web3多维量化策略爱好者";

                _logger.LogInformation("生成昵称：{Nickname}, 默认签名：{Signature}", nickname, defaultSignature);

                _logger.LogInformation("插入新用户到数据库：{Email}", payload.Email);
                var uidValue = await _accountRepository.CreateAccountAsync(
                    payload.Email,
                    passwordHash,
                    nickname,
                    payload.AvatarUrl,
                    defaultSignature).ConfigureAwait(false);
                _logger.LogInformation("插入用户完成，UID={Uid}", uidValue);

                if (uidValue <= 0)
                {
                    _logger.LogError("注册失败：无法获取新创建用户的 UID - 邮箱 = {Email}", payload.Email);
                    return StatusCode(500, ApiResponse<object>.Error("注册失败，请稍后重试"));
                }

                var uid = uidValue.ToString();
                _logger.LogInformation("生成 JWT Token：UID = {Uid}, Email = {Email}", uid, payload.Email);
                var token = _jwtService.GenerateToken(uid, payload.Email);
                await _tokenService.StoreTokenAsync(uid, token, TimeSpan.FromDays(30));

                _logger.LogInformation("注册成功：UID = {Uid}, Email = {Email}", uid, payload.Email);
                return Ok(ApiResponse<object>.Ok(new { token }, "注册成功"));
            }
            catch (MySqlConnector.MySqlException mysqlEx)
            {
                _logger.LogError(mysqlEx, "注册过程中发生 MySQL 错误：邮箱 = {Email}, 错误代码 = {ErrorCode}, 错误消息 = {Message}",
                    request?.Data?.Email ?? "unknown", mysqlEx.ErrorCode, mysqlEx.Message);
                return StatusCode(500, ApiResponse<object>.Error($"注册失败：{mysqlEx.Message}"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "注册过程中发生未知错误：邮箱 = {Email}, 错误类型 = {ExceptionType}, 错误消息 = {Message}",
                    request?.Data?.Email ?? "unknown", ex.GetType().Name, ex.Message);
                return StatusCode(500, ApiResponse<object>.Error("注册失败，请稍后重试"));
            }
        }

        [AllowAnonymous]
        [ProtocolType("auth.login")]
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] ProtocolRequest<LoginRequest> request)
        {
            try
            {
                var payload = request.Data;
                if (payload == null)
                {
                    return BadRequest(ApiResponse<object>.Error("缺少请求数据"));
                }

                if (string.IsNullOrWhiteSpace(payload.Email) || string.IsNullOrWhiteSpace(payload.Password))
                {
                    _logger.LogWarning("登录请求缺少邮箱或密码");
                    return BadRequest(ApiResponse<object>.Error("请输入邮箱和密码"));
                }

                _logger.LogInformation("尝试登录：邮箱 = {Email}", payload.Email);
                var account = await _accountRepository.GetByEmailAsync(payload.Email).ConfigureAwait(false);
                if (account == null)
                {
                    _logger.LogWarning("登录失败：用户不存在 - {Email}", payload.Email);
                    return Unauthorized(ApiResponse<object>.Error("邮箱或密码错误"));
                }

                var passwordHash = account.PasswordHash;
                if (string.IsNullOrWhiteSpace(passwordHash))
                {
                    _logger.LogWarning("登录失败：用户 {Email} 的密码哈希为空", payload.Email);
                    return Unauthorized(ApiResponse<object>.Error("账户状态异常，请联系管理员"));
                }

                if (account.Status == 2)
                {
                    _logger.LogWarning("登录失败：用户 {Email} 的账户已被注销", payload.Email);
                    return Unauthorized(ApiResponse<object>.Error("账户已被注销"));
                }

                var passwordValid = BCrypt.Net.BCrypt.Verify(payload.Password, passwordHash);
                if (!passwordValid)
                {
                    _logger.LogWarning("登录失败：用户 {Email} 的密码验证失败", payload.Email);
                    return Unauthorized(ApiResponse<object>.Error("邮箱或密码错误"));
                }

                _logger.LogInformation("登录成功：用户 {Email}", payload.Email);

                var role = account.Role;
                var uidString = account.Uid.ToString();
                var loginSystem = AuthTokenService.NormalizeSystem(payload.System);
                var token = _jwtService.GenerateToken(uidString, payload.Email);
                var tokenStoreResult = await _tokenService.StoreTokenAsync(uidString, loginSystem, token, TimeSpan.FromDays(30))
                    .ConfigureAwait(false);
                var kickedLocalCount = await _sessionKickService
                    .KickByUserSystemAsync(uidString, loginSystem, HttpContext.RequestAborted)
                    .ConfigureAwait(false);
                var kickedOtherSession = tokenStoreResult.ReplacedExisting || kickedLocalCount > 0;

                await _accountRepository.UpdateLastLoginAsync(account.Uid).ConfigureAwait(false);

                return Ok(ApiResponse<object>.Ok(new
                {
                    token,
                    role,
                    system = loginSystem,
                    kickedOtherSession
                }, kickedOtherSession ? "登录成功，另一台同类型设备已下线" : "登录成功"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "登录过程中发生错误");
                return StatusCode(500, ApiResponse<object>.Error("登录失败，请稍后重试"));
            }
        }

        [ProtocolType("auth.password.change")]
        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ProtocolRequest<ChangePasswordRequest> request)
        {
            try
            {
                var payload = request.Data;
                if (payload == null)
                {
                    return BadRequest(ApiResponse<object>.Error("缺少请求数据"));
                }

                // 验证新密码强度
                if (string.IsNullOrWhiteSpace(payload.NewPassword) || payload.NewPassword.Length < 6)
                {
                    return BadRequest(ApiResponse<object>.Error("新密码长度至少为6位"));
                }

                var token = GetBearerToken(Request.Headers.Authorization.ToString());
                var validation = await _tokenService.ValidateTokenAsync(token ?? string.Empty);
                if (!validation.IsValid)
                {
                    return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
                }

                var account = await _accountRepository.GetByEmailAsync(payload.Email).ConfigureAwait(false);
                if (account == null)
                {
                    return Unauthorized(ApiResponse<object>.Error("账户不存在"));
                }

                var uid = account.Uid;
                var passwordHash = account.PasswordHash;
                if (string.IsNullOrWhiteSpace(passwordHash))
                {
                    _logger.LogWarning("用户 {Email} 的密码哈希为空", payload.Email);
                    return Unauthorized(ApiResponse<object>.Error("账户状态异常，请联系管理员"));
                }

                var uidString = uid.ToString();
                if (!string.Equals(uidString, validation.UserId, StringComparison.Ordinal))
                {
                    return Unauthorized(ApiResponse<object>.Error("无权修改此账户"));
                }

                if (!BCrypt.Net.BCrypt.Verify(payload.OldPassword, passwordHash))
                {
                    return Unauthorized(ApiResponse<object>.Error("原密码错误"));
                }

                var newHash = BCrypt.Net.BCrypt.HashPassword(payload.NewPassword);
                await _accountRepository.ChangePasswordAsync(uid, newHash).ConfigureAwait(false);

                return Ok(ApiResponse<object>.Ok(null, "密码修改成功"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "修改密码过程中发生错误");
                return StatusCode(500, ApiResponse<object>.Error("修改密码失败，请稍后重试"));
            }
        }

        [ProtocolType("auth.account.delete")]
        [HttpPost("delete-account")]
        public async Task<IActionResult> DeleteAccount([FromBody] ProtocolRequest<DeleteAccountRequest> request)
        {
            try
            {
                var payload = request.Data;
                if (payload == null)
                {
                    return BadRequest(ApiResponse<object>.Error("缺少请求数据"));
                }

                var token = GetBearerToken(Request.Headers.Authorization.ToString());
                var validation = await _tokenService.ValidateTokenAsync(token ?? string.Empty);
                if (!validation.IsValid)
                {
                    return Unauthorized(ApiResponse<object>.Error("未授权，请重新登录"));
                }

                var account = await _accountRepository.GetByEmailAsync(payload.Email).ConfigureAwait(false);
                if (account == null)
                {
                    return Unauthorized(ApiResponse<object>.Error("账户不存在"));
                }

                var uid = account.Uid;
                var passwordHash = account.PasswordHash;
                if (string.IsNullOrWhiteSpace(passwordHash))
                {
                    _logger.LogWarning("用户 {Email} 的密码哈希为空", payload.Email);
                    return Unauthorized(ApiResponse<object>.Error("账户状态异常，请联系管理员"));
                }

                var uidString = uid.ToString();
                if (!string.Equals(uidString, validation.UserId, StringComparison.Ordinal))
                {
                    return Unauthorized(ApiResponse<object>.Error("无权注销此账户"));
                }

                if (!BCrypt.Net.BCrypt.Verify(payload.Password, passwordHash))
                {
                    return Unauthorized(ApiResponse<object>.Error("密码错误"));
                }

                await _accountRepository.MarkDeletedAsync(uid).ConfigureAwait(false);

                await _tokenService.RemoveTokenAsync(token!);

                return Ok(ApiResponse<object>.Ok(null, "账号注销成功"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "注销账户过程中发生错误");
                return StatusCode(500, ApiResponse<object>.Error("注销失败，请稍后重试"));
            }
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
                return authorizationHeader.Substring(prefix.Length).Trim();
            }

            return null;
        }
    }
}
