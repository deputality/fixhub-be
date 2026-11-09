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
        var items = await _db.Categories
            .OrderBy(c => c.Name)
            .Select(c => new CategoryDto { Id = c.Id, Name = c.Name, ParentId = c.ParentId })
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

        var category = new Category { Name = request.Name.Trim(), ParentId = request.ParentId };
        _db.Categories.Add(category);
        await _db.SaveChangesAsync();

        return Ok(new CategoryDto { Id = category.Id, Name = category.Name, ParentId = category.ParentId });
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

        category.Name = request.Name.Trim();
        category.ParentId = request.ParentId;
        await _db.SaveChangesAsync();

        return Ok(new CategoryDto { Id = category.Id, Name = category.Name, ParentId = category.ParentId });
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
}