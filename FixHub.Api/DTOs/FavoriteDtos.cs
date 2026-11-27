namespace FixHub.Api.DTOs;

public class FavoritePerformerDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = null!;
    public string? Specialization { get; set; }
    public string? City { get; set; }
    public string? AvatarUrl { get; set; }
    public bool IsVerified { get; set; }
    public decimal? Rating { get; set; }
    public int ReviewCount { get; set; }
    public int ServicesCount { get; set; }
}

public class FavoritesDto
{
    // Только то, что клиент увидит в каталоге: скрытые и заблокированные не показываются
    public List<ServiceDto> Services { get; set; } = new();
    public List<FavoritePerformerDto> Performers { get; set; } = new();
}

// Для сердечек на карточках: какие услуги и мастера уже в избранном
public class FavoriteIdsDto
{
    public List<int> ServiceIds { get; set; } = new();
    public List<int> PerformerIds { get; set; } = new();
}
