using System.Linq.Expressions;
using FixHub.Api.DTOs;
using FixHub.Api.Models;

namespace FixHub.Api.Services;

// Общая проекция отзыва в DTO и округление рейтинга: используются в нескольких контроллерах
public static class ReviewProjection
{
    public static readonly Expression<Func<Review, ReviewDto>> ToDto = r => new ReviewDto
    {
        Id = r.Id,
        OrderItemId = r.OrderItemId,
        ServiceId = r.ServiceId,
        ServiceTitle = r.OrderItem.ServiceTitle,
        ClientId = r.ClientId,
        ClientName = r.Client.FullName,
        Rating = r.Rating,
        Text = r.Text,
        IsHidden = r.IsHidden,
        CreatedAt = r.CreatedAt
    };

    // Рейтинг показываем с одним знаком после запятой
    public static decimal? Round(decimal? value) =>
        value == null ? null : Math.Round(value.Value, 1, MidpointRounding.AwayFromZero);
}