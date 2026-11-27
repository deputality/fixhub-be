using FixHub.Api.Data;
using FixHub.Api.DTOs;
using FixHub.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FixHub.Api.Controllers;

// Документы мастера для проверки. Файлы лежат в закрытой папке (не в /uploads), поэтому
// открыть их можно только через этот контроллер: владельцу и администратору
[ApiController]
[Route("api")]
public class VerificationController : ControllerBase
{
    private const long MaxBytes = 8 * 1024 * 1024;
    private const int MaxDocuments = 5;
    private static readonly string[] Kinds = { "Passport", "Qualification", "Other" };
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<VerificationController> _logger;

    public VerificationController(AppDbContext db, IWebHostEnvironment env, ILogger<VerificationController> logger)
    {
        _db = db;
        _env = env;
        _logger = logger;
    }

    private int CurrentUserId => int.Parse(User.FindFirst("sub")!.Value);

    private string Directory_ => Path.Combine(_env.ContentRootPath, "private-uploads");

    // ----- мастер

    [Authorize(Roles = "Performer")]
    [HttpGet("performer/verification")]
    public async Task<ActionResult<VerificationStatusDto>> GetMine()
    {
        var userId = CurrentUserId;
        return Ok(new VerificationStatusDto
        {
            IsVerified = await _db.Users.Where(u => u.Id == userId).Select(u => u.IsVerified).FirstAsync(),
            Documents = await LoadDocuments(userId)
        });
    }

    [Authorize(Roles = "Performer")]
    [HttpPost("performer/verification/documents")]
    [RequestSizeLimit(MaxBytes + 1024 * 1024)]
    public async Task<ActionResult<VerificationDocumentDto>> Upload(IFormFile file, [FromForm] string? kind)
    {
        var userId = CurrentUserId;
        kind = string.IsNullOrWhiteSpace(kind) ? "Other" : kind;

        if (!Kinds.Contains(kind))
            return BadRequest(new { error = "Неизвестный тип документа" });

        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Файл не выбран" });

        if (file.Length > MaxBytes)
            return BadRequest(new { error = "Файл больше 8 МБ" });

        if (await _db.VerificationDocuments.CountAsync(d => d.UserId == userId) >= MaxDocuments)
            return BadRequest(new { error = $"Можно загрузить не больше {MaxDocuments} документов" });

        // Тип определяем по первым байтам, а не по имени и заголовку: их легко подделать
        var header = new byte[8];
        int read;
        await using (var stream = file.OpenReadStream())
        {
            read = await stream.ReadAsync(header.AsMemory());
        }

        string? extension = null;
        string? contentType = null;
        if (read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            (extension, contentType) = (".jpg", "image/jpeg");
        else if (read >= 8 && header.SequenceEqual(PngSignature))
            (extension, contentType) = (".png", "image/png");
        else if (read >= 5 && header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46 && header[4] == 0x2D)
            (extension, contentType) = (".pdf", "application/pdf");

        if (extension == null)
            return BadRequest(new { error = "Подходят только файлы JPG, PNG и PDF" });

        System.IO.Directory.CreateDirectory(Directory_);
        var storedName = $"{Guid.NewGuid():N}{extension}";

        await using (var target = System.IO.File.Create(Path.Combine(Directory_, storedName)))
        {
            await file.CopyToAsync(target);
        }

        var original = Path.GetFileName(file.FileName);
        var document = new VerificationDocument
        {
            UserId = userId,
            Kind = kind,
            OriginalName = string.IsNullOrWhiteSpace(original) ? $"document{extension}" : Trim(original, 200),
            StoredName = storedName,
            ContentType = contentType!,
            SizeBytes = file.Length
        };
        _db.VerificationDocuments.Add(document);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Мастер {UserId} загрузил документ {DocumentId} для проверки", userId, document.Id);
        return Ok(ToDto(document));
    }

    [Authorize(Roles = "Performer")]
    [HttpDelete("performer/verification/documents/{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = CurrentUserId;
        var document = await _db.VerificationDocuments.FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId);
        if (document == null)
            return NotFound(new { error = "Документ не найден" });

        _db.VerificationDocuments.Remove(document);
        await _db.SaveChangesAsync();

        var path = Path.Combine(Directory_, document.StoredName);
        if (System.IO.File.Exists(path))
            System.IO.File.Delete(path);

        return NoContent();
    }

    // ----- администратор

    [Authorize(Roles = "Admin")]
    [HttpGet("admin/users/{userId:int}/documents")]
    public async Task<ActionResult<List<VerificationDocumentDto>>> GetForAdmin(int userId)
    {
        if (!await _db.Users.AnyAsync(u => u.Id == userId))
            return NotFound(new { error = "Пользователь не найден" });

        return Ok(await LoadDocuments(userId));
    }

    // ----- сам файл: владелец или администратор

    [Authorize(Roles = "Performer,Admin")]
    [HttpGet("verification/documents/{id:int}/file")]
    public async Task<IActionResult> GetFile(int id)
    {
        var document = await _db.VerificationDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);

        // чужой документ мастеру не показываем и не подтверждаем, что он существует
        if (document == null || (!User.IsInRole("Admin") && document.UserId != CurrentUserId))
            return NotFound(new { error = "Документ не найден" });

        var path = Path.Combine(Directory_, document.StoredName);
        if (!System.IO.File.Exists(path))
            return NotFound(new { error = "Файл не найден" });

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Cache-Control"] = "private, no-store";
        return PhysicalFile(path, document.ContentType);
    }

    private async Task<List<VerificationDocumentDto>> LoadDocuments(int userId)
    {
        var items = await _db.VerificationDocuments.AsNoTracking()
            .Where(d => d.UserId == userId)
            .OrderBy(d => d.UploadedAt).ThenBy(d => d.Id)
            .ToListAsync();
        return items.Select(ToDto).ToList();
    }

    private static VerificationDocumentDto ToDto(VerificationDocument d) => new()
    {
        Id = d.Id,
        Kind = d.Kind,
        FileName = d.OriginalName,
        SizeBytes = d.SizeBytes,
        ContentType = d.ContentType,
        UploadedAt = d.UploadedAt
    };

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..max];
}