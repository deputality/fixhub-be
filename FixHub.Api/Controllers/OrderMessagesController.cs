using FixHub.Api.Data;
using FixHub.Api.DTOs;
using FixHub.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FixHub.Api.Controllers;

// Чат по заказу. Писать и читать могут только клиент и мастер этого заказа
[ApiController]
[Route("api/orders")]
[Authorize(Roles = "Client,Performer")]
public class OrderMessagesController : ControllerBase
{
    private const int MaxHistory = 200;
    private const int MaxPerMinute = 15;

    private readonly AppDbContext _db;
    private readonly ILogger<OrderMessagesController> _logger;

    public OrderMessagesController(AppDbContext db, ILogger<OrderMessagesController> logger)
    {
        _db = db;
        _logger = logger;
    }

    private int CurrentUserId => int.Parse(User.FindFirst("sub")!.Value);

    // Переписка по заказу (последние 200 сообщений, старые выше).
    // Заодно помечаем сообщения собеседника прочитанными и гасим уведомление о них
    [HttpGet("{id:int}/messages")]
    public async Task<ActionResult<List<OrderMessageDto>>> Get(int id)
    {
        var userId = CurrentUserId;

        // чужой заказ для пользователя «не существует»
        var exists = await _db.Orders.AsNoTracking()
            .AnyAsync(o => o.Id == id && (o.ClientId == userId || o.PerformerId == userId));
        if (!exists)
            return NotFound(new { error = "Заказ не найден" });

        DateTime? now = DateTime.UtcNow;

        await _db.OrderMessages
            .Where(m => m.OrderId == id && m.SenderId != userId && m.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ReadAt, now));

        await _db.Notifications
            .Where(n => n.UserId == userId && n.OrderId == id && n.Type == NotificationType.NewMessage && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));

        var messages = await _db.OrderMessages
            .AsNoTracking()
            .Where(m => m.OrderId == id)
            .OrderByDescending(m => m.Id)
            .Take(MaxHistory)
            .Select(m => new OrderMessageDto
            {
                Id = m.Id,
                OrderId = m.OrderId,
                SenderId = m.SenderId,
                SenderName = m.Sender.FullName,
                Text = m.Text,
                CreatedAt = m.CreatedAt,
                IsRead = m.ReadAt != null
            })
            .ToListAsync();

        messages.Reverse();
        return Ok(messages);
    }

    // Отправить сообщение собеседнику
    [HttpPost("{id:int}/messages")]
    public async Task<ActionResult<OrderMessageDto>> Send(int id, SendMessageRequest request)
    {
        var userId = CurrentUserId;

        var order = await _db.Orders.AsNoTracking()
            .Where(o => o.Id == id && (o.ClientId == userId || o.PerformerId == userId))
            .Select(o => new { o.ClientId, o.PerformerId, o.Status })
            .FirstOrDefaultAsync();
        if (order == null)
            return NotFound(new { error = "Заказ не найден" });

        if (order.Status == OrderStatus.Cancelled)
            return BadRequest(new { error = "Заказ отменён, писать в чат больше нельзя" });

        var text = request.Text.Trim();
        if (text.Length == 0)
            return BadRequest(new { error = "Сообщение не может быть пустым" });

        // защита от спама: не больше 15 сообщений в минуту от одного человека
        var since = DateTime.UtcNow.AddMinutes(-1);
        var recent = await _db.OrderMessages.CountAsync(m => m.SenderId == userId && m.CreatedAt > since);
        if (recent >= MaxPerMinute)
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { error = "Слишком много сообщений. Подождите минуту" });

        var senderName = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.FullName)
            .FirstAsync();

        var message = new OrderMessage
        {
            OrderId = id,
            SenderId = userId,
            Text = text,
            CreatedAt = DateTime.UtcNow
        };
        _db.OrderMessages.Add(message);

        // Одно уведомление на чат, пока оно не прочитано: иначе колокольчик забьётся сообщениями.
        // Письмо по почте для сообщений не шлём, поэтому сразу помечаем его отправленным
        var recipientId = userId == order.ClientId ? order.PerformerId : order.ClientId;
        var hasUnread = await _db.Notifications.AnyAsync(n =>
            n.UserId == recipientId && n.OrderId == id && n.Type == NotificationType.NewMessage && !n.IsRead);
        if (!hasUnread)
        {
            _db.Notifications.Add(new Notification
            {
                UserId = recipientId,
                Type = NotificationType.NewMessage,
                Text = $"Новое сообщение по заказу №{id} от {senderName}",
                OrderId = id,
                CreatedAt = DateTime.UtcNow,
                EmailSentAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation("Пользователь {UserId} написал в чат заказа {OrderId}", userId, id);

        return Ok(new OrderMessageDto
        {
            Id = message.Id,
            OrderId = id,
            SenderId = userId,
            SenderName = senderName,
            Text = message.Text,
            CreatedAt = message.CreatedAt,
            IsRead = false
        });
    }

    // Сколько непрочитанных сообщений в каждом чате: для значков на кнопках «Чат»
    [HttpGet("messages/unread")]
    public async Task<ActionResult<List<UnreadChatDto>>> Unread()
    {
        var userId = CurrentUserId;

        var items = await _db.OrderMessages
            .AsNoTracking()
            .Where(m => m.ReadAt == null
                        && m.SenderId != userId
                        && (m.Order.ClientId == userId || m.Order.PerformerId == userId))
            .GroupBy(m => m.OrderId)
            .Select(g => new UnreadChatDto { OrderId = g.Key, Count = g.Count() })
            .ToListAsync();

        return Ok(items);
    }
}
