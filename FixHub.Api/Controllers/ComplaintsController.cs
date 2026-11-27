using FixHub.Api.Data;
using FixHub.Api.DTOs;
using FixHub.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FixHub.Api.Controllers;

// Жалоба на отзыв или на мастера. Отправить может любой вошедший пользователь, кроме администратора
[ApiController]
[Route("api/complaints")]
[Authorize(Roles = "Client,Performer")]
public class ComplaintsController : ControllerBase
{
    private static readonly string[] Reasons = { "spam", "abuse", "fake", "fraud", "other" };

    private readonly AppDbContext _db;
    private readonly ILogger<ComplaintsController> _logger;

    public ComplaintsController(AppDbContext db, ILogger<ComplaintsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    private int CurrentUserId => int.Parse(User.FindFirst("sub")!.Value);

    [HttpPost]
    public async Task<IActionResult> Create(ComplaintRequest request)
    {
        if (!Reasons.Contains(request.Reason))
            return BadRequest(new { error = "Укажите причину жалобы" });

        // «Другое» без пояснения администратору нечего проверять
        var comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        if (request.Reason == "other" && comment == null)
            return BadRequest(new { error = "Опишите, что случилось" });

        var complaint = new Complaint
        {
            ReporterId = CurrentUserId,
            Kind = request.Kind,
            Reason = request.Reason,
            Comment = comment
        };

        if (request.Kind == ComplaintKind.Review)
        {
            var review = await _db.Reviews.AsNoTracking()
                .Where(r => r.Id == request.TargetId && !r.IsHidden)
                .Select(r => new { r.Id, r.ClientId, r.PerformerId })
                .FirstOrDefaultAsync();
            if (review == null)
                return NotFound(new { error = "Отзыв не найден" });

            if (review.ClientId == CurrentUserId)
                return BadRequest(new { error = "На собственный отзыв пожаловаться нельзя" });

            complaint.ReviewId = review.Id;
            complaint.PerformerId = review.PerformerId;
        }
        else if (request.Kind == ComplaintKind.Performer)
        {
            var exists = await _db.Users.AnyAsync(u => u.Id == request.TargetId && u.Role == UserRole.Performer);
            if (!exists)
                return NotFound(new { error = "Мастер не найден" });

            if (request.TargetId == CurrentUserId)
                return BadRequest(new { error = "На себя пожаловаться нельзя" });

            complaint.PerformerId = request.TargetId;
        }
        else
        {
            return BadRequest(new { error = "Некорректный вид жалобы" });
        }

        // повторная жалоба на то же, пока первая не рассмотрена, только засоряет очередь
        var reporterId = complaint.ReporterId;
        var reviewId = complaint.ReviewId;
        var performerId = complaint.PerformerId;

        var duplicate = complaint.Kind == ComplaintKind.Review
            ? await _db.Complaints.AnyAsync(c => c.ReporterId == reporterId &&
                c.Status == ComplaintStatus.New && c.Kind == ComplaintKind.Review && c.ReviewId == reviewId)
            : await _db.Complaints.AnyAsync(c => c.ReporterId == reporterId &&
                c.Status == ComplaintStatus.New && c.Kind == ComplaintKind.Performer && c.PerformerId == performerId);
        if (duplicate)
            return Conflict(new { error = "Вы уже отправили жалобу, мы её рассматриваем" });

        _db.Complaints.Add(complaint);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Пользователь {UserId} отправил жалобу {ComplaintId} ({Kind})",
            complaint.ReporterId, complaint.Id, complaint.Kind);

        return NoContent();
    }
}
