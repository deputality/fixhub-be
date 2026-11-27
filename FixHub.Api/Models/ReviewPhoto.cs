namespace FixHub.Api.Models;

// Фото к отзыву клиента (до 3 штук)
public class ReviewPhoto
{
    public int Id { get; set; }

    public int ReviewId { get; set; }
    public Review Review { get; set; } = null!;

    public string Url { get; set; } = null!;
    public int SortOrder { get; set; }
}
