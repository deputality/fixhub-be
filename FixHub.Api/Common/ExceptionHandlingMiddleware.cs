namespace FixHub.Api.Common;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // клиент сам оборвал запрос: это не ошибка сервера
            _logger.LogDebug("Запрос отменён клиентом: {Method} {Path}",
                context.Request.Method, context.Request.Path);
        }
        catch (BadHttpRequestException ex)
        {
            await Write(context, ex.StatusCode, "Некорректный запрос");
        }
        catch (Exception ex)
        {
            // полная причина (с SQL, путями и стеком) только в лог
            _logger.LogError(ex,
                "Необработанная ошибка. TraceId={TraceId}, {Method} {Path}",
                context.TraceIdentifier, context.Request.Method, context.Request.Path);

            if (context.Response.HasStarted)
                throw;

            // клиенту уходит только общее сообщение и traceId для поиска в логе
            await Write(context, StatusCodes.Status500InternalServerError,
                ApiErrors.DefaultMessage(500));
        }
    }

    private static Task Write(HttpContext context, int status, string message)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new ApiError
        {
            Status = status,
            Error = message,
            TraceId = context.TraceIdentifier
        });
    }
}