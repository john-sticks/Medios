using Medios.Security;
using Medios.Services.Respaldos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Medios.Controllers;

[Authorize(Roles = "DESARROLLADOR")]
[HasPermission("VER_MONITOR")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class BackupsContingenciaController(ImportacionRetornoService importacion, RespaldoStore respaldos) : Controller
{
    [HttpGet]
    public IActionResult Index(string? seccion = null)
    {
        if (!User.IsInRole("DESARROLLADOR") || !User.HasClaim("RolReal", "DESARROLLADOR")) return Forbid();
        if (respaldos.Habilitado) return RedirectToAction("Index", "Respaldos", new { seccion });
        if (!importacion.Habilitado) return NotFound();
        ViewData["Title"] = "Backups y Contingencia";
        return View();
    }
}
