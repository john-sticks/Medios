using System.Security.Cryptography;
using System.Text.Json;
using Medios.Security;
using Medios.Services.Respaldos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using Medios.Infrastructure.Middleware;

namespace Medios.Controllers;

[Route("Monitor/Retorno")]
[Authorize(Roles = "DESARROLLADOR")]
[HasPermission("VER_MONITOR")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ImportacionRetornoController(ImportacionRetornoService importacion,
    IDataProtectionProvider protection, ILogger<ImportacionRetornoController> logger,
    ContencionProduccionService contencion, RetornoProduccionUso uso) : Controller
{
    private const long Maximo = 512L * 1024 * 1024;
    private static string Directorio => Path.Combine(Path.GetTempPath(), "keryx-retorno-pendiente");
    private ITimeLimitedDataProtector Protector => protection.CreateProtector("Keryx.RetornoProduccion.Revision.v1").ToTimeLimitedDataProtector();
    private string Usuario => User.FindFirst("UsuarioReal")?.Value ?? "";
    private bool Habilitado => importacion.Habilitado && User.HasClaim("RolReal", "DESARROLLADOR") && Usuario.Length > 0;
    private sealed record Revision(string Id, string Usuario, string Huella, string HuellaRevision, string Origen);

    [HttpGet("")]
    public IActionResult Index() => Habilitado ? View() : NotFound();

    [HttpPost("Revisar"), ValidateAntiForgeryToken, RequestSizeLimit(Maximo + 1024 * 1024),
        RequestFormLimits(MultipartBodyLengthLimit = Maximo)]
    public async Task<IActionResult> Revisar(IFormFile? paquete, string? clave, CancellationToken ct)
    {
        if (!Habilitado) return NotFound();
        clave = ClavePermitida(clave);
        if (paquete is not { Length: > 0 and <= Maximo } || !ClaveValida(clave))
            return VistaError("Elegí el paquete cifrado. Para usar la clave, configurala en el servidor o ingresala por HTTPS.");
        Directory.CreateDirectory(Directorio);
        foreach (var anterior in Directory.EnumerateFiles(Directorio, "*.kbk"))
            if (System.IO.File.GetLastWriteTimeUtc(anterior) < DateTime.UtcNow.AddHours(-1))
                System.IO.File.Delete(anterior);
        var id = Guid.NewGuid().ToString("N");
        var ruta = Ruta(id);
        try
        {
            await using (var salida = new FileStream(ruta, FileMode.CreateNew))
            {
                RespaldoStore.Privado(ruta);
                await paquete.CopyToAsync(salida, ct);
            }
            var resumen = await importacion.RevisarAsync(ruta, clave!, ct);
            var revision = new Revision(id, Usuario, resumen.Huella, resumen.HuellaRevision, resumen.OrigenRespaldo);
            ViewData["Revision"] = Protector.Protect(JsonSerializer.Serialize(revision), TimeSpan.FromMinutes(30));
            return View(nameof(Index), resumen);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            System.IO.File.Delete(ruta);
            return Fallo(e);
        }
        catch (OperationCanceledException)
        {
            System.IO.File.Delete(ruta);
            throw;
        }
    }

    [HttpPost("Aplicar"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Aplicar(string? revision, string? clave, bool confirmar, bool confirmarCoincidencias, CancellationToken ct)
    {
        if (!Habilitado) return NotFound();
        clave = ClavePermitida(clave);
        if (!confirmar || !ClaveValida(clave) || string.IsNullOrEmpty(revision))
            return VistaError("Confirmá la importación e ingresá la clave para aplicar el paquete revisado.");
        try
        {
            var ticket = JsonSerializer.Deserialize<Revision>(Protector.Unprotect(revision));
            if (ticket == null || ticket.Usuario != Usuario || !Guid.TryParseExact(ticket.Id, "N", out _)
                || !System.IO.File.Exists(Ruta(ticket.Id)))
                return VistaError("La revisión ya no está disponible. Volvé a cargar el paquete.");
            var ruta = Ruta(ticket.Id);
            await uso.Operacion.WaitAsync(ct);
            ResumenRetorno resumen;
            try
            {
                uso.Bloquear();
                while (uso.HayActivas) await Task.Delay(100, ct);
                var previo = await contencion.CrearRespaldoPrevioAsync(ticket.Origen, ct);
                logger.LogInformation("Respaldo previo verificado antes de importar. ID {Id}, SHA-256 {Sha}", previo.RespaldoPrevioId, previo.RespaldoPrevioSha256);
                resumen = await importacion.AplicarAsync(ruta, clave!, ticket.Huella, ticket.HuellaRevision, Usuario, confirmarCoincidencias, ct,
                    previo.RespaldoPrevioId, previo.RespaldoPrevioSha256);
            }
            finally { uso.Liberar(); uso.Operacion.Release(); }
            try { System.IO.File.Delete(ruta); }
            catch (IOException) { logger.LogWarning("No se pudo retirar el paquete de retorno temporal."); }
            return View(nameof(Index), resumen);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return Fallo(e);
        }
    }

    private static string Ruta(string id) => Path.Combine(Directorio, id + ".kbk");
    private static bool ClaveValida(string? clave) => !string.IsNullOrWhiteSpace(clave) && clave.Length <= 512;
    private string? ClavePermitida(string? clave)
    {
        if (importacion.ClaveConfigurada != null) return importacion.ClaveConfigurada;
        try { return contencion.ClaveCifrado; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        { return Request.IsHttps ? clave : null; }
    }

    private IActionResult Fallo(Exception e)
    {
        logger.LogWarning("Importación de contingencia detenida ({Tipo}).", e.GetType().Name);
        return VistaError(e switch
        {
            InvalidOperationException => e.Message,
            CryptographicException => "La clave, el paquete o la revisión no son válidos.",
            MySqlException sql => $"La base rechazó la operación (MySQL {sql.Number}). Revisá producción y volvé a analizar el paquete.",
            _ => "No se pudo procesar el paquete. Revisá el archivo y los registros del servidor."
        });
    }

    private IActionResult VistaError(string mensaje)
    {
        ViewData["ErrorRetorno"] = mensaje;
        return View(nameof(Index));
    }
}
