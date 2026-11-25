namespace FixHub.Api.Models;

// Работа в портфолио мастера: фото «после» обязательно, фото «до» по желанию
public class PortfolioItem
{
    public int Id { get; set; }

    public int PerformerId { get; set; }
    public User Performer { get; set; } = null!;

    public string Title { get; set; } = null!;
    public string? Description { get; set; }

    public string? BeforeUrl { get; set; }
    public string AfterUrl { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
