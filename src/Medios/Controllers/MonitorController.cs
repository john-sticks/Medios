using Medios.Services.Respaldos;
using Medios.Models;
using Medios.Security;
using Medios.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Medios.Controllers;

[Authorize(Roles = "DESARROLLADOR")]
[HasPermission("VER_MONITOR")]
public class MonitorController(MonitorEspacioService monitor, ModoOperativoStore? modo = null) : Controller
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Index(int notasPorDia = 100, decimal versionesPorNota = 2, CancellationToken ct = default)
    {
        if (!User.IsInRole("DESARROLLADOR")) return Forbid();
        ViewData["Title"] = "Monitor de espacio";
        if (notasPorDia is < 0 or > 100_000 || versionesPorNota is < 1 or > 100 || !ModelState.IsValid)
        {
            ModelState.AddModelError("", "Ingresá entre 0 y 100.000 notas diarias y entre 1 y 100 versiones por nota.");
            notasPorDia = 100;
            versionesPorNota = 2;
        }
        return View(new MonitorEspacioModel
        {
            Resultado = await monitor.ObtenerAsync(ct), NotasPorDia = notasPorDia, VersionesPorNota = versionesPorNota
        });
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Modo(bool activar)
    {
        if(!User.IsInRole("DESARROLLADOR") || modo==null)return Forbid();
        await modo.GuardarAsync(activar,User.FindFirst("UsuarioReal")?.Value??User.Identity?.Name??"");
        return RedirectToAction(nameof(Index));
    }
}
