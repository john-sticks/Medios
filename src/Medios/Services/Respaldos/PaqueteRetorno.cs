using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Medios.Services.Respaldos;

internal sealed record ManifiestoRetorno(int Version, string OrigenRespaldo, bool BaseSoloConfiguracion,
    DateTimeOffset GeneradoUtc, List<CambioRetorno> Cambios);

internal sealed class PaqueteRetorno : IAsyncDisposable
{
    private readonly string directorio;
    public ManifiestoRetorno Manifiesto { get; }
    public List<TablaReferencia> LineaBase { get; }
    public string Huella { get; }
    public IReadOnlyDictionary<string, string> Archivos { get; }

    private PaqueteRetorno(string directorio, ManifiestoRetorno manifiesto, List<TablaReferencia> lineaBase,
        string huella, Dictionary<string, string> archivos)
    {
        this.directorio = directorio;
        Manifiesto = manifiesto;
        LineaBase = lineaBase;
        Huella = huella;
        Archivos = archivos;
    }

    public static async Task<PaqueteRetorno> AbrirAsync(string cifrado, string clave, CancellationToken ct)
    {
        var directorio = Directory.CreateTempSubdirectory("keryx-retorno-extraido-").FullName;
        try
        {
            var tar = Path.Combine(directorio, "paquete.tgz");
            await RespaldoCifrado.DescifrarAsync(cifrado, tar, clave, ct);
            await using var input = File.OpenRead(cifrado);
            var huella = Convert.ToHexString(await SHA256.HashDataAsync(input, ct));
            await using var comprimido = File.OpenRead(tar);
            await using var gzip = new GZipStream(comprimido, CompressionMode.Decompress);
            using var reader = new TarReader(gzip);
            var archivos = new Dictionary<string, string>(StringComparer.Ordinal);
            var nombres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            const long maximo = 10L * 1024 * 1024 * 1024;
            long bytes = 0;
            while (await reader.GetNextEntryAsync(false, ct) is { } entrada)
            {
                var nombre = entrada.Name;
                var manifiesto = nombre is "cambios.json" or "linea-base.json";
                if ((!manifiesto && !EsAdjunto(nombre)) || !nombres.Add(nombre)
                    || entrada.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                    throw new InvalidOperationException("El paquete contiene una entrada no permitida.");
                if (entrada.Length > maximo - bytes || nombres.Count > 100_000
                    || (manifiesto && entrada.Length > 128 * 1024 * 1024))
                    throw new InvalidOperationException("El paquete excede el límite de extracción.");
                bytes += entrada.Length;
                var destino = Path.Combine(directorio, nombre);
                Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
                await using var archivo = new FileStream(destino, FileMode.CreateNew);
                if (entrada.DataStream != null) await entrada.DataStream.CopyToAsync(archivo, ct);
                if (!manifiesto) archivos.Add(nombre, destino);
            }

            var info = await LeerAsync<ManifiestoRetorno>("cambios.json");
            var baseInicial = await LeerAsync<List<TablaReferencia>>("linea-base.json");
            if (info.Version != 1 || !info.BaseSoloConfiguracion || !Guid.TryParseExact(info.OrigenRespaldo, "N", out _))
                throw new InvalidOperationException("Se necesita un paquete generado desde una copia limpia de contingencia.");
            return new(directorio, info, baseInicial, huella, archivos);

            async Task<T> LeerAsync<T>(string nombre)
            {
                if (!nombres.Contains(nombre)) throw new InvalidOperationException("Falta el manifiesto o la línea base del paquete.");
                await using var archivo = File.OpenRead(Path.Combine(directorio, nombre));
                return await JsonSerializer.DeserializeAsync<T>(archivo, cancellationToken: ct)
                    ?? throw new InvalidOperationException("El manifiesto o la línea base están vacíos.");
            }
        }
        catch
        {
            Directory.Delete(directorio, true);
            throw;
        }
    }

    private static bool EsAdjunto(string nombre)
    {
        var segmentos = nombre.Split('/');
        if (segmentos.Length < 2 || segmentos[0] is not ("pdf" or "uploads")
            || segmentos.Any(s => s is "" or "." or ".." || s.Any(c => char.IsControl(c) || c is '\\' or ':' or '?' or '#' or '%')))
            return false;
        var extension = Path.GetExtension(nombre).ToLowerInvariant();
        return segmentos[0] == "pdf" ? extension == ".pdf"
            : extension is ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" or ".avif" or ".bmp" or ".heic";
    }

    public ValueTask DisposeAsync()
    {
        Directory.Delete(directorio, true);
        return ValueTask.CompletedTask;
    }
}
