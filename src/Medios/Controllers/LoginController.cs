using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Medios.Security;
using Medios.Services;
using System.Security.Claims;

namespace Medios.Controllers
{
    [AllowAnonymous]
    public class LoginController : Controller
    {
        private readonly IAuthService _auth;
        private readonly IServiceProvider _serviceProvider;
        private readonly AuditoriaService _auditoria;
        private readonly AutorizacionService _autorizacionService;

        public LoginController(IAuthService auth, IServiceProvider serviceProvider,
            AuditoriaService auditoria, AutorizacionService autorizacionService)
        {
            _auth = auth;
            _serviceProvider = serviceProvider;
            _auditoria = auditoria;
            _autorizacionService = autorizacionService;
        }

        private string GetIp() =>
            Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString()
            ?? "";

        private static string NormalizeCerberusRole(string? role)
        {
            return role?.ToUpper().Trim() switch
            {
                "ADMINISTRADOR" or "DESARROLLADOR" => "DESARROLLADOR",
                "SUPERVISOR" or "ANALISTA" or "MEDIOS" => "MEDIOS",
                "OPERADOR" or "CONSULTOR" or "ESTRATEGICO" or "DELEGACION" => "DELEGACION",
                _ => "DELEGACION"
            };
        }

        [HttpGet]
        public IActionResult Index(string? msg = null)
        {
            if (msg == "expired") ViewBag.Error = "Sesión expirada";
            else if (msg == "desactivada") ViewBag.Error = "Tu acceso fue desactivado. Contactá al administrador.";
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Index(string Usuario, string Password)
        {
            if (string.IsNullOrWhiteSpace(Usuario) || string.IsNullOrWhiteSpace(Password))
            {
                ViewBag.Error = "Completar usuario y contraseña";
                return View();
            }

            var loginResult = await _auth.LoginAsync(Usuario, Password);

            if (loginResult == null || string.IsNullOrEmpty(loginResult.Token))
            {
                await _auditoria.RegistrarAsync(Usuario, GetIp(), "auth", "POST", "login_fallido",
                    new { usuario_intentado = Usuario });
                ViewBag.Error = "Usuario o contraseña incorrectos";
                return View();
            }

            var user = await _auth.GetUserInfo(loginResult.Token);

            if (user == null)
            {
                ViewBag.Error = "No se pudo obtener la información del usuario. Contacte al administrador.";
                return View();
            }

            var rolCerberus = user.Rol.ToUpper().Trim();
            var rolAuth = NormalizeCerberusRole(rolCerberus);
            var rolesConAutorizacion = new[] { "MEDIOS", "DELEGACION" };

            // Autorización aprobada del usuario (rol efectivo, delegación y ámbito asignados por el admin)
            var aprobada = rolesConAutorizacion.Contains(rolAuth)
                ? await _autorizacionService.GetAprobadaAsync(Usuario)
                : null;
            var rolEfectivo = !string.IsNullOrWhiteSpace(aprobada?.Rol)
                ? aprobada!.Rol!.ToUpper().Trim()
                : rolAuth;
            var delegacionId = aprobada?.DelegacionId;
            var ambitoId = aprobada?.AmbitoId;

            var esDesarrollador = rolEfectivo == "DESARROLLADOR";

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, $"{user.Nombre} {user.Apellido}".Trim()),
                new Claim(ClaimTypes.Role, rolEfectivo),
                new Claim("Dependencia", user.Dependencia),
                new Claim("Token", loginResult.Token),
                new Claim("NombreSolo", user.Nombre),
                new Claim("Apellido", user.Apellido),
                new Claim("Email", user.Email),
                new Claim("Jerarquia", user.Jerarquia),
                new Claim("Legajo", user.Legajo),
                new Claim("Telefono", user.Telefono),
                new Claim("Usuario", Usuario),
                new Claim("RolCerberus", rolCerberus),
                // Identidad real: se preserva sin tocar a través de cualquier simulación MOCK
                // (ver MockSwitchController), para poder restaurarla al salir y para trazabilidad.
                new Claim("UsuarioReal", Usuario),
                new Claim("RolReal", rolEfectivo),
                new Claim("NombreCompletoReal", $"{user.Nombre} {user.Apellido}".Trim()),
                new Claim("NombreSoloReal", user.Nombre),
                new Claim("ApellidoReal", user.Apellido),
                new Claim("DependenciaReal", user.Dependencia),
                new Claim("EmailReal", user.Email),
                new Claim("JerarquiaReal", user.Jerarquia),
                new Claim("LegajoReal", user.Legajo),
                new Claim("TelefonoReal", user.Telefono),
                new Claim("TokenReal", loginResult.Token)
            };
            if (delegacionId.HasValue)
            {
                claims.Add(new Claim("DelegacionId", delegacionId.Value.ToString()));
                claims.Add(new Claim("DelegacionIdReal", delegacionId.Value.ToString()));
                var delegNombre = await _autorizacionService.GetDelegacionDisplayNombreAsync(delegacionId.Value);
                if (!string.IsNullOrWhiteSpace(delegNombre))
                {
                    claims.Add(new Claim("DelegacionNombre", delegNombre));
                    claims.Add(new Claim("DelegacionNombreReal", delegNombre));
                }
            }
            if (ambitoId.HasValue)
            {
                claims.Add(new Claim("AmbitoId", ambitoId.Value.ToString()));
                claims.Add(new Claim("AmbitoIdReal", ambitoId.Value.ToString()));
            }

            // El DESARROLLADOR opera también como MEDIOS (segundo role-claim, así pasa todos los
            // chequeos IsInRole("MEDIOS")) y conserva un marcador para ver el menú MOCK.
            // El primer role-claim sigue siendo DESARROLLADOR → display y menú lo reflejan.
            if (esDesarrollador)
            {
                claims.Add(new Claim(ClaimTypes.Role, "MEDIOS"));
                claims.Add(new Claim("EsDesarrollador", "true"));
            }

            // MEDIOS y DESARROLLADOR pueden simular roles (menú MOCK).
            // EsSimulador habilita el switcher; EsDesarrollador (solo desarrollador) además
            // permite simular MEDIOS y DELEGACION (ver MockSwitchController.Cambiar).
            if (esDesarrollador || rolEfectivo == "MEDIOS")
                claims.Add(new Claim("EsSimulador", "true"));

            var identity = new ClaimsIdentity(claims, "Cookies");
            var principal = new ClaimsPrincipal(identity);
            await HttpContext.SignInAsync("Cookies", principal);

            await _auditoria.RegistrarAsync($"{user.Nombre} {user.Apellido}".Trim(), GetIp(), "auth", "POST", "login");

            // Verificar autorización para roles que la requieren (según el rol del proveedor de auth)
            if (rolesConAutorizacion.Contains(rolAuth))
            {
                var estado = await _autorizacionService.GetEstado(Usuario);

                // Acceso OK solo si está aprobado Y activo. Si fue desactivado, debe revalidar.
                var accesoOk = estado == "aprobada" && aprobada != null && aprobada.Activo;
                var esRevalidacion = estado == "aprobada" && aprobada != null && !aprobada.Activo;

                if (!accesoOk)
                {
                    // El usuario NO entra: se registra/actualiza la petición (alta o revalidación) y se cierra sesión.
                    string codigo;
                    int solId;
                    bool yaExistia = estado == "pendiente";

                    if (yaExistia)
                    {
                        var pend = await _autorizacionService.GetPendiente(Usuario);
                        codigo = pend?.Codigo ?? "";
                        solId = pend?.Id ?? 0;
                    }
                    else
                    {
                        // Sin solicitud / rechazada / desactivada → generar nueva planilla automáticamente
                        var nombreCompleto = $"{user.Nombre} {user.Apellido}".Trim();
                        var (sid, cod) = await _autorizacionService.CrearSolicitud(
                            Usuario, nombreCompleto, user.Email, user.Dependencia,
                            user.Jerarquia, user.Legajo, user.Telefono, rolAuth);
                        codigo = cod;
                        solId = sid;
                        await _auditoria.RegistrarAsync(nombreCompleto, GetIp(), "autorizaciones", "POST",
                            esRevalidacion ? "revalidacion_auto" : "solicitud_auto", new { sid, Usuario });
                    }

                    await HttpContext.SignOutAsync("Cookies");
                    TempData["SolCodigo"] = codigo;
                    TempData["SolNombre"] = $"{user.Nombre} {user.Apellido}".Trim();
                    TempData["SolYaExistia"] = yaExistia;
                    TempData["SolId"] = solId;
                    TempData["SolRevalida"] = esRevalidacion;
                    return RedirectToAction(nameof(SolicitudRegistrada));
                }
            }

            // DELEGACION (rol efectivo) aprobada pero sin delegación asignada → no puede operar
            if (rolEfectivo == "DELEGACION" && !delegacionId.HasValue)
            {
                await HttpContext.SignOutAsync("Cookies");
                return RedirectToAction("SinAmbito", "Home");
            }

            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public IActionResult SolicitudRegistrada()
        {
            // Pantalla pública: el usuario NO está logueado
            if (TempData["SolCodigo"] == null) return RedirectToAction(nameof(Index));
            ViewBag.Codigo = TempData["SolCodigo"];
            ViewBag.Nombre = TempData["SolNombre"];
            ViewBag.YaExistia = (bool)(TempData["SolYaExistia"] ?? false);
            ViewBag.SolicitudId = (int)(TempData["SolId"] ?? 0);
            ViewBag.Revalida = (bool)(TempData["SolRevalida"] ?? false);
            return View();
        }

        public async Task<IActionResult> Logout()
        {
            await _auditoria.RegistrarAsync(User.Identity?.Name ?? "", GetIp(), "auth", "GET", "logout");
            await HttpContext.SignOutAsync("Cookies");
            return RedirectToAction("Index");
        }
    }
}
