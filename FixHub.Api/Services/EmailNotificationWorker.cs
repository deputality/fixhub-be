using FixHub.Api.Data;
using FixHub.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FixHub.Api.Services;

// Берёт новые уведомления из базы и отправляет их на почту.
// Так письма не тормозят запросы, а при сбое почты уведомление просто уйдёт при следующей проверке.
public class EmailNotificationWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(20);

    // слишком старые уведомления письмом уже не отправляем
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly IEmailSender _email;
    private readonly IConfiguration _config;
    private readonly ILogger<EmailNotificationWorker> _logger;

    public EmailNotificationWorker(
        IServiceScopeFactory scopes,
        IEmailSender email,
        IConfiguration config,
        ILogger<EmailNotificationWorker> logger)
    {
        _scopes = scopes;
        _email = email;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_email.IsConfigured)
                    await SendPending(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Не удалось отправить письма с уведомлениями");
            }

            await Task.Delay(Interval, stoppingToken).ContinueWith(_ => { });
        }
    }

    private async Task SendPending(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var since = DateTime.UtcNow - MaxAge;
        var pending = await db.Notifications
            .Include(n => n.User)
            .Where(n => n.EmailSentAt == null && n.CreatedAt > since)
            .OrderBy(n => n.Id)
            .Take(50)
            .ToListAsync(ct);

        var baseUrl = (_config["App:BaseUrl"] ?? "http://localhost:5173").TrimEnd('/');

        foreach (var n in pending)
        {
            // у тех, кто отключил письма или заблокирован, просто закрываем уведомление
            if (!n.User.EmailNotifications || n.User.IsBlocked)
            {
                n.EmailSentAt = DateTime.UtcNow;
                continue;
            }

            var path = n.User.Role == UserRole.Performer
                ? (n.ReviewId != null ? "/performer/reviews" : "/performer/orders")
                : "/orders";

            var firstName = n.User.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            var body = $"Здравствуйте, {firstName}!\n\n{n.Text}\n\n" +
                       $"Открыть на сайте: {baseUrl}{path}\n\n" +
                       "Письма об уведомлениях можно отключить в настройках профиля.";

            try
            {
                await _email.SendAsync(n.User.Email, $"FixHub: {n.Text}", body, ct);
                n.EmailSentAt = DateTime.UtcNow;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Письмо для пользователя {UserId} не отправлено, попробуем позже", n.UserId);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
