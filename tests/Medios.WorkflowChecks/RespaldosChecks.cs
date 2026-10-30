using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Medios.Entities;
using Medios.Infrastructure.Data;
using Medios.Services.Respaldos;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

static class RespaldosChecks
{
    public static async Task<int> RunAsync()
    {
        int count=0;void Check(bool ok,string text){if(!ok)throw new Exception("Respaldos: "+text);count++;}
        var root=Path.Combine(Path.GetTempPath(),"keryx-backup-checks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var protection=new EphemeralDataProtectionProvider();
        IConfiguration Config(bool enabled=true,string environment="Test")=>new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
            ["TestBackup:Enabled"]=enabled.ToString(),["Deployment:Environment"]=environment,["TestBackup:Directory"]=Path.Combine(root,"backup"),["Operations:Directory"]=Path.Combine(root,"modo")}).Build();
        var store=new RespaldoStore(Config(),protection);
        try
        {
            Check(store.Habilitado,"doble habilitación explícita para test");
            Check(!new RespaldoStore(Config(true,"Production"),protection).Habilitado,"el flag no habilita el selector en producción");
            Check(!new RespaldoStore(Config(false),protection).Habilitado,"módulo apagado por defecto");
            try{store.Archivo("../../medios.sql");Check(false,"ruta de respaldo");}catch(InvalidOperationException){Check(true,"rechaza rutas arbitrarias");}
            var apiHttp = new DefaultHttpContext();
            apiHttp.Request.Scheme = "http";
            var api = new Medios.Controllers.RespaldoAgenteController(store, Config()) {
                ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = apiHttp }
            };
            Check(await api.Instalar() is Microsoft.AspNetCore.Mvc.NotFoundResult, "instalación solo por HTTPS");
            apiHttp.Request.Scheme = "https";
            Check(await api.Instalar() is Microsoft.AspNetCore.Mvc.NotFoundResult, "instalación sin código rechazada");
            apiHttp.Request.Headers.Authorization = "Bearer " + new string('A',64);
            await store.Cambiar(s => { s.InstalacionHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(new string('A',64)))); s.InstalacionHastaUtc = DateTimeOffset.UtcNow.AddSeconds(-1); return true; });
            Check(await api.Instalar() is Microsoft.AspNetCore.Mvc.NotFoundResult, "código vencido rechazado");
            var secrets=(await store.SecretosAsync(true))!;
            Check(await store.TokenValidoAsync(secrets.Token),"token válido");Check(!await store.TokenValidoAsync("incorrecto"),"token incorrecto");
            Check(!(await File.ReadAllTextAsync(Path.Combine(store.Directorio,"agente.protegido"))).Contains(secrets.Clave),"secretos protegidos en disco");
            await store.Cambiar(s=>{s.Hora="03:30";s.CopiaLista=true;return true;});
            Check((await new RespaldoStore(Config(),protection).LeerAsync()).Hora=="03:30","estado conservado tras reiniciar el servicio");
            var plain=Path.Combine(root,"plain");var encrypted=Path.Combine(root,"backup.kbk");var restored=Path.Combine(root,"restored");
            await File.WriteAllTextAsync(plain,"Texto de prueba con tildes: información y más contenido");
            await RespaldoCifrado.CifrarAsync(plain,encrypted,secrets.Clave);await RespaldoCifrado.DescifrarAsync(encrypted,restored,secrets.Clave,default);
            Check(await File.ReadAllTextAsync(restored)==await File.ReadAllTextAsync(plain),"cifrado y recuperación exactos");
            try{await RespaldoCifrado.VerificarAsync(encrypted,"otra clave");Check(false,"clave errónea");}catch(InvalidOperationException){Check(true,"rechaza otra clave");}
            var bytes=await File.ReadAllBytesAsync(encrypted);bytes[30]^=1;await File.WriteAllBytesAsync(encrypted,bytes);
            try{await RespaldoCifrado.VerificarAsync(encrypted,secrets.Clave);Check(false,"manipulación");}catch(InvalidOperationException){Check(true,"detecta respaldo manipulado");}
            var archive=Path.Combine(root,"archivo.tar.gz");
            async Task Tar(string name,TarEntryType type=TarEntryType.RegularFile)
            {
                await using var file=File.Create(archive);await using var gzip=new GZipStream(file,CompressionLevel.Fastest);await using var writer=new TarWriter(gzip,TarEntryFormat.Pax);
                var entry=new PaxTarEntry(type,name);if(type==TarEntryType.SymbolicLink)entry.LinkName="/etc/passwd";else entry.DataStream=new MemoryStream("SELECT 1;"u8.ToArray());
                await writer.WriteEntryAsync(entry);
            }
            await Tar("database.sql");await RestauracionWorker.ExtraerAsync(archive,Path.Combine(root,"valid"),default);Check(File.Exists(Path.Combine(root,"valid","database.sql")),"extrae únicamente rutas permitidas");
            foreach(var name in new[]{"../outside","/database.sql","uploads/../../outside","other/file"})
            {
                await Tar(name);try{await RestauracionWorker.ExtraerAsync(archive,Path.Combine(root,"invalid"),default);Check(false,"ruta "+name);}catch(InvalidOperationException){Check(true,"rechaza ruta "+name);}
            }
            await Tar("uploads/enlace",TarEntryType.SymbolicLink);try{await RestauracionWorker.ExtraerAsync(archive,Path.Combine(root,"link"),default);Check(false,"symlink");}catch(InvalidOperationException){Check(true,"rechaza enlaces de archivo");}
            var modo=new ModoOperativoStore(Config(true,"Production"),protection);await modo.GuardarAsync(true,"operador");
            Check((await modo.LeerAsync()).Mantenimiento,"mantenimiento independiente del ambiente de test");
            foreach(var item in new[]{("DELEGACION","POST","/Nota/Editar",423),("DELEGACION","GET","/Nota/Listado",200),("MEDIOS","POST","/Nota/Editar",200),("DESARROLLADOR","POST","/Nota/Editar",200),("DELEGACION","POST","/Login",200),("DELEGACION","POST","/",200)})
            {
                var http=new DefaultHttpContext();http.User=new(new ClaimsIdentity([new(ClaimTypes.Role,item.Item1)],"test"));http.Request.Method=item.Item2;http.Request.Path=item.Item3;http.Response.Body=new MemoryStream();
                var middleware=new ModoOperativoMiddleware(_=>Task.CompletedTask);await middleware.InvokeAsync(http,modo);Check(http.Response.StatusCode==item.Item4,"mantenimiento "+item);
            }
            await modo.GuardarAsync(false,"operador");Check(!(await modo.LeerAsync()).Mantenimiento,"vuelta a Normal persistente");
            var factory=new FakeFactory(Config(),store);
            using(var db=factory.CreateCopy()){
                db.AutorizacionesUsuario.AddRange(new AutorizacionUsuario{Id=1,Usuario="deleg-real",Nombre="Usuario productivo",Rol="DELEGACION",Estado="aprobada",Activo=true,DelegacionId=9},
                    new AutorizacionUsuario{Id=2,Usuario="inactivo",Nombre="Inactivo",Rol="MEDIOS",Estado="aprobada",Activo=false});await db.SaveChangesAsync();}
            var access=new AccesoEmergenciaService(store,factory);Check((await access.UsuariosAsync()).Count==1,"solo usuarios aprobados y activos");
            var password=await access.PrepararAsync("deleg-real","developer");Check(password.Length>=24,"clave individual aleatoria");
            Check(await access.ValidarAsync("deleg-real",password)==null,"no entra antes de activar contingencia");
            await store.Cambiar(s=>{s.ContingenciaHastaUtc=DateTimeOffset.UtcNow.AddHours(24);return true;});
            Check(await access.ValidarAsync("deleg-real",password)!=null,"usuario aprobado accede con su clave individual");
            for(int i=0;i<5;i++)await access.ValidarAsync("deleg-real","incorrecta");Check(await access.ValidarAsync("deleg-real",password)==null,"bloqueo después de cinco intentos erróneos");
            await store.Cambiar(s=>{s.Accesos.Single().BloqueadoHastaUtc=null;return true;});
            await access.CambiarClaveAsync("deleg-real","Una clave nueva individual 2026");Check(await access.ValidarAsync("deleg-real",password)==null,"la clave inicial deja de servir al cambiarla");
            Check(await access.ValidarAsync("deleg-real","Una clave nueva individual 2026")!=null,"acepta nueva clave individual");
            var state=await store.LeerAsync();var credential=state.Accesos.Single();
            var principal=new ClaimsPrincipal(new ClaimsIdentity([new("UsuarioReal","deleg-real"),new("RolReal","DELEGACION"),new("DelegacionIdReal","9"),new("BackupEmergencyEpoch",state.EpocaContingencia),new("BackupAccessRevision",credential.Revision)],"test"));
            Check(await access.SesionValidaAsync(principal,state),"sesión vinculada a aprobación y delegación del respaldo");
            using(var db=factory.CreateCopy()){(await db.AutorizacionesUsuario.FindAsync(1))!.Activo=false;await db.SaveChangesAsync();}
            Check(!await access.SesionValidaAsync(principal,state),"revocación local invalida sesión de emergencia");
            await store.Cambiar(s=>{s.ContingenciaHastaUtc=null;return true;});Check(await access.ValidarAsync("deleg-real","Una clave nueva individual 2026")==null,"desactivar corta nuevos accesos");
            // La selección está firmada, vinculada al desarrollador y a la generación restaurada.
            var uso=new CopiaUso();var ctx=new DatosTestContext();var httpCopy=new DefaultHttpContext();httpCopy.Response.Body=new MemoryStream();httpCopy.Request.Method="POST";httpCopy.Request.Path="/Nota/Editar";
            httpCopy.User=new(new ClaimsIdentity([new(ClaimTypes.Role,"DESARROLLADOR"),new("RolReal","DESARROLLADOR"),new("UsuarioReal","developer")],"test"));
            var profile=protection.CreateProtector("Keryx.TestDatabase.v1").Protect(JsonSerializer.Serialize(new PerfilDatos("developer",state.Generacion,DateTimeOffset.UtcNow.AddHours(8))));
            httpCopy.Request.Headers.Cookie=RespaldoContextMiddleware.CookiePerfil+"="+profile;httpCopy.Request.Headers["X-Keryx-Dataset"]="test";
            bool called=false;var selector=new RespaldoContextMiddleware(_=>{called=true;return Task.CompletedTask;});
            await selector.InvokeAsync(httpCopy,store,ctx,protection,access,uso);Check(!called && httpCopy.Response.StatusCode==409,"bloquea formulario de otra base");
            httpCopy.Response.StatusCode=200;httpCopy.Request.Headers["X-Keryx-Dataset"]="copia:"+state.Generacion;
            await selector.InvokeAsync(httpCopy,store,new(),protection,access,uso);Check(called,"permite guardar si coincide la base del formulario");
            await store.Cambiar(s=>{s.Generacion=Guid.NewGuid().ToString("N");return true;});called=false;
            await selector.InvokeAsync(httpCopy,store,new(),protection,access,uso);Check(!called && httpCopy.Response.StatusCode==409,"invalida formularios anteriores a una restauración");
            Check(RetornoProduccionService.Huella(new Dictionary<string,string?>{{"a","1"},{"b",null}})==RetornoProduccionService.Huella(new Dictionary<string,string?>{{"b",null},{"a","1"}}),"huellas independientes del orden de columnas");
            Check(RetornoProduccionService.Huella(new Dictionary<string,string?>{{"a",null}})!=RetornoProduccionService.Huella(new Dictionary<string,string?>{{"a",""}}),"distingue vacío de nulo para la conciliación");
        }
        finally{Directory.Delete(root,true);}
        return count;
    }
    sealed class FakeFactory(IConfiguration config,RespaldoStore store):MediosDbContextFactory(config,NullLogger<MediosDbContextFactory>.Instance,store)
    {
        readonly string database=Guid.NewGuid().ToString();
        public override MediosDbContext CreateCopy()=>new(new DbContextOptionsBuilder<MediosDbContext>().UseInMemoryDatabase(database).Options);
    }
}
