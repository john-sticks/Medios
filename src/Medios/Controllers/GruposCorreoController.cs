using System.ComponentModel.DataAnnotations;
using Medios.Models;
using Medios.Security;
using Medios.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Medios.Controllers;

[Microsoft.AspNetCore.Authorization.Authorize(Roles = "MEDIOS,DESARROLLADOR")]
[HasPermission("ADMINISTRAR_CONFIGURACION")]
public class GruposCorreoController : Controller
{
    private readonly GruposCorreoService _grupos;
    private readonly AuditoriaService _auditoria;
    public GruposCorreoController(GruposCorreoService grupos, AuditoriaService auditoria)
    {
        _grupos = grupos;
        _auditoria = auditoria;
    }

    [HttpGet]
    public async Task<IActionResult> Index() => View(await _grupos.ListarAsync());

    [HttpGet]
    public IActionResult Nuevo() => View("Editar", new GrupoCorreoModel { Contactos = new() { new() } });

    [HttpGet]
    public async Task<IActionResult> Editar(int id)
    {
        var modelo = await _grupos.ObtenerAsync(id);
        return modelo == null ? NotFound() : View(modelo);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Guardar(GrupoCorreoModel modelo)
    {
        if (!ModelState.IsValid) return View("Editar", modelo);
        try
        {
            var id = await _grupos.GuardarAsync(modelo);
            await RegistrarAsync("guardar", new { id, grupo = modelo.Nombre, contactos = modelo.Contactos.Count, modelo.Activo });
            TempData["Ok"] = "Grupo de correo guardado.";
            return RedirectToAction(nameof(Index));
        }
        catch (ValidationException ex) { ModelState.AddModelError("", ex.Message); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (DbUpdateException) { ModelState.AddModelError("", "No se pudo guardar el grupo. Revisá que el nombre no esté repetido."); }
        return View("Editar", modelo);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Eliminar(int id)
    {
        if (!await _grupos.EliminarAsync(id)) return NotFound();
        await RegistrarAsync("eliminar", new { id });
        TempData["Ok"] = "Grupo eliminado. Los envíos anteriores conservan sus destinatarios.";
        return RedirectToAction(nameof(Index));
    }

    private Task RegistrarAsync(string accion, object detalle) =>
        _auditoria.RegistrarAsync(User.Identity?.Name ?? "",
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? "", "grupos_correo", "POST", accion, detalle);
}
