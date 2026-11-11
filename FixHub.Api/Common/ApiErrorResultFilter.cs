using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FixHub.Api.Common;

public class ApiErrorResultFilter : IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is not ObjectResult objectResult)
            return;

        var status = objectResult.StatusCode
                     ?? (objectResult.Value as ProblemDetails)?.Status
                     ?? 200;

        if (status < 400 || objectResult.Value is ApiError)
            return;

        string message;
        IDictionary<string, string[]>? details = null;

        switch (objectResult.Value)
        {
            case ValidationProblemDetails validation:
                message = "Ошибка валидации данных";
                details = validation.Errors;
                break;
            case ProblemDetails problem:
                message = problem.Detail ?? problem.Title ?? ApiErrors.DefaultMessage(status);
                break;
            case null:
                message = ApiErrors.DefaultMessage(status);
                break;
            default:
                message = ReadErrorProperty(objectResult.Value) ?? ApiErrors.DefaultMessage(status);
                break;
        }

        context.Result = new ObjectResult(new ApiError
        {
            Status = status,
            Error = message,
            TraceId = context.HttpContext.TraceIdentifier,
            Details = details
        })
        {
            StatusCode = status
        };
    }

    public void OnResultExecuted(ResultExecutedContext context) { }

    // достаёт текст из ответов вида new { error = "..." }
    private static string? ReadErrorProperty(object value)
    {
        var property = value.GetType().GetProperty(
            "error", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        return property?.GetValue(value)?.ToString();
    }
}