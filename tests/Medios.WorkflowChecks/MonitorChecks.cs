using System.Security.Claims;
using System.Text.Json;
using System.Xml.Linq;
using Medios.Controllers;
using Medios.Entities;
using Medios.Infrastructure.Data;
using Medios.Models;
using Medios.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

internal static class MonitorChecks
{
    public static async Task<int> RunAsync()
    {
        var count = 0;
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException("Monitor: " + text); count++; }
        var inicio = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        MonitorMuestra M(int dia, long bytes, long total = 1000) => new(inicio.AddDays(dia), bytes, bytes, bytes, 10 + dia, 20 + dia, [new("PDF", total, total - bytes)]);
        var historia = new[] { M(0, 100), M(1, 200), M(2, 300) };
        Check(MonitorEspacioModel.CrecimientoDiario(historia.Take(1), m => m.BaseDatos) == null, "No inventa tendencia con una captura");
        Check(MonitorEspacioModel.CrecimientoDiario(historia.Take(2), m => m.BaseDatos) == null, "Exige tres días distintos");
        Check(MonitorEspacioModel.CrecimientoDiario(historia, m => m.BaseDatos) == 100, "Calcula bytes diarios según tiempo transcurrido");
        Check(MonitorEspacioModel.CrecimientoDiario(historia.Select(m => m with { BaseDatos = null }), m => m.BaseDatos) == null, "Falta de medición no se interpreta como cero");
        Check(MonitorEspacioModel.DiasHasta80(historia, new("PDF", 1000, 700)) == 5, "Proyecta el umbral 80 con espacio y ritmo reales");
        Check(MonitorEspacioModel.DiasHasta80(historia, new("PDF", 2000, 1700)) == null, "Ampliar el disco invalida la tendencia anterior");
        Check(MonitorEspacioModel.DiasHasta80(historia.Reverse().Select((m, i) => m with { FechaUtc = inicio.AddDays(i) }), new("PDF", 1000, 900)) == null, "Una caída no genera fecha de llenado");
        Check(MonitorEspacioModel.DiasHasta80([], new("PDF", 1000, 100)) == 0, "Advierte ocupación actual aunque falte historial");
        var diario = MonitorEspacioService.Incorporar(historia, M(2, 350));
        Check(diario.Count == 3 && diario[^1].BaseDatos == 350, "Reemplaza la lectura del mismo día");
        Check(MonitorEspacioService.Incorporar(Enumerable.Range(0, 100).Select(i => M(i, i)), M(101, 1)).Count == 60, "Limita el historial a 60 días");
        Check(new MonitorDisco("PDF", 1000, 200).Estado == "Advertencia" && new MonitorDisco("PDF", 1000, 100).Estado == "Crítico", "Umbrales 80 y 90");
        Check(MonitorEspacioModel.HoraLocal(inicio).Hour == 9, "Presenta horario de Buenos Aires");
        Check(MonitorEspacioModel.Tamaño(null) == "Sin datos", "No oculta errores como cero bytes");

        var root = Path.Combine(Path.GetTempPath(), "medios-monitor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "respaldos"));
        try
        {
            File.WriteAllBytes(Path.Combine(root, "vigente.pdf"), new byte[100]);
            File.WriteAllBytes(Path.Combine(root, "respaldos", "anterior.pdf"), new byte[200]);
            File.WriteAllBytes(Path.Combine(root, "otro.txt"), new byte[500]);
            var pdf = MonitorEspacioProbe.MedirCarpeta(root, true);
            Check(pdf.Bytes == 300 && pdf.Cantidad == 2 && pdf.Respaldos == 200 && pdf.Completo, "Cuenta PDF y respaldo una sola vez");
            Check(MonitorEspacioProbe.MedirCarpeta(root, false).Bytes == 800, "Cuenta todos los archivos en carpeta de imágenes");
            Check(MonitorEspacioProbe.MedirCarpeta(Path.Combine(root, "ausente"), true).Cantidad == 0, "Una carpeta aún no creada es vacía");
            Check(!MonitorEspacioProbe.MedirCarpeta(Path.Combine(root, "otro.txt"), true).Completo, "Una ruta que no es carpeta no se informa como vacía");
            var disco = MonitorEspacioProbe.LeerDisco("Prueba", root);
            Check(disco is { Total: > 0, Disponible: >= 0 } && disco.Total >= disco.Disponible, "Obtiene el volumen real de una ruta, incluida la raíz");
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            try { MonitorEspacioProbe.MedirCarpeta(root, true, cancel.Token); throw new InvalidOperationException("No canceló"); }
            catch (OperationCanceledException) { Check(true, "El recorrido responde a cancelación"); }
        }
        finally
        {
            if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Ruta de prueba inválida");
            Directory.Delete(root, true);
        }

        var factory = new MonitorFactory();
        var actual = new MonitorMuestra(DateTime.UtcNow, 1000, 200, 300, 10, 20, [new("PDF", 1_000_000, 800_000)]);
        var probe = new MonitorProbe(new MonitorLectura { Muestra = actual, PromedioTextoVersion = 500, VersionesMuestreadas = 20 });
        var service = new MonitorEspacioService(factory, probe, new MonitorEspacioCache(), NullLogger<MonitorEspacioService>.Instance);
        using (var db = factory.Create()) { db.Configuraciones.Add(new Configuracion { Clave = "otra_configuracion", Valor = "conservar" }); await db.SaveChangesAsync(); }
        var resultado = await service.ObtenerAsync();
        await service.ObtenerAsync();
        Check(probe.Llamadas == 1, "Caché evita consultar DB y recorrer archivos en cada apertura");
        Check(resultado.HistorialGuardado && resultado.Historial.Count == 1, "Persiste primera medición sin migración");
        using (var db = factory.Create())
        {
            Check((await db.Configuraciones.FindAsync("otra_configuracion"))!.Valor == "conservar", "No modifica otras configuraciones");
            var json = (await db.Configuraciones.FindAsync(MonitorEspacioService.ClaveHistorial))!.Valor!;
            Check(JsonSerializer.Deserialize<List<MonitorMuestra>>(json)![0].BaseDatos == 1000, "Historial recuperable tras reinicio");
        }
        var despuesReinicio = new MonitorEspacioService(factory, probe, new MonitorEspacioCache(), NullLogger<MonitorEspacioService>.Instance);
        Check((await despuesReinicio.ObtenerAsync()).Historial.Count == 1, "Reiniciar no duplica la medición diaria");
        using (var db = factory.Create()) { (await db.Configuraciones.FindAsync(MonitorEspacioService.ClaveHistorial))!.Valor = "[null]"; await db.SaveChangesAsync(); }
        var conHistorialInvalido = new MonitorEspacioService(factory, probe, new MonitorEspacioCache(), NullLogger<MonitorEspacioService>.Instance);
        var recuperado = await conHistorialInvalido.ObtenerAsync();
        Check(!recuperado.HistorialGuardado && recuperado.Historial.Count == 0 && recuperado.Lectura.Muestra.BaseDatos == 1000, "Historial dañado no impide ver mediciones actuales");
        using (var db = factory.Create()) Check((await db.Configuraciones.FindAsync(MonitorEspacioService.ClaveHistorial))!.Valor == "[null]", "No sobrescribe historial dañado");
        var modelo = new MonitorEspacioModel { Resultado = resultado, NotasPorDia = 100, VersionesPorNota = 2 };
        Check(modelo.TextoProyectado(30) == 3_000_000 && modelo.TextoProyectado(365) == 36_500_000, "Escenario incorpora notas, versiones y bytes de texto");
        var sinTexto = new MonitorEspacioModel { Resultado = resultado with { Lectura = new MonitorLectura { Muestra = actual } } };
        Check(sinTexto.TextoProyectado(30) == null, "Sin muestra no promete crecimiento de texto");
        var controller = new MonitorController(service) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        foreach (var rol in new[] { "MEDIOS", "DELEGACION" })
        {
            controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, rol)], "test"));
            Check(await controller.Index() is ForbidResult, "Bloquea acceso directo a " + rol);
        }
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "DESARROLLADOR")], "test"));
        Check(await controller.Index() is ViewResult, "DESARROLLADOR puede abrir");
        var invalido = (ViewResult)await controller.Index(-1, 200);
        Check(!controller.ModelState.IsValid && ((MonitorEspacioModel)invalido.Model!).NotasPorDia == 100, "Valida parámetros de escenario en servidor");
        Check(typeof(MonitorController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single().Roles == "DESARROLLADOR", "Autorización por rol declarada en controlador");
        var source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Medios/Config/Permisos"));
        foreach (var nivel in new[] { "10", "15", "30" })
        {
            var xml = XDocument.Load(Path.Combine(source, "permisos_" + nivel + ".xml"));
            Check(xml.Descendants("page").Any(e => (string?)e.Attribute("title") == "Monitor") == (nivel == "10"), "Menú exclusivo del nivel 10: " + nivel);
            Check(xml.Descendants("permission").Any(e => (string?)e.Attribute("name") == "VER_MONITOR") == (nivel == "10"), "Permiso exclusivo del nivel 10: " + nivel);
        }
        return count;
    }

    private sealed class MonitorFactory : IMediosDbContextFactory
    {
        private readonly string nombre = Guid.NewGuid().ToString();
        public MediosDbContext Create() => new(new DbContextOptionsBuilder<MediosDbContext>().UseInMemoryDatabase(nombre).Options);
    }
    private sealed class MonitorProbe(MonitorLectura lectura) : IMonitorEspacioProbe
    {
        public int Llamadas { get; private set; }
        public Task<MonitorLectura> MedirAsync(CancellationToken ct) { Llamadas++; return Task.FromResult(lectura); }
    }
}
