using FixHub.Api.DTOs;
using FixHub.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FixHub.Api.Hubs;

// Чат по заказу в реальном времени (WebSocket). Подключаться могут клиенты и мастера;
// в комнату заказа пускают только его участников.
//
// События для клиента:
//   MessageReceived(message)          новое сообщение в открытом чате
//   MessagesRead({ orderId, readerId }) собеседник прочитал сообщения
//   UserTyping({ orderId, userId })   собеседник печатает
//   UnreadChanged({ orderId, count }) сколько непрочитанных в чате (для значков)
//   NotificationsChanged              появилось или прочиталось уведомление
//   OrdersChanged({ orderId })        заказ создан или сменил статус: пора обновить список заказов
[Authorize(Roles = "Client,Performer")]
public class ChatHub : Hub
{
    private readonly IChatService _chat;

    public ChatHub(IChatService chat)
    {
        _chat = chat;
    }

    public static string OrderGroup(int orderId) => $"order-{orderId}";

    // личная комната человека: сюда приходят значки и уведомления, в какой бы вкладке он ни был
    public static string UserGroup(int userId) => $"user-{userId}";

    private int UserId => int.Parse(Context.User!.FindFirst("sub")!.Value);

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(UserId));
        await base.OnConnectedAsync();
    }

    // Войти в комнату заказа; false, если человек не участник заказа
    public async Task<bool> JoinOrder(int orderId)
    {
        if (!await _chat.IsMemberAsync(UserId, orderId))
            return false;

        await Groups.AddToGroupAsync(Context.ConnectionId, OrderGroup(orderId));
        return true;
    }

    public Task LeaveOrder(int orderId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, OrderGroup(orderId));

    public Task<ChatSendResultDto> SendMessage(int orderId, string text) =>
        _chat.SendAsync(UserId, orderId, text);

    public async Task MarkRead(int orderId)
    {
        await _chat.MarkReadAsync(UserId, orderId);
    }

    // «Печатает...»: ничего не сохраняем, просто сообщаем собеседнику
    public async Task Typing(int orderId)
    {
        if (!await _chat.IsMemberAsync(UserId, orderId))
            return;

        await Clients.OthersInGroup(OrderGroup(orderId)).SendAsync("UserTyping", new { orderId, userId = UserId });
    }
}
