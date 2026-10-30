using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Medios.Services.Respaldos;

public class RespaldoStore
{
    public const string BaseCopia = "Test-BKUP-Productiva";
    readonly IConfiguration config;
    readonly IDataProtector protector;
    readonly SemaphoreSlim mutex = new(1,1);
    public RespaldoStore(IConfiguration config, IDataProtectionProvider protection)
    { this.config=config; protector=protection.CreateProtector("Keryx.TestBackup.State.v1"); }
    public bool Habilitado => config.GetValue<bool>("TestBackup:Enabled") && config["Deployment:Environment"] == "Test";
    public string Directorio => Path.GetFullPath(config["TestBackup:Directory"] ?? "/app/operaciones");
    public string Assets => Path.Combine(Directorio,"assets");
    public string Archivo(string id) => Guid.TryParseExact(id,"N",out _) ? Path.Combine(Directorio,"backups",id+".kbk") : throw new InvalidOperationException("Identificador inválido.");
    public void ExigirTest() { if(!Habilitado)throw new InvalidOperationException("Esta función solo está habilitada en test."); }
    public static void Privado(string path)
    { if(!OperatingSystem.IsWindows()) File.SetUnixFileMode(path,UnixFileMode.UserRead|UnixFileMode.UserWrite); }
    RespaldoEstado Leer()
    {
        var path=Path.Combine(Directorio,"estado.protegido");
        return File.Exists(path) ? JsonSerializer.Deserialize<RespaldoEstado>(protector.Unprotect(File.ReadAllText(path)))! : new();
    }
    public async Task<T> Cambiar<T>(Func<RespaldoEstado,T> change)
    {
        ExigirTest(); await mutex.WaitAsync();
        try
        {
            Directory.CreateDirectory(Directorio);Directory.CreateDirectory(Path.Combine(Directorio,"backups"));
            var state=Leer();var result=change(state);
            state.Eventos=state.Eventos.TakeLast(300).ToList();
            var path=Path.Combine(Directorio,"estado.protegido");var tmp=path+".tmp";
            await File.WriteAllTextAsync(tmp,protector.Protect(JsonSerializer.Serialize(state)));Privado(tmp);File.Move(tmp,path,true);
            return result;
        }
        finally{mutex.Release();}
    }
    public Task<RespaldoEstado> LeerAsync() => Cambiar(s=>JsonSerializer.Deserialize<RespaldoEstado>(JsonSerializer.Serialize(s))!);
    public static void Evento(RespaldoEstado state,string user,string action)=>state.Eventos.Add(new(DateTimeOffset.UtcNow,user,action));
    public async Task<AgenteSecretos?> SecretosAsync(bool crear=false)
    {
        ExigirTest(); await mutex.WaitAsync();
        try
        {
            Directory.CreateDirectory(Directorio);var path=Path.Combine(Directorio,"agente.protegido");
            if(File.Exists(path))return JsonSerializer.Deserialize<AgenteSecretos>(protector.Unprotect(await File.ReadAllTextAsync(path)));
            if(!crear)return null;
            var secrets=new AgenteSecretos(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
            await File.WriteAllTextAsync(path,protector.Protect(JsonSerializer.Serialize(secrets)));Privado(path);return secrets;
        }
        finally{mutex.Release();}
    }
    public async Task<bool> TokenValidoAsync(string? value)
    {
        if(!Habilitado || value is null)return false;
        var secrets=await SecretosAsync();
        if(secrets is null)return false;
        var a=System.Text.Encoding.UTF8.GetBytes(value);var b=System.Text.Encoding.UTF8.GetBytes(secrets.Token);
        return a.Length==b.Length && CryptographicOperations.FixedTimeEquals(a,b);
    }
}
