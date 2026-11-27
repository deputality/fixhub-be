using FixHub.Api.Data;
using FixHub.Api.DTOs;
using FixHub.Api.Models;
using FixHub.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FixHub.Api.Controllers;

[ApiController]
[Route("api/reviews")]
public class ReviewsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<ReviewsController> _logger;
    private readonly INotificationService _notifications;
    private readonly IRealtimeNotifier _realtime;

    public ReviewsController(
        AppDbContext db, ILogger<ReviewsController> logger, INotificationService notifications, IRealtimeNotifier realtime)
    {
        _db = db;
        _logger = logger;
        _notifications = notifications;
        _realtime = realtime;
    }

    private const int MaxPhotos = 3;

    private static bool IsUploadUrl(string url) =>
        url.Length <= 300
        && url.StartsWith("/uploads/", StringComparison.Ordinal)
        && !url.Contains("..")
        && !url.Contains('\\');

    private int CurrentUserId => int.Parse(User.FindFirst("sub")!.Value);

    // ---------- Клиент: оставить отзыв ----------

    [Authorize(Roles = "Client")]
    [HttpPost]
    public async Task<ActionResult<ReviewDto>> Create(CreateReviewRequest request)
    {
        var userId = CurrentUserId;

        // чужая позиция заказа для клиента «не существует»
        var item = await _db.OrderItems
            .AsNoTracking()
            .Where(i => i.Id == request.OrderItemId && i.Order.ClientId == userId)
            .Select(i => new
            {
                i.Id,
                i.ServiceId,
                i.Order.PerformerId,
                i.Order.Status
            })
            .FirstOrDefaultAsync();

        if (item == null)
            return NotFound(new { error = "Позиция заказа не найдена" });

        if (item.Status != OrderStatus.Completed)
            return Conflict(new { error = "Отзыв можно оставить только после выполнения заказа" });

        if (await _db.Reviews.AnyAsync(r => r.OrderItemId == item.Id))
            return Conflict(new { error = "Отзыв на эту позицию уже оставлен" });

        var photoUrls = (request.PhotoUrls ?? new List<string>())
            .Select(u => u?.Trim() ?? "")
            .Where(u => u.Length > 0)
            .Distinct()
            .ToList();

        if (photoUrls.Count > MaxPhotos)
            return BadRequest(new { error = $"К отзыву можно приложить не больше {MaxPhotos} фото" });
        if (photoUrls.Any(u => !IsUploadUrl(u)))
            return BadRequest(new { error = "Некорректный адрес фото" });

        var review = new Review
        {
            OrderItemId = item.Id,
            ClientId = userId,
            ServiceId = item.ServiceId,
            PerformerId = item.PerformerId,
            Rating = request.Rating,
            Text = request.Text.Trim(),
            IsHidden = false,
            CreatedAt = DateTime.UtcNow,
            Photos = photoUrls
                .Select((u, i) => new ReviewPhoto { Url = u, SortOrder = i })
                .ToList()
        };

        _db.Reviews.Add(review);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // два одновременных запроса на одну позицию: сработал уникальный индекс
            return Conflict(new { error = "Отзыв на эту позицию уже оставлен" });
        }

        // id отзыва появился только после сохранения, поэтому уведомление сохраняем отдельно
        _notifications.Add(
            item.PerformerId,
            NotificationType.NewReview,
            $"Новый отзыв с оценкой {review.Rating}",
            reviewId: review.Id);
        await _db.SaveChangesAsync();
        await _realtime.NotificationsChangedAsync(item.PerformerId);

        _logger.LogInformation(
            "Клиент {ClientId} оставил отзыв {ReviewId} на услугу {ServiceId}, оценка {Rating}",
            userId, review.Id, review.ServiceId, review.Rating);

        var dto = await _db.Reviews
            .AsNoTracking()
            .Where(r => r.Id == review.Id)
            .Select(ReviewProjection.ToDto)
            .FirstAsync();

        return Ok(dto);
    }

    // ---------- Клиент: мои отзывы ----------

    [Authorize(Roles = "Client")]
    [HttpGet("my")]
    public async Task<ActionResult<List<ReviewDto>>> GetMy()
    {
        var userId = CurrentUserId;

        var items = await _db.Reviews
            .AsNoTracking()
            .Where(r => r.ClientId == userId)
            .OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id)
            .Select(ReviewProjection.ToDto)
            .ToListAsync();

        return Ok(items);
    }

    // ---------- Исполнитель: отзывы о себе и общий рейтинг ----------

    [Authorize(Roles = "Performer")]
    [HttpGet("~/api/performer/reviews")]
    public async Task<ActionResult<PerformerReviewsDto>> GetAboutMe([FromQuery] ReviewQuery query)
    {
        var userId = CurrentUserId;

        // скрытые отзывы не показываются и не входят в рейтинг
        var q = _db.Reviews
            .AsNoTracking()
            .Where(r => r.PerformerId == userId && !r.IsHidden);

        var total = await q.CountAsync();

        decimal? rating = null;
        if (total > 0)
            rating = ReviewProjection.Round(await q.AverageAsync(r => (decimal?)r.Rating));

        var items = await q
            .OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(ReviewProjection.ToDto)
            .ToListAsync();

        return Ok(new PerformerReviewsDto
        {
            Rating = rating,
            ReviewCount = total,
            Reviews = new PagedResult<ReviewDto>
            {
                Items = items,
                Page = query.Page,
                PageSize = query.PageSize,
                TotalCount = total,
                TotalPages = (int)Math.Ceiling(total / (double)query.PageSize)
            }
        });
    }

    // ---------- Исполнитель: публичный ответ на отзыв ----------

    // Создаёт ответ или меняет уже написанный
    [Authorize(Roles = "Performer")]
    [HttpPost("~/api/performer/reviews/{id:int}/reply")]
    public async Task<ActionResult<ReviewDto>> Reply(int id, ReviewReplyRequest request)
    {
        var userId = CurrentUserId;

        var text = request.Text.Trim();
        if (text.Length == 0)
            return BadRequest(new { error = "Напишите ответ" });

        // чужой отзыв для мастера «не существует»
        var review = await _db.Reviews.FirstOrDefaultAsync(r => r.Id == id && r.PerformerId == userId);
        if (review == null)
            return NotFound(new { error = "Отзыв не найден" });

        var isNew = review.ReplyText == null;
        review.ReplyText = text;
        review.RepliedAt = DateTime.UtcNow;

        if (isNew)
            _notifications.Add(
                review.ClientId,
                NotificationType.ReviewReply,
                "Мастер ответил на ваш отзыв",
                reviewId: review.Id);

        await _db.SaveChangesAsync();

        if (isNew)
            await _realtime.NotificationsChangedAsync(review.ClientId);

        return Ok(await LoadAsync(id));
    }

    [Authorize(Roles = "Performer")]
    [HttpDelete("~/api/performer/reviews/{id:int}/reply")]
    public async Task<ActionResult<ReviewDto>> DeleteReply(int id)
    {
        var userId = CurrentUserId;

        var review = await _db.Reviews.FirstOrDefaultAsync(r => r.Id == id && r.PerformerId == userId);
        if (review == null)
            return NotFound(new { error = "Отзыв не найден" });

        review.ReplyText = null;
        review.RepliedAt = null;
        await _db.SaveChangesAsync();

        return Ok(await LoadAsync(id));
    }

    private Task<ReviewDto> LoadAsync(int id) =>
        _db.Reviews.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(ReviewProjection.ToDto)
            .FirstAsync();
}
