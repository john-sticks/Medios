using Medios.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.FileProviders;
using System.Security.Claims;

namespace Medios.Services.Respaldos;

public class DatosTestContext
{
    public bool Copia { get; set; }
    public bool Emergencia { get; set; }
    public bool RetornoSoloLectura { get; set; }
    public string Generacion { get; set; } = "";
    public string Marca => Copia ? "copia:"+Generacion : "test";
    public string Nombre => Copia ? RespaldoStore.BaseCopia : "medios (test)";
}
public record PerfilDatos(string Usuario,string Generacion,DateTimeOffset? HastaUtc = null);
public class RespaldoContextMiddleware(RequestDelegate next)
{
    public const string CookiePerfil = "Keryx.TestDatabase";
    public async Task InvokeAsync(HttpContext http,RespaldoStore store,DatosTestContext datos,
        IDataProtectionProvider protection,AccesoEmergenciaService acceso,CopiaUso uso)
    {
        if(!store.Habilitado){await next(http);return;}
        var requestPath=http.Request.Path;
        if(new[]{"/css","/js","/adminlte","/img","/lib"}.Any(p=>requestPath.StartsWithSegments(p,StringComparison.OrdinalIgnoreCase))) {await next(http);return;}
        // El panel de control siempre usa test; el clon nunca puede sustituir su configuración.
        var panel=http.Request.Path.StartsWithSegments("/Respaldos",StringComparison.OrdinalIgnoreCase)
            || http.Request.Path.StartsWithSegments("/api/testbackup",StringComparison.OrdinalIgnoreCase)
            || http.Request.Path.Equals("/Monitor/Modo",StringComparison.OrdinalIgnoreCase);
        var state=await store.LeerAsync();
        var safe=HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method) || HttpMethods.IsOptions(http.Request.Method);
        var login=http.Request.Path == "/" || http.Request.Path.StartsWithSegments("/Login",StringComparison.OrdinalIgnoreCase);
        var emergency=http.User.FindFirst("BackupEmergencyEpoch")?.Value;
        if(emergency!=null)
        {
            if(!await acceso.SesionValidaAsync(http.User,state))
            {await http.SignOutAsync("Cookies");http.Response.Redirect("/Login/Contingencia");return;}
            if(http.User.HasClaim("CambioClaveEmergencia","true") && !login)
            {http.Response.Redirect("/Login/CambiarClaveEmergencia");return;}
            datos.Emergencia=true;datos.Copia=!panel;
        }
        else if(!panel && http.User.Identity?.IsAuthenticated==true && http.Request.Cookies.TryGetValue(CookiePerfil,out var cookie))
        {
            try
            {
                var raw=protection.CreateProtector("Keryx.TestDatabase.v1").Unprotect(cookie);
                var profile=System.Text.Json.JsonSerializer.Deserialize<PerfilDatos>(raw)!;
                var real=http.User.FindFirst("UsuarioReal")?.Value;
                var developer=http.User.FindFirst("RolReal")?.Value=="DESARROLLADOR";
                if(developer && profile.Usuario==real && (profile.Generacion!=state.Generacion || !state.CopiaLista || state.Restaurando || profile.HastaUtc<=DateTimeOffset.UtcNow || profile.HastaUtc==null))
                {http.Response.StatusCode=409;await http.Response.WriteAsync("La copia cambió o se está restaurando. Volvé a Monitor para seleccionar la base.");return;}
                datos.Copia=developer && profile.Usuario==real && profile.Generacion==state.Generacion && state.CopiaLista;
            }
            catch(System.Security.Cryptography.CryptographicException){http.Response.Cookies.Delete(CookiePerfil);}
            catch(System.Text.Json.JsonException){http.Response.Cookies.Delete(CookiePerfil);}
        }
        datos.Generacion=state.Generacion;datos.RetornoSoloLectura=datos.Copia && state.RetornoSoloLectura;
        if(datos.Copia && !panel && !safe && !login && state.RetornoSoloLectura)
        {http.Response.StatusCode=423;await http.Response.WriteAsJsonAsync(new{success=false,message="La copia está en solo lectura para preparar el retorno a producción."});return;}

        if(!panel && !safe && !login && http.User.Identity?.IsAuthenticated==true)
        {
            var mark=http.Request.Headers["X-Keryx-Dataset"].ToString();
            if(mark.Length==0 && http.Request.HasFormContentType)mark=(await http.Request.ReadFormAsync())["__KeryxDataset"].ToString();
            if(mark!=datos.Marca){http.Response.StatusCode=409;await http.Response.WriteAsJsonAsync(new{success=false,message="La base seleccionada cambió o la página quedó desactualizada. Actualizá la vista antes de guardar."});return;}
        }
        if(datos.Copia && (state.Restaurando || !state.CopiaLista))
        {http.Response.StatusCode=503;await http.Response.WriteAsync("La copia productiva no está disponible durante la restauración.");return;}
        if(datos.Copia && !datos.Emergencia && http.Request.Method!="GET" && http.Request.Path.StartsWithSegments("/Sincronizacion",StringComparison.OrdinalIgnoreCase))
        {http.Response.StatusCode=409;await http.Response.WriteAsync("La sincronización externa está bloqueada al probar la copia productiva.");return;}
        // Los adjuntos del clon están separados de los de test y requieren autenticación.
        if(datos.Copia && http.Request.Path.StartsWithSegments("/uploads",out var relative))
        {
            if(http.User.Identity?.IsAuthenticated!=true){http.Response.StatusCode=401;return;}
            var path=Path.GetFullPath(Path.Combine(store.Assets,"uploads",relative.Value!.TrimStart('/')));
            var root=Path.GetFullPath(Path.Combine(store.Assets,"uploads"))+Path.DirectorySeparatorChar;
            if(!path.StartsWith(root,StringComparison.Ordinal) || !File.Exists(path)){http.Response.StatusCode=404;return;}
            var provider=new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
            http.Response.ContentType=provider.TryGetContentType(path,out var type)?type:"application/octet-stream";
            if (!uso.Entrar()) { http.Response.StatusCode=503; return; }
            try { await http.Response.SendFileAsync(path); } finally { uso.Salir(); }
            return;
        }
        if (datos.Copia && !uso.Entrar()) { http.Response.StatusCode=503; return; }
        try { await next(http); } finally { if(datos.Copia) uso.Salir(); }
    }
}
public class DatosTestEnvironment(IWebHostEnvironment original,RespaldoStore store,DatosTestContext datos):IWebHostEnvironment
{
    public string WebRootPath {get=>datos.Copia?store.Assets:original.WebRootPath;set=>throw new NotSupportedException();}
    public IFileProvider WebRootFileProvider {get=>original.WebRootFileProvider;set=>throw new NotSupportedException();}
    public string ApplicationName {get=>original.ApplicationName;set=>throw new NotSupportedException();}
    public IFileProvider ContentRootFileProvider {get=>original.ContentRootFileProvider;set=>throw new NotSupportedException();}
    public string ContentRootPath {get=>original.ContentRootPath;set=>throw new NotSupportedException();}
    public string EnvironmentName {get=>original.EnvironmentName;set=>throw new NotSupportedException();}
}

public class MonitorPorBaseCache
{
    readonly System.Collections.Concurrent.ConcurrentDictionary<string,Medios.Services.MonitorEspacioCache> caches=new();
    public Medios.Services.MonitorEspacioCache Obtener(DatosTestContext datos)=>caches.GetOrAdd(datos.Copia?"copia-"+datos.Generacion:"test",_=>new());
}
