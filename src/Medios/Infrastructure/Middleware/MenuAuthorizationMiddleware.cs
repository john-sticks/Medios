using Medios.Security;

namespace Medios.Infrastructure.Middleware
{
    public class MenuAuthorizationMiddleware
    {
        private readonly RequestDelegate _next;

        public MenuAuthorizationMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, MenuXmlService menuService)
        {
            var user = context.User;

            if (context.Request.Path.StartsWithSegments("/api"))
            {
                await _next(context);
                return;
            }

            // Los POST son acciones de formulario — su autorización la maneja [HasPermission].
            // El middleware de menú solo aplica a navegación GET.
            if (context.Request.Method == "POST")
            {
                await _next(context);
                return;
            }

            if (user?.Identity == null || !user.Identity.IsAuthenticated)
            {
                await _next(context);
                return;
            }

            var path = context.Request.Path.Value?.ToLower().TrimEnd('/');

            if (IsPublicPath(path))
            {
                await _next(context);
                return;
            }

            var allowed = menuService.GetAllowedUrls(user)
                .Select(x => x.ToLower().TrimEnd('/'))
                .ToList();

            if (!allowed.Any(a => path!.StartsWith(a)))
            {
                context.Response.Redirect("/Home/NoAutorizado");
                return;
            }

            await _next(context);
        }

        private bool IsPublicPath(string? path)
        {
            if (string.IsNullOrEmpty(path)) return true;

            return path.StartsWith("/login")
                || path.StartsWith("/css")
                || path.StartsWith("/js")
                || path.StartsWith("/lib")
                || path.StartsWith("/adminlte")
                || path.StartsWith("/images")
                || path.StartsWith("/favicon")
                || path.StartsWith("/home/noautorizado")
                || path.StartsWith("/home/sinambito");
        }
    }
}
