using System.Text.Json.Serialization;

namespace FixHub.Api.Common;

public class ApiError
{
    public int Status { get; set; }
    public string Error { get; set; } = null!;
    public string? TraceId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IDictionary<string, string[]>? Details { get; set; }
}

public static class ApiErrors
{
    public static string DefaultMessage(int status) => status switch
    {
        400 => "Некорректный запрос",
        401 => "Требуется вход в систему",
        403 => "Недостаточно прав для выполнения операции",
        404 => "Ресурс не найден",
        405 => "Метод не поддерживается",
        409 => "Конфликт данных",
        415 => "Неподдерживаемый тип данных",
        500 => "Внутренняя ошибка сервера",
        _ => "Ошибка запроса"
    };
}