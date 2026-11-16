using System.Linq.Expressions;
using FixHub.Api.Data;
using FixHub.Api.DTOs;
using FixHub.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FixHub.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db;

    public NotificationsController(AppDbContext db)
    {
        _db = db;
    }

    private int CurrentUserId => int.Parse(User.FindFirst("sub")!.Value);

    private static readonly Expression<Func<Notification, NotificationDto>> ToDto = n => new NotificationDto
    {
        Id = n.Id,
        Type = n.Type.ToString(),
        Text = n.Text,
        OrderId = n.OrderId,
        ReviewId = n.ReviewId,
        IsRead = n.IsRead,
        CreatedAt = n.CreatedAt
    };

    // Мои уведомления, новые сверху
    [HttpGet]
    public async Task<ActionResult<PagedResult<NotificationDto>>> Search([FromQuery] NotificationQuery query)
    {
        var userId = CurrentUserId;

        var q = _db.Notifications.AsNoTracking().Where(n => n.UserId == userId);

        if (query.OnlyUnread)
            q = q.Where(n => !n.IsRead);

        var total = await q.CountAsync();

        var items = await q
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(ToDto)
            .ToListAsync();

        return Ok(new PagedResult<NotificationDto>
        {
            Items = items,
            Page = query.Page,
            PageSize = query.PageSize,
            TotalCount = total,
            TotalPages = (int)Math.Ceiling(total / (double)query.PageSize)
        });
    }

    // Число для красного кружка у колокольчика
    [HttpGet("unread-count")]
    public async Task<ActionResult<UnreadCountDto>> UnreadCount()
    {
        var userId = CurrentUserId;
        var count = await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead);
        return Ok(new UnreadCountDto { Count = count });
    }

    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id)
    {
        var userId = CurrentUserId;

        // чужое уведомление для пользователя «не существует»
        var updated = await _db.Notifications
            .Where(n => n.Id == id && n.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));

        if (updated == 0)
            return NotFound(new { error = "Уведомление не найдено" });

        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        var userId = CurrentUserId;

        var marked = await _db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));

        return Ok(new { marked });
    }
}