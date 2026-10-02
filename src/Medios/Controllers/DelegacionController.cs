using Microsoft.AspNetCore.Mvc;
using Medios.Entities;
using Medios.Security;
using Medios.Services;
using System.Text.Json;

namespace Medios.Controllers
{
    public class DelegacionController : Controller
    {
        private readonly DelegacionService _service;
        private readonly NotaService _notaService;
        private readonly AuditoriaService _auditoria;
        private readonly AutorizacionService _autorizacion;

        public DelegacionController(DelegacionService service, NotaService notaService,
            AuditoriaService auditoria, AutorizacionService autorizacion)
        {
            _service = service;
            _notaService = notaService;
            _auditoria = auditoria;
            _autorizacion = autorizacion;
        }

        private string GetNombre() => User.Identity?.Name ?? "";
        private string GetIp() =>
            Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "";

        [HasPermission("ADMINISTRAR_DELEGACIONES")]
        public async Task<IActionResult> Listado()
        {
            ViewData["Title"] = "Delegaciones";
            var lista = await _service.GetTodasAsync();
            lista.RemoveAll(d => d.DelegacionPrometheusId == null && d.Nombre == "Superintendencia");
            ViewBag.UsuariosCount = await _autorizacion.GetUsuariosCountByDelegacionAsync();
            return View(lista);
        }

        [HasPermission("ADMINISTRAR_DELEGACIONES")]
        [HttpGet]
        public async Task<IActionResult> Editar(int id)
        {
            ViewData["Title"] = "Editar Delegación";
            var d = await _service.GetByIdAsync(id);
            if (d == null) return NotFound();

            ViewBag.PortalesJson = d.PortalesPrensa ?? "[]";
            ViewBag.Usuarios = await _autorizacion.GetUsuariosByDelegacionAsync(id);
            ViewBag.Partidos = await _notaService.GetPartidosByDelegacionAsync(id);
            return View(d);
        }

        // Los partidos se asignan directamente a la delegación en Partidos;
        // los usuarios se gestionan en Autorizaciones.
        [HasPermission("ADMINISTRAR_DELEGACIONES")]
        [HttpPost]
        public async Task<IActionResult> Editar(int id, string? portalesJson,
            string? driveSheetNotas, string? driveSheetSintesis, string? email)
        {
            string portalesNorm = "[]";
            if (!string.IsNullOrWhiteSpace(portalesJson))
            {
                try
                {
                    var lista = JsonSerializer.Deserialize<List<string>>(portalesJson) ?? new List<string>();
                    lista = lista.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
                    portalesNorm = JsonSerializer.Serialize(lista);
                }
                catch { portalesNorm = "[]"; }
            }

            var ok = await _service.ActualizarPortalesAsync(id, portalesNorm == "[]" ? null : portalesNorm);
            if (!ok)
            {
                TempData["Error"] = "Delegación no encontrada";
                return RedirectToAction(nameof(Listado));
            }

            await _service.ActualizarDriveSheetsAsync(id, driveSheetNotas, driveSheetSintesis);
            await _service.ActualizarEmailAsync(id, email);

            await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "delegaciones", "POST", "editar_portales", new { id });
            TempData["Ok"] = "Delegación actualizada";
            return RedirectToAction(nameof(Listado));
        }
    }
}
