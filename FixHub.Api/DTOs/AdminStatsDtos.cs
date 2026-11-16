namespace FixHub.Api.DTOs;

public class AdminStatsDto
{
    public int ClientsCount { get; set; }
    public int PerformersCount { get; set; }
    public int BlockedUsersCount { get; set; }

    public int ServicesCount { get; set; }
    public int ActiveServicesCount { get; set; }

    public int OrdersCount { get; set; }
    public Dictionary<string, int> OrdersByStatus { get; set; } = new();
    public int OrdersLast30Days { get; set; }

    // Сумма выполненных заказов
    public decimal CompletedAmount { get; set; }

    public int ReviewsCount { get; set; }
    public int HiddenReviewsCount { get; set; }

    // Средняя оценка по видимым отзывам; null, если отзывов нет
    public decimal? AverageRating { get; set; }

    public List<TopPerformerDto> TopPerformers { get; set; } = new();
}

public class TopPerformerDto
{
    public int PerformerId { get; set; }
    public string PerformerName { get; set; } = null!;
    public int CompletedOrders { get; set; }
    public decimal CompletedAmount { get; set; }
}