using System.Security.Claims;
using Medios.Controllers;
using Medios.Entities;
using Medios.Security;
using Medios.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

static class MockLoginChecks
{
    public static async Task<int> RunAsync()
    {
        var factory = new MemoriaFactory();
        using (var db = factory.Create())
        {
            db.Delegaciones.Add(new Delegacion { Id = 1, Nombre = "Delegación de prueba" });
            db.AutorizacionesUsuario.AddRange(
                new AutorizacionUsuario { Usuario = "medios", Rol = "MEDIOS", Estado = "aprobada", Activo = true },
                new AutorizacionUsuario { Usuario = "delegacion", Rol = "DELEGACION", Estado = "aprobada", Activo = true, DelegacionId = 1 });
            await db.SaveChangesAsync();
        }
        var checks = 0;
        void Check(bool value, string name)
        {
            if (!value) throw new InvalidOperationException(name);
            checks++;
        }
        async Task<(IActionResult Result, ClaimsPrincipal? Principal)> Login(string user, string password, IAuthService? proveedor = null)
        {
            var authentication = new RecordingAuthentication();
            using var services = new ServiceCollection()
                .AddSingleton<IAuthenticationService>(authentication).BuildServiceProvider();
            var context = new DefaultHttpContext { RequestServices = services };
            var accessor = new HttpContextAccessor { HttpContext = context };
            var controller = new LoginController(proveedor ?? new MockAuthService(), services,
                new AuditoriaService(factory, accessor), new AutorizacionService(factory))
            {
                ControllerContext = new ControllerContext { HttpContext = context, RouteData = new Microsoft.AspNetCore.Routing.RouteData(), ActionDescriptor = new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor() },
                TempData = new TempDataDictionary(context, new MemoryTempData())
            };
            controller.Url = new Microsoft.AspNetCore.Mvc.Routing.UrlHelper(controller.ControllerContext);
            return (await controller.Index(user, password), authentication.Principal);
        }
        var developer = await Login("gale", "gale1234!");
        Check(developer.Result is RedirectToActionResult { ControllerName: "Home", ActionName: "Index" }, "Mock: desarrollador entra sin Cerberus");
        Check(developer.Principal?.IsInRole("DESARROLLADOR") == true && developer.Principal.IsInRole("MEDIOS"), "Mock: conservar ambos roles de desarrollador");
        Check(developer.Principal?.HasClaim("EsSimulador", "true") == true, "Mock: conservar simulador");
        var medios = await Login("medios", "1234");
        Check(medios.Principal?.IsInRole("MEDIOS") == true, "Mock: MEDIOS aprobado entra");
        foreach (var variante in new[] { "MEDIOS", "MeDiOs", " medios " })
        {
            var otraSesion = await Login(variante, "1234");
            Check(otraSesion.Principal?.Identity?.Name == medios.Principal?.Identity?.Name,
                "Mock: la misma cuenta tiene el mismo nombre en cada dispositivo: " + variante);
            Check(otraSesion.Principal?.HasClaim("Usuario", "medios") == true
                && otraSesion.Principal.IsInRole("MEDIOS"), "Mock: usuario canónico y autorización aprobada: " + variante);
        }
        var legacy = await new MockAuthService().GetUserInfo("mock-token-MEDIOS");
        Check(legacy?.Nombre == "Medios" && legacy.Apellido == "Division", "Mock: token anterior en mayúsculas resuelve la misma identidad");
        var delegation = await Login("delegacion", "1234");
        Check(delegation.Principal?.IsInRole("DELEGACION") == true && delegation.Principal.HasClaim("DelegacionId", "1"), "Mock: conservar delegación asignada desde DB");
        var pending = await Login("usuarionuevo", "1234");
        Check(pending.Result is RedirectToActionResult { ActionName: "SolicitudRegistrada" } && pending.Principal == null, "Mock: usuario nuevo queda pendiente sin sesión");
        using (var db = factory.Create())
            Check(await db.AutorizacionesUsuario.AnyAsync(a => a.Usuario == "usuarionuevo" && a.Estado == "pendiente"), "Mock: registrar solicitud en DB local");
        var invalid = await Login("medios", "incorrecta");
        Check(invalid.Result is ViewResult && invalid.Principal == null, "Mock: rechazar contraseña incorrecta");
        using (var db = factory.Create())
        {
            db.AutorizacionesUsuario.Add(new AutorizacionUsuario { Usuario = "supervisor", Rol = "MEDIOS", Estado = "aprobada", Activo = true });
            await db.SaveChangesAsync();
        }
        var proveedor = new SupervisorCerberus();
        var supervisor = await Login("supervisor", "prueba", proveedor);
        Check(supervisor.Principal?.IsInRole("DESARROLLADOR") == true && supervisor.Principal.IsInRole("MEDIOS"), "Cerberus: Supervisor aprobado recibe desarrollador y medios");
        Check(supervisor.Principal?.HasClaim("RolCerberus", "SUPERVISOR") == true && supervisor.Principal.HasClaim("RolLocalAlIngresar", "MEDIOS"), "Cerberus: conserva perfil central y asignación local para revalidación");
        using (var db = factory.Create())
        {
            var local = (await db.AutorizacionesUsuario.SingleAsync(a => a.Usuario == "supervisor"));
            Check(AutorizacionLocalPolicy.SesionVigente(supervisor.Principal!, local), "Cerberus: cookie recién emitida sigue vigente");
            local.Activo = false; await db.SaveChangesAsync();
        }
        var desactivado = await Login("supervisor", "prueba", proveedor);
        Check(desactivado.Principal == null, "Cerberus: perfil central no evita desactivación local");
        Check(proveedor.ConsultasPerfil == 1, "Cerberus: no consulta perfil ni concede permisos a usuarios desactivados");
        var pendiente = await Login("nuevo-supervisor", "prueba", proveedor);
        Check(pendiente.Principal == null && pendiente.Result is RedirectToActionResult { ActionName: "SolicitudRegistrada" }, "Cerberus: un nuevo Supervisor requiere aprobación local");
        return checks;
    }

    private sealed class SupervisorCerberus : IAuthService
    {
        public bool UsaAutorizacionLocal => true;
        public int ConsultasPerfil { get; private set; }
        public Task<LoginResponse?> LoginAsync(string usuario, string password) => Task.FromResult<LoginResponse?>(new LoginResponse { Usuario = usuario, Token = "token-de-prueba" });
        public Task<string?> ObtenerRolCerberusAsync(string token) { ConsultasPerfil++; return Task.FromResult<string?>("SUPERVISOR"); }
        public Task<UserSession?> GetUserInfo(string token) => throw new InvalidOperationException("La identidad local no depende de info-servicio");
    }

    private sealed class RecordingAuthentication : IAuthenticationService
    {
        public ClaimsPrincipal? Principal { get; private set; }
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) => Task.FromResult(AuthenticateResult.NoResult());
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties)
        {
            Principal = principal;
            return Task.CompletedTask;
        }
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            Principal = null;
            return Task.CompletedTask;
        }
    }
    private sealed class MemoryTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}