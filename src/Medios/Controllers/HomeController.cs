using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Medios.Services;

namespace Medios.Controllers
{
    public class HomeController : Controller
    {
        private readonly AuditoriaService _auditoria;
        private readonly SesionService _sesionService;
        private readonly AutorizacionService _autorizacionService;
        private readonly SintesisService _sintesisService;

        public HomeController(AuditoriaService auditoria, SesionService sesionService,
            AutorizacionService autorizacionService, SintesisService sintesisService)
        {
            _auditoria = auditoria;
            _sesionService = sesionService;
            _autorizacionService = autorizacionService;
            _sintesisService = sintesisService;
        }

        private string GetUsuario() => User.FindFirst("Usuario")?.Value ?? User.Identity?.Name ?? "";

        public async Task<IActionResult> Index()
        {
            ViewData["Title"] = "Inicio";
            var metricas = await _sesionService.GetMetricasAsync();

            var esMedios     = User.IsInRole("MEDIOS");
            var esDelegacion = User.IsInRole("DELEGACION") && !esMedios;

            // ── MEDIOS (absorbe ex-SUPERVISOR y ex-ANALISTA): panel administrativo + consolidación ──
            if (esMedios)
            {
                ViewBag.AutorizacionesPendientes = await _autorizacionService.GetPendientesCountAsync();
                ViewBag.SesionesFinalizadas    = (await _sesionService.GetBandejaAnalistaAsync("Finalizada")).Count;
                ViewBag.ConsolidadasPendientes = (await _sintesisService.GetConsolidasAsync()).Count;
            }

            // ── DELEGACION: panel de carga ──
            if (esDelegacion)
            {
                var usuario = GetUsuario();
                var mias = await _sesionService.GetMiasAsync(usuario);
                ViewBag.MisBorradores = mias.Count(s => s.Estado == "Borrador");

                var deleg = await _sesionService.GetDelegacionEfectivaAsync(User);
                if (deleg != null)
                {
                    ViewBag.SintesisAprobadas = await _sintesisService.GetAprobadasCountByDelegacionAsync(deleg.Id);

                    var sinRemitir = await _sesionService.GetNotasSinRemitirByDelegacionAsync(deleg.Id);
                    ViewBag.NotasDescartadas = sinRemitir
                        .Count(n => !string.IsNullOrEmpty(n.VersionActual?.MotivoDescarte));
                }
                else
                {
                    ViewBag.SintesisAprobadas = 0;
                    ViewBag.NotasDescartadas  = 0;
                }
            }

            return View(metricas);
        }

        public IActionResult NoAutorizado()
        {
            ViewData["Title"] = "Sin acceso";
            return View();
        }

        [AllowAnonymous]
        public IActionResult SinAmbito()
        {
            ViewData["Title"] = "Acceso pendiente";
            return View();
        }

        public IActionResult MiPerfil()
        {
            ViewData["Title"] = "Mi perfil / Sesión";
            return View(User.Claims.Select(c => new { c.Type, c.Value }).ToList());
        }
    }
}
