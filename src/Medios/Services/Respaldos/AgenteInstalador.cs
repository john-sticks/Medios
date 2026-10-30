using System.IO.Compression;
using System.Text.Json;

namespace Medios.Services.Respaldos;

public static class AgenteInstalador
{
    public static async Task<byte[]> CrearAsync(RespaldoStore store, IConfiguration config)
    {
        var url = config["TestBackup:ReceiverUrl"];
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https") throw new InvalidOperationException("Falta la URL HTTPS del receptor.");
        var secret = (await store.SecretosAsync(true))!;
        var json = JsonSerializer.Serialize(new { receiver = url, token = secret.Token, secret = secret.Clave, source = "/opt/medios", database = "medios" });
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            var entry = zip.CreateEntry("keryx-backup-agent.json"); using (var writer = new StreamWriter(entry.Open())) writer.Write(json);
            foreach (var name in new[] { "keryx-backup-agent.py", "install-backup-agent.sh" })
            {
                var script = zip.CreateEntry(name); using var writer = new StreamWriter(script.Open());
                writer.Write(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "BackupAgent", name)).Replace("\r\n", "\n"));
            }
        }
        return buffer.ToArray();
    }
}
