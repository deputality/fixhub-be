using FixHub.Api.Data;
using FixHub.Api.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FixHub.Api.Controllers;

[ApiController]
[Route("api/admin/categories")]
[Authorize(Roles = "Admin")]
public class AdminCategoriesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<AdminCategoriesController> _logger;

    public AdminCategoriesController(AppDbContext db, ILogger<AdminCategoriesController> logger)
    {
        _db = db;
        _logger = logger;
    }

    // Все категории, включая скрытые; создание, правка и удаление остались в CategoriesController
    [HttpGet]
    public async Task<ActionResult<List<AdminCategoryDto>>> GetAll()
    {
        return Ok(await LoadAll());
    }

    [HttpPost("{id:int}/hide")]
    public Task<ActionResult<AdminCategoryDto>> Hide(int id) => SetHidden(id, true);

    [HttpPost("{id:int}/show")]
    public Task<ActionResult<AdminCategoryDto>> Show(int id) => SetHidden(id, false);

    private async Task<ActionResult<AdminCategoryDto>> SetHidden(int id, bool hidden)
    {
        var category = await _db.Categories.FindAsync(id);
        if (category == null)
            return NotFound(new { error = "Категория не найдена" });

        category.IsHidden = hidden;
        await _db.SaveChangesAsync();

        _logger.LogWarning("Администратор {AdminId} {Action} категорию {CategoryId}",
            User.FindFirst("sub")?.Value,
            hidden ? "скрыл" : "вернул",
            id);

        return Ok((await LoadAll()).First(c => c.Id == id));
    }

    // Услуги и исполнители категории считаются вместе с её подкатегориями
    private async Task<List<AdminCategoryDto>> LoadAll()
    {
        var categories = await _db.Categories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name, c.ParentId, c.IsHidden, c.Icon, c.Color })
            .ToListAsync();

        var services = await _db.Services
            .AsNoTracking()
            .Select(s => new { s.CategoryId, s.PerformerId })
            .ToListAsync();

        var children = categories
            .Where(c => c.ParentId != null)
            .GroupBy(c => c.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(c => c.Id).ToList());

        // id категории и всех её потомков
        List<int> Subtree(int id)
        {
            var result = new List<int> { id };
            if (children.TryGetValue(id, out var direct))
                foreach (var child in direct)
                    result.AddRange(Subtree(child));
            return result;
        }

        return categories.Select(c =>
        {
            var ids = Subtree(c.Id).ToHashSet();
            var inside = services.Where(s => ids.Contains(s.CategoryId)).ToList();

            return new AdminCategoryDto
            {
                Id = c.Id,
                Name = c.Name,
                ParentId = c.ParentId,
                IsHidden = c.IsHidden,
                Icon = c.Icon,
                Color = c.Color,
                ChildrenCount = children.TryGetValue(c.Id, out var direct) ? direct.Count : 0,
                ServicesCount = inside.Count,
                PerformersCount = inside.Select(s => s.PerformerId).Distinct().Count()
            };
        }).ToList();
    }
}
