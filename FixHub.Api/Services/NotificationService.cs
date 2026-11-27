using FixHub.Api.Data;
using FixHub.Api.Models;

namespace FixHub.Api.Services;

public interface INotificationService
{
    void Add(int userId, NotificationType type, string text, int? orderId = null, int? reviewId = null, int? requestId = null);
}

public class NotificationService : INotificationService
{
    private readonly AppDbContext _db;

    public NotificationService(AppDbContext db)
    {
        _db = db;
    }

    // Только добавляет запись в контекст и ничего не сохраняет.
    // Сохранит тот, кто вызвал: так уведомление уйдёт в той же транзакции, что и само действие.
    public void Add(int userId, NotificationType type, string text, int? orderId = null, int? reviewId = null, int? requestId = null)
    {
        _db.Notifications.Add(new Notification
        {
            UserId = userId,
            Type = type,
            Text = text,
            OrderId = orderId,
            ReviewId = reviewId,
            RequestId = requestId,
            CreatedAt = DateTime.UtcNow
        });
    }
}