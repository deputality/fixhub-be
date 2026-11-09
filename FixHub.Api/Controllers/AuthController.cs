using FixHub.Api.Data;
using FixHub.Api.DTOs;
using FixHub.Api.Models;
using FixHub.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FixHub.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<User> _hasher;
    private readonly ITokenService _tokens;

    public AuthController(AppDbContext db, IPasswordHasher<User> hasher, ITokenService tokens)
    {
        _db = db;
        _hasher = hasher;
        _tokens = tokens;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        if (request.Role != UserRole.Client && request.Role != UserRole.Performer)
            return BadRequest(new { error = "Регистрация доступна только для ролей Client и Performer" });

        var email = request.Email.Trim().ToLowerInvariant();

        if (await _db.Users.AnyAsync(u => u.Email == email))
            return Conflict(new { error = "Пользователь с таким email уже существует" });

        var user = new User
        {
            Email = email,
            FullName = request.FullName.Trim(),
            Phone = request.Phone?.Trim(),
            Role = request.Role
        };
        user.PasswordHash = _hasher.HashPassword(user, request.Password);

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return Ok(BuildResponse(user));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user == null)
            return Unauthorized(new { error = "Неверный email или пароль" });

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
            return Unauthorized(new { error = "Неверный email или пароль" });

        if (user.IsBlocked)
            return StatusCode(StatusCodes.Status403Forbidden,
                new { error = "Учётная запись заблокирована" });

        return Ok(BuildResponse(user));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me()
    {
        var idClaim = User.FindFirst("sub")?.Value;
        if (!int.TryParse(idClaim, out var userId))
            return Unauthorized();

        var user = await _db.Users.FindAsync(userId);
        if (user == null || user.IsBlocked)
            return Unauthorized();

        return Ok(ToDto(user));
    }

    private AuthResponse BuildResponse(User user)
    {
        var (token, expiresAt) = _tokens.CreateToken(user);
        return new AuthResponse
        {
            Token = token,
            ExpiresAt = expiresAt,
            User = ToDto(user)
        };
    }

    private static UserDto ToDto(User user) => new()
    {
        Id = user.Id,
        Email = user.Email,
        FullName = user.FullName,
        Phone = user.Phone,
        Role = user.Role.ToString()
    };
}