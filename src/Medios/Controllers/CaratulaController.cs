using Microsoft.AspNetCore.Mvc;
using Medios.Entities;
using Medios.Security;
using Medios.Services;
using Medios.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Medios.Controllers
{
    public class CaratulaController : Controller
    {
        private readonly IMediosDbContextFactory _factory;
        private readonly AuditoriaService _auditoria;

        public CaratulaController(IMediosDbContextFactory factory, AuditoriaService auditoria)
        {
            _factory = factory;
            _auditoria = auditoria;
        }

        private string GetNombre() => User.Identity?.Name ?? "";
        private string GetIp() =>
            Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "";

        // ── Listado de carátulas ──────────────────────────────────────

        [HasPermission("ADMINISTRAR_CATALOGOS")]
        public async Task<IActionResult> Listado()
        {
            ViewData["Title"] = "Carátulas y Modalidades";
            using var db = _factory.Create();
            var caratulas = await db.CaratulasQuiron
                .Include(c => c.Modalidades)
                .OrderBy(c => c.Nombre)
                .ToListAsync();
            return View(caratulas);
        }

        // ── Alta / Edición de carátula ────────────────────────────────

        [HasPermission("ADMINISTRAR_CATALOGOS")]
        [HttpGet]
        public async Task<IActionResult> EditarCaratula(int? id)
        {
            ViewData["Title"] = id.HasValue ? "Editar Carátula" : "Nueva Carátula";
            if (!id.HasValue) return View(new CaratulaQuiron());

            using var db = _factory.Create();
            var c = await db.CaratulasQuiron.FindAsync(id.Value);
            if (c == null) return NotFound();
            return View(c);
        }

        [HasPermission("ADMINISTRAR_CATALOGOS")]
        [HttpPost]
        public async Task<IActionResult> EditarCaratula(int? id, string nombre, bool consumable, bool calificable)
        {
            if (string.IsNullOrWhiteSpace(nombre))
            {
                TempData["Error"] = "El nombre es obligatorio.";
                return RedirectToAction(nameof(EditarCaratula), new { id });
            }

            using var db = _factory.Create();
            if (id.HasValue)
            {
                var c = await db.CaratulasQuiron.FindAsync(id.Value);
                if (c == null) return NotFound();
                c.Nombre = nombre.Trim();
                c.Consumable = consumable;
                c.Calificable = calificable;
                await db.SaveChangesAsync();
                await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "caratulas_quiron", "PUT", "editar", new { id, nombre });
                TempData["Ok"] = "Carátula actualizada.";
            }
            else
            {
                db.CaratulasQuiron.Add(new CaratulaQuiron { Nombre = nombre.Trim(), Consumable = consumable, Calificable = calificable });
                await db.SaveChangesAsync();
                await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "caratulas_quiron", "POST", "crear", new { nombre });
                TempData["Ok"] = "Carátula creada.";
            }
            return RedirectToAction(nameof(Listado));
        }

        [HasPermission("ADMINISTRAR_CATALOGOS")]
        [HttpPost]
        public async Task<IActionResult> EliminarCaratula(int id)
        {
            using var db = _factory.Create();
            var c = await db.CaratulasQuiron.Include(x => x.Modalidades).FirstOrDefaultAsync(x => x.Id == id);
            if (c == null) return NotFound();
            db.CaratulasQuiron.Remove(c);
            await db.SaveChangesAsync();
            await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "caratulas_quiron", "DELETE", "eliminar", new { id });
            TempData["Ok"] = "Carátula eliminada.";
            return RedirectToAction(nameof(Listado));
        }

        // ── Alta / Edición de modalidad ───────────────────────────────

        [HasPermission("ADMINISTRAR_CATALOGOS")]
        [HttpGet]
        public async Task<IActionResult> EditarModalidad(int? id, int? caratulaId)
        {
            ViewData["Title"] = id.HasValue ? "Editar Modalidad" : "Nueva Modalidad";
            using var db = _factory.Create();
            ViewBag.Caratulas = await db.CaratulasQuiron.OrderBy(c => c.Nombre).ToListAsync();
            ViewBag.CaratulaIdActual = caratulaId;

            if (!id.HasValue) return View(new ModalidadQuiron { CaratulaId = caratulaId ?? 0 });

            var m = await db.ModalidadesQuiron.Include(x => x.Caratula).FirstOrDefaultAsync(x => x.Id == id.Value);
            if (m == null) return NotFound();
            ViewBag.CaratulaIdActual = m.CaratulaId;
            return View(m);
        }

        [HasPermission("ADMINISTRAR_CATALOGOS")]
        [HttpPost]
        public async Task<IActionResult> EditarModalidad(int? id, int caratulaId, string nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre) || caratulaId == 0)
            {
                TempData["Error"] = "Nombre y carátula son obligatorios.";
                return RedirectToAction(nameof(EditarModalidad), new { id, caratulaId });
            }

            using var db = _factory.Create();
            if (id.HasValue)
            {
                var m = await db.ModalidadesQuiron.FindAsync(id.Value);
                if (m == null) return NotFound();
                m.Nombre = nombre.Trim();
                m.CaratulaId = caratulaId;
                await db.SaveChangesAsync();
                await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "modalidades_quiron", "PUT", "editar", new { id, nombre });
                TempData["Ok"] = "Modalidad actualizada.";
            }
            else
            {
                db.ModalidadesQuiron.Add(new ModalidadQuiron { Nombre = nombre.Trim(), CaratulaId = caratulaId });
                await db.SaveChangesAsync();
                await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "modalidades_quiron", "POST", "crear", new { caratulaId, nombre });
                TempData["Ok"] = "Modalidad creada.";
            }
            return RedirectToAction(nameof(Listado));
        }

        [HasPermission("ADMINISTRAR_CATALOGOS")]
        [HttpPost]
        public async Task<IActionResult> EliminarModalidad(int id)
        {
            using var db = _factory.Create();
            var m = await db.ModalidadesQuiron.FindAsync(id);
            if (m == null) return NotFound();
            db.ModalidadesQuiron.Remove(m);
            await db.SaveChangesAsync();
            await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "modalidades_quiron", "DELETE", "eliminar", new { id });
            TempData["Ok"] = "Modalidad eliminada.";
            return RedirectToAction(nameof(Listado));
        }
    }
}
