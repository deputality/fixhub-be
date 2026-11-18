using System.ComponentModel.DataAnnotations;
using FixHub.Api.Models;

namespace FixHub.Api.DTOs;

public class RegisterRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = null!;

    [Required, MinLength(8), MaxLength(100)]
    public string Password { get; set; } = null!;

    [Required, MaxLength(200)]
    public string FullName { get; set; } = null!;

    [MaxLength(30)]
    public string? Phone { get; set; }

    [Required]
    public UserRole Role { get; set; }
}

public class LoginRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = null!;

    [Required]
    public string Password { get; set; } = null!;
}

public class GoogleLoginRequest
{
    // access token, полученный на фронте через Google Identity Services
    [Required]
    public string AccessToken { get; set; } = null!;

    // роль нужна только при первой регистрации; если не передана, будет Client
    public UserRole? Role { get; set; }
}

public class UserDto
{
    public int Id { get; set; }
    public string Email { get; set; } = null!;
    public string FullName { get; set; } = null!;
    public string? Phone { get; set; }
    public string Role { get; set; } = null!;
}

public class AuthResponse
{
    public string Token { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public UserDto User { get; set; } = null!;
}