namespace FixHub.Api.Models;

// Документ мастера для проверки (паспорт, диплом, сертификат). Лежит в закрытой папке,
// отдаётся только владельцу и администратору
public class VerificationDocument
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    // Passport, Qualification, Other
    public string Kind { get; set; } = null!;

    public string OriginalName { get; set; } = null!;

    // Имя файла на диске; задаём сами, исходное имя на диск не попадает
    public string StoredName { get; set; } = null!;
    public string ContentType { get; set; } = null!;
    public long SizeBytes { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
