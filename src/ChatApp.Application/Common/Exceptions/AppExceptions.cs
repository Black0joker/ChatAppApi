namespace ChatApp.Application.Common.Exceptions;

public abstract class AppException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class UnauthorizedAppException(string message = "Unauthorized.") : AppException(message, 401);
public sealed class ForbiddenAppException(string message = "Forbidden.") : AppException(message, 403);
public sealed class NotFoundAppException(string message = "Resource not found.") : AppException(message, 404);
public sealed class ConflictAppException(string message = "Conflict.") : AppException(message, 409);
public sealed class ValidationAppException(string message = "Validation failed.") : AppException(message, 400);
