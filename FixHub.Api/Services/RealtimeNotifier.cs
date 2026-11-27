using FixHub.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace FixHub.Api.Services;

// Сообщает людям по WebSocket, что у них что-то изменилось, чтобы страница обновилась сама,
// без перезагрузки. Вызывать нужно ПОСЛЕ сохранения в базе. Если человека сейчас нет на сайте,
// ничего страшного: при следующем открытии страницы данные загрузятся как обычно.
public interface IRealtimeNotifier
{
    // Заказ создан или сменил статус: у этих людей обновится список заказов и колокольчик
    Task OrderChangedAsync(int orderId, params int[] userIds);

    // Появилось новое уведомление (например, предложение по заявке или отзыв)
    Task NotificationsChangedAsync(params int[] userIds);
}

public class RealtimeNotifier : IRealtimeNotifier
{
    private readonly IHubContext<ChatHub> _hub;
    private readonly ILogger<RealtimeNotifier> _logger;

    public RealtimeNotifier(IHubContext<ChatHub> hub, ILogger<RealtimeNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task OrderChangedAsync(int orderId, params int[] userIds)
    {
        foreach (var userId in userIds.Distinct())
        {
            await SendAsync(userId, "OrdersChanged", new { orderId });
            await SendAsync(userId, "NotificationsChanged");
        }
    }

    public async Task NotificationsChangedAsync(params int[] userIds)
    {
        foreach (var userId in userIds.Distinct())
            await SendAsync(userId, "NotificationsChanged");
    }

    // Заказ уже сохранён: сбой доставки не должен превращаться в ошибку для того, кто его оформил
    private async Task SendAsync(int userId, string method, object? arg = null)
    {
        try
        {
            var group = _hub.Clients.Group(ChatHub.UserGroup(userId));
            if (arg == null)
                await group.SendAsync(method);
            else
                await group.SendAsync(method, arg);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось отправить {Method} пользователю {UserId}", method, userId);
        }
    }
}
