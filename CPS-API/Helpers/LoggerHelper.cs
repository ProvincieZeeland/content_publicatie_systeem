using Microsoft.AspNetCore.Mvc;

namespace CPS_API.Helpers
{
    public static class LoggerHelper
    {
        public static IActionResult LogAndThrowInternalServerError(this ControllerBase controller, ILogger logger, Exception? exception = null, string? errorMessage = "Internal Server Error", params object?[] args)
        {
            logger.LogError(exception, errorMessage, args);
            return controller.StatusCode(500, exception?.Message ?? errorMessage);
        }

        public static IActionResult LogAndThrowForbidden(this ControllerBase controller, ILogger logger, Exception? exception = null, string? errorMessage = "Forbidden", params object?[] args)
        {
            logger.LogError(exception, errorMessage, args);
            return controller.StatusCode(403, exception?.Message ?? errorMessage);
        }

        public static IActionResult LogAndThrowNotFound(this ControllerBase controller, ILogger logger, Exception? exception = null, string? errorMessage = "Not Found", params object?[] args)
        {
            logger.LogError(exception, errorMessage, args);
            return controller.NotFound(exception?.Message ?? errorMessage);
        }

        public static IActionResult LogAndThrowUnauthorized(this ControllerBase controller, ILogger logger, Exception? exception = null, string? errorMessage = "Unauthorized", params object?[] args)
        {
            logger.LogError(exception, errorMessage, args);
            return controller.StatusCode(401, exception?.Message ?? errorMessage);
        }

        public static IActionResult LogAndThrowConflict(this ControllerBase controller, ILogger logger, Exception? exception = null, string? errorMessage = "Conflict", params object?[] args)
        {
            logger.LogError(exception, errorMessage, args);
            return controller.StatusCode(409, exception?.Message ?? errorMessage);
        }
    }
}
