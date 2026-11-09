namespace FixHub.Api.Models;

public class OrderItem
{
    public int Id { get; set; }

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public int ServiceId { get; set; }
    public Service Service { get; set; } = null!;

    public string ServiceTitle { get; set; } = null!;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }

    public DateOnly? DesiredDate { get; set; }
    public string? Comment { get; set; }
}