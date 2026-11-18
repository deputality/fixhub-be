namespace FixHub.Api.DTOs;

public class AdminStatsDto
{
    public int UsersCount { get; set; }
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

    // Динамика за последние 7 дней. Проценты считаются от значения 7 дней назад,
    // null, если сравнивать не с чем (тогда на фронте стрелка не показывается)
    public decimal? UsersGrowthPercent { get; set; }
    public decimal? OrdersGrowthPercent { get; set; }
    public decimal? AmountGrowthPercent { get; set; }

    // Изменение средней оценки за 7 дней в баллах (может быть отрицательным)
    public decimal? RatingChange { get; set; }

    public List<TopPerformerDto> TopPerformers { get; set; } = new();
}

public class TopPerformerDto
{
    public int PerformerId { get; set; }
    public string PerformerName { get; set; } = null!;
    public int CompletedOrders { get; set; }
    public decimal CompletedAmount { get; set; }
}

// Одна точка графика «Заказы по дням»
public class OrdersByDayDto
{
    public DateOnly Date { get; set; }
    public int Count { get; set; }
}