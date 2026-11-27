using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using FixHub.Api.Common;

namespace FixHub.Api.DTOs;

public class CheckoutRequest
{
    [Required, MaxLength(500)]
    public string Address { get; set; } = null!;

    [Required, MaxLength(200)]
    public string ContactName { get; set; } = null!;

    [Required, MaxLength(30)]
    public string ContactPhone { get; set; } = null!;
}

// «Заказать» без корзины: одна услуга и контакты в одном запросе
public class QuickOrderRequest
{
    [Required]
    public int ServiceId { get; set; }

    [Range(1, 100)]
    public int Quantity { get; set; } = 1;

    public DateOnly? DesiredDate { get; set; }

    // Время слота из расписания мастера, "HH:mm"
    [RegularExpression(TimeFormat.Pattern, ErrorMessage = "Время указывается в формате ЧЧ:ММ")]
    public string? DesiredTime { get; set; }

    [MaxLength(1000)]
    public string? Comment { get; set; }

    [Required, MaxLength(500)]
    public string Address { get; set; } = null!;

    [Required, MaxLength(200)]
    public string ContactName { get; set; } = null!;

    [Required, MaxLength(30)]
    public string ContactPhone { get; set; } = null!;
}

public class OrderItemDto
{
    public int Id { get; set; }
    public int ServiceId { get; set; }
    public string ServiceTitle { get; set; } = null!;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
    public DateOnly? DesiredDate { get; set; }

    // В БД лежит TimeOnly, наружу отдаём строку "HH:mm"
    [JsonIgnore]
    public TimeOnly? DesiredTimeValue { get; set; }

    public string? DesiredTime
    {
        get => TimeFormat.Format(DesiredTimeValue);
        set => DesiredTimeValue = TimeFormat.Parse(value);
    }

    public string? Comment { get; set; }
}

public class OrderHistoryDto
{
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = null!;
    public int ChangedByUserId { get; set; }
    public string? Reason { get; set; }
    public string ChangedByName { get; set; } = null!;
    public DateTime ChangedAt { get; set; }
}

public class OrderDto
{
    public int Id { get; set; }
    public string Status { get; set; } = null!;
    public decimal TotalAmount { get; set; }
    public string Address { get; set; } = null!;
    public string ContactName { get; set; } = null!;
    public string ContactPhone { get; set; } = null!;
    public DateTime CreatedAt { get; set; }

    public int ClientId { get; set; }
    public string ClientName { get; set; } = null!;
    public int PerformerId { get; set; }
    public string PerformerName { get; set; } = null!;

    // Телефон мастера виден клиенту только после подтверждения заказа
    public string? PerformerPhone { get; set; }

    // Причина отмены или отклонения; null, если заказ не отменён
    public string? CancelReason { get; set; }

    public List<OrderItemDto> Items { get; set; } = new();
    public List<OrderHistoryDto> History { get; set; } = new();   // заполняется только в GET /api/orders/{id}
}

// Отмена клиентом и отклонение мастером: причина обязательна
public class CancelOrderRequest
{
    [Required(ErrorMessage = "Укажите причину"), MinLength(3, ErrorMessage = "Причина слишком короткая"), MaxLength(500)]
    public string Reason { get; set; } = null!;
}

// «Заказать снова»: позиции прошлого заказа добавлены в корзину
public class RepeatOrderResultDto
{
    public int Added { get; set; }

    // Названия услуг, которые добавить нельзя (скрыты, мастер заблокирован)
    public List<string> Skipped { get; set; } = new();
}
