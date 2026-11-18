using FixHub.Api.Data;
using FixHub.Api.DTOs;
using FixHub.Api.Models;
using FixHub.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FixHub.Api.Controllers;

[ApiController]
[Route("api/admin/stats")]
[Authorize(Roles = "Admin")]
public class AdminStatsController : ControllerBase
{
    private readonly AppDbContext _db;

    public AdminStatsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<AdminStatsDto>> Get()
    {
        var roles = await _db.Users
            .GroupBy(u => u.Role)
            .Select(g => new { Role = g.Key, Count = g.Count() })
            .ToListAsync();

        int RoleCount(UserRole role) => roles.FirstOrDefault(x => x.Role == role)?.Count ?? 0;

        var byStatus = await _db.Orders
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        // в ответе есть все статусы, даже если заказов с таким статусом пока нет
        var ordersByStatus = Enum.GetValues<OrderStatus>()
            .ToDictionary(
                s => s.ToString(),
                s => byStatus.FirstOrDefault(x => x.Status == s)?.Count ?? 0);

        var since = DateTime.UtcNow.AddDays(-30);
        var weekAgo = DateTime.UtcNow.AddDays(-7);

        var usersCount = await _db.Users.CountAsync();
        var ordersCount = byStatus.Sum(x => x.Count);

        var completedAmount = await _db.Orders
            .Where(o => o.Status == OrderStatus.Completed)
            .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

        var average = await _db.Reviews
            .Where(r => !r.IsHidden)
            .AverageAsync(r => (decimal?)r.Rating);

        // что изменилось за неделю
        var newUsers = await _db.Users.CountAsync(u => u.CreatedAt >= weekAgo);
        var newOrders = await _db.Orders.CountAsync(o => o.CreatedAt >= weekAgo);

        var weekAmount = await _db.OrderStatusHistory
            .Where(h => h.ToStatus == OrderStatus.Completed && h.ChangedAt >= weekAgo)
            .SumAsync(h => (decimal?)h.Order.TotalAmount) ?? 0;

        var averageBefore = await _db.Reviews
            .Where(r => !r.IsHidden && r.CreatedAt < weekAgo)
            .AverageAsync(r => (decimal?)r.Rating);

        var top = await _db.Orders
            .Where(o => o.Status == OrderStatus.Completed)
            .GroupBy(o => new { o.PerformerId, o.Performer.FullName })
            .Select(g => new TopPerformerDto
            {
                PerformerId = g.Key.PerformerId,
                PerformerName = g.Key.FullName,
                CompletedOrders = g.Count(),
                CompletedAmount = g.Sum(o => o.TotalAmount)
            })
            .OrderByDescending(t => t.CompletedAmount).ThenBy(t => t.PerformerId)
            .Take(5)
            .ToListAsync();

        return Ok(new AdminStatsDto
        {
            UsersCount = usersCount,
            ClientsCount = RoleCount(UserRole.Client),
            PerformersCount = RoleCount(UserRole.Performer),
            BlockedUsersCount = await _db.Users.CountAsync(u => u.IsBlocked),

            ServicesCount = await _db.Services.CountAsync(),
            ActiveServicesCount = await _db.Services.CountAsync(s => s.IsActive),

            OrdersCount = ordersCount,
            OrdersByStatus = ordersByStatus,
            OrdersLast30Days = await _db.Orders.CountAsync(o => o.CreatedAt >= since),

            CompletedAmount = completedAmount,

            ReviewsCount = await _db.Reviews.CountAsync(),
            HiddenReviewsCount = await _db.Reviews.CountAsync(r => r.IsHidden),
            AverageRating = ReviewProjection.Round(average),

            UsersGrowthPercent = GrowthPercent(newUsers, usersCount),
            OrdersGrowthPercent = GrowthPercent(newOrders, ordersCount),
            AmountGrowthPercent = GrowthPercent(weekAmount, completedAmount),
            RatingChange = average != null && averageBefore != null
                ? ReviewProjection.Round(average - averageBefore)
                : null,

            TopPerformers = top
        });
    }

    // Заказы по дням для графика: за каждый из последних N дней (включая сегодня),
    // дни без заказов приходят с нулём, чтобы на графике не было дыр
    [HttpGet("orders-by-day")]
    public async Task<ActionResult<List<OrdersByDayDto>>> OrdersByDay([FromQuery] int days = 14)
    {
        if (days < 1 || days > 90)
            return BadRequest(new { error = "Период должен быть от 1 до 90 дней" });

        var first = DateTime.UtcNow.Date.AddDays(-(days - 1));

        var grouped = await _db.Orders
            .Where(o => o.CreatedAt >= first)
            .GroupBy(o => o.CreatedAt.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToListAsync();

        var result = Enumerable.Range(0, days)
            .Select(i =>
            {
                var day = first.AddDays(i);
                return new OrdersByDayDto
                {
                    Date = DateOnly.FromDateTime(day),
                    Count = grouped.FirstOrDefault(x => x.Day.Date == day)?.Count ?? 0
                };
            })
            .ToList();

        return Ok(result);
    }

    // Прирост за неделю в процентах от значения, которое было до неё
    private static decimal? GrowthPercent(decimal added, decimal total)
    {
        var before = total - added;
        if (before <= 0) return null;
        return Math.Round(added / before * 100, 1, MidpointRounding.AwayFromZero);
    }
}