using Medios.Security;
using Microsoft.AspNetCore.Mvc;

namespace Medios.Controllers;

public partial class SintesisController
{
    [HasPermission("CREAR_SINTESIS")]
    [HttpGet]
    public async Task<IActionResult> CrearFiltrada(int id)
    {
        if (!PuedeConsolidar()) return Forbid();
        var origen = await _sintesisService.GetByIdAsync(id);
        if (origen == null || origen.DelegacionId != null || origen.Estado != "Informada") return NotFound();
        ViewBag.DelegacionesFiltrada = (await _delegService.GetTodasAsync())
            .ToDictionary(d => d.Id, d => d.Nombre);
        return View(origen);
    }

    [HasPermission("CREAR_SINTESIS")]
    [HttpPost, ValidateAntiForgeryToken, ActionName("CrearFiltrada")]
    public async Task<IActionResult> CrearFiltradaPost(int id, List<int>? seleccionIds)
    {
        if (!PuedeConsolidar()) return Forbid();
        var (nuevaId, error) = await _sintesisService.CrearFiltradaAsync(id, seleccionIds ?? new(), GetUsuario());
        if (error != null)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(CrearFiltrada), new { id });
        }
        await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "sintesis", "POST", "crear_filtrada", new { origenId = id, nuevaId });
        await _traza.RegistrarAsync("SINTESIS_FILTRADA", GetNombre(), GetRol(), sintesisId: nuevaId,
            estado: "Borrador", detalle: $"Filtrada de síntesis #{id}; {seleccionIds!.Distinct().Count()} notas.");
        try
        {
            await _sintesisService.GenerarPdfAsync(nuevaId);
            TempData["Ok"] = "Síntesis filtrada creada con su propio PDF. Revisala antes de informar y elegir los destinatarios.";
        }
        catch (Exception ex)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<SintesisController>>()
                .LogError(ex, "Error al generar PDF de filtrada {Id}", nuevaId);
            TempData["Error"] = "La síntesis filtrada se creó, pero no se pudo generar el PDF. Volvé a generarlo antes de informar.";
        }
        return RedirectToAction(nameof(Detalle), new { id = nuevaId });
    }

    [HasPermission("CREAR_SINTESIS")]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminarFiltrada(int id)
    {
        if (!PuedeConsolidar()) return Forbid();
        var s = await _sintesisService.GetByIdAsync(id);
        if (s == null || !s.EsFiltrada) return NotFound();
        if (!await _sintesisService.EliminarConsolidadaAsync(id))
        {
            TempData["Error"] = "No se puede eliminar una filtrada que ya fue informada o tiene un envío en curso.";
            return RedirectToAction(nameof(Detalle), new { id });
        }
        await _traza.RegistrarAsync("FILTRADA_ELIMINADA", GetNombre(), GetRol(), sintesisId: id,
            detalle: $"Se eliminó la filtrada pendiente de origen #{s.SintesisOrigenId}. El origen se conserva.");
        TempData["Ok"] = "Se eliminó la filtrada pendiente. La síntesis de origen y sus sesiones se conservaron.";
        return Redirect("/Sesion/Bandeja#consolidadas");
    }
}
