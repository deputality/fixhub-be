using System.Net;
using System.Net.Mail;

namespace FixHub.Api.Services;

public interface IEmailSender
{
    // false, если SMTP не настроен: письма тогда только пишутся в лог
    bool IsConfigured { get; }

    Task SendAsync(string to, string subject, string body, CancellationToken ct = default);
}

// Настройки берутся из раздела "Smtp" (Host, Port, User, Password, From, EnableSsl)
public class SmtpEmailSender : IEmailSender
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IConfiguration config, ILogger<SmtpEmailSender> logger)
    {
        _config = config;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_config["Smtp:Host"]);

    public async Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            // для разработки: видно ссылку для сброса пароля, не настраивая почту
            _logger.LogWarning("SMTP не настроен, письмо не отправлено. Кому: {To}. Тема: {Subject}\n{Body}", to, subject, body);
            return;
        }

        var from = _config["Smtp:From"];
        if (string.IsNullOrWhiteSpace(from))
            from = _config["Smtp:User"] ?? "no-reply@fixhub.local";

        using var message = new MailMessage(from, to, subject, body);
        using var client = new SmtpClient(_config["Smtp:Host"], _config.GetValue("Smtp:Port", 587))
        {
            EnableSsl = _config.GetValue("Smtp:EnableSsl", true)
        };

        var user = _config["Smtp:User"];
        if (!string.IsNullOrWhiteSpace(user))
            client.Credentials = new NetworkCredential(user, _config["Smtp:Password"]);

        await client.SendMailAsync(message, ct);
    }
}
