using Microsoft.AspNetCore.Mvc;
using Medios.Entities;
using Medios.Security;
using Medios.Services;
using Medios.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Medios.Controllers
{
    public class CategoriaController : Controller
    {
        private readonly IMediosDbContextFactory _factory;
        private readonly AuditoriaService _auditoria;

        public CategoriaController(IMediosDbContextFactory factory, AuditoriaService auditoria)
        {
            _factory = factory;
            _auditoria = auditoria;
        }

        private string GetNombre() => User.Identity?.Name ?? "";
        private string GetIp() =>
            Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "";

        [HasPermission("ADMINISTRAR_CATALOGOS")]
        public async Task<IActionResult> Listado()
        {
            ViewData["Title"] = "Categorías";
            using var db = _factory.Create();
            var lista = await db.CategoriasNoticia
                .OrderBy(c => c.Orden)
                .ThenBy(c => c.Nombre)
                .ToListAsync();
            return View(lista);
        }

        [HasPermission("ADMINISTRAR_CATALOGOS")]
        [HttpGet]
        public async Task<IActionResult> Editar(int? id)
        {
            ViewData["Title"] = id.HasValue ? "Editar Categoría" : "Nueva Categoría";
            if (!id.HasValue)
            {
                using var db2 = _factory.Create();
                var maxOrden = await db2.CategoriasNoticia.MaxAsync(c => (int?)c.Orden) ?? 0;
                return View(new CategoriaNoticia { Orden = maxOrden + 1, Activa = true });
            }

            using var db = _factory.Create();
            var cat = await db.CategoriasNoticia.FindAsync(id.Value);
            if (cat == null) return NotFound();
            return View(cat);
        }

        [HasPermission("ADMINISTRAR_CATALOGOS")]
        [HttpPost]
        public async Task<IActionResult> Editar(int? id, string nombre, int orden, bool activa)
        {
            if (string.IsNullOrWhiteSpace(nombre))
            {
                TempData["Error"] = "El nombre es obligatorio.";
                return RedirectToAction(nameof(Editar), new { id });
            }

            using var db = _factory.Create();
            if (id.HasValue)
            {
                var cat = await db.CategoriasNoticia.FindAsync(id.Value);
                if (cat == null) return NotFound();
                cat.Nombre = nombre.Trim();
                cat.Orden  = orden;
                cat.Activa = activa;
                await db.SaveChangesAsync();
                await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "categorias_noticia", "PUT", "editar", new { id, nombre });
                TempData["Ok"] = "Categoría actualizada.";
            }
            else
            {
                db.CategoriasNoticia.Add(new CategoriaNoticia { Nombre = nombre.Trim(), Orden = orden, Activa = activa });
                await db.SaveChangesAsync();
                await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "categorias_noticia", "POST", "crear", new { nombre });
                TempData["Ok"] = "Categoría creada.";
            }
            return RedirectToAction(nameof(Listado));
        }

        [HasPermission("ADMINISTRAR_CATALOGOS")]
        [HttpPost]
        public async Task<IActionResult> Eliminar(int id)
        {
            using var db = _factory.Create();
            var cat = await db.CategoriasNoticia.FindAsync(id);
            if (cat == null) return NotFound();
            db.CategoriasNoticia.Remove(cat);
            await db.SaveChangesAsync();
            await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "categorias_noticia", "DELETE", "eliminar", new { id });
            TempData["Ok"] = "Categoría eliminada.";
            return RedirectToAction(nameof(Listado));
        }
    }
}
