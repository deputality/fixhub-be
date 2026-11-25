using System.Net;
using FixHub.Api.Data;
using FixHub.Api.Models;
using FixHub.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FixHub.Api.Controllers;

// Страницы для превью ссылок в мессенджерах и соцсетях.
// Фронтенд — SPA и отдаёт всем один и тот же index.html, а краулеры Telegram, WhatsApp и VK
// JavaScript не выполняют. Эти адреса отдают маленький HTML с нужными заголовком, описанием
// и картинкой (Open Graph), а людей сразу перенаправляют на настоящую страницу сайта.
[ApiController]
[Route("share")]
[ApiExplorerSettings(IgnoreApi = true)]
public class ShareController : ControllerBase
{
    private const int DescriptionLimit = 200;

    private readonly AppDbContext _db;
    private readonly string _siteUrl;

    public ShareController(AppDbContext db, IConfiguration configuration)
    {
        _db = db;
        _siteUrl = (configuration["App:BaseUrl"] ?? "http://localhost:5173").TrimEnd('/');
    }

    [HttpGet("services/{id:int}")]
    public async Task<IActionResult> Service(int id)
    {
        var service = await _db.Services
            .AsNoTracking()
            .Where(ServiceProjection.IsVisible)
            .Where(s => s.Id == id)
            .Select(s => new
            {
                s.Title,
                s.Description,
                s.PhotoUrl,
                s.Price,
                s.Unit,
                Performer = s.Performer.FullName,
                s.Performer.City
            })
            .FirstOrDefaultAsync();

        var target = $"{_siteUrl}/services/{id}";
        if (service == null)
            return Redirect(_siteUrl + "/");

        var unit = service.Unit == PricingUnit.Hour ? "час" : "шт.";
        var place = string.IsNullOrWhiteSpace(service.City) ? "" : $", {service.City}";
        var description = $"от {service.Price:0.##} BYN/{unit}. {Shorten(service.Description)}";

        return Page(
            title: $"{service.Title} — {service.Performer}{place} | FixHub",
            description: description,
            image: service.PhotoUrl,
            target: target);
    }

    [HttpGet("performers/{id:int}")]
    public async Task<IActionResult> Performer(int id)
    {
        var performer = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == id && u.Role == UserRole.Performer && !u.IsBlocked)
            .Select(u => new
            {
                u.FullName,
                u.Specialization,
                u.City,
                u.ServiceArea,
                u.Bio,
                u.AvatarUrl,
                Rating = _db.Reviews
                    .Where(r => r.PerformerId == u.Id && !r.IsHidden)
                    .Average(r => (decimal?)r.Rating),
                ReviewCount = _db.Reviews.Count(r => r.PerformerId == u.Id && !r.IsHidden)
            })
            .FirstOrDefaultAsync();

        var target = $"{_siteUrl}/performers/{id}";
        if (performer == null)
            return Redirect(_siteUrl + "/");

        var role = string.IsNullOrWhiteSpace(performer.Specialization) ? "Мастер" : performer.Specialization;
        var place = string.Join(", ", new[] { performer.City, performer.ServiceArea }
            .Where(p => !string.IsNullOrWhiteSpace(p)));

        var facts = new List<string>();
        if (!string.IsNullOrEmpty(place)) facts.Add(place);
        if (performer.Rating != null)
            facts.Add($"рейтинг {ReviewProjection.Round(performer.Rating):0.0} ({performer.ReviewCount} отзывов)");

        var description = string.Join(". ", new[] { string.Join(", ", facts), Shorten(performer.Bio) }
            .Where(p => !string.IsNullOrWhiteSpace(p)));

        return Page(
            title: $"{performer.FullName} — {role} | FixHub",
            description: string.IsNullOrWhiteSpace(description) ? "Мастер на FixHub" : description,
            image: performer.AvatarUrl,
            target: target);
    }

    private ContentResult Page(string title, string description, string? image, string target)
    {
        var imageUrl = AbsoluteUrl(image) ?? $"{_siteUrl}/hero-master.png";

        var html = $$"""
            <!doctype html>
            <html lang="ru">
            <head>
            <meta charset="utf-8">
            <title>{{Enc(title)}}</title>
            <meta name="description" content="{{Enc(description)}}">
            <link rel="canonical" href="{{Enc(target)}}">
            <meta property="og:type" content="website">
            <meta property="og:site_name" content="FixHub">
            <meta property="og:locale" content="ru_RU">
            <meta property="og:title" content="{{Enc(title)}}">
            <meta property="og:description" content="{{Enc(description)}}">
            <meta property="og:url" content="{{Enc(target)}}">
            <meta property="og:image" content="{{Enc(imageUrl)}}">
            <meta name="twitter:card" content="summary_large_image">
            <meta name="twitter:title" content="{{Enc(title)}}">
            <meta name="twitter:description" content="{{Enc(description)}}">
            <meta name="twitter:image" content="{{Enc(imageUrl)}}">
            <meta http-equiv="refresh" content="0;url={{Enc(target)}}">
            </head>
            <body>
            <p><a href="{{Enc(target)}}">Открыть на FixHub</a></p>
            </body>
            </html>
            """;

        return Content(html, "text/html; charset=utf-8");
    }

    // фото услуг лежат на нашем сервере и отдаются по /uploads/...; в превью нужен полный адрес
    private string? AbsoluteUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (url.StartsWith("http://") || url.StartsWith("https://")) return url;
        return _siteUrl + (url.StartsWith('/') ? url : "/" + url);
    }

    private static string Enc(string value) => WebUtility.HtmlEncode(value);

    private static string Shorten(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= DescriptionLimit ? flat : flat[..(DescriptionLimit - 1)].TrimEnd() + "…";
    }
}
