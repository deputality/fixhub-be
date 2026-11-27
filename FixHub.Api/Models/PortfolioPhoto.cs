namespace FixHub.Api.Models;

// Дополнительное фото работы в портфолио мастера
public class PortfolioPhoto
{
    public int Id { get; set; }

    public int PortfolioItemId { get; set; }
    public PortfolioItem PortfolioItem { get; set; } = null!;

    public string Url { get; set; } = null!;
    public int SortOrder { get; set; }
}
