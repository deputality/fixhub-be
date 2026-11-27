using FixHub.Api.Data;
using FixHub.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace FixHub.Tests.Integration;

// Создаётся один раз на все интеграционные тесты:
// временная база + запущенное приложение
public class ApiFixture : IAsyncLifetime
{
    private const string ServerPart = "Host=localhost;Port=5432;Username=fixhub;Password=fixhub123";

    private readonly string _dbName = $"fixhub_test_{Guid.NewGuid():N}";
    private WebApplicationFactory<Program> _factory = null!;

    private string ConnectionString => $"{ServerPart};Database={_dbName}";

    public HttpClient Client { get; private set; } = null!;

    // Для WebSocket-тестов: соединения с SignalR идут в то же запущенное приложение
    public HttpMessageHandler CreateHandler() => _factory.Server.CreateHandler();

    // Вместо настоящей почты письма собираются здесь: из них тесты берут ссылку для сброса пароля
    public FakeEmailSender Emails { get; } = new();

    public Task InitializeAsync()
    {
        // 1. Создаём пустую тестовую базу и накатываем миграции
        using (var db = new AppDbContext(BuildOptions()))
        {
            db.Database.Migrate();
        }

        // 2. Приложение возьмёт строку подключения из переменной окружения
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", ConnectionString);

        // 3. Запускаем приложение внутри теста (при старте создаётся администратор)
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IEmailSender>(Emails)));
        Client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();

        // Удаляем временную базу
        NpgsqlConnection.ClearAllPools();
        using (var db = new AppDbContext(BuildOptions()))
        {
            await db.Database.EnsureDeletedAsync();
        }

        Environment.SetEnvironmentVariable("ConnectionStrings__Default", null);
    }

    private DbContextOptions<AppDbContext> BuildOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
}

// Почтовый «отправитель» для тестов: ничего не отправляет, а запоминает письма
public class FakeEmailSender : IEmailSender
{
    public record Mail(string To, string Subject, string Body);

    private readonly System.Collections.Concurrent.ConcurrentQueue<Mail> _sent = new();

    // false: фоновая рассылка уведомлений при тестах не запускается и не засоряет список
    public bool IsConfigured => false;

    public IReadOnlyList<Mail> To(string email) =>
        _sent.Where(m => string.Equals(m.To, email, StringComparison.OrdinalIgnoreCase)).ToList();

    public Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        _sent.Enqueue(new Mail(to, subject, body));
        return Task.CompletedTask;
    }
}

// Все интеграционные тесты входят в одну коллекцию: у них общая база и они идут по очереди
[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<ApiFixture>
{
}