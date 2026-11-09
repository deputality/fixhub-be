namespace FixHub.Api.Models;

public class CartItem
{
    public int Id { get; set; }

    public int ClientId { get; set; }
    public User Client { get; set; } = null!;

    public int ServiceId { get; set; }
    public Service Service { get; set; } = null!;

    public int Quantity { get; set; } = 1;
    public DateOnly? DesiredDate { get; set; }
    public string? Comment { get; set; }
}