using System.Linq.Expressions;
using FixHub.Api.Data;
using FixHub.Api.DTOs;
using FixHub.Api.Models;

namespace FixHub.Api.Services;

// Общая проекция услуги в DTO: нужна и каталогу, и избранному
public static class ServiceProjection
{
    // Рейтинг считается запросом по нескрытым отзывам; если отзывов нет, рейтинг null
    public static Expression<Func<Service, ServiceDto>> ToDto(AppDbContext db) => s => new ServiceDto
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
        PerformerAvatarUrl = s.Performer.AvatarUrl,
        PerformerSpecialization = s.Performer.Specialization,
        PerformerIsVerified = s.Performer.IsVerified,
        PerformerCity = s.Performer.City,
        PerformerServiceArea = s.Performer.ServiceArea,
        PerformerLatitude = s.Performer.Latitude,
        PerformerLongitude = s.Performer.Longitude,
        Rating = db.Reviews
            .Where(r => r.ServiceId == s.Id && !r.IsHidden)
            .Average(r => (decimal?)r.Rating),
        ReviewCount = db.Reviews
            .Count(r => r.ServiceId == s.Id && !r.IsHidden),
        PerformerRating = db.Reviews
            .Where(r => r.PerformerId == s.PerformerId && !r.IsHidden)
            .Average(r => (decimal?)r.Rating),
        PerformerReviewCount = db.Reviews
            .Count(r => r.PerformerId == s.PerformerId && !r.IsHidden)
    };

    // Услуга видна в каталоге: активна, исполнитель не заблокирован, категория не скрыта
    public static readonly Expression<Func<Service, bool>> IsVisible = s =>
        s.IsActive && !s.Performer.IsBlocked
        && !s.Category.IsHidden
        && (s.Category.Parent == null || !s.Category.Parent.IsHidden);

    public static void RoundRatings(ServiceDto s)
    {
        s.Rating = ReviewProjection.Round(s.Rating);
        s.PerformerRating = ReviewProjection.Round(s.PerformerRating);
    }
}
