using Medios.Services.Respaldos;

namespace Medios.Infrastructure.Middleware;

public sealed class RetornoProduccionUso
{
    private readonly object sync = new();
    private int activas;
    private bool importando;
    public SemaphoreSlim Operacion { get; } = new(1, 1);
    public bool Entrar()
    {
        lock (sync)
        {
            if (importando) return false;
            activas++;
            return true;
        }
    }
    public void Salir() { lock (sync) activas--; }
    public void Bloquear() { lock (sync) importando = true; }
    public void Liberar() { lock (sync) importando = false; }
    public bool HayActivas { get { lock (sync) return activas > 0; } }
}

public sealed class ContencionProduccionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, ContencionProduccionService remoto, RetornoProduccionUso uso)
    {
        if (!remoto.Habilitado) { await next(http); return; }
        var path = http.Request.Path;
        if (new[] { "/css", "/js", "/adminlte", "/img", "/lib", "/favicon.ico" }
            .Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
        { await next(http); return; }

        var login = path == "/" || path.StartsWithSegments("/Login", StringComparison.OrdinalIgnoreCase);
        var developer = http.User.HasClaim("RolReal", "DESARROLLADOR") && http.User.IsInRole("DESARROLLADOR");
        if (!login && !developer && http.User.Identity?.IsAuthenticated == true)
        {
            var estado = await remoto.ConsultarAsync(http.RequestAborted);
            if (estado.Activa)
            {
                if (HttpMethods.IsGet(http.Request.Method)) http.Response.Redirect("/Login");
                else
                {
                    http.Response.StatusCode = StatusCodes.Status423Locked;
                    await http.Response.WriteAsJsonAsync(new { message = estado.MensajeAcceso });
                }
                return;
            }
        }

        if (path.StartsWithSegments("/Monitor/Retorno", StringComparison.OrdinalIgnoreCase))
        { await next(http); return; }
        if (!uso.Entrar())
        {
            http.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            http.Response.Headers.RetryAfter = "10";
            await http.Response.WriteAsync("Se está preparando el retorno operativo. Volvé a intentar en unos instantes.");
            return;
        }
        try { await next(http); }
        finally { uso.Salir(); }
    }
}
