using FixHub.Api.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
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
        _factory = new WebApplicationFactory<Program>();
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

// Все интеграционные тесты входят в одну коллекцию: у них общая база и они идут по очереди
[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<ApiFixture>
{
}