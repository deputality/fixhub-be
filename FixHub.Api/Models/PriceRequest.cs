namespace FixHub.Api.Models;

// Заявка клиента: описывает задачу, мастера присылают свои цены, клиент выбирает одну
public class PriceRequest
{
    public int Id { get; set; }

    public int ClientId { get; set; }
    public User Client { get; set; } = null!;

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    public string Title { get; set; } = null!;
    public string Description { get; set; } = null!;

    public string Address { get; set; } = null!;
    public string ContactName { get; set; } = null!;
    public string ContactPhone { get; set; } = null!;
    public DateOnly? DesiredDate { get; set; }

    public RequestStatus Status { get; set; } = RequestStatus.Open;

    // Заказ, который создан после выбора предложения (ссылка без внешнего ключа)
    public int? OrderId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<PriceOffer> Offers { get; set; } = new();
}

// Предложение мастера по заявке. Привязано к одной из услуг мастера,
// чтобы из выбранного предложения получился обычный заказ (с отзывами, рейтингом и т.д.)
public class PriceOffer
{
    public int Id { get; set; }

    public int RequestId { get; set; }
    public PriceRequest Request { get; set; } = null!;

    public int PerformerId { get; set; }
    public User Performer { get; set; } = null!;

    public int ServiceId { get; set; }
    public Service Service { get; set; } = null!;

    public decimal Price { get; set; }
    public string? Message { get; set; }

    public OfferStatus Status { get; set; } = OfferStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
