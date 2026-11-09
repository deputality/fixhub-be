using FixHub.Api.Data;
using FixHub.Api.DTOs;
using FixHub.Api.Models;
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

        service.IsActive = isActive;
        await _db.SaveChangesAsync();

        return Ok(ToDto(service));
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
        PerformerName = s.Performer.FullName
    };
}