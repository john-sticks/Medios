using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Medios.Services.Respaldos;

public record ModoOperativo(bool Mantenimiento,DateTimeOffset FechaUtc,string Usuario);
public class ModoOperativoStore(IConfiguration config,IDataProtectionProvider protection)
{
    readonly SemaphoreSlim gate=new(1,1);
    string PathState=>Path.Combine(config["Operations:Directory"]??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".aspnet","DataProtection-Keys","operacion"),"modo.protegido");
    IDataProtector Protector=>protection.CreateProtector("Keryx.Operations.Mode.v1");
    public async Task<ModoOperativo> LeerAsync()
    {
        await gate.WaitAsync();try{return File.Exists(PathState)?JsonSerializer.Deserialize<ModoOperativo>(Protector.Unprotect(await File.ReadAllTextAsync(PathState)))!:new(false,DateTimeOffset.MinValue,"");}finally{gate.Release();}
    }
    public async Task GuardarAsync(bool maintenance,string usuario)
    {
        await gate.WaitAsync();try{
            Directory.CreateDirectory(Path.GetDirectoryName(PathState)!);var temp=PathState+".tmp";
            await File.WriteAllTextAsync(temp,Protector.Protect(JsonSerializer.Serialize(new ModoOperativo(maintenance,DateTimeOffset.UtcNow,usuario))));
            RespaldoStore.Privado(temp);File.Move(temp,PathState,true);
        }finally{gate.Release();}
    }
}
public class ModoOperativoMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http,ModoOperativoStore store)
    {
        if(http.User.IsInRole("DELEGACION") && !HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method) && !HttpMethods.IsOptions(http.Request.Method)
            && http.Request.Path != "/" && !http.Request.Path.StartsWithSegments("/Login",StringComparison.OrdinalIgnoreCase) && (await store.LeerAsync()).Mantenimiento)
        {
            http.Response.StatusCode=423;http.Response.Headers.CacheControl="no-store";
            await http.Response.WriteAsJsonAsync(new{success=false,message="El sistema está en mantenimiento. Las delegaciones pueden consultar, pero no guardar cambios."});return;
        }
        await next(http);
    }
}
