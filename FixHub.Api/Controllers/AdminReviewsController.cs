using FixHub.Api.Data;
using FixHub.Api.DTOs;
using FixHub.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FixHub.Api.Controllers;

[ApiController]
[Route("api/admin/reviews")]
[Authorize(Roles = "Admin")]
public class AdminReviewsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<AdminReviewsController> _logger;

    public AdminReviewsController(AppDbContext db, ILogger<AdminReviewsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    // Все отзывы, включая скрытые; можно отфильтровать по признаку «скрыт»
    [HttpGet]
    public async Task<ActionResult<PagedResult<ReviewDto>>> Search([FromQuery] AdminReviewQuery query)
    {
        var q = _db.Reviews.AsNoTracking().AsQueryable();

        if (query.IsHidden != null)
            q = q.Where(r => r.IsHidden == query.IsHidden);

        var total = await q.CountAsync();

        var items = await q
            .OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(ReviewProjection.ToDto)
            .ToListAsync();

        return Ok(new PagedResult<ReviewDto>
        {
            Items = items,
            Page = query.Page,
            PageSize = query.PageSize,
            TotalCount = total,
            TotalPages = (int)Math.Ceiling(total / (double)query.PageSize)
        });
    }

    [HttpPost("{id:int}/hide")]
    public Task<ActionResult<ReviewDto>> Hide(int id) => SetHidden(id, true);

    [HttpPost("{id:int}/show")]
    public Task<ActionResult<ReviewDto>> Show(int id) => SetHidden(id, false);

    private async Task<ActionResult<ReviewDto>> SetHidden(int id, bool hidden)
    {
        var review = await _db.Reviews.FindAsync(id);
        if (review == null)
            return NotFound(new { error = "Отзыв не найден" });

        review.IsHidden = hidden;
        await _db.SaveChangesAsync();

        _logger.LogWarning("Администратор {AdminId} {Action} отзыв {ReviewId}",
            User.FindFirst("sub")?.Value,
            hidden ? "скрыл" : "вернул",
            review.Id);

        var dto = await _db.Reviews
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(ReviewProjection.ToDto)
            .FirstAsync();

        return Ok(dto);
    }
}