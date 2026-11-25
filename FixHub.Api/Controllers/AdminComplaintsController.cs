using FixHub.Api.Data;
using FixHub.Api.DTOs;
using FixHub.Api.Models;
using FixHub.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FixHub.Api.Controllers;

[ApiController]
[Route("api/admin/complaints")]
[Authorize(Roles = "Admin")]
public class AdminComplaintsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly ILogger<AdminComplaintsController> _logger;

    public AdminComplaintsController(AppDbContext db, IAuditService audit, ILogger<AdminComplaintsController> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    private int AdminId => int.Parse(User.FindFirst("sub")!.Value);

    [HttpGet]
    public async Task<ActionResult<PagedResult<ComplaintDto>>> Search([FromQuery] ComplaintQuery query)
    {
        var q = _db.Complaints.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Status))
            q = q.Where(c => c.Status == query.Status);

        var total = await q.CountAsync();

        var items = await Project(q
            .OrderBy(c => c.Status == ComplaintStatus.New ? 0 : 1)
            .ThenByDescending(c => c.CreatedAt).ThenBy(c => c.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize))
            .ToListAsync();

        return Ok(new PagedResult<ComplaintDto>
        {
            Items = items,
            Page = query.Page,
            PageSize = query.PageSize,
            TotalCount = total,
            TotalPages = (int)Math.Ceiling(total / (double)query.PageSize)
        });
    }

    // Число нерассмотренных жалоб: показываем в меню админки
    [HttpGet("count")]
    public async Task<ActionResult<object>> Count()
    {
        var count = await _db.Complaints.CountAsync(c => c.Status == ComplaintStatus.New);
        return Ok(new { count });
    }

    // Принять жалобу: по желанию скрыть отзыв или заблокировать мастера
    [HttpPost("{id:int}/resolve")]
    public async Task<ActionResult<ComplaintDto>> Resolve(int id, ResolveComplaintRequest request)
    {
        var complaint = await _db.Complaints.FindAsync(id);
        if (complaint == null)
            return NotFound(new { error = "Жалоба не найдена" });

        if (complaint.Status != ComplaintStatus.New)
            return Conflict(new { error = "Жалоба уже рассмотрена" });

        var actions = new List<string>();

        if (request.HideReview && complaint.ReviewId != null)
        {
            var review = await _db.Reviews.FindAsync(complaint.ReviewId);
            if (review != null && !review.IsHidden)
            {
                review.IsHidden = true;
                _audit.Add(AdminId, "review.hide", "Review", review.Id, $"Отзыв №{review.Id}", $"По жалобе №{complaint.Id}");
                actions.Add("отзыв скрыт");
            }
        }

        if (request.BlockPerformer && complaint.PerformerId != null)
        {
            var performer = await _db.Users.FindAsync(complaint.PerformerId);
            if (performer != null && !performer.IsBlocked)
            {
                performer.IsBlocked = true;
                _audit.Add(AdminId, "user.block", "User", performer.Id, performer.FullName, $"По жалобе №{complaint.Id}");
                actions.Add("мастер заблокирован");
            }
        }

        Finish(complaint, ComplaintStatus.Resolved, request.Note);
        _audit.Add(AdminId, "complaint.resolve", "Complaint", complaint.Id, $"Жалоба №{complaint.Id}",
            actions.Count > 0 ? string.Join(", ", actions) : null);

        await _db.SaveChangesAsync();
        _logger.LogWarning("Администратор {AdminId} принял жалобу {ComplaintId}", AdminId, id);

        return Ok(await LoadOne(id));
    }

    // Отклонить жалобу: нарушения не найдено
    [HttpPost("{id:int}/reject")]
    public async Task<ActionResult<ComplaintDto>> Reject(int id, RejectComplaintRequest request)
    {
        var complaint = await _db.Complaints.FindAsync(id);
        if (complaint == null)
            return NotFound(new { error = "Жалоба не найдена" });

        if (complaint.Status != ComplaintStatus.New)
            return Conflict(new { error = "Жалоба уже рассмотрена" });

        Finish(complaint, ComplaintStatus.Rejected, request.Note);
        _audit.Add(AdminId, "complaint.reject", "Complaint", complaint.Id, $"Жалоба №{complaint.Id}");

        await _db.SaveChangesAsync();
        _logger.LogWarning("Администратор {AdminId} отклонил жалобу {ComplaintId}", AdminId, id);

        return Ok(await LoadOne(id));
    }

    private void Finish(Complaint complaint, string status, string? note)
    {
        complaint.Status = status;
        complaint.ResolvedAt = DateTime.UtcNow;
        complaint.ResolvedById = AdminId;
        complaint.Resolution = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    private async Task<ComplaintDto> LoadOne(int id) =>
        await Project(_db.Complaints.AsNoTracking().Where(c => c.Id == id)).FirstAsync();

    private IQueryable<ComplaintDto> Project(IQueryable<Complaint> q) =>
        q.Select(c => new ComplaintDto
        {
            Id = c.Id,
            Kind = c.Kind,
            Reason = c.Reason,
            Comment = c.Comment,
            Status = c.Status,
            CreatedAt = c.CreatedAt,
            ReporterId = c.ReporterId,
            ReporterName = c.Reporter.FullName,
            PerformerId = c.PerformerId,
            PerformerName = _db.Users.Where(u => u.Id == c.PerformerId).Select(u => u.FullName).FirstOrDefault(),
            PerformerIsBlocked = _db.Users.Any(u => u.Id == c.PerformerId && u.IsBlocked),
            ReviewId = c.ReviewId,
            ReviewText = _db.Reviews.Where(r => r.Id == c.ReviewId).Select(r => r.Text).FirstOrDefault(),
            ReviewRating = _db.Reviews.Where(r => r.Id == c.ReviewId).Select(r => (int?)r.Rating).FirstOrDefault(),
            ReviewIsHidden = _db.Reviews.Any(r => r.Id == c.ReviewId && r.IsHidden),
            ResolvedAt = c.ResolvedAt,
            Resolution = c.Resolution
        });
}
