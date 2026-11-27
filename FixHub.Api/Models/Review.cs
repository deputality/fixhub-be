namespace FixHub.Api.Models;

// Отзыв клиента о выполненной позиции заказа
public class Review
{
    public int Id { get; set; }

    // Один отзыв на одну позицию заказа (уникальный индекс)
    public int OrderItemId { get; set; }
    public OrderItem OrderItem { get; set; } = null!;

    public int ClientId { get; set; }
    public User Client { get; set; } = null!;

    // Услуга и исполнитель дублируются из заказа, чтобы быстро считать рейтинг
    public int ServiceId { get; set; }
    public Service Service { get; set; } = null!;

    public int PerformerId { get; set; }
    public User Performer { get; set; } = null!;

    // От 1 до 5
    public int Rating { get; set; }
    public string Text { get; set; } = null!;

    // Администратор может скрыть отзыв: скрытый не показывается и не входит в рейтинг
    public bool IsHidden { get; set; }

    // Публичный ответ мастера на отзыв
    public string? ReplyText { get; set; }
    public DateTime? RepliedAt { get; set; }

    public List<ReviewPhoto> Photos { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
