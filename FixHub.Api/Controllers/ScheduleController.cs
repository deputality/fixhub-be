using FixHub.Api.Common;
using FixHub.Api.Data;
using FixHub.Api.DTOs;
using FixHub.Api.Models;
using FixHub.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FixHub.Api.Controllers;

// Расписание мастера: его редактирует сам мастер, свободные слоты видят все
[ApiController]
public class ScheduleController : ControllerBase
{
    private readonly AppDbContext _db;

    public ScheduleController(AppDbContext db)
    {
        _db = db;
    }

    private int CurrentUserId => int.Parse(User.FindFirst("sub")!.Value);

    [Authorize(Roles = "Performer")]
    [HttpGet("api/performer/schedule")]
    public async Task<ActionResult<List<WorkingDayDto>>> GetMine()
    {
        return Ok(await LoadAsync(CurrentUserId));
    }

    // Список целиком заменяет прежнее расписание
    [Authorize(Roles = "Performer")]
    [HttpPut("api/performer/schedule")]
    public async Task<ActionResult<List<WorkingDayDto>>> Save(SaveScheduleRequest request)
    {
        var userId = CurrentUserId;

        if (request.Days.Select(d => d.DayOfWeek).Distinct().Count() != request.Days.Count)
            return BadRequest(new { error = "День недели указан дважды" });

        var rows = new List<WorkingHours>();
        foreach (var day in request.Days)
        {
            var start = TimeFormat.Parse(day.StartTime);
            var end = TimeFormat.Parse(day.EndTime);

            if (start == null || end == null)
                return BadRequest(new { error = "Время указывается в формате ЧЧ:ММ" });

            if (end.Value.AddMinutes(-ScheduleRules.SlotMinutes) < start.Value
                || end.Value <= start.Value)
                return BadRequest(new { error = "Окончание работы должно быть позже начала хотя бы на час" });

            rows.Add(new WorkingHours
            {
                PerformerId = userId,
                DayOfWeek = day.DayOfWeek,
                StartTime = start.Value,
                EndTime = end.Value
            });
        }

        await using var tx = await _db.Database.BeginTransactionAsync();

        await _db.WorkingHours.Where(w => w.PerformerId == userId).ExecuteDeleteAsync();
        _db.WorkingHours.AddRange(rows);
        await _db.SaveChangesAsync();

        await tx.CommitAsync();

        return Ok(await LoadAsync(userId));
    }

    // Свободные слоты мастера на дату (для выбора времени при заказе)
    [HttpGet("api/performers/{id:int}/slots")]
    public async Task<ActionResult<SlotsDto>> GetSlots(int id, [FromQuery] DateOnly date)
    {
        var exists = await _db.Users
            .AnyAsync(u => u.Id == id && u.Role == UserRole.Performer && !u.IsBlocked);

        if (!exists)
            return NotFound(new { error = "Мастер не найден" });

        return Ok(await ScheduleRules.GetSlotsAsync(_db, id, date));
    }

    private async Task<List<WorkingDayDto>> LoadAsync(int performerId)
    {
        var rows = await _db.WorkingHours
            .AsNoTracking()
            .Where(w => w.PerformerId == performerId)
            .OrderBy(w => w.DayOfWeek)
            .ToListAsync();

        return rows.Select(w => new WorkingDayDto
        {
            DayOfWeek = w.DayOfWeek,
            StartTime = w.StartTime.ToString("HH:mm"),
            EndTime = w.EndTime.ToString("HH:mm")
        }).ToList();
    }
}
