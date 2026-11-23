using FixHub.Api.Data;
using FixHub.Api.DTOs;
using FixHub.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FixHub.Api.Controllers;

[ApiController]
[Route("api/profile")]
[Authorize]
public class ProfileController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<User> _hasher;

    public ProfileController(AppDbContext db, IPasswordHasher<User> hasher)
    {
        _db = db;
        _hasher = hasher;
    }

    private int CurrentUserId => int.Parse(User.FindFirst("sub")!.Value);

    [HttpGet]
    public async Task<ActionResult<ProfileDto>> Get()
    {
        var user = await _db.Users.FindAsync(CurrentUserId);
        if (user == null) return Unauthorized();
        return Ok(ToDto(user));
    }

    [HttpPut]
    public async Task<ActionResult<ProfileDto>> Update(UpdateProfileRequest request)
    {
        var user = await _db.Users.FindAsync(CurrentUserId);
        if (user == null) return Unauthorized();

        user.FullName = request.FullName.Trim();
        user.Phone = request.Phone?.Trim();
        user.Bio = request.Bio?.Trim();

        if (user.Role == UserRole.Performer)
        {
            // аватар принимаем только с нашего хранилища, а не произвольную ссылку
            var avatar = request.AvatarUrl?.Trim();
            if (!string.IsNullOrEmpty(avatar) && !avatar.StartsWith("/uploads/"))
                return BadRequest(new { error = "Некорректный адрес фото" });

            user.Specialization = request.Specialization?.Trim();
            user.ExperienceYears = request.ExperienceYears;
            user.City = request.City?.Trim();
            user.ServiceArea = request.ServiceArea?.Trim();
            user.AvatarUrl = string.IsNullOrEmpty(avatar) ? null : avatar;
        }

        await _db.SaveChangesAsync();
        return Ok(ToDto(user));
    }

    // Включить или выключить письма об уведомлениях
    [HttpPut("notifications")]
    public async Task<ActionResult<ProfileDto>> UpdateNotifications(NotificationSettingsRequest request)
    {
        var user = await _db.Users.FindAsync(CurrentUserId);
        if (user == null) return Unauthorized();

        user.EmailNotifications = request.EmailNotifications;
        await _db.SaveChangesAsync();
        return Ok(ToDto(user));
    }

    [HttpPost("password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var user = await _db.Users.FindAsync(CurrentUserId);
        if (user == null) return Unauthorized();

        var check = _hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword);
        if (check == PasswordVerificationResult.Failed)
            return BadRequest(new { error = "Текущий пароль указан неверно" });

        user.PasswordHash = _hasher.HashPassword(user, request.NewPassword);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Пароль изменён" });
    }

    private static ProfileDto ToDto(User u) => new()
    {
        Id = u.Id,
        Email = u.Email,
        FullName = u.FullName,
        Phone = u.Phone,
        Bio = u.Bio,
        Role = u.Role.ToString(),
        Specialization = u.Specialization,
        ExperienceYears = u.ExperienceYears,
        City = u.City,
        ServiceArea = u.ServiceArea,
        AvatarUrl = u.AvatarUrl,
        IsVerified = u.IsVerified,
        EmailNotifications = u.EmailNotifications
    };
}