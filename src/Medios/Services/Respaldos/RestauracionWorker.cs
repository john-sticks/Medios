using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Medios.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace Medios.Services.Respaldos;

public class RestauracionWorker(IServiceScopeFactory scopes,RespaldoStore store,CopiaUso uso,
    IConfiguration config,ILogger<RestauracionWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Yield();using var timer=new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            if(!store.Habilitado)continue;
            try
            {
                var state=await store.LeerAsync();
                // Una interrupción durante un restore invalida el clon; test normal sigue disponible.
                if(state.Restaurando && state.RestauracionPendiente==null)
                    await store.Cambiar(s=>{s.Restaurando=false;s.CopiaLista=false;s.ErrorRestauracion="Restauración interrumpida. Volvé a restaurar un respaldo.";return true;});
                if(state.RestauracionPendiente!=null)await RestaurarAsync(state.RestauracionPendiente,ct);
            }
            catch(OperationCanceledException) when(ct.IsCancellationRequested){break;}
            catch(Exception e){logger.LogWarning("Restauración: {Tipo}",e.GetType().Name);}
        }while(await timer.WaitForNextTickAsync(ct));
    }
    async Task RestaurarAsync(string id,CancellationToken ct)
    {
        var temp=Path.Combine(store.Directorio,"restore-"+Guid.NewGuid().ToString("N"));
        await uso.Operacion.WaitAsync(ct);
        uso.Bloquear();
        try
        {
            await store.Cambiar(s=>{if(s.ContingenciaActiva)throw new InvalidOperationException("Desactivá la contingencia antes de restaurar.");s.Restaurando=true;s.CopiaLista=false;s.ErrorRestauracion=null;return true;});
            while(uso.Activas>0)await Task.Delay(200,ct);
            Directory.CreateDirectory(temp);
            var secrets=await store.SecretosAsync()??throw new InvalidOperationException("Agente no configurado.");
            var tar=Path.Combine(temp,"contenido.tar.gz");
            await RespaldoCifrado.DescifrarAsync(store.Archivo(id),tar,secrets.Clave,ct);
            var unpack=Path.Combine(temp,"extraido");Directory.CreateDirectory(unpack);
            await ExtraerAsync(tar,unpack,ct);
            var sql=Path.Combine(unpack,"database.sql");
            if(!File.Exists(sql) || new FileInfo(sql).Length==0)throw new InvalidOperationException("El respaldo no contiene la base de datos.");
            using var scope=scopes.CreateScope();var factory=scope.ServiceProvider.GetRequiredService<MediosDbContextFactory>();
            using var db=factory.CreateCopy();
            // La cuenta de importación solo tiene permisos en Test-BKUP-Productiva.
            var connection=new MySqlConnectionStringBuilder(db.Database.GetConnectionString());
            if(connection.Database!=RespaldoStore.BaseCopia)throw new InvalidOperationException("Destino de restauración inválido.");
            var defaults=Path.Combine(temp,"mysql.cnf");
            static string Escape(string v)=>v.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\n","\\n").Replace("\r","\\r");
            await File.WriteAllTextAsync(defaults,$"[client]\nhost=\"{Escape(connection.Server)}\"\nport={connection.Port}\nuser=\"{Escape(connection.UserID)}\"\npassword=\"{Escape(connection.Password)}\"\n",ct);RespaldoStore.Privado(defaults);
            // Limpiar únicamente las tablas del clon. No se emite DROP DATABASE ni se usa la conexión de test.
            await using(var con=new MySqlConnection(connection.ConnectionString))
            {
                await con.OpenAsync(ct);var tables=new List<string>();
                await using(var read=new MySqlCommand("SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_TYPE='BASE TABLE'",con))
                await using(var rows=await read.ExecuteReaderAsync(ct))while(await rows.ReadAsync(ct))tables.Add(rows.GetString(0));
                await using var clear=new MySqlCommand("SET FOREIGN_KEY_CHECKS=0;"+string.Join("",tables.Select(t=>"DROP TABLE `"+t.Replace("`","``")+"`;"))+"SET FOREIGN_KEY_CHECKS=1;",con);
                await clear.ExecuteNonQueryAsync(ct);
            }
            var start=new ProcessStartInfo(config["TestBackup:MySqlClient"]??"mariadb"){RedirectStandardInput=true,RedirectStandardError=true,RedirectStandardOutput=true,UseShellExecute=false};
            start.ArgumentList.Add("--defaults-extra-file="+defaults);start.ArgumentList.Add("--database="+RespaldoStore.BaseCopia);start.ArgumentList.Add("--binary-mode");start.ArgumentList.Add("--default-character-set=utf8mb4");
            using var process=Process.Start(start)??throw new InvalidOperationException("No se pudo iniciar el cliente MySQL.");
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromMinutes(20));
            using var kill=timeout.Token.Register(()=>{try{process.Kill(true);}catch{}});
            var errors=process.StandardError.ReadToEndAsync(timeout.Token);var stdout=process.StandardOutput.ReadToEndAsync(timeout.Token);
            await using(var input=File.OpenRead(sql))await input.CopyToAsync(process.StandardInput.BaseStream,timeout.Token);
            process.StandardInput.Close();await process.WaitForExitAsync(timeout.Token);await errors;await stdout;
            if(process.ExitCode!=0)throw new InvalidOperationException("Falló la importación SQL. La copia permanece deshabilitada; el respaldo está conservado.");
            await RetornoProduccionService.GuardarLineaBaseAsync(db.Database.GetConnectionString()!,Path.Combine(store.Directorio,"linea-base.json"),ct);
            // Tablas exclusivas de test, ausentes en la versión productiva actual.
            var extra=Path.Combine(AppContext.BaseDirectory,"BackupSchema","36_grupos_correo.sql");
            if(File.Exists(extra))await db.Database.ExecuteSqlRawAsync(await File.ReadAllTextAsync(extra,ct),ct);
            await db.NotasPrensa.CountAsync(ct);await db.SesionesPrensas.CountAsync(ct);await db.AutorizacionesUsuario.CountAsync(ct);
            Directory.CreateDirectory(store.Assets);
            foreach(var folder in new[]{"pdf","uploads"})
            {
                var target=Path.Combine(store.Assets,folder);if(Directory.Exists(target))Directory.Move(target,Path.Combine(temp,"anterior-"+folder));
                var from=Path.Combine(unpack,folder);if(Directory.Exists(from))Directory.Move(from,target);else Directory.CreateDirectory(target);
            }
            var originals=Path.Combine(AppContext.BaseDirectory,"wwwroot","img");var images=Path.Combine(store.Assets,"img");Directory.CreateDirectory(images);
            foreach(var source in Directory.EnumerateFiles(originals))File.Copy(source,Path.Combine(images,Path.GetFileName(source)),true);
            await store.Cambiar(s=>{s.CopiaLista=true;s.RestauradaUtc=DateTimeOffset.UtcNow;s.CopiaOrigen=id;s.Generacion=Guid.NewGuid().ToString("N");s.EpocaContingencia=Guid.NewGuid().ToString("N");s.RestauracionPendiente=null;s.Restaurando=false;s.ErrorRestauracion=null;s.RetornoSoloLectura=false;RespaldoStore.Evento(s,"sistema","Copia restaurada: "+id);return true;});
        }
        catch(Exception e)
        {
            logger.LogWarning("Restauración fallida ({Tipo}).",e.GetType().Name);
            await store.Cambiar(s=>{s.CopiaLista=false;s.Restaurando=false;s.RestauracionPendiente=null;s.ErrorRestauracion=e is InvalidOperationException?e.Message:"Falló la restauración. Revisá la configuración del clon y los registros del servidor.";return true;});
        }
        finally{uso.Liberar();uso.Operacion.Release();if(Directory.Exists(temp))Directory.Delete(temp,true);}
    }
    public static async Task ExtraerAsync(string tar,string destino,CancellationToken ct)
    {
        await using var input=File.OpenRead(tar);await using var zip=new GZipStream(input,CompressionMode.Decompress);using var reader=new TarReader(zip);
        long total=0;int files=0;var names=new HashSet<string>(StringComparer.Ordinal);
        while(await reader.GetNextEntryAsync(false,ct) is {} entry)
        {
            var name=entry.Name.TrimStart('.','/');
            if(entry.Name.Contains("..") || entry.Name.Contains('\\') || Path.IsPathRooted(entry.Name))throw new InvalidOperationException("Ruta inválida en respaldo.");
            if(name is not ("database.sql" or "manifest.json" or "pdf" or "uploads") && !name.StartsWith("pdf/") && !name.StartsWith("uploads/"))throw new InvalidOperationException("Contenido fuera del alcance del respaldo.");
            if(entry.EntryType==TarEntryType.Directory){Directory.CreateDirectory(Path.Combine(destino,name));continue;}
            if(entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || !names.Add(name))throw new InvalidOperationException("Entrada de respaldo no permitida.");
            if(++files>100000 || (total+=entry.Length)>10L*1024*1024*1024)throw new InvalidOperationException("El respaldo excede los límites de extracción.");
            var target=Path.GetFullPath(Path.Combine(destino,name));if(!target.StartsWith(Path.GetFullPath(destino)+Path.DirectorySeparatorChar,StringComparison.Ordinal))throw new InvalidOperationException("Ruta inválida.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);await using var output=new FileStream(target,FileMode.CreateNew);RespaldoStore.Privado(target);
            if(entry.DataStream!=null)await entry.DataStream.CopyToAsync(output,ct);
        }
    }
}
