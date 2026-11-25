using FixHub.Api.Data;
using FixHub.Api.DTOs;
using FixHub.Api.Models;
using FixHub.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FixHub.Api.Controllers;

[ApiController]
[Route("api/admin/users")]
[Authorize(Roles = "Admin")]
public class AdminUsersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<AdminUsersController> _logger;
    private readonly INotificationService _notifications;
    private readonly IAuditService _audit;

    private int AdminId => int.Parse(User.FindFirst("sub")!.Value);

    public AdminUsersController(AppDbContext db, ILogger<AdminUsersController> logger,
        INotificationService notifications, IAuditService audit)
    {
        _db = db;
        _logger = logger;
        _notifications = notifications;
        _audit = audit;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<AdminUserDto>>> Search([FromQuery] AdminUserQuery query)
    {
        var q = _db.Users.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{EscapeLike(query.Search.Trim())}%";
            q = q.Where(u => EF.Functions.ILike(u.Email, pattern) ||
                             EF.Functions.ILike(u.FullName, pattern));
        }

        if (query.Role != null)
            q = q.Where(u => u.Role == query.Role);

        if (query.IsBlocked != null)
            q = q.Where(u => u.IsBlocked == query.IsBlocked);

        if (query.IsVerified != null)
            q = q.Where(u => u.IsVerified == query.IsVerified);

        var total = await q.CountAsync();

        var items = await q
            .OrderBy(u => u.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(u => new AdminUserDto
            {
                Id = u.Id,
                Email = u.Email,
                FullName = u.FullName,
                Phone = u.Phone,
                Role = u.Role.ToString(),
                IsBlocked = u.IsBlocked,
                IsVerified = u.IsVerified,
                DocumentsCount = _db.VerificationDocuments.Count(d => d.UserId == u.Id),
                CreatedAt = u.CreatedAt
            })
            .ToListAsync();

        return Ok(new PagedResult<AdminUserDto>
        {
            Items = items,
            Page = query.Page,
            PageSize = query.PageSize,
            TotalCount = total,
            TotalPages = (int)Math.Ceiling(total / (double)query.PageSize)
        });
    }

    [HttpPost("{id:int}/block")]
    public Task<ActionResult<AdminUserDto>> Block(int id) => SetBlocked(id, true);

    [HttpPost("{id:int}/unblock")]
    public Task<ActionResult<AdminUserDto>> Unblock(int id) => SetBlocked(id, false);

    private async Task<ActionResult<AdminUserDto>> SetBlocked(int id, bool blocked)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null)
            return NotFound(new { error = "Пользователь не найден" });

        if (user.Role == UserRole.Admin)
            return BadRequest(new { error = "Администратора заблокировать нельзя" });

        user.IsBlocked = blocked;
        _audit.Add(AdminId, blocked ? "user.block" : "user.unblock", "User", user.Id, user.FullName);
        await _db.SaveChangesAsync();

        _logger.LogWarning("Администратор {AdminId} {Action} пользователя {UserId}",
            User.FindFirst("sub")?.Value,
            blocked ? "заблокировал" : "разблокировал",
            user.Id);

        return Ok(ToDto(user));
    }

    // Подтвердить мастера: рядом с его услугами появится отметка «Проверенный специалист»
    [HttpPost("{id:int}/verify")]
    public Task<ActionResult<AdminUserDto>> Verify(int id) => SetVerified(id, true);

    // Снять отметку
    [HttpPost("{id:int}/unverify")]
    public Task<ActionResult<AdminUserDto>> Unverify(int id) => SetVerified(id, false);

    private async Task<ActionResult<AdminUserDto>> SetVerified(int id, bool verified)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null)
            return NotFound(new { error = "Пользователь не найден" });

        // отметка есть только у исполнителей
        if (user.Role != UserRole.Performer)
            return BadRequest(new { error = "Проверять можно только исполнителей" });

        if (user.IsVerified != verified)
        {
            user.IsVerified = verified;

            if (verified)
                _notifications.Add(user.Id, NotificationType.ProfileVerified,
                    "Администратор проверил ваш профиль: теперь у вас отметка «Проверенный мастер»");

            _audit.Add(AdminId, verified ? "user.verify" : "user.unverify", "User", user.Id, user.FullName);
            await _db.SaveChangesAsync();

            _logger.LogWarning("Администратор {AdminId} {Action} исполнителя {UserId}",
                User.FindFirst("sub")?.Value,
                verified ? "подтвердил" : "снял отметку проверки с",
                user.Id);
        }

        return Ok(ToDto(user));
    }

    private AdminUserDto ToDto(User user) => new()
    {
        Id = user.Id,
        Email = user.Email,
        FullName = user.FullName,
        Phone = user.Phone,
        Role = user.Role.ToString(),
        IsBlocked = user.IsBlocked,
        IsVerified = user.IsVerified,
        DocumentsCount = _db.VerificationDocuments.Count(d => d.UserId == user.Id),
        CreatedAt = user.CreatedAt
    };

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}