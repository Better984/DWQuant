using System.ComponentModel.DataAnnotations;

namespace ServerTest.Models.Auth
{
    public class RegisterRequest
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = "";

        [Required]
        public string Password { get; set; } = "";

        public string? Nickname { get; set; }
        public string? AvatarUrl { get; set; }
    }

    public class LoginRequest
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = "";

        [Required]
        public string Password { get; set; } = "";

        /// <summary>
        /// 登录终端类型（如 web / pc / game），用于同类型会话互斥。
        /// </summary>
        public string? System { get; set; }
    }

    public class ChangePasswordRequest
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = "";

        [Required]
        public string OldPassword { get; set; } = "";

        [Required]
        public string NewPassword { get; set; } = "";
    }

    public class DeleteAccountRequest
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = "";

        [Required]
        public string Password { get; set; } = "";
    }
}
