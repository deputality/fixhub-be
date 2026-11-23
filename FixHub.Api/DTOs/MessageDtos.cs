using System.ComponentModel.DataAnnotations;

namespace FixHub.Api.DTOs;

public class SendMessageRequest
{
    [Required, MaxLength(1000)]
    public string Text { get; set; } = null!;
}

public class OrderMessageDto
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int SenderId { get; set; }
    public string SenderName { get; set; } = null!;
    public string Text { get; set; } = null!;
    public DateTime CreatedAt { get; set; }

    // Второй участник уже открыл чат и прочитал сообщение
    public bool IsRead { get; set; }
}

// Сколько непрочитанных сообщений в чате по заказу
public class UnreadChatDto
{
    public int OrderId { get; set; }
    public int Count { get; set; }
}
