using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Medios.Entities;
using Medios.Infrastructure.Data;
using Medios.Security;
using System.Security.Claims;

namespace Medios.Controllers
{
    [HasPermission("VER_PORTALES")]
    public class PortalesController : Controller
    {
        private readonly IMediosDbContextFactory _factory;

        public PortalesController(IMediosDbContextFactory factory)
        {
            _factory = factory;
        }

        // MEDIOS puede gestionar todos; DELEGACION solo los de su propia delegación
        private bool PuedeGestionar() =>
            User.IsInRole("MEDIOS") || User.IsInRole("DELEGACION");

        private int? GetDelegacionId()
        {
            var raw = User.FindFirst("DelegacionId")?.Value;
            return int.TryParse(raw, out var v) ? v : null;
        }

        // Verifica que el usuario en sesión pueda gestionar (editar/eliminar) este portal
        private bool PuedeGestionarPortal(PortalPrensa portal)
        {
            if (User.IsInRole("MEDIOS")) return true;
            if (!User.IsInRole("DELEGACION")) return false;
            var delegacionId = GetDelegacionId();
            if (!delegacionId.HasValue) return false;
            // DELEGACION solo gestiona portales de partidos de su propia delegación
            return portal.Partido != null && portal.Partido.DelegacionId == delegacionId.Value;
        }

        // Partidos disponibles para seleccionar al crear/editar según rol
        private IQueryable<Partido> PartidosPermitidos(MediosDbContext db)
        {
            var query = db.Partidos.AsQueryable();
            if (User.IsInRole("DELEGACION") && !User.IsInRole("MEDIOS"))
            {
                var delegacionId = GetDelegacionId();
                if (delegacionId.HasValue)
                    query = query.Where(p => p.DelegacionId == delegacionId.Value);
            }
            return query.OrderBy(p => p.Nombre);
        }

        [HttpGet]
        public async Task<IActionResult> Listado(string? region, int? partidoId, bool? soloActivos)
        {
            ViewData["Title"] = "Portales de Prensa";

            using var db = _factory.Create();

            var delegacionId = GetDelegacionId();
            var esDelegacion = User.IsInRole("DELEGACION") && !User.IsInRole("MEDIOS");

            var query = db.PortalesPrensas
                .Include(p => p.Partido)
                .AsQueryable();

            // DELEGACION ve solo portales de su propia delegación (más provinciales sin partido)
            if (esDelegacion && delegacionId.HasValue)
                query = query.Where(p => p.PartidoId == null
                    || p.Partido!.DelegacionId == delegacionId.Value);

            if (!string.IsNullOrEmpty(region))
                query = query.Where(p => p.Region == region);

            if (partidoId.HasValue)
                query = query.Where(p => p.PartidoId == partidoId.Value);

            if (soloActivos ?? true)
                query = query.Where(p => p.Activo);

            var portales = await query
                .OrderBy(p => p.Region)
                .ThenBy(p => p.Nombre)
                .ToListAsync();

            var regiones = await db.PortalesPrensas
                .Where(p => p.Region != null)
                .Select(p => p.Region!)
                .Distinct()
                .OrderBy(r => r)
                .ToListAsync();

            var partidos = await db.Partidos
                .OrderBy(p => p.Nombre)
                .ToListAsync();

            ViewBag.Regiones = regiones;
            ViewBag.Partidos = partidos;
            ViewBag.FiltroRegion = region;
            ViewBag.FiltroPartidoId = partidoId;
            ViewBag.SoloActivos = soloActivos ?? true;
            ViewBag.PuedeGestionar = PuedeGestionar();
            ViewBag.EsDelegacion = esDelegacion;
            ViewBag.DelegacionId = delegacionId;

            return View(portales);
        }

        [HasPermission("GESTIONAR_PORTALES")]
        [HttpGet]
        public async Task<IActionResult> Editar(int id)
        {
            using var db = _factory.Create();
            var portal = await db.PortalesPrensas
                .Include(p => p.Partido)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (portal == null) return NotFound();
            if (!PuedeGestionarPortal(portal)) return Forbid();

            ViewBag.Partidos = PartidosPermitidos(db);
            ViewBag.Regiones = await db.PortalesPrensas
                .Where(p => p.Region != null)
                .Select(p => p.Region!)
                .Distinct().OrderBy(r => r).ToListAsync();
            return View(portal);
        }

        [HasPermission("GESTIONAR_PORTALES")]
        [HttpPost]
        public async Task<IActionResult> Editar(int id, string nombre, string url, string? region, int? partidoId, bool activo)
        {
            using var db = _factory.Create();
            var portal = await db.PortalesPrensas
                .Include(p => p.Partido)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (portal == null) return NotFound();
            if (!PuedeGestionarPortal(portal)) return Forbid();

            portal.Nombre = nombre.Trim();
            portal.Url = url.Trim();
            portal.Region = string.IsNullOrWhiteSpace(region) ? null : region.Trim();
            portal.PartidoId = partidoId;
            portal.Activo = activo;

            await db.SaveChangesAsync();
            TempData["Ok"] = "Portal actualizado";
            return RedirectToAction(nameof(Listado));
        }

        [HasPermission("GESTIONAR_PORTALES")]
        [HttpGet]
        public async Task<IActionResult> Nuevo()
        {
            using var db = _factory.Create();
            ViewBag.Partidos = PartidosPermitidos(db);
            ViewBag.Regiones = await db.PortalesPrensas
                .Where(p => p.Region != null)
                .Select(p => p.Region!)
                .Distinct().OrderBy(r => r).ToListAsync();
            return View();
        }

        [HasPermission("GESTIONAR_PORTALES")]
        [HttpPost]
        public async Task<IActionResult> Nuevo(string nombre, string url, string? region, int? partidoId)
        {
            if (string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(url))
            {
                TempData["Error"] = "Nombre y URL son obligatorios";
                return RedirectToAction(nameof(Nuevo));
            }

            // DELEGACION solo puede agregar portales de su propia delegación (con partido)
            var delegacionId = GetDelegacionId();
            var esDelegacion = User.IsInRole("DELEGACION") && !User.IsInRole("MEDIOS");
            if (esDelegacion)
            {
                if (!partidoId.HasValue)
                { TempData["Error"] = "Debes asignar un partido a tu portal."; return RedirectToAction(nameof(Nuevo)); }
                using var dbCheck = _factory.Create();
                var partido = await dbCheck.Partidos.FindAsync(partidoId.Value);
                if (partido == null || partido.DelegacionId != delegacionId)
                { TempData["Error"] = "Solo podés agregar portales de tu delegación."; return RedirectToAction(nameof(Nuevo)); }
            }

            using var db = _factory.Create();
            db.PortalesPrensas.Add(new PortalPrensa
            {
                Nombre = nombre.Trim(),
                Url = url.Trim(),
                Region = string.IsNullOrWhiteSpace(region) ? null : region.Trim(),
                PartidoId = partidoId,
                Activo = true
            });
            await db.SaveChangesAsync();

            TempData["Ok"] = "Portal agregado";
            return RedirectToAction(nameof(Listado));
        }

        [HasPermission("GESTIONAR_PORTALES")]
        [HttpPost]
        public async Task<IActionResult> Eliminar(int id)
        {
            using var db = _factory.Create();
            var portal = await db.PortalesPrensas
                .Include(p => p.Partido)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (portal != null)
            {
                if (!PuedeGestionarPortal(portal)) return Forbid();
                db.PortalesPrensas.Remove(portal);
                await db.SaveChangesAsync();
            }
            TempData["Ok"] = "Portal eliminado";
            return RedirectToAction(nameof(Listado));
        }
    }
}
