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
[Route("api/profile")]
[Authorize]
public class ProfileController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<User> _hasher;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ProfileController> _logger;

    public ProfileController(
        AppDbContext db,
        IPasswordHasher<User> hasher,
        IWebHostEnvironment env,
        ILogger<ProfileController> logger)
    {
        _db = db;
        _hasher = hasher;
        _env = env;
        _logger = logger;
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

            if ((request.Latitude == null) != (request.Longitude == null))
                return BadRequest(new { error = "Укажите и широту, и долготу" });

            user.Specialization = request.Specialization?.Trim();
            user.ExperienceYears = request.ExperienceYears;
            user.City = request.City?.Trim();
            user.ServiceArea = request.ServiceArea?.Trim();
            user.Latitude = GeoMath.RoundCoordinate(request.Latitude);
            user.Longitude = GeoMath.RoundCoordinate(request.Longitude);
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

    // Удаление аккаунта. Заказы, отзывы и история связаны с другими людьми, поэтому строку пользователя
    // не стираем, а обезличиваем: имя, почта, телефон, фото и документы удаляются, вход закрывается.
    // Освободившийся email можно использовать для новой регистрации.
    [HttpPost("delete")]
    public async Task<IActionResult> DeleteAccount(DeleteAccountRequest request)
    {
        var user = await _db.Users.FindAsync(CurrentUserId);
        if (user == null) return Unauthorized();

        if (user.Role == UserRole.Admin)
            return StatusCode(StatusCodes.Status403Forbidden,
                new { error = "Учётную запись администратора удалить нельзя" });

        var check = _hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (check == PasswordVerificationResult.Failed)
            return BadRequest(new { error = "Пароль указан неверно" });

        var hasActiveOrders = await _db.Orders.AnyAsync(o =>
            (o.ClientId == user.Id || o.PerformerId == user.Id)
            && (o.Status == OrderStatus.New || o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.InProgress));
        if (hasActiveOrders)
            return Conflict(new { error = "Есть незавершённые заказы. Завершите или отмените их, затем удалите аккаунт" });

        var userId = user.Id;

        // файлы документов лежат в закрытой папке, их стираем и с диска
        var documents = await _db.VerificationDocuments.Where(d => d.UserId == userId).ToListAsync();
        var privateDir = Path.Combine(_env.ContentRootPath, "private-uploads");
        foreach (var document in documents)
        {
            try
            {
                var path = Path.Combine(privateDir, document.StoredName);
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Не удалось удалить файл документа {DocumentId}", document.Id);
            }
        }
        _db.VerificationDocuments.RemoveRange(documents);

        _db.CartItems.RemoveRange(_db.CartItems.Where(c => c.ClientId == userId));
        _db.FavoriteServices.RemoveRange(_db.FavoriteServices.Where(f => f.UserId == userId));
        _db.FavoritePerformers.RemoveRange(_db.FavoritePerformers.Where(f => f.UserId == userId || f.PerformerId == userId));
        _db.PasswordResetTokens.RemoveRange(_db.PasswordResetTokens.Where(t => t.UserId == userId));
        _db.Notifications.RemoveRange(_db.Notifications.Where(n => n.UserId == userId));
        _db.PortfolioItems.RemoveRange(_db.PortfolioItems.Where(p => p.PerformerId == userId));

        // услуги мастера снимаем с публикации
        foreach (var service in await _db.Services.Where(s => s.PerformerId == userId).ToListAsync())
            service.IsActive = false;

        user.Email = $"deleted-{userId}@deleted.invalid";
        user.FullName = "Удалённый пользователь";
        user.Phone = null;
        user.Bio = null;
        user.Specialization = null;
        user.ExperienceYears = null;
        user.City = null;
        user.ServiceArea = null;
        user.Latitude = null;
        user.Longitude = null;
        user.AvatarUrl = null;
        user.IsVerified = false;
        user.EmailNotifications = false;
        user.IsBlocked = true;
        user.PasswordHash = _hasher.HashPassword(user, Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));

        await _db.SaveChangesAsync();

        _logger.LogInformation("Пользователь {UserId} удалил аккаунт", userId);
        return Ok(new { message = "Аккаунт удалён" });
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
        Latitude = u.Latitude,
        Longitude = u.Longitude,
        AvatarUrl = u.AvatarUrl,
        IsVerified = u.IsVerified,
        EmailNotifications = u.EmailNotifications
    };
}