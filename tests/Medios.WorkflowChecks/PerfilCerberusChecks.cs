using System.Security.Claims;
using System.Net;
using Microsoft.Extensions.Configuration;
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

static class PerfilCerberusChecks
{
    public static async Task<int> RunAsync()
    {
        int count = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception("Perfil Cerberus: " + message); count++; }
        var factory = new MemoriaFactory();
        var service = new AutorizacionService(factory);
        var fecha = new DateTime(2026, 10, 1);
        var approved = new AutorizacionUsuario { Usuario = "aprobado", Nombre = "aprobado", Rol = "MEDIOS", Estado = "aprobada", Activo = true, DelegacionId = 9, Codigo = "123456", FechaSolicitud = fecha, FechaResolucion = fecha, AprobadoPor = "administrador" };
        var pending = new AutorizacionUsuario { Usuario = "pendiente", Nombre = "pendiente", Rol = "DELEGACION", Estado = "pendiente", Codigo = "654321", FechaSolicitud = fecha };
        using (var db = factory.Create()) { db.AutorizacionesUsuario.AddRange(approved, pending); await db.SaveChangesAsync(); }
        var perfil = new UserSession { Nombre = "Nombre", Apellido = "Apellido", Email = "correo@ejemplo.invalid", Dependencia = "Destino central", Jerarquia = "Oficial de Policía", Legajo = "1111111", Telefono = "11111", Rol = "ANALISTA" };

        async Task<(IActionResult Result, ClaimsPrincipal? Principal)> Login(string usuario, UserSession? central)
        {
            var auth = new RecordingAuthentication();
            using var services = new ServiceCollection().AddSingleton<IAuthenticationService>(auth).BuildServiceProvider();
            var ctx = new DefaultHttpContext { RequestServices = services };
            var controller = new LoginController(new AuthLocal(central), services, new AuditoriaService(factory, new HttpContextAccessor { HttpContext = ctx }), service) {
                ControllerContext = new ControllerContext { HttpContext = ctx, RouteData = new Microsoft.AspNetCore.Routing.RouteData(), ActionDescriptor = new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor() },
                TempData = new TempDataDictionary(ctx, new MemoryTempData())
            };
            controller.Url = new Microsoft.AspNetCore.Mvc.Routing.UrlHelper(controller.ControllerContext);
            return (await controller.Index(usuario, "clave-ficticia"), auth.Principal);
        }
        var alta = await Login("nuevo", perfil);
        Check(alta.Result is RedirectToActionResult { ActionName: "SolicitudRegistrada" } && alta.Principal == null, "un perfil central no habilita acceso sin aprobación");
        using (var db = factory.Create()) {
            var n = await db.AutorizacionesUsuario.SingleAsync(a => a.Usuario == "nuevo");
            Check(n.Nombre == "Nombre Apellido" && n.Legajo == "1111111" && n.Destino == "Destino central", "solicitud nueva con datos centrales");
        }
        await Login("pendiente", perfil);
        using (var db = factory.Create()) {
            var n = await db.AutorizacionesUsuario.FindAsync(pending.Id);
            Check(n!.Nombre == "Nombre Apellido" && n.Email == perfil.Email, "actualizar la solicitud pendiente existente");
            Check(n.Codigo == "654321" && n.FechaSolicitud == fecha && n.Estado == "pendiente", "conservar código, fecha y estado pendientes");
            Check(await db.AutorizacionesUsuario.CountAsync(a => a.Usuario == "pendiente") == 1, "no duplicar solicitudes pendientes");
        }
        var ingreso = await Login("aprobado", perfil);
        Check(ingreso.Principal?.Identity?.Name == "Nombre Apellido", "claims actualizados desde Cerberus");
        Check(ingreso.Principal!.IsInRole("MEDIOS") && ingreso.Principal.HasClaim("DelegacionId", "9"), "conservar rol local y delegación asignada");
        using (var db = factory.Create()) {
            var n = await db.AutorizacionesUsuario.FindAsync(approved.Id);
            Check(n!.Nombre == "Nombre Apellido" && n.Jerarquia == perfil.Jerarquia && n.Telefono == perfil.Telefono, "actualizar los datos del listado y planilla");
            Check(n.Rol == "MEDIOS" && n.DelegacionId == 9 && n.Estado == "aprobada" && n.Activo && n.Codigo == "123456" && n.FechaSolicitud == fecha && n.FechaResolucion == fecha && n.AprobadoPor == "administrador", "no modificar decisiones de autorización");
        }
        var sinPerfil = await Login("aprobado", null);
        Check(sinPerfil.Principal?.Identity?.Name == "Nombre Apellido", "perfil no disponible conserva acceso y nombre previos");
        await service.ActualizarPerfilAsync(approved.Id, "aprobado", new UserSession());
        using (var db = factory.Create()) {
            var n = await db.AutorizacionesUsuario.FindAsync(approved.Id);
            Check(n!.Email == perfil.Email && n.Nombre == "Nombre Apellido", "campos centrales vacíos no borran datos previos");
            n.Activo = false; await db.SaveChangesAsync();
        }
        var revocado = await Login("aprobado", perfil);
        Check(revocado.Principal == null && revocado.Result is ViewResult, "el perfil central no reactiva un usuario desactivado");
        await service.ActualizarPerfilAsync(approved.Id, "otro-usuario", new UserSession { Nombre = "No corresponde" });
        using (var db = factory.Create()) Check((await db.AutorizacionesUsuario.FindAsync(approved.Id))!.Nombre == "Nombre Apellido", "vincular la actualización con el usuario correcto");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Cerberus:BaseUrl"] = "https://cerberus.ejemplo.invalid", ["Cerberus:ServiceApiKey"] = "clave-ficticia"
        }).Build();
        var handler = new PerfilHandler();
        var client = new CerberusAuthService(new HttpClient(handler), config);
        var datos = await client.ObtenerPerfilAsync("token-ficticio");
        Check(datos is { Nombre: "Nombre", Apellido: "Apellido", Legajo: "1111111", Telefono: "11111", Jerarquia: "Oficial de Policía" }, "deserializar perfil real y valores numéricos");
        Check(handler.Autenticado, "consultar únicamente el perfil autenticado con la clave del servicio");
        handler.Status = HttpStatusCode.Forbidden;
        Check(await client.ObtenerPerfilAsync("token-ficticio") == null, "perfil restringido no impide el acceso local aprobado");
        handler.Status = HttpStatusCode.OK; handler.Body = "respuesta no JSON";
        Check(await client.ObtenerPerfilAsync("token-ficticio") == null, "respuesta inválida conserva el perfil local");
        handler.Fallo = true;
        Check(await client.ObtenerPerfilAsync("token-ficticio") == null, "fallo de red conserva el perfil local");
        return count;
    }
    sealed class PerfilHandler : HttpMessageHandler
    {
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Body = """{"nombre":"Nombre","apellido":"Apellido","legajo":1111111,"telefono":11111,"jerarquia":"Oficial de Policía"}""";
        public bool Fallo;
        public bool Autenticado;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Fallo) throw new HttpRequestException("Fallo ficticio");
            Autenticado = request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/cerberus/api/v1/users/info-servicio"
                && request.Headers.Authorization?.ToString() == "Bearer token-ficticio"
                && request.Headers.GetValues("Service-Api-Key").Single() == "clave-ficticia";
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body) });
        }
    }
    sealed class AuthLocal(UserSession? perfil) : IAuthService
    {
        public bool UsaAutorizacionLocal => true;
        public Task<LoginResponse?> LoginAsync(string usuario, string clave) => Task.FromResult<LoginResponse?>(new() { Usuario = usuario, Token = "token-ficticio" });
        public Task<UserSession?> ObtenerPerfilAsync(string token) => Task.FromResult(perfil);
        public Task<UserSession?> GetUserInfo(string token) => throw new Exception("No usar la ruta de autenticación delegada");
    }
    sealed class RecordingAuthentication : IAuthenticationService
    {
        public ClaimsPrincipal? Principal { get; private set; }
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) => Task.FromResult(AuthenticateResult.NoResult());
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) { Principal = principal; return Task.CompletedTask; }
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) { Principal = null; return Task.CompletedTask; }
    }
    sealed class MemoryTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
