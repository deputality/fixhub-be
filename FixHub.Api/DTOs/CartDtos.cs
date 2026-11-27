using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using FixHub.Api.Common;

namespace FixHub.Api.DTOs;

public class AddToCartRequest
{
    [Required]
    public int ServiceId { get; set; }

    [Range(1, 100)]
    public int Quantity { get; set; } = 1;

    public DateOnly? DesiredDate { get; set; }

    [RegularExpression(TimeFormat.Pattern, ErrorMessage = "Время указывается в формате ЧЧ:ММ")]
    public string? DesiredTime { get; set; }

    [MaxLength(1000)]
    public string? Comment { get; set; }
}

public class UpdateCartItemRequest
{
    [Range(1, 100)]
    public int Quantity { get; set; }

    public DateOnly? DesiredDate { get; set; }

    [RegularExpression(TimeFormat.Pattern, ErrorMessage = "Время указывается в формате ЧЧ:ММ")]
    public string? DesiredTime { get; set; }

    [MaxLength(1000)]
    public string? Comment { get; set; }
}

public class CartItemDto
{
    public int Id { get; set; }
    public int ServiceId { get; set; }
    public string ServiceTitle { get; set; } = null!;
    public string? PhotoUrl { get; set; }
    public int PerformerId { get; set; }
    public string PerformerName { get; set; } = null!;
    public decimal UnitPrice { get; set; }
    public string Unit { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
    public DateOnly? DesiredDate { get; set; }

    [JsonIgnore]
    public TimeOnly? DesiredTimeValue { get; set; }

    public string? DesiredTime
    {
        get => TimeFormat.Format(DesiredTimeValue);
        set => DesiredTimeValue = TimeFormat.Parse(value);
    }

    public string? Comment { get; set; }
    public bool IsAvailable { get; set; }
}

public class CartDto
{
    public List<CartItemDto> Items { get; set; } = new();
    public decimal TotalAmount { get; set; }
}