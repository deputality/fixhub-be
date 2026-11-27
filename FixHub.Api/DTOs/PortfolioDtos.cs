using System.ComponentModel.DataAnnotations;

namespace FixHub.Api.DTOs;

// Что присылает мастер при добавлении и изменении работы
public class PortfolioItemRequest
{
    [Required, MaxLength(200)]
    public string Title { get; set; } = null!;

    [MaxLength(1000)]
    public string? Description { get; set; }

    // Адреса берутся из ответа POST /api/performer/uploads
    [MaxLength(300)]
    public string? BeforeUrl { get; set; }

    [Required, MaxLength(300)]
    public string AfterUrl { get; set; } = null!;

    // Дополнительные фото работы, до 10 штук
    [MaxLength(10, ErrorMessage = "Дополнительных фото не больше 10")]
    public List<string>? PhotoUrls { get; set; }
}

public class PortfolioItemDto
{
    public int Id { get; set; }
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public string? BeforeUrl { get; set; }
    public string AfterUrl { get; set; } = null!;
    public List<string> PhotoUrls { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}
