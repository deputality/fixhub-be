using System.ComponentModel.DataAnnotations;
using FixHub.Api.Models;

namespace FixHub.Api.DTOs;

// Что присылает исполнитель при создании и изменении услуги
public class ServiceRequest
{
    [Required]
    public int CategoryId { get; set; }

    [Required, MaxLength(200)]
    public string Title { get; set; } = null!;

    [Required, MaxLength(4000)]
    public string Description { get; set; } = null!;

    [MaxLength(500)]
    public string? PhotoUrl { get; set; }

    [Range(0.01, 1000000)]
    public decimal Price { get; set; }

    [EnumDataType(typeof(PricingUnit))]
    public PricingUnit Unit { get; set; }
}

// Что API отдаёт клиенту
public class ServiceDto
{
    public int Id { get; set; }
    public string Title { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string? PhotoUrl { get; set; }
    public decimal Price { get; set; }
    public string Unit { get; set; } = null!;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }

    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = null!;

    public int PerformerId { get; set; }
    public string PerformerName { get; set; } = null!;
    public string? PerformerAvatarUrl { get; set; }
    public string? PerformerSpecialization { get; set; }
    public bool PerformerIsVerified { get; set; }
    public string? PerformerCity { get; set; }
    public string? PerformerServiceArea { get; set; }
    public double? PerformerLatitude { get; set; }
    public double? PerformerLongitude { get; set; }

    // Расстояние от точки пользователя в км; заполняется, только если в запросе есть lat и lng
    public double? DistanceKm { get; set; }

    // Рейтинг считается по нескрытым отзывам; null, если отзывов ещё нет
    public decimal? Rating { get; set; }
    public int ReviewCount { get; set; }
    public decimal? PerformerRating { get; set; }
    public int PerformerReviewCount { get; set; }
}

// Способы сортировки каталога
public enum CatalogSort
{
    Newest,
    PriceAsc,
    PriceDesc,
    TitleAsc,
    RatingDesc,
    // Сначала ближайшие; нужны lat и lng. Мастера без точки на карте идут в конце
    Distance
}

// Параметры поиска в каталоге (приходят в адресе запроса)
public class CatalogQuery
{
    [MaxLength(100)]
    public string? Search { get; set; }

    public int? CategoryId { get; set; }

    // Услуги одного мастера (для страницы мастера)
    public int? PerformerId { get; set; }

    // Город или район мастера (часть названия, без учёта регистра)
    [MaxLength(100)]
    public string? Location { get; set; }

    // Точка пользователя для расстояния, радиуса и сортировки «Ближайшие»
    [Range(-90, 90)]
    public double? Lat { get; set; }

    [Range(-180, 180)]
    public double? Lng { get; set; }

    [Range(0.1, 20000)]
    public double? RadiusKm { get; set; }

    [Range(0, 1000000)]
    public decimal? MinPrice { get; set; }

    [Range(0, 1000000)]
    public decimal? MaxPrice { get; set; }

    [Range(1, 5)]
    public decimal? MinRating { get; set; }

    public CatalogSort Sort { get; set; } = CatalogSort.Newest;

    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 50)]
    public int PageSize { get; set; } = 12;
}

// Обёртка для постраничного ответа
public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
}