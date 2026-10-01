using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Medios.Services;
using System.Security.Claims;

namespace Medios.Controllers
{
    // Solo activo en modo Mock — permite simular rol/Delegación para testing.
    // MOCK no es un rol: la identidad real (RolReal/UsuarioReal, y el resto de los claims
    // "*Real") se preserva sin tocar a través de cualquier cambio de simulación, y "Salir"
    // la restaura por completo. Ver matriz de simulación permitida en Cambiar().
    [Authorize]
    public class MockSwitchController : Controller
    {
        private readonly IConfiguration _config;
        private readonly AutorizacionService _autorizacion;

        // Claims de identidad real que deben sobrevivir sin cambios a cualquier simulación
        // (seteados una única vez en el login real, ver LoginController).
        private static readonly string[] RealClaimTypes =
        {
            "UsuarioReal", "RolReal", "NombreCompletoReal", "NombreSoloReal", "ApellidoReal",
            "DependenciaReal", "EmailReal", "JerarquiaReal", "LegajoReal", "TelefonoReal", "TokenReal",
            "DelegacionIdReal", "DelegacionNombreReal", "AmbitoIdReal"
        };

        public MockSwitchController(IConfiguration config, AutorizacionService autorizacion)
        {
            _config = config;
            _autorizacion = autorizacion;
        }

        [HttpPost]
        public async Task<IActionResult> Cambiar(string rol, string destino, int? delegacionId)
        {
            if (_config["Auth:Mode"] != "Mock") return Forbid();
            if (!User.HasClaim("EsSimulador", "true")) return Forbid();

            var esDev = User.HasClaim("EsDesarrollador", "true");
            var rolReal = (User.FindFirst("RolReal")?.Value ?? "").ToUpper();
            var rolUpper = (rol ?? "").ToUpper();

            // Matriz de simulación: DESARROLLADOR → {MEDIOS, DELEGACION}; MEDIOS → {DELEGACION}.
            // Volver al propio rol real (usado por Salir) siempre está permitido: no es una
            // simulación nueva, así que no pasa por esta lista.
            var simulables = rolReal switch
            {
                "DESARROLLADOR" => new[] { "MEDIOS", "DELEGACION" },
                "MEDIOS" => new[] { "DELEGACION" },
                _ => Array.Empty<string>()
            };
            if (rolUpper != rolReal && !simulables.Contains(rolUpper))
                return Forbid();

            string nombre;
            string usuarioSimulado;
            string? delegNombre = null;

            if (rolUpper == "DELEGACION")
            {
                if (!delegacionId.HasValue)
                    return BadRequest("Debe seleccionar una Delegación para simular.");

                delegNombre = await _autorizacion.GetDelegacionDisplayNombreAsync(delegacionId.Value);
                if (string.IsNullOrWhiteSpace(delegNombre))
                    return BadRequest("Delegación inválida.");

                // El claim DelegacionId (seteado más abajo) es la fuente de verdad real —
                // SesionService.GetDelegacionEfectivaAsync lo prioriza sobre el lookup por
                // username. Si la delegación tiene un usuario real asociado lo impersonamos
                // (para ver continuidad con sus borradores/notas ya cargados); si no, usamos
                // una identidad sintética estable por delegación, sin bloquear la simulación.
                var usuarioDeleg = await _autorizacion.GetUsuarioDeDelegacionAsync(delegacionId.Value);
                usuarioSimulado = !string.IsNullOrWhiteSpace(usuarioDeleg)
                    ? usuarioDeleg
                    : $"sim-deleg-{delegacionId.Value}";
                nombre = $"{delegNombre} (simulado)";
            }
            else
            {
                usuarioSimulado = rolUpper.ToLower();
                nombre = MockPerfiles.GetNombre(rolUpper);
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, nombre),
                new Claim(ClaimTypes.Role, rolUpper),
                new Claim("Dependencia", destino ?? ""),
                new Claim("Token", $"mock-{rolUpper.ToLower()}"),
                new Claim("NombreSolo", nombre.Split(' ')[0]),
                new Claim("Apellido", nombre.Contains(' ') ? nombre.Split(' ')[1] : ""),
                new Claim("Email", $"{rolUpper.ToLower()}@medios.test"),
                new Claim("Jerarquia", "Oficial"),
                new Claim("Legajo", MockPerfiles.GetLegajo(rolUpper)),
                new Claim("Telefono", ""),
                new Claim("Usuario", usuarioSimulado),
                new Claim("EsSimulador", "true")
            };

            // Preservar la identidad real intacta a través de la simulación.
            foreach (var tipo in RealClaimTypes)
            {
                var valor = User.FindFirst(tipo)?.Value;
                if (!string.IsNullOrEmpty(valor))
                    claims.Add(new Claim(tipo, valor));
            }

            if (esDev)
                claims.Add(new Claim("EsDesarrollador", "true"));

            // Al volver a DESARROLLADOR, opera también como MEDIOS (segundo role-claim).
            if (rolUpper == "DESARROLLADOR")
                claims.Add(new Claim(ClaimTypes.Role, "MEDIOS"));

            if (delegacionId.HasValue)
            {
                claims.Add(new Claim("DelegacionId", delegacionId.Value.ToString()));
                if (!string.IsNullOrWhiteSpace(delegNombre))
                    claims.Add(new Claim("DelegacionNombre", delegNombre));
            }

            var identity = new ClaimsIdentity(claims, "Cookies");
            var principal = new ClaimsPrincipal(identity);
            await HttpContext.SignInAsync("Cookies", principal);

            return RedirectToAction("Index", "Home");
        }

        // Deshace cualquier simulación en curso y restaura la identidad real completa
        // (perfil, delegación/ámbito reales si correspondía) tal como quedó al hacer login.
        [HttpPost]
        public async Task<IActionResult> Salir()
        {
            if (_config["Auth:Mode"] != "Mock") return Forbid();
            if (!User.HasClaim("EsSimulador", "true")) return Forbid();

            var rolReal = User.FindFirst("RolReal")?.Value;
            var usuarioReal = User.FindFirst("UsuarioReal")?.Value;
            if (string.IsNullOrWhiteSpace(rolReal) || string.IsNullOrWhiteSpace(usuarioReal))
                return Forbid();

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, User.FindFirst("NombreCompletoReal")?.Value ?? ""),
                new Claim(ClaimTypes.Role, rolReal),
                new Claim("Dependencia", User.FindFirst("DependenciaReal")?.Value ?? ""),
                new Claim("Token", User.FindFirst("TokenReal")?.Value ?? ""),
                new Claim("NombreSolo", User.FindFirst("NombreSoloReal")?.Value ?? ""),
                new Claim("Apellido", User.FindFirst("ApellidoReal")?.Value ?? ""),
                new Claim("Email", User.FindFirst("EmailReal")?.Value ?? ""),
                new Claim("Jerarquia", User.FindFirst("JerarquiaReal")?.Value ?? ""),
                new Claim("Legajo", User.FindFirst("LegajoReal")?.Value ?? ""),
                new Claim("Telefono", User.FindFirst("TelefonoReal")?.Value ?? ""),
                new Claim("Usuario", usuarioReal),
                new Claim("EsSimulador", "true")
            };

            foreach (var tipo in RealClaimTypes)
            {
                var valor = User.FindFirst(tipo)?.Value;
                if (!string.IsNullOrEmpty(valor))
                    claims.Add(new Claim(tipo, valor));
            }

            if (User.HasClaim("EsDesarrollador", "true"))
                claims.Add(new Claim("EsDesarrollador", "true"));

            if (rolReal == "DESARROLLADOR")
                claims.Add(new Claim(ClaimTypes.Role, "MEDIOS"));

            if (int.TryParse(User.FindFirst("DelegacionIdReal")?.Value, out var delegacionIdReal))
            {
                claims.Add(new Claim("DelegacionId", delegacionIdReal.ToString()));
                var delegNombreReal = User.FindFirst("DelegacionNombreReal")?.Value;
                if (!string.IsNullOrWhiteSpace(delegNombreReal))
                    claims.Add(new Claim("DelegacionNombre", delegNombreReal));
            }
            if (int.TryParse(User.FindFirst("AmbitoIdReal")?.Value, out var ambitoIdReal))
                claims.Add(new Claim("AmbitoId", ambitoIdReal.ToString()));

            var identity = new ClaimsIdentity(claims, "Cookies");
            var principal = new ClaimsPrincipal(identity);
            await HttpContext.SignInAsync("Cookies", principal);

            return RedirectToAction("Index", "Home");
        }
    }

    public static class MockPerfiles
    {
        private static readonly Dictionary<string, (string Nombre, string Legajo)> _perfiles = new()
        {
            { "DESARROLLADOR", ("Gale Desarrollador", "100000") },
            { "MEDIOS",        ("Medios División", "100002") },
            { "DELEGACION",    ("Agente Delegación", "100005") },
        };

        public static string GetNombre(string rol) =>
            _perfiles.TryGetValue(rol.ToUpper(), out var p) ? p.Nombre : "Usuario";

        public static string GetLegajo(string rol) =>
            _perfiles.TryGetValue(rol.ToUpper(), out var p) ? p.Legajo : "000000";
    }
}
