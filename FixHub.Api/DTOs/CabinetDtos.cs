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

    public string? Specialization { get; set; }
    public int? ExperienceYears { get; set; }
    public string? City { get; set; }
    public string? ServiceArea { get; set; }
    public string? AvatarUrl { get; set; }
    public bool IsVerified { get; set; }
    public bool EmailNotifications { get; set; }
}

public class UpdateProfileRequest
{
    [Required, MaxLength(200)]
    public string FullName { get; set; } = null!;

    [MaxLength(30)]
    public string? Phone { get; set; }

    [MaxLength(2000)]
    public string? Bio { get; set; }

    // Поля ниже заполняет только исполнитель; у остальных ролей они игнорируются
    [MaxLength(100)]
    public string? Specialization { get; set; }

    [Range(0, 80)]
    public int? ExperienceYears { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(200)]
    public string? ServiceArea { get; set; }

    // Адрес, который вернула загрузка фото (/api/performer/uploads)
    [MaxLength(300)]
    public string? AvatarUrl { get; set; }
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

    // Отметка «Проверенный мастер»; для клиентов и админа всегда false
    public bool IsVerified { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AdminUserQuery
{
    [MaxLength(100)]
    public string? Search { get; set; }

    public UserRole? Role { get; set; }
    public bool? IsBlocked { get; set; }

    // true / false: только проверенные / только непроверенные
    public bool? IsVerified { get; set; }

    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 50)]
    public int PageSize { get; set; } = 20;
}

// Публичная страница мастера: без email и телефона
public class PerformerPublicDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = null!;
    public string? Specialization { get; set; }
    public int? ExperienceYears { get; set; }
    public string? City { get; set; }
    public string? ServiceArea { get; set; }
    public string? Bio { get; set; }
    public string? AvatarUrl { get; set; }
    public bool IsVerified { get; set; }
    public DateTime CreatedAt { get; set; }

    public decimal? Rating { get; set; }
    public int ReviewCount { get; set; }
    public int CompletedOrders { get; set; }
    public int ServicesCount { get; set; }
}

public class NotificationSettingsRequest
{
    public bool EmailNotifications { get; set; }
}

public class ChangePasswordRequest
{
    [Required]
    public string CurrentPassword { get; set; } = null!;

    [Required, MinLength(8), MaxLength(100)]
    public string NewPassword { get; set; } = null!;
}
