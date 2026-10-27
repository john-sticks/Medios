using Medios.Security;
using Medios.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Medios.Controllers;

[HasPermission("VER_SINTESIS")]
public class ArchivoPdfController(ArchivoPdfService archivos, AuditoriaService auditoria) : Controller
{
    private bool EsMedios => User.IsInRole("MEDIOS");

    [HttpGet]
    public async Task<IActionResult> Index(DateOnly? desde, DateOnly? hasta, string tab = "delegaciones", int? delegacionId = null)
    {
        if (!EsMedios) return Forbid();
        if (desde == null && hasta == null && ModelState.IsValid)
        {
            hasta = DateOnly.FromDateTime(DateTime.Today);
            desde = hasta.Value.AddDays(1 - ArchivoPdfService.MaxDias);
        }
        ViewData["Title"] = "Archivo de PDF";
        ViewBag.Desde = desde?.ToString("yyyy-MM-dd");
        ViewBag.Hasta = hasta?.ToString("yyyy-MM-dd");
        ViewBag.Tab = tab == "consolidados" ? tab : "delegaciones";
        ViewBag.DelegacionId = delegacionId;
        var delegaciones = await archivos.DelegacionesAsync();
        ViewBag.Delegaciones = delegaciones;
        ViewBag.Almacenamiento = archivos.ObtenerAlmacenamiento();
        if (!desde.HasValue || !hasta.HasValue)
            ModelState.AddModelError("", "Ingresá las dos fechas del período.");
        else if (hasta < desde || hasta.Value.DayNumber - desde.Value.DayNumber >= ArchivoPdfService.MaxDias)
            ModelState.AddModelError("", "Seleccioná un período de hasta 15 días, contando ambas fechas.");
        bool consolidados = ViewBag.Tab == "consolidados";
        if (!consolidados && delegacionId.HasValue && !delegaciones.Any(d => d.Id == delegacionId))
            ModelState.AddModelError("", "Seleccioná una delegación válida.");
        if (!ModelState.IsValid || (!consolidados && !delegacionId.HasValue))
        {
            return View(new List<ArchivoPdf>());
        }
        return View(await archivos.ListarAsync(desde!.Value, hasta!.Value, delegacionId, consolidados));
    }

    [HttpGet]
    public async Task<IActionResult> Descargar(int id, bool sesion = false)
    {
        if (!EsMedios) return Forbid();
        var ruta = await archivos.ObtenerRutaAsync(id, sesion);
        if (ruta == null || !System.IO.File.Exists(ruta)) return NotFound();
        return PhysicalFile(ruta, "application/pdf", Path.GetFileName(ruta));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(ArchivoPdfService.MaxBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = ArchivoPdfService.MaxBytes + 1024 * 1024)]
    public async Task<IActionResult> Reemplazar(int id, string rutaEsperada, IFormFile? archivo, DateOnly? desde = null, DateOnly? hasta = null)
    {
        if (!EsMedios) return Forbid();
        try
        {
            if (archivo == null) throw new ArgumentException("Seleccioná el PDF modificado que querés subir.");
            await archivos.ReemplazarAsync(id, rutaEsperada, archivo, HttpContext.RequestAborted);
            await auditoria.RegistrarAsync(User.Identity?.Name ?? "", HttpContext.Connection.RemoteIpAddress?.ToString() ?? "",
                "sintesis", "POST", "reemplazar_pdf", new { id, rutaAnterior = rutaEsperada, archivo = Path.GetFileName(archivo.FileName) });
            TempData["Ok"] = "PDF reemplazado. Se utilizará en las descargas y los próximos envíos; el archivo anterior quedó conservado como respaldo.";
        }
        catch (ArgumentException ex) { TempData["Error"] = ex.Message; }
        catch (DbUpdateConcurrencyException) { TempData["Error"] = "Otra persona modificó la síntesis. Actualizá la página y volvé a intentar."; }
        return RedirectToAction(nameof(Index), new { tab = "consolidados", desde = desde?.ToString("yyyy-MM-dd"), hasta = hasta?.ToString("yyyy-MM-dd") });
    }
}
