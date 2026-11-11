using FixHub.Api.Models;

namespace FixHub.Api.Services;

public enum OrderActor
{
    Client,
    Performer
}

public record OrderTransition(OrderStatus From, OrderStatus To, OrderActor Actor);

public static class OrderStatusRules
{
    // Единственное место, где описано, кто и куда может перевести заказ
    private static readonly OrderTransition[] Allowed =
    {
        new(OrderStatus.New, OrderStatus.Cancelled, OrderActor.Client),         // клиент отменяет новый заказ
        new(OrderStatus.New, OrderStatus.Confirmed, OrderActor.Performer),      // исполнитель подтверждает
        new(OrderStatus.New, OrderStatus.Cancelled, OrderActor.Performer),      // исполнитель отклоняет
        new(OrderStatus.Confirmed, OrderStatus.InProgress, OrderActor.Performer),
        new(OrderStatus.InProgress, OrderStatus.Completed, OrderActor.Performer)
    };

    public static bool CanTransition(OrderStatus from, OrderStatus to, OrderActor actor) =>
        Array.Exists(Allowed, t => t.From == from && t.To == to && t.Actor == actor);

    public static IReadOnlyList<OrderStatus> NextStatuses(OrderStatus from, OrderActor actor) =>
        Allowed
            .Where(t => t.From == from && t.Actor == actor)
            .Select(t => t.To)
            .ToList();
}
