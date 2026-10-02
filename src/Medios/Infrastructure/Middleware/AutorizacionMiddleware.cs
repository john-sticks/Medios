using System.Security.Claims;
using Medios.Services;
using Medios.Security;
using Microsoft.AspNetCore.Authentication;

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

        public async Task InvokeAsync(HttpContext context, AutorizacionService autorizacionService,
            IAuthService authService)
        {
            if (context.User.Identity?.IsAuthenticated != true)
            {
                await _next(context);
                return;
            }

            var pathLower = (context.Request.Path.Value ?? "").ToLower();

            if (authService.UsaAutorizacionLocal && !context.Request.Path.StartsWithSegments("/login",
                StringComparison.OrdinalIgnoreCase))
            {
                // Validar también APIs y administradores. Una cookie anterior no conserva
                // permisos revocados ni puede saltar la comprobación por ser simulador.
                var usuarioReal = context.User.FindFirst("UsuarioReal")?.Value
                    ?? context.User.FindFirst("Usuario")?.Value ?? "";
                var autorizacion = await autorizacionService.GetUltimaAsync(usuarioReal);
                if (AutorizacionLocalPolicy.SesionVigente(context.User, autorizacion))
                {
                    await _next(context);
                    return;
                }

                await context.SignOutAsync("Cookies");
                if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        success = false,
                        message = "Tu acceso o tus permisos de Medios cambiaron. Ingresá nuevamente."
                    });
                }
                else
                {
                    var motivo = autorizacion is { Estado: "aprobada", Activo: false }
                        ? "desactivada" : "permisos_actualizados";
                    context.Response.Redirect($"/Login?msg={motivo}");
                }
                return;
            }

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
