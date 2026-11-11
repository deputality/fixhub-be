using FixHub.Api.Data;
using FixHub.Api.DTOs;
using FixHub.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FixHub.Api.Controllers;

[ApiController]
[Route("api/cart")]
[Authorize(Roles = "Client")]
public class CartController : ControllerBase
{
    private const int MaxQuantity = 100;

    private readonly AppDbContext _db;

    public CartController(AppDbContext db)
    {
        _db = db;
    }

    private int CurrentUserId => int.Parse(User.FindFirst("sub")!.Value);

    [HttpGet]
    public async Task<ActionResult<CartDto>> Get()
    {
        return Ok(await BuildCart(CurrentUserId));
    }

    [HttpPost("items")]
    public async Task<ActionResult<CartDto>> Add(AddToCartRequest request)
    {
        var userId = CurrentUserId;

        var dateError = ValidateDate(request.DesiredDate);
        if (dateError != null)
            return BadRequest(new { error = dateError });

        var service = await _db.Services
            .Include(s => s.Performer)
            .FirstOrDefaultAsync(s => s.Id == request.ServiceId);

        if (service == null)
            return NotFound(new { error = "Услуга не найдена" });

        if (!service.IsActive || service.Performer.IsBlocked)
            return BadRequest(new { error = "Эта услуга недоступна для заказа" });

        var item = await _db.CartItems
            .FirstOrDefaultAsync(c => c.ClientId == userId && c.ServiceId == service.Id);

        if (item == null)
        {
            _db.CartItems.Add(new CartItem
            {
                ClientId = userId,
                ServiceId = service.Id,
                Quantity = request.Quantity,
                DesiredDate = request.DesiredDate,
                Comment = request.Comment?.Trim()
            });
        }
        else
        {
            // повторное добавление той же услуги увеличивает количество
            if (item.Quantity + request.Quantity > MaxQuantity)
                return BadRequest(new { error = $"Количество не может быть больше {MaxQuantity}" });

            item.Quantity += request.Quantity;
            if (request.DesiredDate != null) item.DesiredDate = request.DesiredDate;
            if (!string.IsNullOrWhiteSpace(request.Comment)) item.Comment = request.Comment.Trim();
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // два одинаковых запроса одновременно упёрлись в уникальный индекс
            return Conflict(new { error = "Корзина изменилась, повторите запрос" });
        }

        return Ok(await BuildCart(userId));
    }

    [HttpPut("items/{id:int}")]
    public async Task<ActionResult<CartDto>> Update(int id, UpdateCartItemRequest request)
    {
        var userId = CurrentUserId;

        var dateError = ValidateDate(request.DesiredDate);
        if (dateError != null)
            return BadRequest(new { error = dateError });

        var item = await _db.CartItems
            .FirstOrDefaultAsync(c => c.Id == id && c.ClientId == userId);

        if (item == null)
            return NotFound(new { error = "Позиция корзины не найдена" });

        item.Quantity = request.Quantity;
        item.DesiredDate = request.DesiredDate;
        item.Comment = request.Comment?.Trim();

        await _db.SaveChangesAsync();
        return Ok(await BuildCart(userId));
    }

    [HttpDelete("items/{id:int}")]
    public async Task<ActionResult<CartDto>> Remove(int id)
    {
        var userId = CurrentUserId;

        var item = await _db.CartItems
            .FirstOrDefaultAsync(c => c.Id == id && c.ClientId == userId);

        if (item == null)
            return NotFound(new { error = "Позиция корзины не найдена" });

        _db.CartItems.Remove(item);
        await _db.SaveChangesAsync();

        return Ok(await BuildCart(userId));
    }

    [HttpDelete]
    public async Task<IActionResult> Clear()
    {
        var userId = CurrentUserId;
        await _db.CartItems.Where(c => c.ClientId == userId).ExecuteDeleteAsync();
        return NoContent();
    }

    private static string? ValidateDate(DateOnly? date)
    {
        // запас в один день из-за разницы часовых поясов
        var earliest = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        if (date != null && date < earliest)
            return "Желаемая дата не может быть в прошлом";
        return null;
    }

    private async Task<CartDto> BuildCart(int userId)
    {
        var items = await _db.CartItems
            .AsNoTracking()
            .Where(c => c.ClientId == userId)
            .OrderBy(c => c.Id)
            .Select(c => new CartItemDto
            {
                Id = c.Id,
                ServiceId = c.ServiceId,
                ServiceTitle = c.Service.Title,
                PhotoUrl = c.Service.PhotoUrl,
                PerformerId = c.Service.PerformerId,
                PerformerName = c.Service.Performer.FullName,
                UnitPrice = c.Service.Price,
                Unit = c.Service.Unit.ToString(),
                Quantity = c.Quantity,
                LineTotal = c.Service.Price * c.Quantity,
                DesiredDate = c.DesiredDate,
                Comment = c.Comment,
                IsAvailable = c.Service.IsActive && !c.Service.Performer.IsBlocked
            })
            .ToListAsync();

        return new CartDto
        {
            Items = items,
            // недоступные позиции в сумму не входят: их нельзя оформить
            TotalAmount = items.Where(i => i.IsAvailable).Sum(i => i.LineTotal)
        };
    }
}