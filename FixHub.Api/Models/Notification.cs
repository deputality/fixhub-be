namespace FixHub.Api.Models;

// Уведомление для пользователя внутри приложения
public class Notification
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public NotificationType Type { get; set; }
    public string Text { get; set; } = null!;

    // К чему относится уведомление, чтобы фронт мог открыть нужную страницу.
    // Внешних ключей нет: это просто ссылки для удобства.
    public int? OrderId { get; set; }
    public int? ReviewId { get; set; }

    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}