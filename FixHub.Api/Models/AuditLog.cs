namespace FixHub.Api.Models;

// Запись журнала: что именно сделал администратор. Пишется вместе с самим действием в одной транзакции
public class AuditLog
{
    public int Id { get; set; }

    public int AdminId { get; set; }
    public User Admin { get; set; } = null!;

    // Ключ действия, например "user.block" или "review.hide"; подписи по ключам хранятся на фронтенде
    public string Action { get; set; } = null!;

    // Над чем совершено действие: User, Review, Category, Complaint
    public string TargetType { get; set; } = null!;
    public int? TargetId { get; set; }

    // Название объекта на момент действия (имя, текст), чтобы запись читалась и после удаления объекта
    public string? TargetName { get; set; }
    public string? Details { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
