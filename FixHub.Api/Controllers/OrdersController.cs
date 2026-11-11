using System.Linq.Expressions;
using FixHub.Api.Data;
using FixHub.Api.DTOs;
using FixHub.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FixHub.Api.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    private readonly AppDbContext _db;

    public OrdersController(AppDbContext db)
    {
        _db = db;
    }

    private int CurrentUserId => int.Parse(User.FindFirst("sub")!.Value);

    private static readonly Expression<Func<Order, OrderDto>> ToDto = o => new OrderDto
    {
        Id = o.Id,
        Status = o.Status.ToString(),
        TotalAmount = o.TotalAmount,
        Address = o.Address,
        ContactName = o.ContactName,
        ContactPhone = o.ContactPhone,
        CreatedAt = o.CreatedAt,
        ClientId = o.ClientId,
        ClientName = o.Client.FullName,
        PerformerId = o.PerformerId,
        PerformerName = o.Performer.FullName,
        Items = o.Items
            .OrderBy(i => i.Id)
            .Select(i => new OrderItemDto
            {
                Id = i.Id,
                ServiceId = i.ServiceId,
                ServiceTitle = i.ServiceTitle,
                UnitPrice = i.UnitPrice,
                Quantity = i.Quantity,
                LineTotal = i.UnitPrice * i.Quantity,
                DesiredDate = i.DesiredDate,
                Comment = i.Comment
            })
            .ToList()
    };

    // ---------- Оформление заказа ----------

    [Authorize(Roles = "Client")]
    [HttpPost]
    public async Task<ActionResult<List<OrderDto>>> Checkout(CheckoutRequest request)
    {
        var userId = CurrentUserId;

        await using var tx = await _db.Database.BeginTransactionAsync();

        var cart = await _db.CartItems
            .Include(c => c.Service).ThenInclude(s => s.Performer)
            .Where(c => c.ClientId == userId)
            .OrderBy(c => c.Id)
            .ToListAsync();

        if (cart.Count == 0)
            return BadRequest(new { error = "Корзина пуста" });

        var unavailable = cart
            .Where(c => !c.Service.IsActive || c.Service.Performer.IsBlocked)
            .Select(c => c.Service.Title)
            .ToList();

        if (unavailable.Count > 0)
            return BadRequest(new
            {
                error = "Уберите из корзины недоступные услуги: " + string.Join(", ", unavailable)
            });

        var now = DateTime.UtcNow;
        var orders = new List<Order>();

        foreach (var group in cart.GroupBy(c => c.Service.PerformerId))
        {
            var order = new Order
            {
                ClientId = userId,
                PerformerId = group.Key,
                Status = OrderStatus.New,
                Address = request.Address.Trim(),
                ContactName = request.ContactName.Trim(),
                ContactPhone = request.ContactPhone.Trim(),
                CreatedAt = now,
                Items = group.Select(c => new OrderItem
                {
                    ServiceId = c.ServiceId,
                    ServiceTitle = c.Service.Title,   // название и цена фиксируются на момент оформления
                    UnitPrice = c.Service.Price,
                    Quantity = c.Quantity,
                    DesiredDate = c.DesiredDate,
                    Comment = c.Comment
                }).ToList()
            };

            order.TotalAmount = order.Items.Sum(i => i.UnitPrice * i.Quantity);

            order.History.Add(new OrderStatusHistory
            {
                FromStatus = null,
                ToStatus = OrderStatus.New,
                ChangedByUserId = userId,
                ChangedAt = now
            });

            orders.Add(order);
        }

        _db.Orders.AddRange(orders);
        _db.CartItems.RemoveRange(cart);

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        var ids = orders.Select(o => o.Id).ToList();
        var result = await _db.Orders
            .AsNoTracking()
            .Where(o => ids.Contains(o.Id))
            .OrderBy(o => o.Id)
            .Select(ToDto)
            .ToListAsync();

        return Ok(result);
    }

    // ---------- Просмотр ----------

    [Authorize(Roles = "Client")]
    [HttpGet("my")]
    public async Task<ActionResult<List<OrderDto>>> GetMy([FromQuery] OrderStatus? status)
    {
        var userId = CurrentUserId;

        var q = _db.Orders.AsNoTracking().Where(o => o.ClientId == userId);
        if (status != null)
            q = q.Where(o => o.Status == status);

        var items = await q.OrderByDescending(o => o.CreatedAt).Select(ToDto).ToListAsync();
        return Ok(items);
    }

    [Authorize(Roles = "Performer")]
    [HttpGet("~/api/performer/orders")]
    public async Task<ActionResult<List<OrderDto>>> GetIncoming([FromQuery] OrderStatus? status)
    {
        var userId = CurrentUserId;

        var q = _db.Orders.AsNoTracking().Where(o => o.PerformerId == userId);
        if (status != null)
            q = q.Where(o => o.Status == status);

        var items = await q.OrderByDescending(o => o.CreatedAt).Select(ToDto).ToListAsync();
        return Ok(items);
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("~/api/admin/orders")]
    public async Task<ActionResult<PagedResult<OrderDto>>> GetAll(
        [FromQuery] OrderStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var q = _db.Orders.AsNoTracking().AsQueryable();
        if (status != null)
            q = q.Where(o => o.Status == status);

        var total = await q.CountAsync();
        var items = await q
            .OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToDto)
            .ToListAsync();

        return Ok(new PagedResult<OrderDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        });
    }

    [Authorize]
    [HttpGet("{id:int}")]
    public async Task<ActionResult<OrderDto>> GetById(int id)
    {
        var userId = CurrentUserId;
        var role = User.FindFirst("role")?.Value;

        var q = _db.Orders.AsNoTracking().Where(o => o.Id == id);

        // клиент видит только свои заказы, исполнитель только входящие, админ все
        if (role == nameof(UserRole.Client))
            q = q.Where(o => o.ClientId == userId);
        else if (role == nameof(UserRole.Performer))
            q = q.Where(o => o.PerformerId == userId);
        else if (role != nameof(UserRole.Admin))
            return Forbid();

        var dto = await q.Select(ToDto).FirstOrDefaultAsync();
        if (dto == null)
            return NotFound(new { error = "Заказ не найден" });

        dto.History = await _db.OrderStatusHistory
            .AsNoTracking()
            .Where(h => h.OrderId == id)
            .OrderBy(h => h.ChangedAt).ThenBy(h => h.Id)
            .Select(h => new OrderHistoryDto
            {
                FromStatus = h.FromStatus == null ? null : h.FromStatus.Value.ToString(),
                ToStatus = h.ToStatus.ToString(),
                ChangedByUserId = h.ChangedByUserId,
                ChangedByName = h.ChangedByUser.FullName,
                ChangedAt = h.ChangedAt
            })
            .ToListAsync();

        return Ok(dto);
    }

    // ---------- Смена статуса ----------

    [Authorize(Roles = "Client")]
    [HttpPost("{id:int}/cancel")]
    public Task<ActionResult<OrderDto>> Cancel(int id) =>
        ChangeStatus(id, OrderStatus.New, OrderStatus.Cancelled, asClient: true);

    [Authorize(Roles = "Performer")]
    [HttpPost("{id:int}/confirm")]
    public Task<ActionResult<OrderDto>> Confirm(int id) =>
        ChangeStatus(id, OrderStatus.New, OrderStatus.Confirmed, asClient: false);

    [Authorize(Roles = "Performer")]
    [HttpPost("{id:int}/reject")]
    public Task<ActionResult<OrderDto>> Reject(int id) =>
        ChangeStatus(id, OrderStatus.New, OrderStatus.Cancelled, asClient: false);

    [Authorize(Roles = "Performer")]
    [HttpPost("{id:int}/start")]
    public Task<ActionResult<OrderDto>> Start(int id) =>
        ChangeStatus(id, OrderStatus.Confirmed, OrderStatus.InProgress, asClient: false);

    [Authorize(Roles = "Performer")]
    [HttpPost("{id:int}/complete")]
    public Task<ActionResult<OrderDto>> Complete(int id) =>
        ChangeStatus(id, OrderStatus.InProgress, OrderStatus.Completed, asClient: false);

    private async Task<ActionResult<OrderDto>> ChangeStatus(
        int id, OrderStatus from, OrderStatus to, bool asClient)
    {
        var userId = CurrentUserId;

        // чужой заказ для пользователя «не существует»
        var q = _db.Orders.Where(o => o.Id == id);
        q = asClient
            ? q.Where(o => o.ClientId == userId)
            : q.Where(o => o.PerformerId == userId);

        var current = await q.Select(o => (OrderStatus?)o.Status).FirstOrDefaultAsync();

        if (current == null)
            return NotFound(new { error = "Заказ не найден" });

        if (current != from)
            return Conflict(new
            {
                error = $"Нельзя перевести заказ из статуса {current} в статус {to}"
            });

        await using var tx = await _db.Database.BeginTransactionAsync();

        // условное обновление: сработает, только если статус всё ещё прежний
        var updated = await _db.Orders
            .Where(o => o.Id == id && o.Status == from)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, to));

        if (updated == 0)
            return Conflict(new { error = "Статус заказа уже изменился, обновите данные" });

        _db.OrderStatusHistory.Add(new OrderStatusHistory
        {
            OrderId = id,
            FromStatus = from,
            ToStatus = to,
            ChangedByUserId = userId,
            ChangedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        var dto = await _db.Orders.AsNoTracking()
            .Where(o => o.Id == id)
            .Select(ToDto)
            .FirstAsync();

        return Ok(dto);
    }
}