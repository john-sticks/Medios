using Microsoft.EntityFrameworkCore;
using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Medios.Infrastructure.Data;
using MySqlConnector;

namespace Medios.Services.Respaldos;

public record TablaReferencia(string Nombre, string[] Claves, List<FilaReferencia> Filas);
public record FilaReferencia(string Clave, string Hash);
public record CambioRetorno(string Tabla, string Clave, string Operacion, string? HashEsperado, Dictionary<string, string?>? Datos);
public class RetornoProduccionService(RespaldoStore store, MediosDbContextFactory factory, CopiaUso uso)
{
    static string Valor(object value) => value switch
    { DateTime d => d.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture), byte[] b => "base64:" + Convert.ToBase64String(b), _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "" };
    public static string Huella(IReadOnlyDictionary<string, string?> row)
    {
        using var stream = new MemoryStream();
        foreach (var pair in row.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            static void Part(Stream s, string text) { var bytes = Encoding.UTF8.GetBytes(text); Span<byte> size = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(size, bytes.Length); s.Write(size); s.Write(bytes); }
            Part(stream, pair.Key); stream.WriteByte(pair.Value == null ? (byte)0 : (byte)1); if (pair.Value != null) Part(stream, pair.Value);
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }
    static async Task<List<(TablaReferencia Tabla, List<Dictionary<string, string?>> Datos)>> LeerAsync(string cs, CancellationToken ct)
    {
        await using var db = new MySqlConnection(cs); await db.OpenAsync(ct); await using var tx = await db.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var names = new List<string>();
        await using (var cmd = new MySqlCommand("SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_TYPE='BASE TABLE' ORDER BY TABLE_NAME", db, tx))
        await using (var rows = await cmd.ExecuteReaderAsync(ct)) while (await rows.ReadAsync(ct)) names.Add(rows.GetString(0));
        var result = new List<(TablaReferencia, List<Dictionary<string, string?>>)>();
        foreach (var name in names)
        {
            var escaped = "`" + name.Replace("`", "``") + "`"; var keys = new List<string>();
            await using (var cmd = new MySqlCommand("SHOW INDEX FROM " + escaped + " WHERE Key_name='PRIMARY'", db, tx))
            await using (var rows = await cmd.ExecuteReaderAsync(ct)) while (await rows.ReadAsync(ct)) keys.Add(rows.GetString("Column_name"));
            if (keys.Count == 0) throw new InvalidOperationException("Una tabla no tiene clave primaria; requiere revisión antes del retorno: " + name);
            var data = new List<Dictionary<string, string?>>(); var fingerprints = new List<FilaReferencia>();
            await using (var cmd = new MySqlCommand("SELECT * FROM " + escaped, db, tx))
            await using (var rows = await cmd.ExecuteReaderAsync(ct)) while (await rows.ReadAsync(ct))
                {
                    var row = new Dictionary<string, string?>(); for (int i = 0; i < rows.FieldCount; i++) row[rows.GetName(i)] = rows.IsDBNull(i) ? null : Valor(rows.GetValue(i));
                    var key = JsonSerializer.Serialize(keys.Select(k => row[k]).ToArray()); data.Add(row); fingerprints.Add(new(key, Huella(row)));
                }
            result.Add((new(name, keys.ToArray(), fingerprints), data));
        }
        await tx.CommitAsync(ct); return result;
    }
    public static async Task GuardarLineaBaseAsync(string cs, string path, CancellationToken ct)
    {
        var data = await LeerAsync(cs, ct); await File.WriteAllTextAsync(path, JsonSerializer.Serialize(data.Select(t => t.Tabla)), ct); RespaldoStore.Privado(path);
    }
    static List<CambioRetorno> Cambios(List<TablaReferencia> baseline, List<(TablaReferencia Tabla, List<Dictionary<string, string?>> Datos)> actual)
    {
        var changes = new List<CambioRetorno>();
        foreach (var table in baseline)
        {
            var current = actual.SingleOrDefault(t => t.Tabla.Nombre == table.Nombre);
            if (current.Tabla == null) throw new InvalidOperationException("Cambió el esquema del clon: " + table.Nombre);
            var previous = table.Filas.ToDictionary(f => f.Clave); var next = current.Tabla.Filas.ToDictionary(f => f.Clave);
            for (int i = 0; i < current.Tabla.Filas.Count; i++)
            {
                var f = current.Tabla.Filas[i]; previous.TryGetValue(f.Clave, out var old);
                if (old == null || old.Hash != f.Hash) changes.Add(new(table.Nombre, f.Clave, old == null ? "Alta" : "Modificacion", old?.Hash, current.Datos[i]));
            }
            foreach (var old in table.Filas.Where(f => !next.ContainsKey(f.Clave))) changes.Add(new(table.Nombre, old.Clave, "Eliminacion", old.Hash, null));
        }
        return changes;
    }
    public async Task ValidarInicioAsync(CancellationToken ct)
    {
        using var db = factory.CreateCopy(); var baseline = JsonSerializer.Deserialize<List<TablaReferencia>>(await File.ReadAllTextAsync(Path.Combine(store.Directorio, "linea-base.json"), ct))!;
        var changes = Cambios(baseline, await LeerAsync(db.Database.GetConnectionString()!, ct));
        if (changes.Any(c => c.Tabla is not ("auditoria" or "traza_eventos") && !(c.Tabla == "configuracion" && c.Clave.Contains("monitor_"))))
            throw new InvalidOperationException("La copia contiene cambios de pruebas. Restaurá un respaldo antes de activar la contingencia.");
    }
    public async Task<string> PrepararAsync(string actor, CancellationToken ct)
    {
        var state = await store.LeerAsync(); if (!state.CopiaLista || state.Restaurando || state.RestauracionPendiente != null) throw new InvalidOperationException("No hay una copia disponible.");
        await uso.Operacion.WaitAsync(ct);
        try
        {
            await store.Cambiar(s => { if (s.Restaurando || s.RestauracionPendiente != null) throw new InvalidOperationException("La copia se está restaurando."); s.MantenimientoDelegaciones = true; s.RetornoSoloLectura = true; RespaldoStore.Evento(s, actor, "Copia en solo lectura para preparar retorno"); return true; });
            uso.Bloquear();
            try
            {
                while (uso.Activas > 0) await Task.Delay(200, ct);
                using var db = factory.CreateCopy(); var baselinePath = Path.Combine(store.Directorio, "linea-base.json");
                var baseline = JsonSerializer.Deserialize<List<TablaReferencia>>(await File.ReadAllTextAsync(baselinePath, ct))!;
                var changes = Cambios(baseline, await LeerAsync(db.Database.GetConnectionString()!, ct));
                var temp = Path.Combine(store.Directorio, "retorno-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
                try
                {
                    var info = new
                    {
                        Version = 1,
                        OrigenRespaldo = state.CopiaOrigen,
                        GeneradoUtc = DateTimeOffset.UtcNow,
                        Actor = actor,
                        Cambios = changes,
                        Procedimiento = "Comparación de tres vías: respaldo de origen, cambios de contingencia y producción actual. No restaurar directamente sobre producción. Las colisiones de claves y diferencias de contenido requieren resolución explícita."
                    };
                    await File.WriteAllTextAsync(Path.Combine(temp, "cambios.json"), JsonSerializer.Serialize(info), ct); RespaldoStore.Privado(Path.Combine(temp, "cambios.json"));
                    var tar = Path.Combine(temp, "retorno.tar.gz");
                    await using (var file = File.Create(tar)) await using (var gzip = new GZipStream(file, CompressionLevel.Optimal)) await using (var writer = new TarWriter(gzip, TarEntryFormat.Pax, true))
                    {
                        await writer.WriteEntryAsync(baselinePath, "linea-base.json", ct); await writer.WriteEntryAsync(Path.Combine(temp, "cambios.json"), "cambios.json", ct);
                        foreach (var folder in new[] { "pdf", "uploads" })
                        {
                            var root = Path.Combine(store.Assets, folder); if (!Directory.Exists(root)) continue;
                            foreach (var asset in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                            {
                                if ((File.GetAttributes(asset) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Se encontró un enlace no permitido entre los adjuntos.");
                                await writer.WriteEntryAsync(asset, folder + "/" + Path.GetRelativePath(root, asset).Replace('\\', '/'), ct);
                            }
                        }
                    }
                    var output = Path.Combine(store.Directorio, "retorno-produccion.kbk"); var secret = (await store.SecretosAsync())!;
                    await RespaldoCifrado.CifrarAsync(tar, output, secret.Clave, ct);
                    await store.Cambiar(s => { RespaldoStore.Evento(s, actor, $"Paquete de retorno preparado: {changes.Count} cambios. Pendiente de conciliación en producción."); return true; });
                    return output;
                }
                finally { Directory.Delete(temp, true); }
            }
            finally { uso.Liberar(); }
        }
        finally { uso.Operacion.Release(); }
    }
}
