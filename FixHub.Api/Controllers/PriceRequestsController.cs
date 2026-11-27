using System.Linq.Expressions;
using FixHub.Api.Data;
using FixHub.Api.DTOs;
using FixHub.Api.Models;
using FixHub.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FixHub.Api.Controllers;

// Заявки «назовите цену»: клиент описывает задачу, мастера присылают цены, клиент выбирает одну
// и из неё получается обычный заказ.
[ApiController]
public class PriceRequestsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<PriceRequestsController> _logger;
    private readonly INotificationService _notifications;
    private readonly IRealtimeNotifier _realtime;

    public PriceRequestsController(
        AppDbContext db, ILogger<PriceRequestsController> logger, INotificationService notifications,
        IRealtimeNotifier realtime)
    {
        _db = db;
        _logger = logger;
        _notifications = notifications;
        _realtime = realtime;
    }

    private int CurrentUserId => int.Parse(User.FindFirst("sub")!.Value);

    private static readonly Expression<Func<PriceOffer, OfferDto>> OfferToDto = o => new OfferDto
    {
        Id = o.Id,
        RequestId = o.RequestId,
        PerformerId = o.PerformerId,
        PerformerName = o.Performer.FullName,
        PerformerAvatarUrl = o.Performer.AvatarUrl,
        PerformerIsVerified = o.Performer.IsVerified,
        ServiceId = o.ServiceId,
        ServiceTitle = o.Service.Title,
        Price = o.Price,
        Message = o.Message,
        Status = o.Status.ToString(),
        CreatedAt = o.CreatedAt
    };

    private static Expression<Func<PriceRequest, RequestDto>> ToDto(bool showContacts) => r => new RequestDto
    {
        Id = r.Id,
        CategoryId = r.CategoryId,
        CategoryName = r.Category.Name,
        Title = r.Title,
        Description = r.Description,
        Address = showContacts ? r.Address : null,
        ContactName = showContacts ? r.ContactName : null,
        ContactPhone = showContacts ? r.ContactPhone : null,
        DesiredDate = r.DesiredDate,
        Status = r.Status.ToString(),
        CreatedAt = r.CreatedAt,
        ClientId = r.ClientId,
        ClientName = r.Client.FullName,
        OffersCount = r.Offers.Count(o => o.Status != OfferStatus.Withdrawn),
        OrderId = r.OrderId
    };

    // Рейтинг мастера считаем так же, как везде: по нескрытым отзывам
    private async Task FillRatingsAsync(IEnumerable<OfferDto> offers)
    {
        var list = offers.ToList();
        var ids = list.Select(o => o.PerformerId).Distinct().ToList();
        if (ids.Count == 0)
            return;

        var ratings = await _db.Reviews
            .AsNoTracking()
            .Where(r => ids.Contains(r.PerformerId) && !r.IsHidden)
            .GroupBy(r => r.PerformerId)
            .Select(g => new { PerformerId = g.Key, Avg = g.Average(r => (decimal?)r.Rating) })
            .ToListAsync();

        foreach (var o in list)
            o.PerformerRating = ReviewProjection.Round(ratings.FirstOrDefault(r => r.PerformerId == o.PerformerId)?.Avg);
    }

    // ---------- Клиент ----------

    [Authorize(Roles = "Client")]
    [HttpPost("api/requests")]
    public async Task<ActionResult<RequestDto>> Create(CreateRequestRequest request)
    {
        var userId = CurrentUserId;

        // запас в один день из-за разницы часовых поясов
        var earliest = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        if (request.DesiredDate != null && request.DesiredDate < earliest)
            return BadRequest(new { error = "Желаемая дата не может быть в прошлом" });

        var category = await _db.Categories
            .AsNoTracking()
            .Include(c => c.Parent)
            .FirstOrDefaultAsync(c => c.Id == request.CategoryId);

        if (category == null || category.IsHidden || category.Parent?.IsHidden == true)
            return BadRequest(new { error = "Категория не найдена" });

        var entity = new PriceRequest
        {
            ClientId = userId,
            CategoryId = category.Id,
            Title = request.Title.Trim(),
            Description = request.Description.Trim(),
            Address = request.Address.Trim(),
            ContactName = request.ContactName.Trim(),
            ContactPhone = request.ContactPhone.Trim(),
            DesiredDate = request.DesiredDate,
            Status = RequestStatus.Open,
            CreatedAt = DateTime.UtcNow
        };

        _db.PriceRequests.Add(entity);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Клиент {ClientId} создал заявку {RequestId} в категории {CategoryId}",
            userId, entity.Id, category.Id);

        var dto = await _db.PriceRequests.AsNoTracking()
            .Where(r => r.Id == entity.Id)
            .Select(ToDto(true))
            .FirstAsync();

        return Ok(dto);
    }

    [Authorize(Roles = "Client")]
    [HttpGet("api/requests/my")]
    public async Task<ActionResult<List<RequestDto>>> GetMy()
    {
        var userId = CurrentUserId;

        var items = await _db.PriceRequests.AsNoTracking()
            .Where(r => r.ClientId == userId)
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Select(ToDto(true))
            .ToListAsync();

        return Ok(items);
    }

    [Authorize(Roles = "Client")]
    [HttpGet("api/requests/{id:int}")]
    public async Task<ActionResult<RequestDetailDto>> GetById(int id)
    {
        var userId = CurrentUserId;

        var head = await _db.PriceRequests.AsNoTracking()
            .Where(r => r.Id == id && r.ClientId == userId)
            .Select(ToDto(true))
            .FirstOrDefaultAsync();

        if (head == null)
            return NotFound(new { error = "Заявка не найдена" });

        var offers = await _db.PriceOffers.AsNoTracking()
            .Where(o => o.RequestId == id && o.Status != OfferStatus.Withdrawn)
            .OrderBy(o => o.Price).ThenBy(o => o.Id)
            .Select(OfferToDto)
            .ToListAsync();

        await FillRatingsAsync(offers);

        return Ok(new RequestDetailDto
        {
            Id = head.Id,
            CategoryId = head.CategoryId,
            CategoryName = head.CategoryName,
            Title = head.Title,
            Description = head.Description,
            Address = head.Address,
            ContactName = head.ContactName,
            ContactPhone = head.ContactPhone,
            DesiredDate = head.DesiredDate,
            Status = head.Status,
            CreatedAt = head.CreatedAt,
            ClientId = head.ClientId,
            ClientName = head.ClientName,
            OffersCount = head.OffersCount,
            OrderId = head.OrderId,
            Offers = offers
        });
    }

    [Authorize(Roles = "Client")]
    [HttpPost("api/requests/{id:int}/cancel")]
    public async Task<ActionResult<RequestDto>> Cancel(int id)
    {
        var userId = CurrentUserId;

        await using var tx = await _db.Database.BeginTransactionAsync();

        var request = await _db.PriceRequests
            .Include(r => r.Offers)
            .FirstOrDefaultAsync(r => r.Id == id && r.ClientId == userId);

        if (request == null)
            return NotFound(new { error = "Заявка не найдена" });

        if (request.Status != RequestStatus.Open)
            return Conflict(new { error = "Заявка уже закрыта" });

        request.Status = RequestStatus.Cancelled;
        foreach (var offer in request.Offers.Where(o => o.Status == OfferStatus.Pending))
            offer.Status = OfferStatus.Rejected;

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        var dto = await _db.PriceRequests.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(ToDto(true))
            .FirstAsync();

        return Ok(dto);
    }

    // Выбор предложения: создаётся обычный заказ у этого мастера по предложенной цене
    [Authorize(Roles = "Client")]
    [HttpPost("api/requests/{id:int}/offers/{offerId:int}/accept")]
    public async Task<ActionResult<AcceptOfferResultDto>> Accept(int id, int offerId)
    {
        var userId = CurrentUserId;

        await using var tx = await _db.Database.BeginTransactionAsync();

        var request = await _db.PriceRequests
            .Include(r => r.Offers)
            .FirstOrDefaultAsync(r => r.Id == id && r.ClientId == userId);

        if (request == null)
            return NotFound(new { error = "Заявка не найдена" });

        var offer = request.Offers.FirstOrDefault(o => o.Id == offerId);
        if (offer == null)
            return NotFound(new { error = "Предложение не найдено" });

        if (request.Status != RequestStatus.Open || offer.Status != OfferStatus.Pending)
            return Conflict(new { error = "Это предложение уже нельзя выбрать" });

        var service = await _db.Services
            .Include(s => s.Performer)
            .FirstAsync(s => s.Id == offer.ServiceId);

        if (!service.IsActive || service.Performer.IsBlocked)
            return BadRequest(new { error = "Услуга мастера больше недоступна, выберите другое предложение" });

        var now = DateTime.UtcNow;

        var comment = $"{request.Title}: {request.Description}";
        if (comment.Length > 1000)
            comment = comment[..1000];

        var order = OrdersController.NewOrder(
            userId, offer.PerformerId, request.Address, request.ContactName, request.ContactPhone,
            new List<OrderItem>
            {
                new()
                {
                    ServiceId = service.Id,
                    ServiceTitle = service.Title,
                    UnitPrice = offer.Price,
                    Quantity = 1,
                    DesiredDate = request.DesiredDate,
                    Comment = comment
                }
            },
            now);

        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        request.Status = RequestStatus.Chosen;
        request.OrderId = order.Id;
        offer.Status = OfferStatus.Accepted;
        foreach (var other in request.Offers.Where(o => o.Id != offer.Id && o.Status == OfferStatus.Pending))
            other.Status = OfferStatus.Rejected;

        _notifications.Add(
            offer.PerformerId,
            NotificationType.OfferAccepted,
            $"Клиент выбрал ваше предложение по заявке «{Short(request.Title)}», заказ №{order.Id}",
            orderId: order.Id,
            requestId: request.Id);

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        await _realtime.OrderChangedAsync(order.Id, offer.PerformerId, userId);

        _logger.LogInformation("Клиент {ClientId} выбрал предложение {OfferId} по заявке {RequestId}, заказ {OrderId}",
            userId, offer.Id, request.Id, order.Id);

        var orderDto = await _db.Orders.AsNoTracking()
            .Where(o => o.Id == order.Id)
            .Select(OrdersController.ToDto)
            .FirstAsync();

        var requestDto = await _db.PriceRequests.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(ToDto(true))
            .FirstAsync();

        return Ok(new AcceptOfferResultDto { Request = requestDto, Order = orderDto });
    }

    // ---------- Мастер ----------

    // Открытые заявки в категориях его услуг и те, где он уже сделал предложение
    [Authorize(Roles = "Performer")]
    [HttpGet("api/performer/requests")]
    public async Task<ActionResult<List<RequestDto>>> GetFeed()
    {
        var userId = CurrentUserId;

        var myCategories = _db.Services
            .Where(s => s.PerformerId == userId)
            .Select(s => s.CategoryId);

        var requests = await _db.PriceRequests.AsNoTracking()
            .Where(r =>
                (r.Status == RequestStatus.Open && !r.Client.IsBlocked && myCategories.Contains(r.CategoryId))
                || r.Offers.Any(o => o.PerformerId == userId))
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Take(100)
            .Select(ToDto(false))
            .ToListAsync();

        var ids = requests.Select(r => r.Id).ToList();

        var mine = await _db.PriceOffers.AsNoTracking()
            .Where(o => o.PerformerId == userId && ids.Contains(o.RequestId))
            .Select(OfferToDto)
            .ToListAsync();

        foreach (var r in requests)
        {
            r.MyOffer = mine.FirstOrDefault(o => o.RequestId == r.Id);

            // контакты открываются после выбора именно этого мастера
            if (r.MyOffer?.Status == nameof(OfferStatus.Accepted))
            {
                var full = await _db.PriceRequests.AsNoTracking()
                    .Where(x => x.Id == r.Id)
                    .Select(x => new { x.Address, x.ContactName, x.ContactPhone })
                    .FirstAsync();
                r.Address = full.Address;
                r.ContactName = full.ContactName;
                r.ContactPhone = full.ContactPhone;
            }
        }

        return Ok(requests);
    }

    // Создаёт предложение или меняет своё, пока клиент не выбрал
    [Authorize(Roles = "Performer")]
    [HttpPut("api/performer/requests/{id:int}/offer")]
    public async Task<ActionResult<OfferDto>> SaveOffer(int id, SaveOfferRequest body)
    {
        var userId = CurrentUserId;

        var request = await _db.PriceRequests
            .Include(r => r.Client)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (request == null || request.Client.IsBlocked)
            return NotFound(new { error = "Заявка не найдена" });

        if (request.Status != RequestStatus.Open)
            return Conflict(new { error = "Заявка уже закрыта" });

        var service = await _db.Services
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == body.ServiceId && s.PerformerId == userId);

        if (service == null || !service.IsActive)
            return BadRequest(new { error = "Выберите свою активную услугу" });

        var offer = await _db.PriceOffers
            .FirstOrDefaultAsync(o => o.RequestId == id && o.PerformerId == userId);

        var notify = false;

        if (offer == null)
        {
            offer = new PriceOffer
            {
                RequestId = id,
                PerformerId = userId,
                CreatedAt = DateTime.UtcNow
            };
            _db.PriceOffers.Add(offer);
            notify = true;
        }
        else if (offer.Status is OfferStatus.Accepted or OfferStatus.Rejected)
        {
            return Conflict(new { error = "Предложение уже рассмотрено" });
        }
        else if (offer.Status == OfferStatus.Withdrawn)
        {
            // мастер вернул отозванное предложение
            notify = true;
        }

        offer.ServiceId = service.Id;
        offer.Price = body.Price;
        offer.Message = string.IsNullOrWhiteSpace(body.Message) ? null : body.Message.Trim();
        offer.Status = OfferStatus.Pending;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // два одинаковых запроса одновременно упёрлись в уникальный индекс
            return Conflict(new { error = "Предложение уже отправлено, обновите страницу" });
        }

        if (notify)
        {
            _notifications.Add(
                request.ClientId,
                NotificationType.NewOffer,
                $"Новое предложение по заявке «{Short(request.Title)}»: {offer.Price:0.##}",
                requestId: request.Id);
            await _db.SaveChangesAsync();
            await _realtime.NotificationsChangedAsync(request.ClientId);
        }

        var dto = await _db.PriceOffers.AsNoTracking()
            .Where(o => o.Id == offer.Id)
            .Select(OfferToDto)
            .FirstAsync();

        await FillRatingsAsync(new[] { dto });
        return Ok(dto);
    }

    [Authorize(Roles = "Performer")]
    [HttpDelete("api/performer/requests/{id:int}/offer")]
    public async Task<IActionResult> Withdraw(int id)
    {
        var userId = CurrentUserId;

        var offer = await _db.PriceOffers
            .FirstOrDefaultAsync(o => o.RequestId == id && o.PerformerId == userId);

        if (offer == null)
            return NotFound(new { error = "Предложение не найдено" });

        if (offer.Status != OfferStatus.Pending)
            return Conflict(new { error = "Предложение уже нельзя отозвать" });

        offer.Status = OfferStatus.Withdrawn;
        await _db.SaveChangesAsync();

        return NoContent();
    }

    private static string Short(string text) => text.Length > 80 ? text[..80] + "…" : text;
}
