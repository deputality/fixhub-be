using FixHub.Api.Data;
using FixHub.Api.DTOs;
using FixHub.Api.Hubs;
using FixHub.Api.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace FixHub.Api.Services;

public interface IChatService
{
    // Участник заказа (клиент или мастер) и не заблокирован
    Task<bool> IsMemberAsync(int userId, int orderId);

    // Переписка (последние 200 сообщений); null, если заказа для этого человека «не существует».
    // Заодно помечает сообщения собеседника прочитанными
    Task<List<OrderMessageDto>?> GetHistoryAsync(int userId, int orderId);

    Task<ChatSendResultDto> SendAsync(int userId, int orderId, string? text);

    // Пометить сообщения собеседника прочитанными; false, если человек не участник заказа
    Task<bool> MarkReadAsync(int userId, int orderId);
}

// Вся логика чата в одном месте: её используют и обычные запросы (OrderMessagesController),
// и WebSocket (ChatHub). После каждого изменения участникам уходят события в реальном времени.
public class ChatService : IChatService
{
    private const int MaxHistory = 200;
    private const int MaxPerMinute = 15;
    private const int MaxLength = 1000;

    private readonly AppDbContext _db;
    private readonly IHubContext<ChatHub> _hub;
    private readonly ILogger<ChatService> _logger;

    public ChatService(AppDbContext db, IHubContext<ChatHub> hub, ILogger<ChatService> logger)
    {
        _db = db;
        _hub = hub;
        _logger = logger;
    }

    // заказ, в котором человек участвует; заблокированный пользователь ничего не находит
    private IQueryable<Order> MemberOrders(int userId, int orderId) =>
        _db.Orders.Where(o => o.Id == orderId
                              && (o.ClientId == userId || o.PerformerId == userId)
                              && _db.Users.Any(u => u.Id == userId && !u.IsBlocked));

    public Task<bool> IsMemberAsync(int userId, int orderId) =>
        MemberOrders(userId, orderId).AsNoTracking().AnyAsync();

    public async Task<List<OrderMessageDto>?> GetHistoryAsync(int userId, int orderId)
    {
        if (!await IsMemberAsync(userId, orderId))
            return null;

        await MarkReadCoreAsync(userId, orderId);

        var messages = await _db.OrderMessages
            .AsNoTracking()
            .Where(m => m.OrderId == orderId)
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
        return messages;
    }

    public async Task<bool> MarkReadAsync(int userId, int orderId)
    {
        if (!await IsMemberAsync(userId, orderId))
            return false;

        await MarkReadCoreAsync(userId, orderId);
        return true;
    }

    private async Task MarkReadCoreAsync(int userId, int orderId)
    {
        DateTime? now = DateTime.UtcNow;

        var read = await _db.OrderMessages
            .Where(m => m.OrderId == orderId && m.SenderId != userId && m.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ReadAt, now));

        var notifications = await _db.Notifications
            .Where(n => n.UserId == userId && n.OrderId == orderId && n.Type == NotificationType.NewMessage && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));

        if (read > 0)
        {
            // собеседник сразу увидит «прочитано», а у самого человека (в других вкладках) погаснет значок
            await _hub.Clients.Group(ChatHub.OrderGroup(orderId))
                .SendAsync("MessagesRead", new { orderId, readerId = userId });
            await _hub.Clients.Group(ChatHub.UserGroup(userId))
                .SendAsync("UnreadChanged", new { orderId, count = 0 });
        }

        if (notifications > 0)
            await _hub.Clients.Group(ChatHub.UserGroup(userId)).SendAsync("NotificationsChanged");
    }

    public async Task<ChatSendResultDto> SendAsync(int userId, int orderId, string? text)
    {
        var order = await MemberOrders(userId, orderId).AsNoTracking()
            .Select(o => new { o.ClientId, o.PerformerId, o.Status })
            .FirstOrDefaultAsync();
        if (order == null)
            return Fail(404, "Заказ не найден");

        if (order.Status == OrderStatus.Cancelled)
            return Fail(400, "Заказ отменён, писать в чат больше нельзя");

        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0)
            return Fail(400, "Сообщение не может быть пустым");

        // обычный запрос проверяется атрибутами, а через WebSocket длину нужно проверить самим
        if (value.Length > MaxLength)
            return Fail(400, $"Сообщение длиннее {MaxLength} символов");

        // защита от спама: не больше 15 сообщений в минуту от одного человека
        var since = DateTime.UtcNow.AddMinutes(-1);
        var recent = await _db.OrderMessages.CountAsync(m => m.SenderId == userId && m.CreatedAt > since);
        if (recent >= MaxPerMinute)
            return Fail(429, "Слишком много сообщений. Подождите минуту");

        var senderName = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.FullName)
            .FirstAsync();

        var message = new OrderMessage
        {
            OrderId = orderId,
            SenderId = userId,
            Text = value,
            CreatedAt = DateTime.UtcNow
        };
        _db.OrderMessages.Add(message);

        // Одно уведомление на чат, пока оно не прочитано: иначе колокольчик забьётся сообщениями.
        // Письмо по почте для сообщений не шлём, поэтому сразу помечаем его отправленным
        var recipientId = userId == order.ClientId ? order.PerformerId : order.ClientId;
        var hasUnread = await _db.Notifications.AnyAsync(n =>
            n.UserId == recipientId && n.OrderId == orderId && n.Type == NotificationType.NewMessage && !n.IsRead);
        if (!hasUnread)
        {
            _db.Notifications.Add(new Notification
            {
                UserId = recipientId,
                Type = NotificationType.NewMessage,
                Text = $"Новое сообщение по заказу №{orderId} от {senderName}",
                OrderId = orderId,
                CreatedAt = DateTime.UtcNow,
                EmailSentAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation("Пользователь {UserId} написал в чат заказа {OrderId}", userId, orderId);

        var dto = new OrderMessageDto
        {
            Id = message.Id,
            OrderId = orderId,
            SenderId = userId,
            SenderName = senderName,
            Text = message.Text,
            CreatedAt = message.CreatedAt,
            IsRead = false
        };

        await PublishAsync(dto, recipientId, newNotification: !hasUnread);

        return new ChatSendResultDto { Ok = true, Status = 200, Message = dto };
    }

    // Рассылка новых данных. Сообщение уже сохранено, поэтому сбой рассылки (например, оборвалась связь)
    // не должен ломать отправку: собеседник всё равно увидит сообщение при следующей загрузке чата
    private async Task PublishAsync(OrderMessageDto message, int recipientId, bool newNotification)
    {
        try
        {
            await _hub.Clients.Group(ChatHub.OrderGroup(message.OrderId)).SendAsync("MessageReceived", message);

            var unread = await _db.OrderMessages.CountAsync(m =>
                m.OrderId == message.OrderId && m.SenderId != recipientId && m.ReadAt == null);
            await _hub.Clients.Group(ChatHub.UserGroup(recipientId))
                .SendAsync("UnreadChanged", new { orderId = message.OrderId, count = unread });

            if (newNotification)
                await _hub.Clients.Group(ChatHub.UserGroup(recipientId)).SendAsync("NotificationsChanged");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось разослать сообщение {MessageId} по WebSocket", message.Id);
        }
    }

    private static ChatSendResultDto Fail(int status, string error) =>
        new() { Ok = false, Status = status, Error = error };
}
