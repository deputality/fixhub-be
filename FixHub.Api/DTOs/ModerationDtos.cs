using System.ComponentModel.DataAnnotations;

namespace FixHub.Api.DTOs;

// ---------- Журнал действий

public class AuditLogDto
{
    public int Id { get; set; }
    public int AdminId { get; set; }
    public string AdminName { get; set; } = null!;
    public string Action { get; set; } = null!;
    public string TargetType { get; set; } = null!;
    public int? TargetId { get; set; }
    public string? TargetName { get; set; }
    public string? Details { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AuditQuery
{
    // User, Review, Category, Complaint; пусто = всё
    [MaxLength(20)]
    public string? TargetType { get; set; }

    [MaxLength(100)]
    public string? Search { get; set; }

    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;
}

// ---------- Жалобы

public class ComplaintRequest
{
    // Review или Performer
    [Required, MaxLength(20)]
    public string Kind { get; set; } = null!;

    // Id отзыва или мастера, в зависимости от Kind
    [Range(1, int.MaxValue)]
    public int TargetId { get; set; }

    // spam, abuse, fake, fraud, other
    [Required, MaxLength(30)]
    public string Reason { get; set; } = null!;

    [MaxLength(1000)]
    public string? Comment { get; set; }
}

public class ComplaintDto
{
    public int Id { get; set; }
    public string Kind { get; set; } = null!;
    public string Reason { get; set; } = null!;
    public string? Comment { get; set; }
    public string Status { get; set; } = null!;
    public DateTime CreatedAt { get; set; }

    public int ReporterId { get; set; }
    public string ReporterName { get; set; } = null!;

    // На кого жалоба: мастер, а для жалобы на отзыв ещё и сам отзыв
    public int? PerformerId { get; set; }
    public string? PerformerName { get; set; }
    public bool PerformerIsBlocked { get; set; }

    public int? ReviewId { get; set; }
    public string? ReviewText { get; set; }
    public int? ReviewRating { get; set; }
    public bool ReviewIsHidden { get; set; }

    public DateTime? ResolvedAt { get; set; }
    public string? Resolution { get; set; }
}

public class ComplaintQuery
{
    // New, Resolved, Rejected; пусто = все
    [MaxLength(20)]
    public string? Status { get; set; }

    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;
}

public class ResolveComplaintRequest
{
    [MaxLength(500)]
    public string? Note { get; set; }

    // Для жалобы на отзыв: скрыть его
    public bool HideReview { get; set; }

    // Для жалобы на мастера: заблокировать
    public bool BlockPerformer { get; set; }
}

public class RejectComplaintRequest
{
    [MaxLength(500)]
    public string? Note { get; set; }
}

// ---------- Документы для проверки мастера

public class VerificationDocumentDto
{
    public int Id { get; set; }
    public string Kind { get; set; } = null!;
    public string FileName { get; set; } = null!;
    public long SizeBytes { get; set; }
    public string ContentType { get; set; } = null!;
    public DateTime UploadedAt { get; set; }
}

public class VerificationStatusDto
{
    public bool IsVerified { get; set; }
    public List<VerificationDocumentDto> Documents { get; set; } = new();
    public PerformerLimitsDto Limits { get; set; } = new();
}

// Что мастеру можно сейчас. Фронтенд показывает лимиты, а проверяет их всё равно сервер
public class PerformerLimitsDto
{
    public int? MaxServices { get; set; }
    public int MaxPortfolioItems { get; set; }
    public bool CanSendOffers { get; set; }
}
