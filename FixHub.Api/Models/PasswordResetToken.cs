namespace FixHub.Api.Models;

// Одноразовая ссылка для сброса пароля. В базе лежит только хэш токена, сам токен уходит в письмо
public class PasswordResetToken
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public string TokenHash { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
