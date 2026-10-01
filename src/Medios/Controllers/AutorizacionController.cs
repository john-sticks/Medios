using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Medios.Security;
using Medios.Services;
using System.Security.Claims;

namespace Medios.Controllers
{
    [Authorize]
    public class AutorizacionController : Controller
    {
        private readonly AutorizacionService _service;
        private readonly AuditoriaService _auditoria;
        private readonly MenuXmlService _menuService;

        public AutorizacionController(AutorizacionService service, AuditoriaService auditoria, MenuXmlService menuService)
        {
            _service = service;
            _auditoria = auditoria;
            _menuService = menuService;
        }

        private string GetUsuario() =>
            User.FindFirst("Usuario")?.Value ?? User.Identity?.Name ?? "";

        private string GetIp() =>
            Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString()
            ?? "";

        [HttpGet]
        public async Task<IActionResult> Solicitud()
        {
            var usuario = GetUsuario();
            var estado = await _service.GetEstado(usuario);
            if (estado == "aprobada") return RedirectToAction("Index", "Home");
            if (estado == "pendiente") return RedirectToAction("Pendiente");
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Solicitud(string _ = "")
        {
            var usuario = GetUsuario();
            var nombre = User.FindFirst("NombreSolo")?.Value ?? "";
            var apellido = User.FindFirst("Apellido")?.Value ?? "";
            var email = User.FindFirst("Email")?.Value;
            var destino = User.FindFirst("Dependencia")?.Value;
            var jerarquia = User.FindFirst("Jerarquia")?.Value;
            var legajo = User.FindFirst("Legajo")?.Value;
            var telefono = User.FindFirst("Telefono")?.Value;
            var nombreCompleto = $"{nombre} {apellido}".Trim();

            var (id, _) = await _service.CrearSolicitud(usuario, nombreCompleto, email, destino, jerarquia, legajo, telefono);

            await _auditoria.RegistrarAsync(nombreCompleto, GetIp(), "autorizaciones", "POST", "solicitud",
                new { id, usuario, legajo });

            return RedirectToAction("Pendiente");
        }

        public async Task<IActionResult> Pendiente()
        {
            var usuario = GetUsuario();
            var solicitud = await _service.GetPendiente(usuario);
            if (solicitud == null)
            {
                var estado = await _service.GetEstado(usuario);
                if (estado == "aprobada") return RedirectToAction("Index", "Home");
            }
            ViewBag.Solicitud = solicitud;
            return View();
        }

        [AllowAnonymous]
        public async Task<IActionResult> Imprimir(int id, string? codigo = null)
        {
            var solicitud = await _service.GetById(id);
            if (solicitud == null) return NotFound();

            // Acceso permitido si: (a) admin autenticado con permiso para ver autorizaciones, o
            // (b) viene con el código correcto de la solicitud (el propio solicitante).
            var esAdmin = User.Identity?.IsAuthenticated == true
                          && _menuService.GetPermissions(User).Contains("VER_AUTORIZACIONES");
            var codigoOk = !string.IsNullOrWhiteSpace(codigo) && solicitud.Codigo == codigo.Trim();
            if (!esAdmin && !codigoOk) return NotFound();

            return View(solicitud);
        }

        [HasPermission("VER_AUTORIZACIONES")]
        public async Task<IActionResult> Listado()
        {
            ViewData["Title"] = "Autorizaciones de Usuarios";
            var delegaciones = await _service.GetDelegacionesAsync();
            var ambitos = await _service.GetAmbitosAsync();
            ViewBag.Delegaciones = delegaciones;
            ViewBag.Ambitos = ambitos;
            ViewBag.EsDesarrollador = User.HasClaim("EsDesarrollador", "true");
            return View();
        }
    }

    [Authorize]
    [ApiController]
    [Route("api/autorizacion")]
    public class AutorizacionApiController : ControllerBase
    {
        private readonly AutorizacionService _service;
        private readonly AuditoriaService _auditoria;

        public AutorizacionApiController(AutorizacionService service, AuditoriaService auditoria)
        {
            _service = service;
            _auditoria = auditoria;
        }

        private string GetIp() =>
            Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString()
            ?? "";

        [HttpGet("listado")]
        [HasPermission("VER_AUTORIZACIONES")]
        public async Task<IActionResult> GetListado([FromQuery] string? estado = null)
        {
            var esDesarrollador = User.HasClaim("EsDesarrollador", "true");
            var data = await _service.GetListado(estado, esDesarrollador);
            return Ok(data);
        }

        [HttpPost("aprobar")]
        [HasPermission("APROBAR_AUTORIZACION")]
        public async Task<IActionResult> Aprobar([FromBody] AprobarDto dto)
        {
            // Solo un desarrollador puede designar a otro como DESARROLLADOR
            if (string.Equals(dto.Rol, "DESARROLLADOR", StringComparison.OrdinalIgnoreCase)
                && !User.HasClaim("EsDesarrollador", "true"))
                return Forbid();

            var aprobadoPor = User.Identity?.Name ?? "";
            var solicitud = await _service.GetById(dto.Id);
            var ok = await _service.Aprobar(dto.Id, dto.Codigo, aprobadoPor, dto.DelegacionId, dto.AmbitoId, dto.Rol);

            if (!ok) return BadRequest(new { error = "Código incorrecto o solicitud no válida" });

            await _auditoria.RegistrarAsync(aprobadoPor, GetIp(), "autorizaciones", "POST", "alta",
                new { id = dto.Id, usuario_aprobado = solicitud?.Usuario, rol = dto.Rol, delegacionId = dto.DelegacionId, ambitoId = dto.AmbitoId });

            return Ok();
        }

        [HttpPost("rechazar")]
        [HasPermission("RECHAZAR_AUTORIZACION")]
        public async Task<IActionResult> Rechazar([FromBody] int id)
        {
            var aprobadoPor = User.Identity?.Name ?? "";
            var solicitud = await _service.GetById(id);
            var ok = await _service.Rechazar(id, aprobadoPor);
            if (!ok) return NotFound();

            await _auditoria.RegistrarAsync(aprobadoPor, GetIp(), "autorizaciones", "POST", "baja",
                new { id, usuario_rechazado = solicitud?.Usuario });

            return Ok();
        }

        // Activar / desactivar el acceso de un usuario aprobado
        [HttpPost("toggle-activo")]
        [HasPermission("APROBAR_AUTORIZACION")]
        public async Task<IActionResult> ToggleActivo([FromBody] int id)
        {
            var quien = User.Identity?.Name ?? "";
            var solicitud = await _service.GetById(id);
            if (solicitud == null || solicitud.Estado != "aprobada")
                return BadRequest(new { error = "Solo se puede activar/desactivar un usuario aprobado" });

            var activo = await _service.ToggleActivoAsync(id);
            await _auditoria.RegistrarAsync(quien, GetIp(), "autorizaciones", "POST",
                activo ? "activar_usuario" : "desactivar_usuario",
                new { id, usuario = solicitud.Usuario });
            return Ok(new { activo });
        }

        // Cambiar la delegación de un usuario aprobado
        [HttpPost("cambiar-delegacion")]
        [HasPermission("APROBAR_AUTORIZACION")]
        public async Task<IActionResult> CambiarDelegacion([FromBody] CambiarDelegacionDto dto)
        {
            var quien = User.Identity?.Name ?? "";
            var ok = await _service.CambiarDelegacionAsync(dto.Id, dto.DelegacionId);
            if (!ok) return BadRequest(new { error = "No se pudo cambiar la delegación" });

            await _auditoria.RegistrarAsync(quien, GetIp(), "autorizaciones", "POST", "cambiar_delegacion",
                new { id = dto.Id, delegacionId = dto.DelegacionId });
            return Ok();
        }

        // Cambiar el rol de un usuario aprobado
        [HttpPost("cambiar-rol")]
        [HasPermission("APROBAR_AUTORIZACION")]
        public async Task<IActionResult> CambiarRol([FromBody] CambiarRolDto dto)
        {
            // Solo un desarrollador puede designar a otro como DESARROLLADOR
            if (string.Equals(dto.Rol, "DESARROLLADOR", StringComparison.OrdinalIgnoreCase)
                && !User.HasClaim("EsDesarrollador", "true"))
                return Forbid();

            var quien = User.Identity?.Name ?? "";
            var ok = await _service.CambiarRolAsync(dto.Id, dto.Rol);
            if (!ok) return BadRequest(new { error = "Rol inválido o usuario no aprobado" });

            await _auditoria.RegistrarAsync(quien, GetIp(), "autorizaciones", "POST", "cambiar_rol",
                new { id = dto.Id, rol = dto.Rol });
            return Ok();
        }
    }

    public class AprobarDto
    {
        public int Id { get; set; }
        public string Codigo { get; set; } = "";
        public int? DelegacionId { get; set; }
        public int? AmbitoId { get; set; }
        public string? Rol { get; set; }
    }

    public class CambiarDelegacionDto
    {
        public int Id { get; set; }
        public int DelegacionId { get; set; }
    }

    public class CambiarRolDto
    {
        public int Id { get; set; }
        public string Rol { get; set; } = "";
    }
}
