using System.Text;
using FixHub.Api.Common;
using FixHub.Api.Data;
using FixHub.Api.Models;
using FixHub.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;

// Временный логгер: ловит ошибки, возникшие до загрузки настроек
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Логи: настройки берутся из раздела "Serilog" в appsettings.json
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    // База данных
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

    // Контроллеры, единый формат ошибок и Swagger
    builder.Services.AddControllers(options => options.Filters.Add<ApiErrorResultFilter>())
        .AddJsonOptions(o =>
            o.JsonSerializerOptions.Converters.Add(
                new System.Text.Json.Serialization.JsonStringEnumConverter()));
    builder.Services.AddEndpointsApiExplorer();

    builder.Services.AddSwaggerGen(options =>
    {
        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Вставь JWT-токен (без слова Bearer)"
        });

        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });
    });

    // JWT-аутентификация
    var jwtKey = builder.Configuration["Jwt:Key"]
        ?? throw new InvalidOperationException("Jwt:Key не задан в настройках");

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = builder.Configuration["Jwt:Issuer"],
                ValidateAudience = true,
                ValidAudience = builder.Configuration["Jwt:Audience"],
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
                NameClaimType = "sub",
                RoleClaimType = "role"
            };

            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = async ctx =>
                {
                    var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();

                    if (!int.TryParse(ctx.Principal?.FindFirst("sub")?.Value, out var userId))
                    {
                        ctx.Fail("Некорректный токен");
                        return;
                    }

                    var blocked = await db.Users
                        .AsNoTracking()
                        .Where(u => u.Id == userId)
                        .Select(u => (bool?)u.IsBlocked)
                        .FirstOrDefaultAsync();

                    // null: пользователя нет, true: заблокирован
                    if (blocked != false)
                        ctx.Fail("Пользователь заблокирован или не найден");
                }
            };
        });

    builder.Services.AddAuthorization();

    // Свои сервисы
    builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
    builder.Services.AddScoped<ITokenService, TokenService>();

    var app = builder.Build();

    // Одна строка в логе на каждый запрос (метод, адрес, код ответа, время)
    app.UseSerilogRequestLogging(options =>
    {
        options.GetLevel = (httpContext, elapsed, ex) =>
            ex != null || httpContext.Response.StatusCode >= 500
                ? LogEventLevel.Error
                : httpContext.Request.Path.StartsWithSegments("/swagger")
                    ? LogEventLevel.Verbose
                    : LogEventLevel.Information;
    });

    // Перехват необработанных исключений: клиенту 500 в едином формате, в лог полная причина
    app.UseMiddleware<ExceptionHandlingMiddleware>();

    // Ответы без тела (401, 403, 404 для несуществующего адреса) тоже в едином формате
    app.UseStatusCodePages(async statusContext =>
    {
        var http = statusContext.HttpContext;
        var status = http.Response.StatusCode;

        await http.Response.WriteAsJsonAsync(new ApiError
        {
            Status = status,
            Error = ApiErrors.DefaultMessage(status),
            TraceId = http.TraceIdentifier
        });
    });

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseHttpsRedirection();

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();

    // Создание администратора при первом запуске
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();

        if (!db.Users.Any(u => u.Role == UserRole.Admin))
        {
            var admin = new User
            {
                Email = builder.Configuration["Admin:Email"]!.ToLowerInvariant(),
                FullName = "Администратор",
                Role = UserRole.Admin
            };
            admin.PasswordHash = hasher.HashPassword(admin, builder.Configuration["Admin:Password"]!);
            db.Users.Add(admin);
            db.SaveChanges();

            app.Logger.LogInformation("Создан администратор по умолчанию");
        }
    }

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    // HostAbortedException штатно бросает dotnet ef, его в лог не пишем
    Log.Fatal(ex, "Приложение аварийно завершилось");
}
finally
{
    Log.CloseAndFlush();
}
public partial class Program { }