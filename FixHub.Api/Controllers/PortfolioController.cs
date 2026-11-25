using FixHub.Api.Data;
using FixHub.Api.DTOs;
using FixHub.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FixHub.Api.Controllers;

// Портфолио мастера: работы «до/после». Фото загружаются через POST /api/performer/uploads,
// сюда приходят только их адреса. Публичный список лежит в PerformersController.
[ApiController]
[Route("api/performer/portfolio")]
[Authorize(Roles = "Performer")]
public class PortfolioController : ControllerBase
{
    private const int MaxItems = 30;
    private const string UploadsPrefix = "/uploads/";

    private readonly AppDbContext _db;

    public PortfolioController(AppDbContext db)
    {
        _db = db;
    }

    private int CurrentUserId => int.Parse(User.FindFirst("sub")!.Value);

    [HttpGet]
    public async Task<ActionResult<List<PortfolioItemDto>>> GetMine()
    {
        var userId = CurrentUserId;

        var items = await _db.PortfolioItems
            .AsNoTracking()
            .Where(p => p.PerformerId == userId)
            .OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)
            .ToListAsync();

        return Ok(items.Select(ToDto).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<PortfolioItemDto>> Create(PortfolioItemRequest request)
    {
        var userId = CurrentUserId;

        var error = Validate(request);
        if (error != null)
            return BadRequest(new { error });

        if (await _db.PortfolioItems.CountAsync(p => p.PerformerId == userId) >= MaxItems)
            return BadRequest(new { error = $"В портфолио не больше {MaxItems} работ" });

        var item = new PortfolioItem
        {
            PerformerId = userId,
            Title = request.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            BeforeUrl = string.IsNullOrWhiteSpace(request.BeforeUrl) ? null : request.BeforeUrl.Trim(),
            AfterUrl = request.AfterUrl.Trim()
        };

        _db.PortfolioItems.Add(item);
        await _db.SaveChangesAsync();

        return Ok(ToDto(item));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<PortfolioItemDto>> Update(int id, PortfolioItemRequest request)
    {
        var error = Validate(request);
        if (error != null)
            return BadRequest(new { error });

        var userId = CurrentUserId;
        var item = await _db.PortfolioItems.FirstOrDefaultAsync(p => p.Id == id && p.PerformerId == userId);

        if (item == null)
            return NotFound(new { error = "Работа не найдена" });

        item.Title = request.Title.Trim();
        item.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        item.BeforeUrl = string.IsNullOrWhiteSpace(request.BeforeUrl) ? null : request.BeforeUrl.Trim();
        item.AfterUrl = request.AfterUrl.Trim();

        await _db.SaveChangesAsync();
        return Ok(ToDto(item));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = CurrentUserId;
        var item = await _db.PortfolioItems.FirstOrDefaultAsync(p => p.Id == id && p.PerformerId == userId);

        if (item == null)
            return NotFound(new { error = "Работа не найдена" });

        _db.PortfolioItems.Remove(item);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // Принимаем только адреса наших загрузок, чужие ссылки на картинки в портфолио не нужны
    private static string? Validate(PortfolioItemRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return "Укажите название работы";

        if (!IsUploadUrl(request.AfterUrl))
            return "Загрузите фото результата";

        if (!string.IsNullOrWhiteSpace(request.BeforeUrl) && !IsUploadUrl(request.BeforeUrl))
            return "Некорректное фото «до»";

        return null;
    }

    private static bool IsUploadUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && url.Trim().StartsWith(UploadsPrefix, StringComparison.Ordinal)
        && !url.Contains("..")
        && !url.Contains('\\');

    public static PortfolioItemDto ToDto(PortfolioItem p) => new()
    {
        Id = p.Id,
        Title = p.Title,
        Description = p.Description,
        BeforeUrl = p.BeforeUrl,
        AfterUrl = p.AfterUrl,
        CreatedAt = p.CreatedAt
    };
}
