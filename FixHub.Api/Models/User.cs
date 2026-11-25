namespace FixHub.Api.Models;

public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string FullName { get; set; } = null!;
    public string? Phone { get; set; }
    public string? Bio { get; set; }

    // Профессиональные данные исполнителя (для клиентов и администратора не заполняются)
    public string? Specialization { get; set; }
    public int? ExperienceYears { get; set; }
    public string? City { get; set; }
    public string? ServiceArea { get; set; }

    // Точка на карте, от которой мастер выезжает (округляется до ~1 км, точный адрес не хранится)
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? AvatarUrl { get; set; }

    // Отметка «Проверенный мастер»: ставится после проверки документов
    public bool IsVerified { get; set; }
    public UserRole Role { get; set; }
    public bool IsBlocked { get; set; }

    // Присылать ли уведомления о заказах на почту
    public bool EmailNotifications { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}