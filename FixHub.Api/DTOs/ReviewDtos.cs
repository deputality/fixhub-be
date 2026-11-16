using System.ComponentModel.DataAnnotations;

namespace FixHub.Api.DTOs;

// Что присылает клиент при создании отзыва
public class CreateReviewRequest
{
    [Required]
    public int OrderItemId { get; set; }

    [Range(1, 5)]
    public int Rating { get; set; }

    [Required, MaxLength(2000)]
    public string Text { get; set; } = null!;
}

// Отзыв в ответах API
public class ReviewDto
{
    public int Id { get; set; }
    public int OrderItemId { get; set; }

    public int ServiceId { get; set; }
    public string ServiceTitle { get; set; } = null!;

    public int ClientId { get; set; }
    public string ClientName { get; set; } = null!;

    public int Rating { get; set; }
    public string Text { get; set; } = null!;
    public bool IsHidden { get; set; }
    public DateTime CreatedAt { get; set; }
}

// Отзывы об исполнителе с итоговым рейтингом
public class PerformerReviewsDto
{
    public decimal? Rating { get; set; }
    public int ReviewCount { get; set; }
    public PagedResult<ReviewDto> Reviews { get; set; } = new();
}

// Параметры списка отзывов (в адресе запроса)
public class ReviewQuery
{
    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 50)]
    public int PageSize { get; set; } = 10;
}

// Для администратора: список отзывов с фильтром по скрытым
public class AdminReviewQuery : ReviewQuery
{
    public bool? IsHidden { get; set; }
}
