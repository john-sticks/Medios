namespace Medios.Infrastructure.Middleware;

// Los PDF se sirven por los controladores, que verifican permisos y delegación.
// Impide eludirlos adivinando el nombre de un archivo en wwwroot/pdf.
public sealed class PdfPrivadosMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/pdf", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        await next(context);
    }
}
