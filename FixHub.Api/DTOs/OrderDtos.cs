using System.ComponentModel.DataAnnotations;

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

public class OrderItemDto
{
    public int Id { get; set; }
    public int ServiceId { get; set; }
    public string ServiceTitle { get; set; } = null!;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
    public DateOnly? DesiredDate { get; set; }
    public string? Comment { get; set; }
}

public class OrderHistoryDto
{
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = null!;
    public int ChangedByUserId { get; set; }
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

    public List<OrderItemDto> Items { get; set; } = new();
    public List<OrderHistoryDto> History { get; set; } = new();   // заполняется только в GET /api/orders/{id}
}