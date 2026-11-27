using FixHub.Api.Data;
using FixHub.Api.DTOs;
using FixHub.Api.Models;
using FixHub.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FixHub.Api.Controllers;

[ApiController]
[Route("api/performer/services")]
[Authorize(Roles = "Performer")]
public class PerformerServicesController : ControllerBase
{
    private readonly AppDbContext _db;

    public PerformerServicesController(AppDbContext db)
    {
        _db = db;
    }

    private int CurrentUserId => int.Parse(User.FindFirst("sub")!.Value);

    // Список моих услуг, включая скрытые
    [HttpGet]
    public async Task<ActionResult<List<ServiceDto>>> GetMine()
    {
        var userId = CurrentUserId;

        var items = await _db.Services
            .Include(s => s.Category)
            .Include(s => s.Performer)
            .Where(s => s.PerformerId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        return Ok(items.Select(ToDto).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<ServiceDto>> Create(ServiceRequest request)
    {
        if (!await _db.Categories.AnyAsync(c => c.Id == request.CategoryId))
            return BadRequest(new { error = "Категория не найдена" });

        var limitError = await CheckServicesLimitAsync(activeOnly: false);
        if (limitError != null)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = limitError });

        var service = new Service
        {
            PerformerId = CurrentUserId,
            CategoryId = request.CategoryId,
            Title = request.Title.Trim(),
            Description = request.Description.Trim(),
            PhotoUrl = request.PhotoUrl?.Trim(),
            Price = request.Price,
            Unit = request.Unit,
            IsActive = true
        };

        _db.Services.Add(service);
        await _db.SaveChangesAsync();

        await _db.Entry(service).Reference(s => s.Category).LoadAsync();
        await _db.Entry(service).Reference(s => s.Performer).LoadAsync();

        return Ok(ToDto(service));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ServiceDto>> Update(int id, ServiceRequest request)
    {
        var service = await FindMine(id);
        if (service == null)
            return NotFound(new { error = "Услуга не найдена" });

        if (!await _db.Categories.AnyAsync(c => c.Id == request.CategoryId))
            return BadRequest(new { error = "Категория не найдена" });

        service.CategoryId = request.CategoryId;
        service.Title = request.Title.Trim();
        service.Description = request.Description.Trim();
        service.PhotoUrl = request.PhotoUrl?.Trim();
        service.Price = request.Price;
        service.Unit = request.Unit;

        await _db.SaveChangesAsync();
        await _db.Entry(service).Reference(s => s.Category).LoadAsync();

        return Ok(ToDto(service));
    }

    // Скрыть услугу: она пропадёт из каталога
    [HttpPatch("{id:int}/hide")]
    public Task<ActionResult<ServiceDto>> Hide(int id) => SetActive(id, false);

    // Показать услугу снова
    [HttpPatch("{id:int}/show")]
    public Task<ActionResult<ServiceDto>> Show(int id) => SetActive(id, true);

    private async Task<ActionResult<ServiceDto>> SetActive(int id, bool isActive)
    {
        var service = await FindMine(id);
        if (service == null)
            return NotFound(new { error = "Услуга не найдена" });

        // иначе лимит обходится: создали, скрыли, создали ещё, показали все
        if (isActive && !service.IsActive)
        {
            var limitError = await CheckServicesLimitAsync(activeOnly: true);
            if (limitError != null)
                return StatusCode(StatusCodes.Status403Forbidden, new { error = limitError });
        }

        service.IsActive = isActive;
        await _db.SaveChangesAsync();

        return Ok(ToDto(service));
    }

    // Лимит услуг для непроверенного мастера. При создании считаем все услуги (скрытые тоже),
    // при показе скрытой услуги только опубликованные
    private async Task<string?> CheckServicesLimitAsync(bool activeOnly)
    {
        var userId = CurrentUserId;
        var isVerified = await _db.Users.Where(u => u.Id == userId).Select(u => u.IsVerified).FirstAsync();
        var limits = PerformerLimits.For(isVerified);

        var count = await _db.Services.CountAsync(s => s.PerformerId == userId && (!activeOnly || s.IsActive));
        return count >= limits.MaxServices ? PerformerLimits.ServicesMessage(limits.MaxServices) : null;
    }

    // Ищет услугу среди СВОИХ: чужая услуга для исполнителя «не существует»
    private Task<Service?> FindMine(int id)
    {
        var userId = CurrentUserId;
        return _db.Services
            .Include(s => s.Category)
            .Include(s => s.Performer)
            .FirstOrDefaultAsync(s => s.Id == id && s.PerformerId == userId);
    }

    private static ServiceDto ToDto(Service s) => new()
    {
        Id = s.Id,
        Title = s.Title,
        Description = s.Description,
        PhotoUrl = s.PhotoUrl,
        Price = s.Price,
        Unit = s.Unit.ToString(),
        IsActive = s.IsActive,
        CreatedAt = s.CreatedAt,
        CategoryId = s.CategoryId,
        CategoryName = s.Category.Name,
        PerformerId = s.PerformerId,
        PerformerName = s.Performer.FullName,
        PerformerAvatarUrl = s.Performer.AvatarUrl,
        PerformerSpecialization = s.Performer.Specialization,
        PerformerIsVerified = s.Performer.IsVerified
    };
}