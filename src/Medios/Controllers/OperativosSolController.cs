using Medios.Infrastructure.Data;
using Medios.Models;
using Medios.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace Medios.Controllers;

[Authorize(Roles = "DESARROLLADOR,MEDIOS")]
public class OperativosSolController(IMediosDbContextFactory factory, AuditoriaService auditoria) : Controller
{
    private async Task<OperativosSolViewModel> ModeloAsync(OperativoSolModel formulario, bool expandido)
    {
        using var db = factory.Create();
        return new() { Formulario = formulario, Expandido = expandido,
            Operativos = await new OperativoSolService(factory).ListarAsync(),
            Partidos = await db.Partidos.AsNoTracking().OrderBy(p => p.Nombre).ToListAsync() };
    }
    [HttpGet]
    public async Task<IActionResult> Index(Guid? id)
    {
        var modelo = await ModeloAsync(new(), id.HasValue);
        if (id.HasValue)
        {
            var operativo = modelo.Operativos.FirstOrDefault(o => o.Id == id);
            if (operativo == null) return NotFound();
            modelo.Formulario = operativo;
        }
        return View(modelo);
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Guardar([Bind(Prefix = "Formulario")] OperativoSolModel formulario)
    {
        if (ModelState.IsValid)
        {
            try { await new OperativoSolService(factory).GuardarAsync(formulario); }
            catch (ArgumentException error) { ModelState.AddModelError("", error.Message); }
            if (ModelState.IsValid)
            {
                await auditoria.RegistrarAsync(User.Identity?.Name ?? "", HttpContext.Connection.RemoteIpAddress?.ToString() ?? "",
                    "configuracion", "PUT", "operativo_sol", new { formulario.Id, formulario.Descripcion,
                        formulario.FechaDesde, formulario.FechaHasta, formulario.Activo, formulario.PartidoIds });
                TempData["Ok"] = "Operativo SOL guardado.";
                return RedirectToAction(nameof(Index));
            }
        }
        return View("Index", await ModeloAsync(formulario, true));
    }
}
