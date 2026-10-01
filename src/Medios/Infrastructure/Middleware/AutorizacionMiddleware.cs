using System.Security.Claims;
using Medios.Services;

namespace Medios.Infrastructure.Middleware
{
    public class AutorizacionMiddleware
    {
        private readonly RequestDelegate _next;
        private static readonly string[] RolesConAutorizacion =
            { "MEDIOS", "DELEGACION" };

        public AutorizacionMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, AutorizacionService autorizacionService)
        {
            if (!context.User.Identity?.IsAuthenticated == true)
            {
                await _next(context);
                return;
            }

            var pathLower = (context.Request.Path.Value ?? "").ToLower();

            if (pathLower.StartsWith("/api") ||
                pathLower.StartsWith("/login") ||
                pathLower.StartsWith("/autorizacion") ||
                pathLower.Contains(".") ||
                pathLower.StartsWith("/home/noautorizado"))
            {
                await _next(context);
                return;
            }

            // ADMINISTRADOR/DESARROLLADOR simulando un rol (menú MOCK): no se les exige
            // autorización de uso aunque el rol simulado normalmente la requiera.
            if (context.User.HasClaim("EsSimulador", "true"))
            {
                await _next(context);
                return;
            }

            var rol = context.User.FindFirst(ClaimTypes.Role)?.Value?.ToUpper() ?? "";
            if (!RolesConAutorizacion.Contains(rol))
            {
                await _next(context);
                return;
            }

            // "Usuario" claim = login username; Identity.Name = nombre completo
            var usuario = context.User.FindFirst("Usuario")?.Value
                       ?? context.User.Identity?.Name ?? "";
            var estado = await autorizacionService.GetEstado(usuario);

            if (estado == "aprobada")
            {
                await _next(context);
                return;
            }

            var destino = estado == "pendiente"
                ? "/Autorizacion/Pendiente"
                : "/Autorizacion/Solicitud";

            context.Response.Redirect(destino);
        }
    }
}
