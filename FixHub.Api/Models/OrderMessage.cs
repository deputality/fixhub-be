namespace FixHub.Api.Models;

// Сообщение в чате по заказу: переписка клиента и мастера, чтобы уточнить время, адрес и детали
public class OrderMessage
{
    public int Id { get; set; }

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public int SenderId { get; set; }
    public User Sender { get; set; } = null!;

    public string Text { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Когда сообщение прочитал второй участник; null, пока не прочитано
    public DateTime? ReadAt { get; set; }
}
