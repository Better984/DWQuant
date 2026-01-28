using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using ServerTest.Models.Auth;
using ServerTest.Services;

namespace ServerTest.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly DatabaseService _db;
        private readonly JwtService _jwtService;
        private readonly AuthTokenService _tokenService;
        private readonly VerificationCodeService _verificationCodeService;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            DatabaseService db,
            JwtService jwtService,
            AuthTokenService tokenService,
            VerificationCodeService verificationCodeService,
            IEmailSender emailSender,
            ILogger<AuthController> logger)
        {
            _db = db;
            _jwtService = jwtService;
            _tokenService = tokenService;
            _verificationCodeService = verificationCodeService;
            _emailSender = emailSender;
            _logger = logger;
        }

        [AllowAnonymous]
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            try
            {
                _logger.LogInformation("开始注册流程：邮箱 = {Email}", request.Email ?? "null");

                // 验证输入
                if (string.IsNullOrWhiteSpace(request.Email))
                {
                    _logger.LogWarning("注册失败：邮箱为空");
                    return BadRequest(new { status = "error", message = "请输入邮箱" });
                }

                if (string.IsNullOrWhiteSpace(request.Password))
                {
                    _logger.LogWarning("注册失败：密码为空");
                    return BadRequest(new { status = "error", message = "请输入密码" });
                }

                // 验证密码强度
                if (request.Password.Length < 6)
                {
                    _logger.LogWarning("注册失败：密码长度不足 - 邮箱 = {Email}, 密码长度 = {Length}", request.Email, request.Password.Length);
                    return BadRequest(new { status = "error", message = "密码长度至少为6位" });
                }

                _logger.LogInformation("验证邮箱是否已存在：{Email}", request.Email);
                using var connection = await _db.GetConnectionAsync();

                var existsCmd = new MySqlCommand("SELECT 1 FROM account WHERE email = @email AND deleted_at IS NULL LIMIT 1", connection);
                existsCmd.Parameters.AddWithValue("@email", request.Email);
                var exists = await existsCmd.ExecuteScalarAsync();
                if (exists != null)
                {
                    _logger.LogWarning("注册失败：邮箱已被注册 - {Email}", request.Email);
                    return BadRequest(new { status = "error", message = "该邮箱已被注册" });
                }

                _logger.LogInformation("生成密码哈希：{Email}", request.Email);
                var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

                // 生成6位大写英文+数字的随机字符串
                var random = new Random();
                const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
                var randomCode = new string(Enumerable.Repeat(chars, 6)
                    .Select(s => s[random.Next(s.Length)]).ToArray());
                var nickname = "用户" + randomCode;
                const string defaultSignature = "Web3多维量化策略爱好者";

                _logger.LogInformation("生成昵称：{Nickname}, 默认签名：{Signature}", nickname, defaultSignature);

                _logger.LogInformation("插入新用户到数据库：{Email}", request.Email);
                var insertCmd = new MySqlCommand(@"
INSERT INTO account (email, password_hash, nickname, avatar_url, signature, status, role, register_at)
VALUES (@email, @password_hash, @nickname, @avatar_url, @signature, 0, 0, CURRENT_TIMESTAMP)
", connection);
                insertCmd.Parameters.AddWithValue("@email", request.Email);
                insertCmd.Parameters.AddWithValue("@password_hash", passwordHash);
                insertCmd.Parameters.AddWithValue("@nickname", nickname);
                insertCmd.Parameters.AddWithValue("@avatar_url", (object?)request.AvatarUrl ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@signature", defaultSignature);

                var rowsAffected = await insertCmd.ExecuteNonQueryAsync();
                _logger.LogInformation("插入用户完成，影响行数：{RowsAffected}", rowsAffected);

                _logger.LogInformation("查询新创建用户的 UID：{Email}", request.Email);
                var uidCmd = new MySqlCommand("SELECT uid FROM account WHERE email = @email LIMIT 1", connection);
                uidCmd.Parameters.AddWithValue("@email", request.Email);
                var uidResult = await uidCmd.ExecuteScalarAsync();
                var uid = uidResult != null ? Convert.ToString(uidResult) ?? string.Empty : string.Empty;

                if (string.IsNullOrEmpty(uid))
                {
                    _logger.LogError("注册失败：无法获取新创建用户的 UID - 邮箱 = {Email}", request.Email);
                    return StatusCode(500, new { status = "error", message = "注册失败，请稍后重试" });
                }

                _logger.LogInformation("生成 JWT Token：UID = {Uid}, Email = {Email}", uid, request.Email);
                var token = _jwtService.GenerateToken(uid, request.Email);
                await _tokenService.StoreTokenAsync(uid, token, TimeSpan.FromDays(30));

                _logger.LogInformation("生成验证码：{Email}", request.Email);
                var code = await _verificationCodeService.CreateAndStoreAsync(request.Email, TimeSpan.FromMinutes(10));

                _logger.LogInformation("发送验证码邮件：{Email}", request.Email);
                await _emailSender.SendAsync(request.Email, "注册验证码", $"您的验证码是: {code}");

                _logger.LogInformation("注册成功：UID = {Uid}, Email = {Email}", uid, request.Email);
                return Ok(new { status = "success", message = "注册成功", token });
            }
            catch (MySqlConnector.MySqlException mysqlEx)
            {
                _logger.LogError(mysqlEx, "注册过程中发生 MySQL 错误：邮箱 = {Email}, 错误代码 = {ErrorCode}, 错误消息 = {Message}",
                    request?.Email ?? "unknown", mysqlEx.ErrorCode, mysqlEx.Message);
                return StatusCode(500, new { status = "error", message = $"注册失败：{mysqlEx.Message}" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "注册过程中发生未知错误：邮箱 = {Email}, 错误类型 = {ExceptionType}, 错误消息 = {Message}",
                    request?.Email ?? "unknown", ex.GetType().Name, ex.Message);
                return StatusCode(500, new { status = "error", message = "注册失败，请稍后重试" });
            }
        }

        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
                {
                    _logger.LogWarning("登录请求缺少邮箱或密码");
                    return BadRequest(new { status = "error", message = "请输入邮箱和密码" });
                }

                _logger.LogInformation("尝试登录：邮箱 = {Email}", request.Email);

                using var connection = await _db.GetConnectionAsync();

                var cmd = new MySqlCommand(@"
SELECT uid, password_hash, status, role
FROM account
WHERE email = @email AND deleted_at IS NULL
LIMIT 1
", connection);
                cmd.Parameters.AddWithValue("@email", request.Email);

                using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    _logger.LogWarning("登录失败：用户不存在 - {Email}", request.Email);
                    return Unauthorized(new { status = "error", message = "邮箱或密码错误" });
                }

                // 修复：使用 GetInt32 而不是 GetUInt64（因为数据库是 int 类型）
                var uid = reader.GetInt32("uid");

                // 添加密码哈希空值检查
                var passwordHashValue = reader["password_hash"];
                if (passwordHashValue == DBNull.Value || passwordHashValue == null)
                {
                    _logger.LogWarning("登录失败：用户 {Email} 的密码哈希为 NULL", request.Email);
                    return Unauthorized(new { status = "error", message = "账户状态异常，请联系管理员" });
                }

                var passwordHash = reader.GetString("password_hash");
                if (string.IsNullOrWhiteSpace(passwordHash))
                {
                    _logger.LogWarning("登录失败：用户 {Email} 的密码哈希为空字符串", request.Email);
                    return Unauthorized(new { status = "error", message = "账户状态异常，请联系管理员" });
                }

                var status = reader.GetByte("status");
                if (status == 2)
                {
                    _logger.LogWarning("登录失败：用户 {Email} 的账户已被注销", request.Email);
                    return Unauthorized(new { status = "error", message = "账户已被注销" });
                }

                var passwordValid = BCrypt.Net.BCrypt.Verify(request.Password, passwordHash);
                if (!passwordValid)
                {
                    _logger.LogWarning("登录失败：用户 {Email} 的密码验证失败", request.Email);
                    return Unauthorized(new { status = "error", message = "邮箱或密码错误" });
                }

                _logger.LogInformation("登录成功：用户 {Email}", request.Email);

                var role = reader.IsDBNull(reader.GetOrdinal("role")) ? 0 : reader.GetInt32("role");

                var uidString = uid.ToString();
                var token = _jwtService.GenerateToken(uidString, request.Email);
                await _tokenService.StoreTokenAsync(uidString, token, TimeSpan.FromDays(30));

                await reader.CloseAsync();
                var updateLoginCmd = new MySqlCommand("UPDATE account SET last_login_at = CURRENT_TIMESTAMP WHERE uid = @uid", connection);
                updateLoginCmd.Parameters.AddWithValue("@uid", uid);
                await updateLoginCmd.ExecuteNonQueryAsync();

                return Ok(new { status = "success", message = "登录成功", token, role });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "登录过程中发生错误");
                return StatusCode(500, new { status = "error", message = "登录失败，请稍后重试" });
            }
        }

        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            try
            {
                // 验证新密码强度
                if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
                {
                    return BadRequest(new { status = "error", message = "新密码长度至少为6位" });
                }

                var token = GetBearerToken(Request.Headers.Authorization.ToString());
                var validation = await _tokenService.ValidateTokenAsync(token ?? string.Empty);
                if (!validation.IsValid)
                {
                    return Unauthorized(new { status = "error", message = "未授权，请重新登录" });
                }

                using var connection = await _db.GetConnectionAsync();
                var cmd = new MySqlCommand(@"
SELECT uid, password_hash
FROM account
WHERE email = @email AND deleted_at IS NULL
LIMIT 1
", connection);
                cmd.Parameters.AddWithValue("@email", request.Email);

                using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    return Unauthorized(new { status = "error", message = "账户不存在" });
                }

                // 修复：使用 GetInt32 而不是 GetUInt64
                var uid = reader.GetInt32("uid");

                // 添加密码哈希空值检查
                var passwordHashValue = reader["password_hash"];
                if (passwordHashValue == DBNull.Value || passwordHashValue == null)
                {
                    _logger.LogWarning("用户 {Email} 的密码哈希为空", request.Email);
                    return Unauthorized(new { status = "error", message = "账户状态异常，请联系管理员" });
                }

                var passwordHash = reader.GetString("password_hash");
                if (string.IsNullOrWhiteSpace(passwordHash))
                {
                    _logger.LogWarning("用户 {Email} 的密码哈希为空字符串", request.Email);
                    return Unauthorized(new { status = "error", message = "账户状态异常，请联系管理员" });
                }

                var uidString = uid.ToString();
                if (!string.Equals(uidString, validation.UserId, StringComparison.Ordinal))
                {
                    return Unauthorized(new { status = "error", message = "无权修改此账户" });
                }

                if (!BCrypt.Net.BCrypt.Verify(request.OldPassword, passwordHash))
                {
                    return Unauthorized(new { status = "error", message = "原密码错误" });
                }

                await reader.CloseAsync();

                var newHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
                var updateCmd = new MySqlCommand(@"
UPDATE account
SET password_hash = @password_hash, password_updated_at = CURRENT_TIMESTAMP
WHERE uid = @uid
", connection);
                updateCmd.Parameters.AddWithValue("@password_hash", newHash);
                updateCmd.Parameters.AddWithValue("@uid", uid);
                await updateCmd.ExecuteNonQueryAsync();

                return Ok(new { status = "success", message = "密码修改成功" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "修改密码过程中发生错误");
                return StatusCode(500, new { status = "error", message = "修改密码失败，请稍后重试" });
            }
        }

        [HttpPost("delete-account")]
        public async Task<IActionResult> DeleteAccount([FromBody] DeleteAccountRequest request)
        {
            try
            {
                var token = GetBearerToken(Request.Headers.Authorization.ToString());
                var validation = await _tokenService.ValidateTokenAsync(token ?? string.Empty);
                if (!validation.IsValid)
                {
                    return Unauthorized(new { status = "error", message = "未授权，请重新登录" });
                }

                using var connection = await _db.GetConnectionAsync();
                var cmd = new MySqlCommand(@"
SELECT uid, password_hash
FROM account
WHERE email = @email AND deleted_at IS NULL
LIMIT 1
", connection);
                cmd.Parameters.AddWithValue("@email", request.Email);

                using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    return Unauthorized(new { status = "error", message = "账户不存在" });
                }

                // 修复：使用 GetInt32 而不是 GetUInt64
                var uid = reader.GetInt32("uid");

                // 添加密码哈希空值检查
                var passwordHashValue = reader["password_hash"];
                if (passwordHashValue == DBNull.Value || passwordHashValue == null)
                {
                    _logger.LogWarning("用户 {Email} 的密码哈希为空", request.Email);
                    return Unauthorized(new { status = "error", message = "账户状态异常，请联系管理员" });
                }

                var passwordHash = reader.GetString("password_hash");
                if (string.IsNullOrWhiteSpace(passwordHash))
                {
                    _logger.LogWarning("用户 {Email} 的密码哈希为空字符串", request.Email);
                    return Unauthorized(new { status = "error", message = "账户状态异常，请联系管理员" });
                }

                var uidString = uid.ToString();
                if (!string.Equals(uidString, validation.UserId, StringComparison.Ordinal))
                {
                    return Unauthorized(new { status = "error", message = "无权注销此账户" });
                }

                if (!BCrypt.Net.BCrypt.Verify(request.Password, passwordHash))
                {
                    return Unauthorized(new { status = "error", message = "密码错误" });
                }

                await reader.CloseAsync();

                var updateCmd = new MySqlCommand(@"
UPDATE account
SET status = 2, deleted_at = CURRENT_TIMESTAMP
WHERE uid = @uid
", connection);
                updateCmd.Parameters.AddWithValue("@uid", uid);
                await updateCmd.ExecuteNonQueryAsync();

                await _tokenService.RemoveTokenAsync(token!);

                return Ok(new { status = "success", message = "账号注销成功" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "注销账户过程中发生错误");
                return StatusCode(500, new { status = "error", message = "注销失败，请稍后重试" });
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
