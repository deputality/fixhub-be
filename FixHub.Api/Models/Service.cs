namespace FixHub.Api.Models;

public class Service
{
    public int Id { get; set; }

    public int PerformerId { get; set; }
    public User Performer { get; set; } = null!;

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    public string Title { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string? PhotoUrl { get; set; }

    public decimal Price { get; set; }
    public PricingUnit Unit { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}