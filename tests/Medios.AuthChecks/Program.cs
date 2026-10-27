using System.Net;
using System.Security.Claims;
using Medios.Entities;
using Medios.Security;
using Microsoft.Extensions.Configuration;

var comprobaciones = 0;
void Verificar(bool condicion, string caso)
{
    if (!condicion) throw new InvalidOperationException(caso);
    comprobaciones++;
}

ClaimsPrincipal Sesion(string rol, string? delegacion = null, string? ambito = null)
{
    var claims = new List<Claim>
    {
        new("Usuario", "usuario"), new("UsuarioReal", "usuario"),
        new(ClaimTypes.Role, rol), new("RolReal", rol), new("EsSimulador", "true")
    };
    if (delegacion != null) claims.Add(new("DelegacionIdReal", delegacion));
    if (ambito != null) claims.Add(new("AmbitoIdReal", ambito));
    return new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies"));
}

Verificar(!AutorizacionLocalPolicy.PermiteAcceso(null), "Usuario nuevo necesita aprobación local");
foreach (var rol in new[] { "MEDIOS", "DELEGACION", "DESARROLLADOR" })
{
    var autorizacion = new AutorizacionUsuario
    {
        Usuario = "usuario", Estado = "aprobada", Activo = true, Rol = rol
    };
    var sesion = Sesion(rol);
    Verificar(AutorizacionLocalPolicy.SesionVigente(sesion, autorizacion), $"Rol local {rol} aprobado");
    autorizacion.Activo = false;
    Verificar(!AutorizacionLocalPolicy.SesionVigente(sesion, autorizacion), $"Revocar sesión {rol}, incluso simuladores");
    autorizacion.Activo = true;
    foreach (var estado in new[] { "pendiente", "rechazada", "reemplazada" })
    {
        autorizacion.Estado = estado;
        Verificar(!AutorizacionLocalPolicy.SesionVigente(sesion, autorizacion), $"No acceder con estado {estado}, rol {rol}");
    }
    autorizacion.Estado = "aprobada";
    autorizacion.Rol = rol == "MEDIOS" ? "DELEGACION" : "MEDIOS";
    Verificar(!AutorizacionLocalPolicy.SesionVigente(sesion, autorizacion), $"No conservar rol anterior {rol}");
}

var delegacion = new AutorizacionUsuario
{
    Usuario = "usuario", Estado = "aprobada", Activo = true,
    Rol = "DELEGACION", DelegacionId = 3, AmbitoId = 8
};
Verificar(AutorizacionLocalPolicy.SesionVigente(Sesion("DELEGACION", "3", "8"), delegacion), "Delegación asignada coincide");
Verificar(!AutorizacionLocalPolicy.SesionVigente(Sesion("DELEGACION", "4", "8"), delegacion), "No conservar delegación anterior");
Verificar(!AutorizacionLocalPolicy.SesionVigente(Sesion("DELEGACION", "3", "9"), delegacion), "No conservar ámbito anterior");
delegacion.Rol = "ADMINISTRADOR";
Verificar(!AutorizacionLocalPolicy.PermiteAcceso(delegacion), "No importar rol de Cerberus como rol local");
delegacion.Rol = null;
Verificar(!AutorizacionLocalPolicy.PermiteAcceso(delegacion), "No acceder sin rol local");

var configuracion = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Cerberus:BaseUrl"] = "http://cerberus.test"
}).Build();
foreach (var caso in new[]
{
    (HttpStatusCode.Unauthorized, "{\"detail\":\"Usuario no encontrado\"}", false),
    (HttpStatusCode.Forbidden, "{\"detail\":\"Contraseña inválida\"}", false),
    (HttpStatusCode.Forbidden, "{\"detail\":\"Usuario inactivo\"}", false),
    (HttpStatusCode.OK, "{\"access_token\":\"token-de-prueba\"}", true),
    (HttpStatusCode.OK, "{}", false),
    (HttpStatusCode.OK, "{\"access_token\":\" \"}", false)
})
{
    using var manejador = new RespuestaToken(caso.Item1, caso.Item2);
    using var cliente = new HttpClient(manejador);
    IAuthService auth = new CerberusAuthService(cliente, configuracion);
    Verificar(auth.UsaAutorizacionLocal, "Cerberus delega la autorización a Medios");
    var resultado = await auth.LoginAsync("usuario", "contraseña-de-prueba");
    Verificar((resultado != null) == caso.Item3, $"Validación del token HTTP {(int)caso.Item1}");
    Verificar(manejador.Solicitudes == 1, "Validar credenciales no consulta info-servicio");
}
var supervisor = new AutorizacionUsuario { Usuario = "usuario", Rol = "MEDIOS", Estado = "aprobada", Activo = true };
Verificar(AutorizacionLocalPolicy.ResolverRol(supervisor, " supervisor ") == "DESARROLLADOR", "Supervisor de Cerberus equivale a desarrollador");
Verificar(AutorizacionLocalPolicy.ResolverRol(supervisor, "ANALISTA") == "MEDIOS", "Otros perfiles conservan el rol local");
Verificar(AutorizacionLocalPolicy.ResolverRol(supervisor, null) == "MEDIOS", "Perfil no disponible no interrumpe autorización vigente");
var cookieSupervisor = Sesion("DESARROLLADOR");
((ClaimsIdentity)cookieSupervisor.Identity!).AddClaims(new[] { new Claim("RolCerberus", "SUPERVISOR"), new Claim("RolLocalAlIngresar", "MEDIOS") });
Verificar(AutorizacionLocalPolicy.SesionVigente(cookieSupervisor, supervisor), "Nueva cookie de supervisor es válida con autorización local");
supervisor.Rol = "DELEGACION";
Verificar(!AutorizacionLocalPolicy.SesionVigente(cookieSupervisor, supervisor), "Un cambio local revoca también al supervisor");
supervisor.Rol = "MEDIOS";
supervisor.Activo = false;
Verificar(AutorizacionLocalPolicy.ResolverRol(supervisor, "SUPERVISOR") == null && !AutorizacionLocalPolicy.SesionVigente(cookieSupervisor, supervisor), "Supervisor desactivado no accede");
supervisor.Activo = true;
supervisor.Estado = "pendiente";
Verificar(AutorizacionLocalPolicy.ResolverRol(supervisor, "SUPERVISOR") == null, "Supervisor necesita aprobación local");
Verificar(AutorizacionLocalPolicy.ResolverRol(null, "SUPERVISOR") == null, "El perfil central solo no habilita acceso");
foreach (var caso in new[] { (HttpStatusCode.OK, "{\"rol\":\"Supervisor\"}", "SUPERVISOR"), (HttpStatusCode.Forbidden, "{}", (string?)null), (HttpStatusCode.OK, "no-json", (string?)null) })
{
    using var manejador = new RespuestaPerfil(caso.Item1, caso.Item2);
    using var cliente = new HttpClient(manejador);
    var configPerfil = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Cerberus:BaseUrl"] = "http://cerberus.test", ["Cerberus:ServiceApiKey"] = "clave-de-prueba" }).Build();
    var auth = new CerberusAuthService(cliente, configPerfil);
    Verificar(await auth.ObtenerRolCerberusAsync("token-de-prueba") == caso.Item3, "Rol obtenido del servidor y fallo sin elevación");
    Verificar(manejador.Solicitudes == 1, "Una consulta acotada del perfil");
}
Console.WriteLine($"OK: {comprobaciones} comprobaciones de autenticación y autorización local.");

sealed class RespuestaToken(HttpStatusCode estado, string cuerpo) : HttpMessageHandler
{
    public int Solicitudes { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage solicitud, CancellationToken cancelacion)
    {
        if (solicitud.Method != HttpMethod.Post || solicitud.RequestUri?.AbsolutePath != "/cerberus/api/v1/auth/token")
            throw new InvalidOperationException("Endpoint inesperado durante validación de credenciales");
        Solicitudes++;
        return Task.FromResult(new HttpResponseMessage(estado) { Content = new StringContent(cuerpo) });
    }
}

sealed class RespuestaPerfil(HttpStatusCode estado, string cuerpo) : HttpMessageHandler
{
    public int Solicitudes { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage solicitud, CancellationToken ct)
    {
        if (solicitud.Method != HttpMethod.Get || solicitud.RequestUri?.AbsolutePath != "/cerberus/api/v1/users/info-servicio"
            || solicitud.Headers.Authorization?.Parameter != "token-de-prueba" || !solicitud.Headers.Contains("Service-Api-Key"))
            throw new InvalidOperationException("Consulta de perfil inesperada");
        Solicitudes++;
        return Task.FromResult(new HttpResponseMessage(estado) { Content = new StringContent(cuerpo) });
    }
}
