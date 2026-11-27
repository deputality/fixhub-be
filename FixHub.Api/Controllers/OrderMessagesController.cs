using FixHub.Api.Data;
using FixHub.Api.DTOs;
using FixHub.Api.Models;
using FixHub.Api.Services;
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
    private readonly AppDbContext _db;
    private readonly IChatService _chat;

    public OrderMessagesController(AppDbContext db, IChatService chat)
    {
        _db = db;
        _chat = chat;
    }

    private int CurrentUserId => int.Parse(User.FindFirst("sub")!.Value);

    // Переписка по заказу (последние 200 сообщений, старые выше).
    // Заодно помечаем сообщения собеседника прочитанными и гасим уведомление о них.
    // Новые сообщения в открытом чате приходят по WebSocket (ChatHub), этот запрос нужен для истории
    [HttpGet("{id:int}/messages")]
    public async Task<ActionResult<List<OrderMessageDto>>> Get(int id)
    {
        // чужой заказ для пользователя «не существует»
        var messages = await _chat.GetHistoryAsync(CurrentUserId, id);
        if (messages == null)
            return NotFound(new { error = "Заказ не найден" });

        return Ok(messages);
    }

    // Отправить сообщение собеседнику (запасной путь, если WebSocket недоступен)
    [HttpPost("{id:int}/messages")]
    public async Task<ActionResult<OrderMessageDto>> Send(int id, SendMessageRequest request)
    {
        var result = await _chat.SendAsync(CurrentUserId, id, request.Text);
        if (!result.Ok)
            return StatusCode(result.Status, new { error = result.Error });

        return Ok(result.Message);
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
