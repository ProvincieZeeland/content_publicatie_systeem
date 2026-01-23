using Microsoft.AspNetCore.Mvc;

namespace CPS_API.Helpers
{
    public static class LoggerHelper
    {
        public static IActionResult LogAndThrowInternalServerError(this ControllerBase controller, ILogger logger, Exception? exception = null, string? errorMessage = "Internal Server Error", Dictionary<string, string>? properties = null)
        {
            LogError(logger, exception, errorMessage ?? "Internal Server Error", properties);
            return controller.StatusCode(500, exception?.Message ?? errorMessage);
        }

        public static IActionResult LogAndThrowForbidden(this ControllerBase controller, ILogger logger, Exception? exception = null, string? errorMessage = "Forbidden", Dictionary<string, string>? properties = null)
        {
            LogError(logger, exception, errorMessage ?? "Forbidden", properties);
            return controller.StatusCode(403, exception?.Message ?? errorMessage);
        }

        public static IActionResult LogAndThrowNotFound(this ControllerBase controller, ILogger logger, Exception? exception = null, string? errorMessage = "Not Found", Dictionary<string, string>? properties = null)
        {
            LogError(logger, exception, errorMessage ?? "Not Found", properties);
            return controller.NotFound(exception?.Message ?? errorMessage);
        }

        public static IActionResult LogAndThrowUnauthorized(this ControllerBase controller, ILogger logger, Exception? exception = null, string? errorMessage = "Unauthorized", Dictionary<string, string>? properties = null)
        {
            LogError(logger, exception, errorMessage ?? "Unauthorized", properties);
            return controller.StatusCode(401, exception?.Message ?? errorMessage);
        }

        public static IActionResult LogAndThrowConflict(this ControllerBase controller, ILogger logger, Exception? exception = null, string? errorMessage = "Conflict", Dictionary<string, string>? properties = null)
        {
            LogError(logger, exception, errorMessage ?? "Conflict", properties);
            return controller.StatusCode(409, exception?.Message ?? errorMessage);
        }

        private static void LogError(this ILogger logger, Exception? exception, string errorMessage, Dictionary<string, string>? properties = null)
        {
            if (properties != null && properties.Count > 0)
            {
                using (logger.BeginScope(properties))
                {
                    logger.LogError(exception, "{ErrorMessage}", errorMessage);
                }
            }
            else
            {
                logger.LogError(exception, "{ErrorMessage}", errorMessage);
            }
        }
    }
}
