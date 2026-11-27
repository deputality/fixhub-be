namespace FixHub.Api.Models;

public class OrderStatusHistory
{
    public int Id { get; set; }

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public OrderStatus? FromStatus { get; set; }
    public OrderStatus ToStatus { get; set; }

    public int ChangedByUserId { get; set; }
    public User ChangedByUser { get; set; } = null!;

    // Причина: заполняется при отмене или отклонении заказа
    public string? Reason { get; set; }

    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}