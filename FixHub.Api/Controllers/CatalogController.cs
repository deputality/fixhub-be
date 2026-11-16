using System.Linq.Expressions;
using FixHub.Api.Data;
using FixHub.Api.DTOs;
using FixHub.Api.Models;
using FixHub.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FixHub.Api.Controllers;

[ApiController]
[Route("api/catalog")]
public class CatalogController : ControllerBase
{
    private readonly AppDbContext _db;

    public CatalogController(AppDbContext db)
    {
        _db = db;
    }

    // Рейтинг считается запросом по нескрытым отзывам; если отзывов нет, рейтинг null
    private Expression<Func<Service, ServiceDto>> ToDto => s => new ServiceDto
    {
        Id = s.Id,
        Title = s.Title,
        Description = s.Description,
        PhotoUrl = s.PhotoUrl,
        Price = s.Price,
        Unit = s.Unit.ToString(),
        IsActive = s.IsActive,
        CreatedAt = s.CreatedAt,
        CategoryId = s.CategoryId,
        CategoryName = s.Category.Name,
        PerformerId = s.PerformerId,
        PerformerName = s.Performer.FullName,
        Rating = _db.Reviews
            .Where(r => r.ServiceId == s.Id && !r.IsHidden)
            .Average(r => (decimal?)r.Rating),
        ReviewCount = _db.Reviews
            .Count(r => r.ServiceId == s.Id && !r.IsHidden),
        PerformerRating = _db.Reviews
            .Where(r => r.PerformerId == s.PerformerId && !r.IsHidden)
            .Average(r => (decimal?)r.Rating),
        PerformerReviewCount = _db.Reviews
            .Count(r => r.PerformerId == s.PerformerId && !r.IsHidden)
    };

    // В каталог попадают только активные услуги незаблокированных исполнителей
    private IQueryable<Service> Visible() =>
        _db.Services
            .AsNoTracking()
            .Where(s => s.IsActive && !s.Performer.IsBlocked);

    [HttpGet]
    public async Task<ActionResult<PagedResult<ServiceDto>>> Search([FromQuery] CatalogQuery query)
    {
        if (query.MinPrice != null && query.MaxPrice != null && query.MinPrice > query.MaxPrice)
            return BadRequest(new { error = "Минимальная цена не может быть больше максимальной" });

        var q = Visible();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{EscapeLike(query.Search.Trim())}%";
            q = q.Where(s => EF.Functions.ILike(s.Title, pattern));
        }

        if (query.CategoryId != null)
        {
            var categoryId = query.CategoryId.Value;
            // выбранная категория и её прямые подкатегории
            q = q.Where(s => s.CategoryId == categoryId || s.Category.ParentId == categoryId);
        }

        if (query.MinPrice != null)
            q = q.Where(s => s.Price >= query.MinPrice);

        if (query.MaxPrice != null)
            q = q.Where(s => s.Price <= query.MaxPrice);

        // услуги без отзывов (рейтинг null) под фильтр не попадают
        if (query.MinRating != null)
        {
            var minRating = query.MinRating.Value;
            q = q.Where(s => _db.Reviews
                .Where(r => r.ServiceId == s.Id && !r.IsHidden)
                .Average(r => (decimal?)r.Rating) >= minRating);
        }

        var totalCount = await q.CountAsync();

        // Id в конце нужен, чтобы порядок на страницах не «прыгал» при равных значениях
        var ordered = query.Sort switch
        {
            CatalogSort.PriceAsc => q.OrderBy(s => s.Price).ThenBy(s => s.Id),
            CatalogSort.PriceDesc => q.OrderByDescending(s => s.Price).ThenBy(s => s.Id),
            CatalogSort.TitleAsc => q.OrderBy(s => s.Title).ThenBy(s => s.Id),
            // в PostgreSQL null при сортировке по убыванию идёт первым,
            // поэтому услуги без отзывов отправляем в конец отдельным признаком
            CatalogSort.RatingDesc => q
                .OrderByDescending(s => _db.Reviews.Any(r => r.ServiceId == s.Id && !r.IsHidden))
                .ThenByDescending(s => _db.Reviews
                    .Where(r => r.ServiceId == s.Id && !r.IsHidden)
                    .Average(r => (decimal?)r.Rating))
                .ThenByDescending(s => _db.Reviews.Count(r => r.ServiceId == s.Id && !r.IsHidden))
                .ThenBy(s => s.Id),
            _ => q.OrderByDescending(s => s.CreatedAt).ThenBy(s => s.Id)
        };

        var items = await ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(ToDto)
            .ToListAsync();

        items.ForEach(RoundRatings);

        return Ok(new PagedResult<ServiceDto>
        {
            Items = items,
            Page = query.Page,
            PageSize = query.PageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)query.PageSize)
        });
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ServiceDto>> GetById(int id)
    {
        var service = await Visible()
            .Where(s => s.Id == id)
            .Select(ToDto)
            .FirstOrDefaultAsync();

        if (service == null)
            return NotFound(new { error = "Услуга не найдена" });

        RoundRatings(service);
        return Ok(service);
    }

    // Нескрытые отзывы об услуге, новые первыми
    [HttpGet("{id:int}/reviews")]
    public async Task<ActionResult<PagedResult<ReviewDto>>> GetReviews(int id, [FromQuery] ReviewQuery query)
    {
        // как и карточка услуги: скрытая услуга «не существует»
        if (!await Visible().AnyAsync(s => s.Id == id))
            return NotFound(new { error = "Услуга не найдена" });

        var q = _db.Reviews
            .AsNoTracking()
            .Where(r => r.ServiceId == id && !r.IsHidden);

        var total = await q.CountAsync();

        var items = await q
            .OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(ReviewProjection.ToDto)
            .ToListAsync();

        return Ok(new PagedResult<ReviewDto>
        {
            Items = items,
            Page = query.Page,
            PageSize = query.PageSize,
            TotalCount = total,
            TotalPages = (int)Math.Ceiling(total / (double)query.PageSize)
        });
    }

    private static void RoundRatings(ServiceDto s)
    {
        s.Rating = ReviewProjection.Round(s.Rating);
        s.PerformerRating = ReviewProjection.Round(s.PerformerRating);
    }

    // Спецсимволы LIKE (% и _) в поиске должны считаться обычными буквами
    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}