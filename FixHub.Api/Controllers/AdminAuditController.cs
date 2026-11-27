using FixHub.Api.Data;
using FixHub.Api.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FixHub.Api.Controllers;

// Журнал действий администраторов. Записи только читаются: изменить или удалить их нельзя
[ApiController]
[Route("api/admin/audit")]
[Authorize(Roles = "Admin")]
public class AdminAuditController : ControllerBase
{
    private readonly AppDbContext _db;

    public AdminAuditController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<AuditLogDto>>> Search([FromQuery] AuditQuery query)
    {
        var q = _db.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.TargetType))
            q = q.Where(a => a.TargetType == query.TargetType);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{EscapeLike(query.Search.Trim())}%";
            q = q.Where(a => EF.Functions.ILike(a.Admin.FullName, pattern) ||
                             (a.TargetName != null && EF.Functions.ILike(a.TargetName, pattern)));
        }

        var total = await q.CountAsync();

        var items = await q
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(a => new AuditLogDto
            {
                Id = a.Id,
                AdminId = a.AdminId,
                AdminName = a.Admin.FullName,
                Action = a.Action,
                TargetType = a.TargetType,
                TargetId = a.TargetId,
                TargetName = a.TargetName,
                Details = a.Details,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync();

        return Ok(new PagedResult<AuditLogDto>
        {
            Items = items,
            Page = query.Page,
            PageSize = query.PageSize,
            TotalCount = total,
            TotalPages = (int)Math.Ceiling(total / (double)query.PageSize)
        });
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
