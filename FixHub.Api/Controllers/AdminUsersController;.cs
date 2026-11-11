using FixHub.Api.Data;
using FixHub.Api.DTOs;
using FixHub.Api.Models;
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

    public AdminUsersController(AppDbContext db)
    {
        _db = db;
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
        await _db.SaveChangesAsync();

        return Ok(new AdminUserDto
        {
            Id = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            Phone = user.Phone,
            Role = user.Role.ToString(),
            IsBlocked = user.IsBlocked,
            CreatedAt = user.CreatedAt
        });
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}