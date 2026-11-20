using FixHub.Api.Data;
using FixHub.Api.DTOs;
using FixHub.Api.Models;
using FixHub.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FixHub.Api.Controllers;

// Публичная страница мастера. Её видят все, включая гостей, поэтому
// здесь нет контактов (email и телефон) и не показываются заблокированные.
[ApiController]
[Route("api/performers")]
public class PerformersController : ControllerBase
{
    private readonly AppDbContext _db;

    public PerformersController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PerformerPublicDto>> GetById(int id)
    {
        var performer = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == id && u.Role == UserRole.Performer && !u.IsBlocked)
            .Select(u => new PerformerPublicDto
            {
                Id = u.Id,
                FullName = u.FullName,
                Specialization = u.Specialization,
                ExperienceYears = u.ExperienceYears,
                City = u.City,
                ServiceArea = u.ServiceArea,
                Bio = u.Bio,
                AvatarUrl = u.AvatarUrl,
                IsVerified = u.IsVerified,
                CreatedAt = u.CreatedAt,
                Rating = _db.Reviews
                    .Where(r => r.PerformerId == u.Id && !r.IsHidden)
                    .Average(r => (decimal?)r.Rating),
                ReviewCount = _db.Reviews
                    .Count(r => r.PerformerId == u.Id && !r.IsHidden),
                CompletedOrders = _db.Orders
                    .Count(o => o.PerformerId == u.Id && o.Status == OrderStatus.Completed),
                // столько же услуг увидит клиент в каталоге
                ServicesCount = _db.Services.Count(s => s.PerformerId == u.Id && s.IsActive
                    && !s.Category.IsHidden
                    && (s.Category.Parent == null || !s.Category.Parent.IsHidden))
            })
            .FirstOrDefaultAsync();

        if (performer == null)
            return NotFound(new { error = "Мастер не найден" });

        performer.Rating = ReviewProjection.Round(performer.Rating);
        return Ok(performer);
    }
}
