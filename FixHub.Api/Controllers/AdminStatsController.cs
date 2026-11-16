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

        var completedAmount = await _db.Orders
            .Where(o => o.Status == OrderStatus.Completed)
            .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

        var average = await _db.Reviews
            .Where(r => !r.IsHidden)
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
            ClientsCount = RoleCount(UserRole.Client),
            PerformersCount = RoleCount(UserRole.Performer),
            BlockedUsersCount = await _db.Users.CountAsync(u => u.IsBlocked),

            ServicesCount = await _db.Services.CountAsync(),
            ActiveServicesCount = await _db.Services.CountAsync(s => s.IsActive),

            OrdersCount = byStatus.Sum(x => x.Count),
            OrdersByStatus = ordersByStatus,
            OrdersLast30Days = await _db.Orders.CountAsync(o => o.CreatedAt >= since),

            CompletedAmount = completedAmount,

            ReviewsCount = await _db.Reviews.CountAsync(),
            HiddenReviewsCount = await _db.Reviews.CountAsync(r => r.IsHidden),
            AverageRating = ReviewProjection.Round(average),

            TopPerformers = top
        });
    }
}