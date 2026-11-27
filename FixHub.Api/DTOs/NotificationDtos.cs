using System.ComponentModel.DataAnnotations;

namespace FixHub.Api.DTOs;

public class NotificationDto
{
    public int Id { get; set; }
    public string Type { get; set; } = null!;
    public string Text { get; set; } = null!;
    public int? OrderId { get; set; }
    public int? ReviewId { get; set; }
    public int? RequestId { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class NotificationQuery
{
    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 50)]
    public int PageSize { get; set; } = 20;

    // true: показать только непрочитанные
    public bool OnlyUnread { get; set; }
}

public class UnreadCountDto
{
    public int Count { get; set; }
}