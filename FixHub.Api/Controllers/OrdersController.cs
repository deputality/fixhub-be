using System.Linq.Expressions;
using FixHub.Api.Data;
using FixHub.Api.DTOs;
using FixHub.Api.Models;
using FixHub.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FixHub.Api.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<OrdersController> _logger;
    private readonly INotificationService _notifications;

    public OrdersController(AppDbContext db, ILogger<OrdersController> logger, INotificationService notifications)
    {
        _db = db;
        _logger = logger;
        _notifications = notifications;
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
        PerformerPhone = o.Status == OrderStatus.Confirmed
            || o.Status == OrderStatus.InProgress
            || o.Status == OrderStatus.Completed
                ? o.Performer.Phone
                : null,
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
            // название и цена фиксируются на момент оформления
            var items = group.Select(c => NewItem(c.Service, c.Quantity, c.DesiredDate, c.Comment)).ToList();
            orders.Add(NewOrder(userId, group.Key, request.Address, request.ContactName, request.ContactPhone, items, now));
        }

        _db.Orders.AddRange(orders);
        _db.CartItems.RemoveRange(cart);

        await _db.SaveChangesAsync();

        foreach (var order in orders)
        {
            _notifications.Add(
                order.PerformerId,
                NotificationType.NewOrder,
                $"Новый заказ №{order.Id} на сумму {order.TotalAmount:0.##}",
                order.Id);
        }

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        _logger.LogInformation(
            "Клиент {ClientId} оформил заказы {OrderIds} на сумму {Total}",
            userId,
            string.Join(", ", orders.Select(o => o.Id)),
            orders.Sum(o => o.TotalAmount));

        var ids = orders.Select(o => o.Id).ToList();
        var result = await _db.Orders
            .AsNoTracking()
            .Where(o => ids.Contains(o.Id))
            .OrderBy(o => o.Id)
            .Select(ToDto)
            .ToListAsync();

        return Ok(result);
    }

    // Заказ одной услуги без корзины: кнопка «Заказать» на странице услуги.
    // Корзина клиента при этом не трогается.
    [Authorize(Roles = "Client")]
    [HttpPost("quick")]
    public async Task<ActionResult<OrderDto>> QuickOrder(QuickOrderRequest request)
    {
        var userId = CurrentUserId;

        // запас в один день из-за разницы часовых поясов
        var earliest = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        if (request.DesiredDate != null && request.DesiredDate < earliest)
            return BadRequest(new { error = "Желаемая дата не может быть в прошлом" });

        var service = await _db.Services
            .Include(s => s.Performer)
            .Include(s => s.Category).ThenInclude(c => c.Parent)
            .FirstOrDefaultAsync(s => s.Id == request.ServiceId);

        if (service == null)
            return NotFound(new { error = "Услуга не найдена" });

        if (!service.IsActive || service.Performer.IsBlocked
            || service.Category.IsHidden || service.Category.Parent?.IsHidden == true)
            return BadRequest(new { error = "Эта услуга недоступна для заказа" });

        var now = DateTime.UtcNow;
        var order = NewOrder(
            userId, service.PerformerId, request.Address, request.ContactName, request.ContactPhone,
            new List<OrderItem> { NewItem(service, request.Quantity, request.DesiredDate, request.Comment) },
            now);

        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        _notifications.Add(
            order.PerformerId,
            NotificationType.NewOrder,
            $"Новый заказ №{order.Id} на сумму {order.TotalAmount:0.##}",
            order.Id);

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Клиент {ClientId} заказал услугу {ServiceId} напрямую, заказ {OrderId} на сумму {Total}",
            userId, service.Id, order.Id, order.TotalAmount);

        var result = await _db.Orders
            .AsNoTracking()
            .Where(o => o.Id == order.Id)
            .Select(ToDto)
            .FirstAsync();

        return Ok(result);
    }

    private static OrderItem NewItem(Service service, int quantity, DateOnly? desiredDate, string? comment) => new()
    {
        ServiceId = service.Id,
        ServiceTitle = service.Title,
        UnitPrice = service.Price,
        Quantity = quantity,
        DesiredDate = desiredDate,
        Comment = comment?.Trim()
    };

    private static Order NewOrder(
        int clientId, int performerId, string address, string contactName, string contactPhone,
        List<OrderItem> items, DateTime now)
    {
        var order = new Order
        {
            ClientId = clientId,
            PerformerId = performerId,
            Status = OrderStatus.New,
            Address = address.Trim(),
            ContactName = contactName.Trim(),
            ContactPhone = contactPhone.Trim(),
            CreatedAt = now,
            Items = items
        };

        order.TotalAmount = order.Items.Sum(i => i.UnitPrice * i.Quantity);

        order.History.Add(new OrderStatusHistory
        {
            FromStatus = null,
            ToStatus = OrderStatus.New,
            ChangedByUserId = clientId,
            ChangedAt = now
        });

        return order;
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
        ChangeStatus(id, OrderStatus.Cancelled, OrderActor.Client);

    [Authorize(Roles = "Performer")]
    [HttpPost("{id:int}/confirm")]
    public Task<ActionResult<OrderDto>> Confirm(int id) =>
        ChangeStatus(id, OrderStatus.Confirmed, OrderActor.Performer);

    [Authorize(Roles = "Performer")]
    [HttpPost("{id:int}/reject")]
    public Task<ActionResult<OrderDto>> Reject(int id) =>
        ChangeStatus(id, OrderStatus.Cancelled, OrderActor.Performer);

    [Authorize(Roles = "Performer")]
    [HttpPost("{id:int}/start")]
    public Task<ActionResult<OrderDto>> Start(int id) =>
        ChangeStatus(id, OrderStatus.InProgress, OrderActor.Performer);

    [Authorize(Roles = "Performer")]
    [HttpPost("{id:int}/complete")]
    public Task<ActionResult<OrderDto>> Complete(int id) =>
        ChangeStatus(id, OrderStatus.Completed, OrderActor.Performer);

    private async Task<ActionResult<OrderDto>> ChangeStatus(int id, OrderStatus to, OrderActor actor)
    {
        var userId = CurrentUserId;

        // чужой заказ для пользователя «не существует»
        var q = _db.Orders.Where(o => o.Id == id);
        q = actor == OrderActor.Client
            ? q.Where(o => o.ClientId == userId)
            : q.Where(o => o.PerformerId == userId);

        // кроме статуса берём обоих участников, чтобы знать, кого уведомить
        var order = await q
            .Select(o => new { o.Status, o.ClientId, o.PerformerId })
            .FirstOrDefaultAsync();

        if (order == null)
            return NotFound(new { error = "Заказ не найден" });

        var from = order.Status;

        // правила переходов хранятся в одном месте и покрыты unit-тестами
        if (!OrderStatusRules.CanTransition(from, to, actor))
            return Conflict(new
            {
                error = $"Нельзя перевести заказ из статуса {from} в статус {to}"
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

        // Если статус меняет исполнитель, уведомляем клиента, и наоборот
        var receiverId = actor == OrderActor.Client ? order.PerformerId : order.ClientId;
        _notifications.Add(receiverId, NotificationType.OrderStatusChanged, $"Заказ №{id} {StatusText(to)}", id);

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        _logger.LogInformation(
            "Заказ {OrderId}: статус {From} -> {To}, изменил пользователь {UserId}",
            id, from, to, userId);

        var dto = await _db.Orders.AsNoTracking()
            .Where(o => o.Id == id)
            .Select(ToDto)
            .FirstAsync();

        return Ok(dto);


    }
    private static string StatusText(OrderStatus status) => status switch
    {
        OrderStatus.Confirmed => "подтверждён",
        OrderStatus.InProgress => "взят в работу",
        OrderStatus.Completed => "выполнен",
        OrderStatus.Cancelled => "отменён",
        _ => status.ToString()
    };
}