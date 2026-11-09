namespace FixHub.Api.Models;

public class Order
{
    public int Id { get; set; }

    public int ClientId { get; set; }
    public User Client { get; set; } = null!;

    public int PerformerId { get; set; }
    public User Performer { get; set; } = null!;

    public OrderStatus Status { get; set; } = OrderStatus.New;
    public decimal TotalAmount { get; set; }

    public string Address { get; set; } = null!;
    public string ContactName { get; set; } = null!;
    public string ContactPhone { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<OrderItem> Items { get; set; } = new();
    public List<OrderStatusHistory> History { get; set; } = new();
}