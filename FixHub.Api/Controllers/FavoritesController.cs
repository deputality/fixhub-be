using FixHub.Api.Data;
using FixHub.Api.DTOs;
using FixHub.Api.Models;
using FixHub.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FixHub.Api.Controllers;

// Избранное клиента: услуги и мастера, к которым хочется вернуться.
// Добавление и удаление идемпотентны: повторный вызов ничего не ломает.
[ApiController]
[Route("api/favorites")]
[Authorize(Roles = "Client")]
public class FavoritesController : ControllerBase
{
    private readonly AppDbContext _db;

    public FavoritesController(AppDbContext db)
    {
        _db = db;
    }

    private int CurrentUserId => int.Parse(User.FindFirst("sub")!.Value);

    // Полный список: новые добавления первыми. Услуги и мастера, которых уже нельзя увидеть
    // в каталоге (скрыты, заблокированы), в список не попадают, но в избранном остаются
    [HttpGet]
    public async Task<ActionResult<FavoritesDto>> Get()
    {
        var userId = CurrentUserId;

        var services = await _db.FavoriteServices
            .AsNoTracking()
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAt).ThenByDescending(f => f.Id)
            .Select(f => f.Service)
            .Where(ServiceProjection.IsVisible)
            .Select(ServiceProjection.ToDto(_db))
            .ToListAsync();

        services.ForEach(ServiceProjection.RoundRatings);

        var performers = await _db.FavoritePerformers
            .AsNoTracking()
            .Where(f => f.UserId == userId
                && f.Performer.Role == UserRole.Performer
                && !f.Performer.IsBlocked)
            .OrderByDescending(f => f.CreatedAt).ThenByDescending(f => f.Id)
            .Select(f => new FavoritePerformerDto
            {
                Id = f.PerformerId,
                FullName = f.Performer.FullName,
                Specialization = f.Performer.Specialization,
                City = f.Performer.City,
                AvatarUrl = f.Performer.AvatarUrl,
                IsVerified = f.Performer.IsVerified,
                Rating = _db.Reviews
                    .Where(r => r.PerformerId == f.PerformerId && !r.IsHidden)
                    .Average(r => (decimal?)r.Rating),
                ReviewCount = _db.Reviews
                    .Count(r => r.PerformerId == f.PerformerId && !r.IsHidden),
                ServicesCount = _db.Services.Count(s => s.PerformerId == f.PerformerId && s.IsActive
                    && !s.Category.IsHidden
                    && (s.Category.Parent == null || !s.Category.Parent.IsHidden))
            })
            .ToListAsync();

        performers.ForEach(p => p.Rating = ReviewProjection.Round(p.Rating));

        return Ok(new FavoritesDto { Services = services, Performers = performers });
    }

    // Только идентификаторы: по ним на карточках закрашиваются сердечки
    [HttpGet("ids")]
    public async Task<ActionResult<FavoriteIdsDto>> GetIds()
    {
        var userId = CurrentUserId;

        return Ok(new FavoriteIdsDto
        {
            ServiceIds = await _db.FavoriteServices
                .AsNoTracking()
                .Where(f => f.UserId == userId)
                .Select(f => f.ServiceId)
                .ToListAsync(),
            PerformerIds = await _db.FavoritePerformers
                .AsNoTracking()
                .Where(f => f.UserId == userId)
                .Select(f => f.PerformerId)
                .ToListAsync()
        });
    }

    [HttpPost("services/{serviceId:int}")]
    public async Task<IActionResult> AddService(int serviceId)
    {
        var userId = CurrentUserId;

        var visible = await _db.Services
            .AsNoTracking()
            .Where(s => s.Id == serviceId)
            .Where(ServiceProjection.IsVisible)
            .AnyAsync();

        if (!visible)
            return NotFound(new { error = "Услуга не найдена" });

        if (!await _db.FavoriteServices.AnyAsync(f => f.UserId == userId && f.ServiceId == serviceId))
        {
            _db.FavoriteServices.Add(new FavoriteService { UserId = userId, ServiceId = serviceId });

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // два одинаковых запроса одновременно: запись уже есть, это не ошибка
            }
        }

        return NoContent();
    }

    [HttpDelete("services/{serviceId:int}")]
    public async Task<IActionResult> RemoveService(int serviceId)
    {
        var userId = CurrentUserId;

        await _db.FavoriteServices
            .Where(f => f.UserId == userId && f.ServiceId == serviceId)
            .ExecuteDeleteAsync();

        return NoContent();
    }

    [HttpPost("performers/{performerId:int}")]
    public async Task<IActionResult> AddPerformer(int performerId)
    {
        var userId = CurrentUserId;

        var exists = await _db.Users.AnyAsync(u =>
            u.Id == performerId && u.Role == UserRole.Performer && !u.IsBlocked);

        if (!exists)
            return NotFound(new { error = "Мастер не найден" });

        if (!await _db.FavoritePerformers.AnyAsync(f => f.UserId == userId && f.PerformerId == performerId))
        {
            _db.FavoritePerformers.Add(new FavoritePerformer { UserId = userId, PerformerId = performerId });

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // запись уже есть
            }
        }

        return NoContent();
    }

    [HttpDelete("performers/{performerId:int}")]
    public async Task<IActionResult> RemovePerformer(int performerId)
    {
        var userId = CurrentUserId;

        await _db.FavoritePerformers
            .Where(f => f.UserId == userId && f.PerformerId == performerId)
            .ExecuteDeleteAsync();

        return NoContent();
    }
}
