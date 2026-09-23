namespace AgroTrade.Application.Common;

/// <summary>
/// A business-rule failure with an HTTP status, a machine-readable code, and a
/// user-safe message. Global exception handling renders it as ProblemDetails.
/// </summary>
public sealed class ApiException : Exception
{
    public ApiException(int statusCode, string code, string message, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public string Code { get; }

    public static ApiException BadRequest(string code, string message) => new(400, code, message);
    public static ApiException Unauthorized(string code = "UNAUTHORIZED", string message = "Invalid credentials.") => new(401, code, message);
    public static ApiException NotFound(string code = "NOT_FOUND", string message = "Resource not found.") => new(404, code, message);
    public static ApiException Conflict(string code, string message) => new(409, code, message);
    public static ApiException Forbidden(string code = "FORBIDDEN", string message = "You do not have permission to perform this action.") => new(403, code, message);
}