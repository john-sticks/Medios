using Microsoft.AspNetCore.Mvc;
using Medios.Entities;
using Medios.Security;
using Medios.Services;
using Medios.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Medios.Controllers
{
    public class PartidoController : Controller
    {
        private readonly IMediosDbContextFactory _factory;
        private readonly AuditoriaService _auditoria;

        public PartidoController(IMediosDbContextFactory factory, AuditoriaService auditoria)
        {
            _factory = factory;
            _auditoria = auditoria;
        }

        private string GetNombre() => User.Identity?.Name ?? "";
        private string GetIp() =>
            Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "";

        private static IQueryable<Delegacion> DelegacionesDisponibles(MediosDbContext db) =>
            db.Delegaciones.Where(d => d.Activa
                && (d.DelegacionPrometheusId != null || d.Nombre != "Superintendencia"));

        [HasPermission("ADMINISTRAR_CATALOGOS")]
        public async Task<IActionResult> Listado(string? q, int? delegacionId)
        {
            ViewData["Title"] = "Partidos";
            using var db = _factory.Create();

            var query = db.Partidos
                .Include(p => p.Delegacion)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(q))
                query = query.Where(p => p.Nombre.Contains(q));
            if (delegacionId.HasValue)
                query = query.Where(p => p.DelegacionId == delegacionId.Value);

            var partidos = await query.OrderBy(p => p.Nombre).ToListAsync();

            ViewBag.Delegaciones = await DelegacionesDisponibles(db)
                .OrderBy(d => d.Nombre).ToListAsync();
            ViewBag.FiltroQ = q;
            ViewBag.FiltroDelegacionId = delegacionId;

            return View(partidos);
        }

        [HasPermission("ADMINISTRAR_CATALOGOS")]
        [HttpGet]
        public async Task<IActionResult> Editar(int id)
        {
            ViewData["Title"] = "Editar Partido";
            using var db = _factory.Create();
            var partido = await db.Partidos
                .Include(p => p.Delegacion)
                .FirstOrDefaultAsync(p => p.IdPartido == id);
            if (partido == null) return NotFound();

            ViewBag.Delegaciones = await DelegacionesDisponibles(db)
                .OrderBy(d => d.Nombre).ToListAsync();
            return View(partido);
        }

        [HasPermission("ADMINISTRAR_CATALOGOS")]
        [HttpPost]
        public async Task<IActionResult> Editar(int id, int delegacionId)
        {
            using var db = _factory.Create();
            var partido = await db.Partidos.FindAsync(id);
            if (partido == null) return NotFound();

            if (!ModelState.IsValid || !await DelegacionesDisponibles(db).AnyAsync(d => d.Id == delegacionId))
            {
                TempData["Error"] = "Seleccioná una delegación activa válida.";
                return RedirectToAction(nameof(Editar), new { id });
            }

            partido.DelegacionId = delegacionId;
            await db.SaveChangesAsync();

            await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "partidos_prometheus", "PUT", "editar",
                new { id, delegacionId });
            TempData["Ok"] = "Partido actualizado.";
            return RedirectToAction(nameof(Listado));
        }
    }
}
