using FixHub.Api.Data;
using FixHub.Api.DTOs;
using FixHub.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FixHub.Api.Controllers;

[ApiController]
[Route("api/categories")]
public class CategoriesController : ControllerBase
{
    private readonly AppDbContext _db;

    public CategoriesController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<List<CategoryDto>>> GetAll()
    {
        // скрытые категории (и подкатегории скрытой) клиентам не показываем
        var items = await _db.Categories
            .Where(c => !c.IsHidden && (c.Parent == null || !c.Parent.IsHidden))
            .OrderBy(c => c.Name)
            .Select(c => new CategoryDto { Id = c.Id, Name = c.Name, ParentId = c.ParentId, Icon = c.Icon, Color = c.Color })
            .ToListAsync();
        return Ok(items);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<ActionResult<CategoryDto>> Create(CategoryRequest request)
    {
        if (request.ParentId != null &&
            !await _db.Categories.AnyAsync(c => c.Id == request.ParentId))
            return BadRequest(new { error = "Родительская категория не найдена" });

        var category = new Category
        {
            Name = request.Name.Trim(),
            ParentId = request.ParentId,
            Icon = NormalizeIcon(request.Icon),
            Color = NormalizeColor(request.Color)
        };
        _db.Categories.Add(category);
        await _db.SaveChangesAsync();

        return Ok(ToDto(category));
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<CategoryDto>> Update(int id, CategoryRequest request)
    {
        var category = await _db.Categories.FindAsync(id);
        if (category == null)
            return NotFound(new { error = "Категория не найдена" });

        if (request.ParentId == id)
            return BadRequest(new { error = "Категория не может быть родителем самой себя" });

        if (request.ParentId != null &&
            !await _db.Categories.AnyAsync(c => c.Id == request.ParentId))
            return BadRequest(new { error = "Родительская категория не найдена" });

        // родителем нельзя сделать собственного потомка, иначе дерево зациклится
        var ancestorId = request.ParentId;
        while (ancestorId != null)
        {
            if (ancestorId == id)
                return BadRequest(new { error = "Нельзя вложить категорию в её же подкатегорию" });
            ancestorId = await _db.Categories
                .Where(c => c.Id == ancestorId)
                .Select(c => c.ParentId)
                .FirstOrDefaultAsync();
        }

        category.Name = request.Name.Trim();
        category.ParentId = request.ParentId;
        category.Icon = NormalizeIcon(request.Icon);
        category.Color = NormalizeColor(request.Color);
        await _db.SaveChangesAsync();

        return Ok(ToDto(category));
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var category = await _db.Categories.FindAsync(id);
        if (category == null)
            return NotFound(new { error = "Категория не найдена" });

        var hasChildren = await _db.Categories.AnyAsync(c => c.ParentId == id);
        var hasServices = await _db.Services.AnyAsync(s => s.CategoryId == id);
        if (hasChildren || hasServices)
            return Conflict(new { error = "Нельзя удалить категорию: в ней есть подкатегории или услуги" });

        _db.Categories.Remove(category);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static CategoryDto ToDto(Category c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        ParentId = c.ParentId,
        Icon = c.Icon,
        Color = c.Color
    };

    private static string? NormalizeIcon(string? icon) =>
        string.IsNullOrWhiteSpace(icon) ? null : icon.Trim().ToLowerInvariant();

    // цвет храним в нижнем регистре, чтобы #16A34A и #16a34a не считались разными
    private static string? NormalizeColor(string? color) =>
        string.IsNullOrWhiteSpace(color) ? null : color.Trim().ToLowerInvariant();
}
