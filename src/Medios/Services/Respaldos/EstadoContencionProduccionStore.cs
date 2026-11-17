using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Medios.Services.Respaldos;

public sealed class EstadoContencionProduccionStore(IConfiguration configuration, IDataProtectionProvider protection)
{
    public SemaphoreSlim Consulta { get; } = new(1, 1);
    private IDataProtector Protector => protection.CreateProtector("Keryx.Production.Containment.v1");
    private string Archivo => Path.Combine(configuration["Operations:Directory"] ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".aspnet", "DataProtection-Keys", "operacion"), "contencion.protegido");
    private sealed record Confirmacion(bool Activa, DateTimeOffset ConfirmadaUtc);

    public bool? UltimoEstado()
    {
        if (!File.Exists(Archivo)) return null;
        return JsonSerializer.Deserialize<Confirmacion>(Protector.Unprotect(File.ReadAllText(Archivo)))?.Activa;
    }

    public async Task GuardarAsync(bool activa, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Archivo)!);
        var temporal = Archivo + ".tmp";
        await File.WriteAllTextAsync(temporal,
            Protector.Protect(JsonSerializer.Serialize(new Confirmacion(activa, DateTimeOffset.UtcNow))), ct);
        RespaldoStore.Privado(temporal);
        File.Move(temporal, Archivo, true);
    }
}
