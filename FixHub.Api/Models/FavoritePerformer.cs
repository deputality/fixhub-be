namespace FixHub.Api.Models;

// Мастер, которого клиент сохранил в избранное
public class FavoritePerformer
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int PerformerId { get; set; }
    public User Performer { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
