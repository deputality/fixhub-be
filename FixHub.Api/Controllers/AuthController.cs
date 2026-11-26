using FixHub.Api.Data;
using FixHub.Api.DTOs;
using FixHub.Api.Models;
using FixHub.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace FixHub.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<User> _hasher;
    private readonly ITokenService _tokens;
    private readonly ILogger<AuthController> _logger;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IEmailSender _email;
    private readonly ILoginThrottle _throttle;

    public AuthController(
        AppDbContext db,
        IPasswordHasher<User> hasher,
        ITokenService tokens,
        ILogger<AuthController> logger,
        IConfiguration config,
        IHttpClientFactory httpFactory,
        IEmailSender email,
        ILoginThrottle throttle)
    {
        _db = db;
        _hasher = hasher;
        _tokens = tokens;
        _logger = logger;
        _config = config;
        _httpFactory = httpFactory;
        _email = email;
        _throttle = throttle;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        if (request.Role != UserRole.Client && request.Role != UserRole.Performer)
            return BadRequest(new { error = "Регистрация доступна только для ролей Client и Performer" });

        var email = request.Email.Trim().ToLowerInvariant();

        if (await _db.Users.AnyAsync(u => u.Email == email))
            return Conflict(new { error = "Пользователь с таким email уже существует" });

        var user = new User
        {
            Email = email,
            FullName = request.FullName.Trim(),
            Phone = request.Phone?.Trim(),
            Role = request.Role
        };
        user.PasswordHash = _hasher.HashPassword(user, request.Password);

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Зарегистрирован пользователь {UserId} с ролью {Role}", user.Id, user.Role);

        return Ok(BuildResponse(user));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        // после серии неверных паролей вход для этого email закрыт на время, даже если следующий пароль верный
        var wait = _throttle.GetLockRemaining(email);
        if (wait != null)
        {
            var minutes = Math.Max(1, (int)Math.Ceiling(wait.Value.TotalMinutes));
            Response.Headers.RetryAfter = ((int)Math.Ceiling(wait.Value.TotalSeconds)).ToString();
            _logger.LogWarning("Вход временно закрыт из-за частых неверных паролей: {Email}", email);
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { error = $"Слишком много неудачных попыток входа. Повторите через {minutes} мин." });
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user == null)
        {
            // считаем и несуществующие адреса: так по ответу не узнать, зарегистрирован ли email
            _throttle.RegisterFailure(email);
            _logger.LogWarning("Неудачная попытка входа: {Email}", email);
            return Unauthorized(new { error = "Неверный email или пароль" });
        }

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            _throttle.RegisterFailure(email);
            _logger.LogWarning("Неудачная попытка входа: {Email}", email);
            return Unauthorized(new { error = "Неверный email или пароль" });
        }

        _throttle.Reset(email);

        if (user.IsBlocked)
        {
            _logger.LogWarning("Попытка входа заблокированного пользователя {UserId}", user.Id);
            return StatusCode(StatusCodes.Status403Forbidden,
                new { error = "Учётная запись заблокирована" });
        }

        _logger.LogInformation("Вход пользователя {UserId}", user.Id);
        return Ok(BuildResponse(user));
    }

    // Вход и регистрация через Google: фронт присылает access token,
    // бэкенд проверяет его у Google и выдаёт обычный JWT FixHub
    [HttpPost("google")]
    public async Task<ActionResult<AuthResponse>> Google(GoogleLoginRequest request)
    {
        var clientId = _config["Google:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId))
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Вход через Google не настроен" });

        var profile = await VerifyGoogleToken(request.AccessToken, clientId);
        if (profile == null)
            return Unauthorized(new { error = "Не удалось подтвердить аккаунт Google" });

        var email = profile.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user == null)
        {
            var role = request.Role ?? UserRole.Client;
            if (role != UserRole.Client && role != UserRole.Performer)
                return BadRequest(new { error = "Регистрация доступна только для ролей Client и Performer" });

            var name = string.IsNullOrWhiteSpace(profile.Name) ? email.Split('@')[0] : profile.Name.Trim();

            user = new User
            {
                Email = email,
                FullName = name.Length > 200 ? name[..200] : name,
                Role = role
            };
            // пароль не используется: ставим случайный, чтобы по нему нельзя было войти
            user.PasswordHash = _hasher.HashPassword(user, Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));

            _db.Users.Add(user);
            await _db.SaveChangesAsync();
            _logger.LogInformation("Через Google зарегистрирован пользователь {UserId} с ролью {Role}", user.Id, user.Role);
        }
        else if (user.IsBlocked)
        {
            _logger.LogWarning("Попытка входа заблокированного пользователя {UserId}", user.Id);
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Учётная запись заблокирована" });
        }
        else
        {
            _logger.LogInformation("Вход через Google: пользователь {UserId}", user.Id);
        }

        return Ok(BuildResponse(user));
    }

    private sealed record GoogleProfile(string Email, string? Name);

    private async Task<GoogleProfile?> VerifyGoogleToken(string accessToken, string clientId)
    {
        try
        {
            var http = _httpFactory.CreateClient();

            // 1. токен должен быть выдан именно нашему приложению
            var info = await http.PostAsync("https://oauth2.googleapis.com/tokeninfo",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["access_token"] = accessToken }));
            if (!info.IsSuccessStatusCode) return null;

            using var infoJson = System.Text.Json.JsonDocument.Parse(await info.Content.ReadAsStringAsync());
            var aud = infoJson.RootElement.TryGetProperty("aud", out var a) ? a.GetString() : null;
            if (aud != clientId) return null;

            // 2. данные пользователя
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v3/userinfo");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            var res = await http.SendAsync(req);
            if (!res.IsSuccessStatusCode) return null;

            using var json = System.Text.Json.JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            var root = json.RootElement;

            var email = root.TryGetProperty("email", out var e) ? e.GetString() : null;
            var verified = root.TryGetProperty("email_verified", out var v) && v.ValueKind == System.Text.Json.JsonValueKind.True;
            if (string.IsNullOrWhiteSpace(email) || !verified) return null;

            var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
            return new GoogleProfile(email, name);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Не удалось проверить токен Google");
            return null;
        }
    }

    // Шаг 1 восстановления пароля: отправляем ссылку на почту.
    // Ответ всегда одинаковый, чтобы по нему нельзя было узнать, зарегистрирован ли email.
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request)
    {
        const string answer = "Если такой email зарегистрирован, мы отправили на него ссылку для сброса пароля";

        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null || user.IsBlocked)
            return Ok(new { message = answer });

        var now = DateTime.UtcNow;

        // не чаще одного письма в минуту, чтобы форму нельзя было использовать для рассылки спама
        var tooSoon = await _db.PasswordResetTokens
            .AnyAsync(t => t.UserId == user.Id && t.CreatedAt > now.AddMinutes(-1));
        if (tooSoon)
            return Ok(new { message = answer });

        // прежние неиспользованные ссылки перестают работать
        var old = await _db.PasswordResetTokens
            .Where(t => t.UserId == user.Id && t.UsedAt == null)
            .ToListAsync();
        foreach (var t in old) t.UsedAt = now;

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        _db.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = HashToken(token),
            ExpiresAt = now.AddHours(1),
            CreatedAt = now
        });
        await _db.SaveChangesAsync();

        var baseUrl = (_config["App:BaseUrl"] ?? "http://localhost:5173").TrimEnd('/');
        var link = $"{baseUrl}/reset-password?token={token}";

        try
        {
            await _email.SendAsync(user.Email, "FixHub: сброс пароля",
                $"Здравствуйте, {user.FullName.Split(' ')[0]}!\n\n" +
                "Вы запросили сброс пароля. Перейдите по ссылке, чтобы задать новый (она действует 1 час):\n" +
                $"{link}\n\n" +
                "Если это были не вы, просто проигнорируйте письмо: пароль останется прежним.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось отправить письмо для сброса пароля пользователю {UserId}", user.Id);
        }

        _logger.LogInformation("Запрошен сброс пароля пользователя {UserId}", user.Id);
        return Ok(new { message = answer });
    }

    // Шаг 2: по ссылке из письма задаём новый пароль
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
    {
        var hash = HashToken(request.Token.Trim());
        var token = await _db.PasswordResetTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash);

        if (token == null || token.UsedAt != null || token.ExpiresAt < DateTime.UtcNow || token.User.IsBlocked)
            return BadRequest(new { error = "Ссылка недействительна или устарела. Запросите новую." });

        token.User.PasswordHash = _hasher.HashPassword(token.User, request.NewPassword);
        token.UsedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Пароль пользователя {UserId} сброшен по ссылке из письма", token.UserId);
        return Ok(new { message = "Пароль изменён. Теперь можно войти." });
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me()
    {
        var idClaim = User.FindFirst("sub")?.Value;
        if (!int.TryParse(idClaim, out var userId))
            return Unauthorized();

        var user = await _db.Users.FindAsync(userId);
        if (user == null || user.IsBlocked)
            return Unauthorized();

        return Ok(ToDto(user));
    }

    private AuthResponse BuildResponse(User user)
    {
        var (token, expiresAt) = _tokens.CreateToken(user);
        return new AuthResponse
        {
            Token = token,
            ExpiresAt = expiresAt,
            User = ToDto(user)
        };
    }

    private static UserDto ToDto(User user) => new()
    {
        Id = user.Id,
        Email = user.Email,
        FullName = user.FullName,
        Phone = user.Phone,
        Role = user.Role.ToString()
    };
}