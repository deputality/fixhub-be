using System.ComponentModel.DataAnnotations;
using FixHub.Api.Models;

namespace FixHub.Api.DTOs;

public class ProfileDto
{
    public int Id { get; set; }
    public string Email { get; set; } = null!;
    public string FullName { get; set; } = null!;
    public string? Phone { get; set; }
    public string? Bio { get; set; }
    public string Role { get; set; } = null!;
}

public class UpdateProfileRequest
{
    [Required, MaxLength(200)]
    public string FullName { get; set; } = null!;

    [MaxLength(30)]
    public string? Phone { get; set; }

    [MaxLength(2000)]
    public string? Bio { get; set; }
}

public class PerformerStatsDto
{
    public int CompletedOrders { get; set; }
    public decimal CompletedAmount { get; set; }
    public int NewOrders { get; set; }
    public int ActiveOrders { get; set; }      // подтверждены или в работе
    public int CancelledOrders { get; set; }
}

public class AdminUserDto
{
    public int Id { get; set; }
    public string Email { get; set; } = null!;
    public string FullName { get; set; } = null!;
    public string? Phone { get; set; }
    public string Role { get; set; } = null!;
    public bool IsBlocked { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AdminUserQuery
{
    [MaxLength(100)]
    public string? Search { get; set; }

    public UserRole? Role { get; set; }
    public bool? IsBlocked { get; set; }

    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 50)]
    public int PageSize { get; set; } = 20;
}