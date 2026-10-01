using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Medios.Security;
using Medios.Services;

namespace Medios.Controllers
{
    [HasPermission("BUSCAR_NOTAS")]
    public class NotaBuscadorController : Controller
    {
        private readonly NotaService _notaService;
        private readonly SesionService _sesionService;
        private readonly AuditoriaService _auditoria;
        private readonly MenuXmlService _menuService;

        public NotaBuscadorController(NotaService notaService, SesionService sesionService, AuditoriaService auditoria, MenuXmlService menuService)
        {
            _notaService = notaService;
            _sesionService = sesionService;
            _auditoria = auditoria;
            _menuService = menuService;
        }

        private string GetUsuario() => User.FindFirst("Usuario")?.Value ?? User.Identity?.Name ?? "";
        private string GetNombre() => User.Identity?.Name ?? "";
        private string GetIp() =>
            Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString()
            ?? "";

        private bool EsDelegacion() =>
            string.Equals(User.FindFirst(ClaimTypes.Role)?.Value, "DELEGACION", StringComparison.OrdinalIgnoreCase);

        private bool TienePermiso(string permiso) =>
            _menuService.GetPermissions(User).Contains(permiso.ToUpper());

        // ── Búsqueda ──────────────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> Index(
            string? texto,
            int? categoriaId,
            int? partidoId,
            string? estadoRevision,
            string? fechaDesde,
            string? fechaHasta,
            int? delegacionId,
            string? estadoSesion,
            double? geoLat,
            double? geoLng,
            double? geoRadio,
            bool usarFechaNoticia = false,
            bool buscar = false)
        {
            var categorias = await _notaService.GetCategoriasAsync();
            var partidos = await _notaService.GetPartidosAsync();

            ViewBag.Categorias = categorias;
            ViewBag.Partidos = partidos;
            ViewBag.EsDelegacion = EsDelegacion();

            if (!EsDelegacion())
                ViewBag.Delegaciones = await _sesionService.GetDelegacionesAsync();

            List<NotaBuscadorItem> resultados = [];

            if (buscar)
            {
                int? delegFiltro = delegacionId;

                if (EsDelegacion())
                {
                    var deleg = await _sesionService.GetDelegacionEfectivaAsync(User);
                    // -1 → no delegation assigned → no results (avoids showing all notes)
                    delegFiltro = deleg?.Id ?? -1;
                }

                DateOnly? desde = DateOnly.TryParse(fechaDesde, out var d) ? d : null;
                DateOnly? hasta = DateOnly.TryParse(fechaHasta, out var h) ? h : null;

                resultados = await _notaService.BuscarAsync(
                    texto, categoriaId, partidoId, estadoRevision,
                    desde, hasta, delegFiltro, estadoSesion,
                    geoLat, geoLng, geoRadio, usarFechaNoticia);
            }

            ViewBag.Resultados = resultados;
            ViewBag.Buscar = buscar;

            // Mantener valores del formulario
            ViewBag.Texto = texto;
            ViewBag.CategoriaId = categoriaId;
            ViewBag.PartidoId = partidoId;
            ViewBag.EstadoRevision = estadoRevision;
            ViewBag.FechaDesde = fechaDesde;
            ViewBag.FechaHasta = fechaHasta;
            ViewBag.DelegacionId = delegacionId;
            ViewBag.EstadoSesion = estadoSesion;
            ViewBag.UsarFechaNoticia = usarFechaNoticia;
            ViewBag.GeoLat = geoLat;
            ViewBag.GeoLng = geoLng;
            ViewBag.GeoRadio = geoRadio ?? 5.0;

            return View();
        }

        // ── Detalle de nota ───────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> Detalle(int id, int? versionId = null)
        {
            var nota = await _notaService.GetDetalleAsync(id);
            if (nota == null) return NotFound();

            if (EsDelegacion())
            {
                var deleg = await _sesionService.GetDelegacionEfectivaAsync(User);
                bool permitida = nota.Sesion != null
                    ? nota.Sesion.DelegacionId == deleg?.Id
                    : nota.DelegacionId == deleg?.Id;
                if (!permitida) return Forbid();
            }

            // Si se solicita una versión específica (ej: desde el detalle de síntesis), mostrarla
            if (versionId.HasValue)
            {
                var version = nota.Versiones.FirstOrDefault(v => v.Id == versionId.Value);
                if (version != null)
                    ViewBag.VersionMostrada = version;
            }

            ViewBag.PuedeAmpliar = TienePermiso("AMPLIAR_NOTA");
            ViewBag.PuedeRelacionar = TienePermiso("RELACIONAR_NOTAS");
            ViewBag.PuedeDescartarBorrador = nota.VersionActual?.EstadoRevision == "Borrador"
                && (EsDelegacion() || User.IsInRole("MEDIOS"));
            ViewBag.SintesisPorVersion = await _notaService.GetSintesisPorVersionAsync(nota.Id);
            return View(nota);
        }

        // ── Descartar borrador / eliminar nota ────────────────────────

        [HttpPost]
        public async Task<IActionResult> DescartarBorrador(int id)
        {
            var nota = await _notaService.GetDetalleAsync(id);
            if (nota == null) return NotFound();

            if (EsDelegacion())
            {
                var deleg = await _sesionService.GetDelegacionEfectivaAsync(User);
                bool permitida = nota.Sesion != null
                    ? nota.Sesion.DelegacionId == deleg?.Id
                    : nota.DelegacionId == deleg?.Id;
                if (!permitida) return Forbid();
            }
            else if (!User.IsInRole("MEDIOS")) return Forbid();

            var (success, notaEliminada) = await _notaService.DescartarBorradorAsync(id);
            if (!success)
            {
                TempData["Error"] = "No se pudo descartar el borrador.";
                return RedirectToAction("Detalle", new { id });
            }

            await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "notas_prensa", "DELETE",
                notaEliminada ? "eliminar_nota" : "descartar_borrador", new { notaId = id });

            if (notaEliminada)
            {
                TempData["Ok"] = "Nota eliminada.";
                return RedirectToAction("Index");
            }

            TempData["Ok"] = "Borrador descartado. Volviste a la versión anterior.";
            return RedirectToAction("Detalle", new { id });
        }

        // ── Relacionar notas ──────────────────────────────────────────

        [HasPermission("RELACIONAR_NOTAS")]
        [HttpPost]
        public async Task<IActionResult> AgregarRelacion(int notaId, int relacionadaId)
        {
            await _notaService.AgregarRelacionAsync(notaId, relacionadaId, GetNombre());
            await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "nota_relaciones", "POST", "agregar_relacion",
                new { notaId, relacionadaId });
            return RedirectToAction("Detalle", new { id = notaId });
        }

        [HasPermission("RELACIONAR_NOTAS")]
        [HttpPost]
        public async Task<IActionResult> QuitarRelacion(int notaId, int relacionadaId)
        {
            await _notaService.QuitarRelacionAsync(notaId, relacionadaId);
            await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "nota_relaciones", "POST", "quitar_relacion",
                new { notaId, relacionadaId });
            return RedirectToAction("Detalle", new { id = notaId });
        }

        // ── AJAX: autocomplete para relacionar ────────────────────────

        [HttpGet]
        public async Task<IActionResult> BuscarParaRelacionar(string q, int excluirNotaId)
        {
            if (string.IsNullOrWhiteSpace(q) || q.Length < 3)
                return Json(new List<object>());

            var resultados = await _notaService.BuscarParaRelacionarAsync(q, excluirNotaId);
            return Json(resultados.Select(r => new
            {
                id = r.NotaId,
                titulo = r.Titulo,
                categoria = r.CategoriaNombre,
                fecha = r.SesionFecha.ToString("dd/MM/yyyy"),
                delegacion = r.DelegacionNombre ?? "División"
            }));
        }
    }
}
