using System.Net;
using System.Text.Json;

namespace Medios.Infrastructure.Middleware
{
    public class ErrorMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ErrorMiddleware> _logger;

        public ErrorMiddleware(RequestDelegate next, ILogger<ErrorMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                await HandleException(context, ex);
            }
        }

        private async Task HandleException(HttpContext context, Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");

            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "application/json";

            var inner = ex.InnerException?.InnerException?.Message
                     ?? ex.InnerException?.Message
                     ?? "";
            var result = JsonSerializer.Serialize(new
            {
                success = false,
                message = "Error inesperado en el servidor",
                detail = ex.Message,
                inner
            });

            await context.Response.WriteAsync(result);
        }
    }
}
