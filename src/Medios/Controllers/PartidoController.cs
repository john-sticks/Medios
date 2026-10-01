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

        [HasPermission("ADMINISTRAR_CATALOGOS")]
        public async Task<IActionResult> Listado(string? q, int? delegacionId, int? superintendenciaId)
        {
            ViewData["Title"] = "Partidos";
            using var db = _factory.Create();

            var query = db.Partidos
                .Include(p => p.Delegacion)
                .Include(p => p.AreaResponsabilidad)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(q))
                query = query.Where(p => p.Nombre.Contains(q));
            if (delegacionId.HasValue)
                query = query.Where(p => p.DelegacionId == delegacionId.Value);
            if (superintendenciaId.HasValue)
                query = query.Where(p => p.IdAreaResponsabilidad == superintendenciaId.Value);

            var partidos = await query.OrderBy(p => p.Nombre).ToListAsync();

            ViewBag.Delegaciones = await db.Delegaciones.Where(d => d.Activa).OrderBy(d => d.Nombre).ToListAsync();
            ViewBag.Superintendencias = await db.AreasResponsabilidad.OrderBy(a => a.AmbitoResponsabilidad).ToListAsync();
            ViewBag.FiltroQ = q;
            ViewBag.FiltroDelegacionId = delegacionId;
            ViewBag.FiltroSuperintendenciaId = superintendenciaId;

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
                .Include(p => p.AreaResponsabilidad)
                .FirstOrDefaultAsync(p => p.IdPartido == id);
            if (partido == null) return NotFound();

            ViewBag.Delegaciones = await db.Delegaciones.Where(d => d.Activa).OrderBy(d => d.Nombre).ToListAsync();
            ViewBag.Superintendencias = await db.AreasResponsabilidad.OrderBy(a => a.AmbitoResponsabilidad).ToListAsync();
            return View(partido);
        }

        [HasPermission("ADMINISTRAR_CATALOGOS")]
        [HttpPost]
        public async Task<IActionResult> Editar(int id, int delegacionId, int superintendenciaId)
        {
            using var db = _factory.Create();
            var partido = await db.Partidos.FindAsync(id);
            if (partido == null) return NotFound();

            partido.DelegacionId = delegacionId;
            partido.IdAreaResponsabilidad = superintendenciaId;
            await db.SaveChangesAsync();

            await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "partidos_prometheus", "PUT", "editar",
                new { id, delegacionId, superintendenciaId });
            TempData["Ok"] = "Partido actualizado.";
            return RedirectToAction(nameof(Listado));
        }
    }
}
