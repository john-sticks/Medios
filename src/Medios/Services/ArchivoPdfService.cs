using Medios.Services.Respaldos;
using System.Globalization;
using System.Text;
using Medios.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Medios.Services;

public record ArchivoPdf(int Id, bool EsSesion, int? DelegacionId, string Delegacion,
    DateOnly Fecha, string Tipo, string Estado, string Ruta, DateTime? FechaGeneracion,
    bool Disponible)
{
    public bool Consolidado => !EsSesion && DelegacionId == null;
    public DateTime? FechaCargaUtc => ArchivoPdfService.FechaCargaUtc(Ruta);
    public string Origen => FechaCargaUtc.HasValue ? "Subido manualmente" : "Generado por el sistema";
    public DateTime? FechaArchivo => FechaCargaUtc?.ToLocalTime() ?? FechaGeneracion;
}

public class ArchivoPdfService(IMediosDbContextFactory factory, IWebHostEnvironment env, DatosTestEnvironment? rutas = null)
{
    public const int MaxDias = 15;
    public const int MaxBytes = 20 * 1024 * 1024;
    private const string MarcaCarga = "_subido_";
    private const string FormatoFecha = "yyyyMMddHHmmssfff";

    // La procedencia viaja con el archivo y PDFPath; no requiere migrar la DB.
    public static DateTime? FechaCargaUtc(string? ruta)
    {
        var nombre = Path.GetFileName(ruta);
        var inicio = nombre?.LastIndexOf(MarcaCarga, StringComparison.Ordinal) ?? -1;
        if (inicio < 0) return null;
        var fecha = nombre![(inicio + MarcaCarga.Length)..].Split('_')[0];
        return DateTime.TryParseExact(fecha, FormatoFecha, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var valor) ? valor : null;
    }

    public string? RutaFisica(string? ruta)
    {
        if (string.IsNullOrWhiteSpace(ruta) || !ruta.StartsWith("/pdf/", StringComparison.Ordinal)) return null;
        var nombre = ruta[5..];
        if (nombre.Contains('/') || nombre.Contains('\\') || !nombre.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) return null;
        return Path.Combine((rutas ?? env).WebRootPath, "pdf", nombre);
    }

    public async Task<List<ArchivoPdf>> ListarAsync(DateOnly desde, DateOnly hasta, int? delegacionId = null, bool consolidados = false)
    {
        if (hasta < desde || hasta.DayNumber - desde.DayNumber >= MaxDias)
            throw new ArgumentException("Seleccioná un período de hasta 15 días, contando ambas fechas.");
        if (!consolidados && (!delegacionId.HasValue || delegacionId.Value <= 0))
            throw new ArgumentException("Seleccioná una delegación para consultar sus PDF.");
        using var db = factory.Create();
        var sintesis = db.Sintesis.AsNoTracking().Where(s => s.PDFPath != null && s.PDFPath != ""
            && s.Fecha >= desde && s.Fecha <= hasta);
        sintesis = consolidados ? sintesis.Where(s => s.DelegacionId == null)
            : sintesis.Where(s => s.DelegacionId == delegacionId);
        var documentos = await sintesis.Select(s => new ArchivoPdf(s.Id, false, s.DelegacionId,
            s.Delegacion != null ? s.Delegacion.Nombre : "Consolidados", s.Fecha, s.SintesisOrigenId.HasValue ? s.Tipo + " (filtrada)" : s.Tipo, s.Estado,
            s.PDFPath!, s.FechaGeneracion, false)).ToListAsync();
        if (!consolidados)
        {
            documentos.AddRange(await db.SesionesPrensas.AsNoTracking()
                .Where(s => s.PDFPath != null && s.PDFPath != "" && s.DelegacionId == delegacionId
                    && s.Fecha >= desde && s.Fecha <= hasta)
                .Select(s => new ArchivoPdf(s.Id, true, s.DelegacionId,
                    s.Delegacion != null ? s.Delegacion.Nombre : "Sin delegación", s.Fecha, s.Turno, s.Estado,
                    s.PDFPath!, s.FechaGeneracionPdf, false)).ToListAsync());
        }
        return documentos.DistinctBy(d => d.Ruta).Select(d => d with
            { Disponible = File.Exists(RutaFisica(d.Ruta)) })
            .OrderByDescending(d => d.Fecha).ThenByDescending(d => d.FechaArchivo).ThenByDescending(d => d.Id).ToList();
    }

    public async Task<List<DelegacionPdf>> DelegacionesAsync()
    {
        using var db = factory.Create();
        return await db.Delegaciones.AsNoTracking().OrderBy(d => d.Nombre)
            .Select(d => new DelegacionPdf(d.Id, d.Nombre)).ToListAsync();
    }

    public AlmacenamientoPdf ObtenerAlmacenamiento()
    {
        var carpeta = Path.Combine((rutas ?? env).WebRootPath, "pdf");
        long bytes = 0;
        var cantidad = 0;
        DateTime? desde = null, hasta = null;
        if (!Directory.Exists(carpeta)) return new(bytes, cantidad, desde, hasta);
        var opciones = new EnumerationOptions
        {
            RecurseSubdirectories = true, MatchCasing = MatchCasing.CaseInsensitive,
            AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false
        };
        foreach (var ruta in Directory.EnumerateFiles(carpeta, "*.pdf", opciones))
        {
            var archivo = new FileInfo(ruta);
            // Una reversión puede eliminar un PDF mientras se recorre la carpeta.
            try
            {
                var longitud = archivo.Length;
                var fecha = archivo.LastWriteTimeUtc;
                bytes += longitud;
                cantidad++;
                if (desde == null || fecha < desde) desde = fecha;
                if (hasta == null || fecha > hasta) hasta = fecha;
            }
            catch (FileNotFoundException) { }
        }
        return new(bytes, cantidad, desde, hasta);
    }

    public async Task<string?> ObtenerRutaAsync(int id, bool sesion)
    {
        using var db = factory.Create();
        var ruta = sesion
            ? await db.SesionesPrensas.Where(s => s.Id == id).Select(s => s.PDFPath).SingleOrDefaultAsync()
            : await db.Sintesis.Where(s => s.Id == id).Select(s => s.PDFPath).SingleOrDefaultAsync();
        return RutaFisica(ruta);
    }

    public async Task ReemplazarAsync(int id, string rutaEsperada, IFormFile archivo, CancellationToken ct = default)
    {
        if (archivo.Length <= 0 || archivo.Length > MaxBytes
            || !Path.GetExtension(archivo.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Seleccioná un PDF de hasta 20 MB.");

        await using var contenido = new MemoryStream();
        await using (var entrada = archivo.OpenReadStream())
        {
            var buffer = new byte[81920];
            int leidos;
            while ((leidos = await entrada.ReadAsync(buffer, ct)) > 0)
            {
                if (contenido.Length + leidos > MaxBytes) throw new ArgumentException("El PDF supera los 20 MB.");
                await contenido.WriteAsync(buffer.AsMemory(0, leidos), ct);
            }
        }
        var bytes = contenido.ToArray();
        if (bytes.Length < 12 || !bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8)
            || !Encoding.ASCII.GetString(bytes, Math.Max(0, bytes.Length - 1024), Math.Min(bytes.Length, 1024)).Contains("%%EOF"))
            throw new ArgumentException("El archivo no tiene el formato de un PDF. Exportalo nuevamente como PDF.");

        using var db = factory.Create();
        var sintesis = await db.Sintesis.SingleOrDefaultAsync(s => s.Id == id, ct);
        if (sintesis == null || sintesis.DelegacionId != null || string.IsNullOrEmpty(sintesis.PDFPath))
            throw new ArgumentException("Solo se puede reemplazar el PDF de una síntesis consolidada ya generada.");
        if (sintesis.Estado == "Informando")
            throw new ArgumentException("La síntesis se está informando. Esperá a que finalice antes de reemplazar el PDF.");
        if (sintesis.PDFPath != rutaEsperada)
            throw new ArgumentException("El PDF cambió desde que abriste la página. Actualizala antes de reemplazarlo.");

        var nombre = $"sintesis_{id}{MarcaCarga}{DateTime.UtcNow.ToString(FormatoFecha, CultureInfo.InvariantCulture)}_{Guid.NewGuid():N}.pdf";
        var rutaNueva = "/pdf/" + nombre;
        var destino = RutaFisica(rutaNueva)!;
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        var anterior = RutaFisica(sintesis.PDFPath);
        if (File.Exists(anterior))
        {
            var respaldos = Path.Combine((rutas ?? env).WebRootPath, "pdf", "respaldos");
            Directory.CreateDirectory(respaldos);
            File.Copy(anterior, Path.Combine(respaldos, $"sintesis_{id}_{Guid.NewGuid():N}.pdf"));
        }
        try
        {
            // Nombre único: el archivo anterior queda conservado, también ante cargas simultáneas.
            await File.WriteAllBytesAsync(destino, bytes, ct);
            sintesis.PDFPath = rutaNueva;
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            if (File.Exists(destino)) File.Delete(destino);
            throw;
        }
    }
}

public record DelegacionPdf(int Id, string Nombre);
public record AlmacenamientoPdf(long Bytes, int Cantidad, DateTime? DesdeUtc, DateTime? HastaUtc)
{
    public decimal Megabytes => Bytes / 1_000_000m;
}
