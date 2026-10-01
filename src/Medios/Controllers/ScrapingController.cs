using Microsoft.AspNetCore.Mvc;
using Medios.Security;
using Medios.Services;
using Medios.Entities;

namespace Medios.Controllers
{
    public class ScrapingController : Controller
    {
        private readonly ScrapingReglaService _reglas;
        private readonly ScraperService _scraper;
        private readonly AuditoriaService _auditoria;

        public ScrapingController(ScrapingReglaService reglas, ScraperService scraper, AuditoriaService auditoria)
        {
            _reglas = reglas;
            _scraper = scraper;
            _auditoria = auditoria;
        }

        private string GetNombre() => User.Identity?.Name ?? "";
        private string GetIp() =>
            Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "";

        [HasPermission("ADMINISTRAR_CONFIGURACION")]
        public async Task<IActionResult> Listado()
        {
            ViewData["Title"] = "Scraping de Portales";
            var reglas = await _reglas.GetTodasAsync();
            return View(reglas);
        }

        [HasPermission("ADMINISTRAR_CONFIGURACION")]
        [HttpGet]
        public async Task<IActionResult> Editar(int id = 0)
        {
            ViewData["Title"] = id == 0 ? "Nueva regla de scraping" : "Editar regla de scraping";
            var regla = id == 0 ? new ScrapingRegla() : await _reglas.GetByIdAsync(id);
            if (regla == null) return NotFound();
            return View(regla);
        }

        [HasPermission("ADMINISTRAR_CONFIGURACION")]
        [HttpPost]
        public async Task<IActionResult> Editar(ScrapingRegla regla)
        {
            var (ok, error) = await _reglas.GuardarAsync(regla);
            if (!ok)
            {
                TempData["Error"] = error;
                ViewData["Title"] = regla.Id == 0 ? "Nueva regla de scraping" : "Editar regla de scraping";
                return View(regla);
            }
            await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "scraping_reglas", "POST",
                regla.Id == 0 ? "alta" : "editar", new { regla.Dominio });
            TempData["Ok"] = "Regla guardada";
            return RedirectToAction(nameof(Listado));
        }

        [HasPermission("ADMINISTRAR_CONFIGURACION")]
        [HttpPost]
        public async Task<IActionResult> Eliminar(int id)
        {
            await _reglas.EliminarAsync(id);
            await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "scraping_reglas", "POST", "baja", new { id });
            TempData["Ok"] = "Regla eliminada";
            return RedirectToAction(nameof(Listado));
        }

        // Prueba en vivo: scrapea una URL y devuelve título + texto extraídos
        [HasPermission("ADMINISTRAR_CONFIGURACION")]
        [HttpPost]
        public async Task<IActionResult> Probar([FromBody] ProbarScrapingDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto?.Url))
                return BadRequest(new { error = "URL vacía" });
            var (titulo, texto, direccion, fuente, fecha, imagenUrl, videoUrl, error) = await _scraper.ScrapearAsync(dto.Url);
            if (error != null) return StatusCode(503, new { error });
            return Ok(new { titulo, texto, direccion, fuente, fecha = fecha?.ToString("dd/MM/yyyy HH:mm"), imagenUrl, videoUrl });
        }
    }

    public class ProbarScrapingDto
    {
        public string? Url { get; set; }
    }
}
