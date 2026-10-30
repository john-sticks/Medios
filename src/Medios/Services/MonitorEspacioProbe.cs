using Medios.Services.Respaldos;
using System.Data.Common;
using Medios.Infrastructure.Data;
using Medios.Models;
using Microsoft.EntityFrameworkCore;

namespace Medios.Services;

public interface IMonitorEspacioProbe
{
    Task<MonitorLectura> MedirAsync(CancellationToken ct);
}

public class MonitorEspacioProbe(IMediosDbContextFactory factory, IWebHostEnvironment env,
    ILogger<MonitorEspacioProbe> logger, DatosTestEnvironment? rutas = null) : IMonitorEspacioProbe
{
    public async Task<MonitorLectura> MedirAsync(CancellationToken ct)
    {
        var avisos = new List<string>();
        var tablas = new List<MonitorTabla>();
        long? notas = null, versiones = null, baseDatos = null;
        double? promedioTexto = null;
        long muestraTexto = 0;
        try
        {
            await using var db = factory.Create();
            await db.Database.OpenConnectionAsync(ct);
            var conn = db.Database.GetDbConnection();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT TABLE_NAME, COALESCE(DATA_LENGTH,0), COALESCE(INDEX_LENGTH,0) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_TYPE='BASE TABLE' ORDER BY DATA_LENGTH+INDEX_LENGTH DESC";
                cmd.CommandTimeout = 15;
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                    tablas.Add(new MonitorTabla(r.GetString(0), Convert.ToInt64(r.GetValue(1)), Convert.ToInt64(r.GetValue(2))));
            }
            baseDatos = tablas.Count > 0 ? tablas.Sum(t => t.Total) : null;
            notas = await ContarAsync(conn, "SELECT COUNT(*) FROM notas_prensa", ct);
            versiones = await ContarAsync(conn, "SELECT COUNT(*) FROM notas_prensa_versiones", ct);
            await using var texto = conn.CreateCommand();
            texto.CommandText = "SELECT COUNT(*), AVG(OCTET_LENGTH(Texto)+COALESCE(OCTET_LENGTH(Sintesis),0)) FROM (SELECT Texto,Sintesis FROM notas_prensa_versiones ORDER BY Id DESC LIMIT 200) v";
            texto.CommandTimeout = 15;
            await using var reader = await texto.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                muestraTexto = Convert.ToInt64(reader.GetValue(0));
                if (!reader.IsDBNull(1)) promedioTexto = Convert.ToDouble(reader.GetValue(1));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // No publicar mensajes del motor ni conexiones en la pantalla.
            logger.LogWarning("Monitor: lectura de base de datos incompleta ({Tipo}).", ex.GetType().Name);
            avisos.Add("No se pudieron obtener todas las métricas de la base de datos. La aplicación continúa funcionando.");
        }

        var pdfPath = Path.Combine((rutas ?? env).WebRootPath, "pdf");
        var imagenesPath = Path.Combine((rutas ?? env).WebRootPath, "uploads", "notas");
        var pdf = MedirCarpeta(pdfPath, true, ct);
        var imagenes = MedirCarpeta(imagenesPath, false, ct);
        if (!pdf.Completo || !imagenes.Completo)
            avisos.Add("Alguna carpeta o archivo no pudo leerse; los tamaños de archivos son parciales y no se usan para calcular tendencias.");
        var discos = new List<MonitorDisco>();
        foreach (var item in new[] { ("Aplicación", env.ContentRootPath), ("PDF", pdfPath), ("Imágenes", imagenesPath) })
        {
            var disco = LeerDisco(item.Item1, item.Item2);
            if (disco != null) discos.Add(disco);
            else avisos.Add("No se pudo determinar la capacidad del volumen de " + item.Item1.ToLowerInvariant() + ".");
        }
        return new MonitorLectura
        {
            Muestra = new(DateTime.UtcNow, baseDatos, pdf.Completo ? pdf.Bytes : null,
                imagenes.Completo ? imagenes.Bytes : null, notas, versiones, discos),
            Tablas = tablas, Pdf = pdf, Imagenes = imagenes, PromedioTextoVersion = promedioTexto,
            VersionesMuestreadas = muestraTexto, Avisos = avisos
        };
    }

    private static async Task<long> ContarAsync(DbConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 15;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    public static MonitorArchivos MedirCarpeta(string path, bool soloPdf, CancellationToken ct = default)
    {
        try
        {
            var atributos = File.GetAttributes(path);
            if ((atributos & FileAttributes.Directory) == 0 || (atributos & FileAttributes.ReparsePoint) != 0)
                return new(0, 0, 0, false);
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or FileNotFoundException) { return new(0, 0, 0, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new(0, 0, 0, false); }
        long bytes = 0, cantidad = 0, respaldos = 0;
        var completo = true;
        var pendientes = new Stack<string>();
        pendientes.Push(path);
        while (pendientes.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var actual = pendientes.Pop();
            try
            {
                foreach (var archivo in new DirectoryInfo(actual).EnumerateFileSystemInfos())
                {
                    ct.ThrowIfCancellationRequested();
                    if ((archivo.Attributes & FileAttributes.ReparsePoint) != 0) { completo = false; continue; }
                    if (archivo is DirectoryInfo) pendientes.Push(archivo.FullName);
                    else if (archivo is FileInfo f && (!soloPdf || f.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)))
                    {
                        bytes += f.Length;
                        cantidad++;
                        if (Path.GetRelativePath(path, f.FullName).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            .Any(p => p.Equals("respaldos", StringComparison.OrdinalIgnoreCase))) respaldos += f.Length;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { completo = false; }
        }
        return new(bytes, cantidad, respaldos, completo);
    }

    public static MonitorDisco? LeerDisco(string ambito, string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            // Los bind mounts de Docker pueden estar más abajo que la raíz del contenedor.
            var drive = DriveInfo.GetDrives().Where(d => d.IsReady && Directory.Exists(d.Name))
                .Where(d => full.Equals(Path.TrimEndingDirectorySeparator(d.Name), comparison)
                    || full.StartsWith(Path.EndsInDirectorySeparator(d.Name) ? d.Name : d.Name + Path.DirectorySeparatorChar, comparison))
                .OrderByDescending(d => d.Name.Length).FirstOrDefault();
            return drive is { TotalSize: > 0 } ? new(ambito, drive.TotalSize, drive.AvailableFreeSpace) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }
}
