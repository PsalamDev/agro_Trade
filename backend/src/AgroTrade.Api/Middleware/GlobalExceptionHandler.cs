using AgroTrade.Application.Common;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace AgroTrade.Api.Middleware;

/// <summary>
/// Renders business-rule and validation failures as ProblemDetails (plan §03).
/// Unknown exceptions are logged and returned as a generic 500.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code, message, fieldErrors) = exception switch
        {
            ApiException api => (api.StatusCode, api.Code, api.Message, null),
            ValidationException validation => (
                StatusCodes.Status400BadRequest,
                "VALIDATION_ERROR",
                "One or more fields failed validation.",
                validation.Errors
                    .GroupBy(e => e.PropertyName, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())),
            _ => (StatusCodes.Status500InternalServerError, "INTERNAL_ERROR",
                "An unexpected error occurred.", null),
        };

        if (status >= 500)
        {
            logger.LogError(exception, "Unhandled exception: {Message}", exception.Message);
        }
        else
        {
            logger.LogWarning("Business failure {Code}: {Message}", code, message);
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Title = GetTitle(status),
            Detail = message,
        };
        problem.Extensions["code"] = code;
        if (fieldErrors is not null)
        {
            problem.Extensions["fieldErrors"] = fieldErrors;
        }

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }

    private static string GetTitle(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "Bad Request",
        StatusCodes.Status401Unauthorized => "Unauthorized",
        StatusCodes.Status403Forbidden => "Forbidden",
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status429TooManyRequests => "Too Many Requests",
        _ => "Internal Server Error",
    };
}