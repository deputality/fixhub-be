namespace FixHub.Api.Models;

public static class ComplaintKind
{
    public const string Review = "Review";
    public const string Performer = "Performer";
}

public static class ComplaintStatus
{
    public const string New = "New";
    public const string Resolved = "Resolved";
    public const string Rejected = "Rejected";
}

// Жалоба на отзыв или на мастера
public class Complaint
{
    public int Id { get; set; }

    public int ReporterId { get; set; }
    public User Reporter { get; set; } = null!;

    // Review или Performer; заполнено ровно одно из полей ReviewId / PerformerId
    public string Kind { get; set; } = null!;
    public int? ReviewId { get; set; }
    public int? PerformerId { get; set; }

    // spam, abuse, fake, fraud, other
    public string Reason { get; set; } = null!;
    public string? Comment { get; set; }

    public string Status { get; set; } = ComplaintStatus.New;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ResolvedAt { get; set; }
    public int? ResolvedById { get; set; }

    // Комментарий администратора к решению
    public string? Resolution { get; set; }
}
