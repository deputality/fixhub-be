using System.Globalization;

namespace FixHub.Api.Common;

// Время слотов ходит по API строкой "HH:mm", в БД лежит TimeOnly
public static class TimeFormat
{
    // Для атрибута [RegularExpression]: часы 0-23, минуты 00-59
    public const string Pattern = @"^([01]?\d|2[0-3]):[0-5]\d$";

    private static readonly string[] Formats = { "H:mm", "HH:mm" };

    public static string? Format(TimeOnly? time) =>
        time?.ToString("HH:mm", CultureInfo.InvariantCulture);

    // Пустая или неверная строка даёт null: форму проверяет атрибут в запросе
    public static TimeOnly? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return TimeOnly.TryParseExact(value.Trim(), Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t
            : null;
    }
}
