using Microsoft.AspNetCore.Mvc;
using Medios.Security;
using Medios.Services;

namespace Medios.Controllers
{
    public class TrazaController : Controller
    {
        private readonly NotaService _notaService;
        private readonly SintesisService _sintesisService;
        private readonly SesionService _sesionService;
        private readonly TrazaService _traza;

        public TrazaController(NotaService notaService, SintesisService sintesisService,
            SesionService sesionService, TrazaService traza)
        {
            _notaService = notaService;
            _sintesisService = sintesisService;
            _sesionService = sesionService;
            _traza = traza;
        }

        // Buscador de notas y síntesis para trazar (ANALISTA y superiores)
        [HasPermission("VER_TRAZA")]
        [HttpGet]
        public async Task<IActionResult> Index(string modo = "nota", string? q = null,
            string? fechaDesde = null, string? fechaHasta = null, string? tipo = null, int? delegacionId = null)
        {
            ViewData["Title"] = "Traza";
            ViewBag.Modo = modo;
            ViewBag.Tipos = new[] { "Matutina", "Vespertina", "Ampliacion Matutina", "Ampliacion Vespertina", "Especial" };
            ViewBag.Delegaciones = await _sesionService.GetDelegacionesAsync();
            ViewBag.FiltroTipo = tipo;
            ViewBag.FiltroDelegacionId = delegacionId;
            ViewBag.FiltroFechaDesde = fechaDesde;
            ViewBag.FiltroFechaHasta = fechaHasta;

            if (modo == "sintesis")
            {
                DateOnly? desde = DateOnly.TryParse(fechaDesde, out var d) ? d : null;
                DateOnly? hasta = DateOnly.TryParse(fechaHasta, out var h) ? h : null;
                var buscar = fechaDesde != null || fechaHasta != null || tipo != null || delegacionId != null;
                ViewBag.ResultadosSintesis = buscar
                    ? await _sintesisService.BuscarParaTrazaAsync(desde, hasta, tipo, delegacionId)
                    : null;
            }
            else
            {
                ViewBag.Q = q;
                ViewBag.Resultados = q != null ? await _notaService.BuscarParaTrazaAsync(q) : null;
            }
            return View();
        }

        // Traza / ciclo de vida completo de una nota
        [HasPermission("VER_TRAZA")]
        [HttpGet]
        public async Task<IActionResult> Detalle(int id)
        {
            var traza = await _notaService.GetTrazaAsync(id);
            if (traza == null) return NotFound();
            ViewData["Title"] = $"Traza — Nota #{id}";
            ViewBag.Eventos = await _traza.GetEventosNotaAsync(id);
            return View(traza);
        }

        // Traza / ciclo de vida de una síntesis
        [HasPermission("VER_TRAZA")]
        [HttpGet]
        public async Task<IActionResult> Sintesis(int id)
        {
            var sintesis = await _sintesisService.GetByIdAsync(id);
            if (sintesis == null) return NotFound();
            ViewData["Title"] = $"Traza — Síntesis #{id}";
            ViewBag.Grupos = await _traza.GetTrazaSintesisAgrupadaAsync(id);
            return View(sintesis);
        }
    }
}
