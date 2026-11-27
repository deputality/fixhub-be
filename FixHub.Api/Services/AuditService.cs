using FixHub.Api.Data;
using FixHub.Api.Models;

namespace FixHub.Api.Services;

public interface IAuditService
{
    void Add(int adminId, string action, string targetType, int? targetId, string? targetName, string? details = null);
}

public class AuditService : IAuditService
{
    private readonly AppDbContext _db;

    public AuditService(AppDbContext db)
    {
        _db = db;
    }

    // Как и уведомления: только добавляет запись в контекст, сохраняет вызвавший.
    // Поэтому запись попадает в журнал тогда и только тогда, когда сохранилось само действие.
    public void Add(int adminId, string action, string targetType, int? targetId, string? targetName, string? details = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            AdminId = adminId,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            TargetName = Trim(targetName, 200),
            Details = Trim(details, 500),
            CreatedAt = DateTime.UtcNow
        });
    }

    private static string? Trim(string? value, int max) =>
        value == null ? null : value.Length <= max ? value : value[..max];
}
