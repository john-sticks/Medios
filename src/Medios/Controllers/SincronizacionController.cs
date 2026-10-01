using Microsoft.AspNetCore.Mvc;
using Medios.Security;
using Medios.Services;

namespace Medios.Controllers;

public class SincronizacionController : Controller
{
    private readonly SincronizacionService _sync;
    private readonly SintesisService _sintesis;
    private readonly AuditoriaService _auditoria;
    private readonly CapturaDriveService _captura;
    private readonly DelegacionService _delegService;

    public SincronizacionController(SincronizacionService sync, SintesisService sintesis,
        AuditoriaService auditoria, CapturaDriveService captura, DelegacionService delegService)
    {
        _sync = sync;
        _sintesis = sintesis;
        _auditoria = auditoria;
        _captura = captura;
        _delegService = delegService;
    }

    private string GetNombre() => User.Identity?.Name ?? "";
    private string GetIp() =>
        Request.Headers["X-Forwarded-For"].FirstOrDefault()
        ?? HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "";

    [HasPermission("VER_SINCRONIZACION")]
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "Sincronización";
        ViewBag.Root = await _sync.GetRootAsync();
        ViewBag.RootPorDefecto = SincronizacionService.RootPorDefecto;
        ViewBag.PuedeConfigurar = User.IsInRole("MEDIOS") || User.IsInRole("DESARROLLADOR");
        // Síntesis consolidadas (pendientes o informadas) disponibles para sincronizar
        var consolidadas = await _sintesis.GetConsolidasAsync();
        var informadas = await _sintesis.GetInformadasAsync();
        ViewBag.Sintesis = consolidadas.Concat(informadas)
            .OrderByDescending(s => s.Fecha)
            .ThenByDescending(s => s.FechaCreacion)
            .ToList();
        return View();
    }

    // Solo ADMINISTRADOR configura la carpeta raíz de sincronización
    [HasPermission("ADMINISTRAR_CONFIGURACION")]
    [HttpPost]
    public async Task<IActionResult> GuardarConfig(string? root)
    {
        await _sync.SetRootAsync(root);
        await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "sincronizacion", "POST", "config_root",
            new { root });
        TempData["Ok"] = "Carpeta de sincronización guardada.";
        return RedirectToAction(nameof(Index));
    }

    [HasPermission("VER_SINCRONIZACION")]
    [HttpPost]
    public async Task<IActionResult> Generar(int sintesisId)
    {
        var r = await _sync.GenerarSintesisAsync(sintesisId);
        if (r.Ok)
        {
            await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "sincronizacion", "POST", "generar",
                new { sintesisId, r.Carpeta, r.NotasGeneradas });
            TempData["Ok"] = $"Estructura generada en «{r.Carpeta}» — {r.NotasGeneradas} nota(s), {r.ImagenesCopiadas} imagen(es).";
        }
        else
        {
            TempData["Error"] = r.Error;
        }
        return RedirectToAction(nameof(Index));
    }

    // ── Captura de contingencia (Drive → Sistema) ─────────────────────

    [HasPermission("VER_SINCRONIZACION")]
    [HttpGet]
    public async Task<IActionResult> Capturar(int? delegacionId)
    {
        ViewData["Title"] = "Capturar desde Drive";
        ViewBag.PuedeConfigurar = User.IsInRole("MEDIOS") || User.IsInRole("DESARROLLADOR");
        ViewBag.SaJsonPath = await _captura.GetSaJsonPathAsync();
        ViewBag.Delegaciones = await _delegService.GetTodasAsync(soloActivas: true);
        ViewBag.DelegacionId = delegacionId;

        if (delegacionId.HasValue)
        {
            try
            {
                var (notas, sintesis) = await _captura.PreviewAsync(delegacionId.Value);
                ViewBag.PreviewNotas = notas;
                ViewBag.PreviewSintesis = sintesis;
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }
        }
        return View();
    }

    // Solo ADMINISTRADOR define la ruta del archivo de credenciales (Service Account)
    [HasPermission("ADMINISTRAR_CONFIGURACION")]
    [HttpPost]
    public async Task<IActionResult> GuardarSaJson(string? saJsonPath)
    {
        await _captura.SetSaJsonPathAsync(saJsonPath);
        await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "sincronizacion", "POST", "config_sajson", null);
        TempData["Ok"] = "Credencial de Google guardada.";
        return RedirectToAction(nameof(Capturar));
    }

    [HasPermission("VER_SINCRONIZACION")]
    [HttpPost]
    public async Task<IActionResult> CapturarConfirmar(int delegacionId)
    {
        var r = await _captura.CapturarAsync(delegacionId, GetNombre());
        if (r.Ok)
        {
            await _auditoria.RegistrarAsync(GetNombre(), GetIp(), "sincronizacion", "POST", "capturar",
                new { delegacionId, r.NotasImportadas, r.SintesisImportadas });
            TempData["Ok"] = $"Captura realizada — {r.NotasImportadas} nota(s) en {r.SesionesCreadas} sesión(es), {r.SintesisImportadas} síntesis.";
            if (r.Avisos.Any())
                TempData["Aviso"] = string.Join(" · ", r.Avisos.Take(10));
        }
        else
        {
            TempData["Error"] = r.Error;
        }
        return RedirectToAction(nameof(Capturar), new { delegacionId });
    }
}
