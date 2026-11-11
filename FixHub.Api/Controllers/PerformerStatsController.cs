using FixHub.Api.Data;
using FixHub.Api.DTOs;
using FixHub.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FixHub.Api.Controllers;

[ApiController]
[Route("api/performer/stats")]
[Authorize(Roles = "Performer")]
public class PerformerStatsController : ControllerBase
{
    private readonly AppDbContext _db;

    public PerformerStatsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<PerformerStatsDto>> Get()
    {
        var userId = int.Parse(User.FindFirst("sub")!.Value);

        var rows = await _db.Orders
            .AsNoTracking()
            .Where(o => o.PerformerId == userId)
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Sum = g.Sum(o => o.TotalAmount) })
            .ToListAsync();

        int CountOf(OrderStatus s) => rows.Where(r => r.Status == s).Sum(r => r.Count);

        return Ok(new PerformerStatsDto
        {
            CompletedOrders = CountOf(OrderStatus.Completed),
            CompletedAmount = rows.Where(r => r.Status == OrderStatus.Completed).Sum(r => r.Sum),
            NewOrders = CountOf(OrderStatus.New),
            ActiveOrders = CountOf(OrderStatus.Confirmed) + CountOf(OrderStatus.InProgress),
            CancelledOrders = CountOf(OrderStatus.Cancelled)
        });
    }
}