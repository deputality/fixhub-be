using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FixHub.Api.Controllers;

[ApiController]
[Route("api/performer/uploads")]
[Authorize(Roles = "Performer")]
public class UploadsController : ControllerBase
{
    private const long MaxBytes = 5 * 1024 * 1024;

    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private readonly IWebHostEnvironment _env;

    public UploadsController(IWebHostEnvironment env)
    {
        _env = env;
    }

    // Загрузка фото услуги: только JPG и PNG до 5 МБ. Возвращает адрес, который кладём в PhotoUrl услуги
    [HttpPost]
    [RequestSizeLimit(MaxBytes + 1024 * 1024)]
    public async Task<ActionResult<UploadResultDto>> Upload(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Файл не выбран" });

        if (file.Length > MaxBytes)
            return BadRequest(new { error = "Файл больше 5 МБ" });

        // Тип определяем по первым байтам файла, а не по имени и заголовку: их легко подделать
        var header = new byte[8];
        int read;
        await using (var stream = file.OpenReadStream())
        {
            read = await stream.ReadAsync(header, 0, header.Length);
        }

        string? extension = null;
        if (read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            extension = ".jpg";
        else if (read >= 8 && header.SequenceEqual(PngSignature))
            extension = ".png";

        if (extension == null)
            return BadRequest(new { error = "Подходят только файлы JPG и PNG" });

        var directory = Path.Combine(_env.ContentRootPath, "uploads");
        Directory.CreateDirectory(directory);

        // Имя генерируем сами, чтобы исходное имя файла не попало на диск
        var fileName = $"{Guid.NewGuid():N}{extension}";

        await using (var target = System.IO.File.Create(Path.Combine(directory, fileName)))
        {
            await file.CopyToAsync(target);
        }

        return Ok(new UploadResultDto { Url = $"/uploads/{fileName}" });
    }
}

public class UploadResultDto
{
    public string Url { get; set; } = null!;
}
