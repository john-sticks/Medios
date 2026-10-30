using System.Text.Json;
using Medios.Entities;
using Medios.Infrastructure.Data;
using Medios.Models;
using Microsoft.EntityFrameworkCore;

namespace Medios.Services;

public sealed class MonitorEspacioCache
{
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public MonitorResultado? Resultado { get; set; }
}

public class MonitorEspacioService(IMediosDbContextFactory factory, IMonitorEspacioProbe probe,
    MonitorEspacioCache cache, ILogger<MonitorEspacioService> logger, Medios.Services.Respaldos.DatosTestContext? datos = null)
{
    public const string ClaveHistorial = "monitor_espacio_historial_v1";
    public static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(10);

    public async Task<MonitorResultado> ObtenerAsync(CancellationToken ct = default)
    {
        await cache.Gate.WaitAsync(ct);
        try
        {
            if (cache.Resultado != null && DateTime.UtcNow - cache.Resultado.Lectura.Muestra.FechaUtc < Intervalo)
                return cache.Resultado;
            var lectura = await probe.MedirAsync(ct);
            var historial = new List<MonitorMuestra>();
            var guardado = false;
            try
            {
                await using var db = factory.Create();
                var fila = await db.Configuraciones.SingleOrDefaultAsync(c => c.Clave == ClaveHistorial, ct);
                if (!string.IsNullOrWhiteSpace(fila?.Valor))
                {
                    try
                    {
                        historial = JsonSerializer.Deserialize<List<MonitorMuestra>>(fila.Valor) ?? [];
                        if (historial.Any(m => m == null || m.FechaUtc == default || m.Discos == null || m.Discos.Any(d => d == null)))
                            throw new JsonException("Mediciones incompletas");
                    }
                    catch (JsonException)
                    {
                        lectura.Avisos.Add("El historial guardado no pudo interpretarse. No se reemplazó; se muestran las mediciones actuales.");
                        return cache.Resultado = new(lectura, [], false);
                    }
                }
                historial = Incorporar(historial, lectura.Muestra);
                var json = JsonSerializer.Serialize(historial);
                // Cabe holgadamente en configuracion.valor TEXT; no acumular sin límite.
                if (System.Text.Encoding.UTF8.GetByteCount(json) > 60_000) throw new InvalidOperationException("Historial demasiado grande");
                if (fila == null) db.Configuraciones.Add(new Configuracion { Clave = ClaveHistorial, Valor = json });
                else fila.Valor = json;
                if (datos?.RetornoSoloLectura != true)
                {
                    await db.SaveChangesAsync(ct);
                    guardado = true;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Monitor: no se pudo guardar el historial ({Tipo}).", ex.GetType().Name);
                lectura.Avisos.Add("No se pudo guardar la medición. Revisá los permisos de configuración de la base; las métricas actuales siguen disponibles.");
                historial = Incorporar(historial, lectura.Muestra);
            }
            return cache.Resultado = new(lectura, historial, guardado);
        }
        finally { cache.Gate.Release(); }
    }

    public static List<MonitorMuestra> Incorporar(IEnumerable<MonitorMuestra> historial, MonitorMuestra muestra) =>
        historial.Where(m => m.FechaUtc.Date != muestra.FechaUtc.Date && m.FechaUtc <= muestra.FechaUtc)
            .Append(muestra).OrderBy(m => m.FechaUtc).TakeLast(60).ToList();
}

public class MonitorEspacioWorker(IServiceScopeFactory scopes, ILogger<MonitorEspacioWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Yield antes de consultar MySQL o recorrer archivos para no demorar el arranque.
        await Task.Yield();
        using var timer = new PeriodicTimer(MonitorEspacioService.Intervalo);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<MonitorEspacioService>().ObtenerAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Monitor: medición pendiente ({Tipo}).", ex.GetType().Name); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
