using System.ComponentModel.DataAnnotations;

namespace FixHub.Api.DTOs;

public class AddToCartRequest
{
    [Required]
    public int ServiceId { get; set; }

    [Range(1, 100)]
    public int Quantity { get; set; } = 1;

    public DateOnly? DesiredDate { get; set; }

    [MaxLength(1000)]
    public string? Comment { get; set; }
}

public class UpdateCartItemRequest
{
    [Range(1, 100)]
    public int Quantity { get; set; }

    public DateOnly? DesiredDate { get; set; }

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
    public string? Comment { get; set; }
    public bool IsAvailable { get; set; }
}

public class CartDto
{
    public List<CartItemDto> Items { get; set; } = new();
    public decimal TotalAmount { get; set; }
}