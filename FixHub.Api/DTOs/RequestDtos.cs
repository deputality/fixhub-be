using System.ComponentModel.DataAnnotations;

namespace FixHub.Api.DTOs;

// Клиент описывает задачу
public class CreateRequestRequest
{
    [Required]
    public int CategoryId { get; set; }

    [Required, MaxLength(200)]
    public string Title { get; set; } = null!;

    [Required, MaxLength(2000)]
    public string Description { get; set; } = null!;

    [Required, MaxLength(500)]
    public string Address { get; set; } = null!;

    [Required, MaxLength(200)]
    public string ContactName { get; set; } = null!;

    [Required, MaxLength(30)]
    public string ContactPhone { get; set; } = null!;

    public DateOnly? DesiredDate { get; set; }
}

// Предложение мастера: его услуга и цена
public class SaveOfferRequest
{
    [Required]
    public int ServiceId { get; set; }

    [Range(0.01, 1000000)]
    public decimal Price { get; set; }

    [MaxLength(1000)]
    public string? Message { get; set; }
}

public class OfferDto
{
    public int Id { get; set; }
    public int RequestId { get; set; }

    public int PerformerId { get; set; }
    public string PerformerName { get; set; } = null!;
    public string? PerformerAvatarUrl { get; set; }
    public decimal? PerformerRating { get; set; }
    public bool PerformerIsVerified { get; set; }

    public int ServiceId { get; set; }
    public string ServiceTitle { get; set; } = null!;

    public decimal Price { get; set; }
    public string? Message { get; set; }
    public string Status { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}

public class RequestDto
{
    public int Id { get; set; }

    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = null!;

    public string Title { get; set; } = null!;
    public string Description { get; set; } = null!;

    // Мастеру адрес и контакты клиента видны только после выбора его предложения
    public string? Address { get; set; }
    public string? ContactName { get; set; }
    public string? ContactPhone { get; set; }

    public DateOnly? DesiredDate { get; set; }
    public string Status { get; set; } = null!;
    public DateTime CreatedAt { get; set; }

    public int ClientId { get; set; }
    public string ClientName { get; set; } = null!;

    // Сколько предложений ждут выбора (отозванные не считаются)
    public int OffersCount { get; set; }

    // Заказ, созданный после выбора предложения
    public int? OrderId { get; set; }

    // Только в списке мастера: его предложение по заявке
    public OfferDto? MyOffer { get; set; }
}

// Заявка вместе со всеми предложениями (для клиента)
public class RequestDetailDto : RequestDto
{
    public List<OfferDto> Offers { get; set; } = new();
}

public class AcceptOfferResultDto
{
    public RequestDto Request { get; set; } = null!;
    public OrderDto Order { get; set; } = null!;
}
