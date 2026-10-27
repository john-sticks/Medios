namespace Medios.Models;

public record MonitorDisco(string Ambito, long Total, long Disponible)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public long Usado => Math.Max(0, Total - Disponible);
    [System.Text.Json.Serialization.JsonIgnore]
    public double Porcentaje => Total > 0 ? 100d * Usado / Total : 0;
    [System.Text.Json.Serialization.JsonIgnore]
    public string Estado => Porcentaje >= 90 ? "Crítico" : Porcentaje >= 80 ? "Advertencia" : "Con margen";
}

public record MonitorTabla(string Nombre, long Datos, long Indices)
{
    public long Total => Datos + Indices;
}

public record MonitorArchivos(long Bytes, long Cantidad, long Respaldos, bool Completo);
public record MonitorMuestra(DateTime FechaUtc, long? BaseDatos, long? Pdf, long? Imagenes,
    long? Notas, long? Versiones, List<MonitorDisco> Discos);

public class MonitorLectura
{
    public required MonitorMuestra Muestra { get; init; }
    public List<MonitorTabla> Tablas { get; init; } = [];
    public MonitorArchivos? Pdf { get; init; }
    public MonitorArchivos? Imagenes { get; init; }
    public double? PromedioTextoVersion { get; init; }
    public long VersionesMuestreadas { get; init; }
    public List<string> Avisos { get; init; } = [];
}

public record MonitorResultado(MonitorLectura Lectura, List<MonitorMuestra> Historial, bool HistorialGuardado);

public class MonitorEspacioModel
{
    public required MonitorResultado Resultado { get; init; }
    public int NotasPorDia { get; init; } = 100;
    public decimal VersionesPorNota { get; init; } = 2;
    public long? TextoProyectado(int dias) => Resultado.Lectura.PromedioTextoVersion is double promedio
        ? (long)((decimal)promedio * NotasPorDia * VersionesPorNota * dias) : null;

    // Tres días distintos y al menos 48 horas; no extrapolar una sola captura.
    public static double? CrecimientoDiario(IEnumerable<MonitorMuestra> historial, Func<MonitorMuestra, long?> valor)
    {
        var validas = historial.Where(m => valor(m).HasValue).OrderBy(m => m.FechaUtc).TakeLast(30).ToList();
        if (validas.Select(m => m.FechaUtc.Date).Distinct().Count() < 3) return null;
        var dias = (validas[^1].FechaUtc - validas[0].FechaUtc).TotalDays;
        if (dias < 2) return null;
        return (valor(validas[^1])!.Value - valor(validas[0])!.Value) / dias;
    }

    public static double? DiasHasta80(IEnumerable<MonitorMuestra> historial, MonitorDisco disco)
    {
        if (disco.Porcentaje >= 80) return 0;
        // Una ampliación del volumen invalida la tendencia previa de ese volumen.
        long? Uso(MonitorMuestra m)
        {
            var d = m.Discos.FirstOrDefault(d => d.Ambito == disco.Ambito && d.Total == disco.Total);
            return d?.Usado;
        }
        var crecimiento = CrecimientoDiario(historial, Uso);
        return crecimiento > 0 ? (disco.Total * .8 - disco.Usado) / crecimiento : null;
    }

    public static string Tamaño(long? bytes)
    {
        if (!bytes.HasValue) return "Sin datos";
        var valor = (double)bytes.Value;
        var unidades = new[] { "B", "KB", "MB", "GB", "TB" };
        var indice = 0;
        while (Math.Abs(valor) >= 1000 && indice < unidades.Length - 1) { valor /= 1000; indice++; }
        return valor.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("es-AR")) + " " + unidades[indice];
    }

    public static DateTime HoraLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc),
            TimeZoneInfo.FindSystemTimeZoneById("America/Argentina/Buenos_Aires"));
}
