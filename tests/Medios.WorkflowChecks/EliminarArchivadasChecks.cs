using Medios.Entities;
using Medios.Services;
using Microsoft.EntityFrameworkCore;

static class EliminarArchivadasChecks
{
    public static async Task<int> RunAsync()
    {
        int count=0;
        void Check(bool ok,string text){if(!ok) throw new InvalidOperationException(text);count++;}
        async Task<(MemoriaFactory factory,SesionService service,int id,int nota,int version,int publicacion)> Crear()
        {
            var factory=new MemoriaFactory();
            var notas=new NotaService(factory);
            var service=new SesionService(factory,null!,new SintesisService(factory,null!,notas),notas);
            using var db=factory.Create();
            var d=new Delegacion{Nombre="QA"};
            var s=new SesionPrensa{Estado="Archivada",Delegacion=d,Fecha=new(2026,10,29),Turno="Vespertina",UsuarioArchivo="medios",MotivoArchivo="Prueba",FechaArchivo=DateTime.Now,PDFPath="pdf/conservar.pdf"};
            var n=new NotaPrensa{Sesion=s};
            var v1=new NotaVersion{Nota=n,Version=1,Titulo="Original",Texto="Primera",EsActual=false};
            var v2=new NotaVersion{Nota=n,Version=2,Titulo="Actual",Texto="Conservar hasta confirmar",ParentVersion=v1,EstadoRevision="Finalizada"};
            n.VersionActual=v2;
            var publicacion=new Sintesis{Delegacion=d,Fecha=s.Fecha,Tipo=s.Turno,Estado="Remitida"};
            db.NotasPrensa.Add(n);db.NotasVersion.Add(v1);db.Sintesis.Add(publicacion);
            db.SesionesPrensas.Add(new SesionPrensa{Estado="Remitida",UsuarioCarga="No tocar"});
            await db.SaveChangesAsync();
            db.SintesisNotas.Add(new SintesisNota{SintesisId=publicacion.Id,NotaPrensaId=n.Id,NotaVersionId=v2.Id});
            db.TrazaEventos.Add(new TrazaEventoEntity{SesionId=s.Id,Evento="SESION_ARCHIVADA",Usuario="medios"});
            await db.SaveChangesAsync();
            return(factory,service,s.Id,n.Id,v2.Id,publicacion.Id);
        }
        var caso=await Crear();
        var vista=(await caso.service.AnalizarEliminacionArchivadaAsync(caso.id))!;
        Check(vista.PuedeEliminar && vista.NotaIds.Count==1 && vista.VersionIds.Count==2 && vista.SintesisIds.SequenceEqual(new[]{caso.publicacion}),"Advertir el alcance exacto, incluyendo publicación propia exclusiva");
        Check(!await caso.service.EliminarArchivadaAsync(caso.id,"medios","MEDIOS",vista.Token,false),"Sin confirmación explícita no elimina");
        Check(!await caso.service.EliminarArchivadaAsync(caso.id,"delegado","DELEGACION",vista.Token,true),"No permitir borrar a Delegación");
        Check(!await caso.service.EliminarArchivadaAsync(caso.id,"medios","MEDIOS","otro alcance",true),"Token manipulado no elimina");
        using(var db=caso.factory.Create()){(await db.NotasVersion.FindAsync(caso.version))!.Texto="Corregida después de abrir confirmación";await db.SaveChangesAsync();}
        Check(!await caso.service.EliminarArchivadaAsync(caso.id,"medios","MEDIOS",vista.Token,true),"Pantalla obsoleta no elimina una noticia corregida");
        vista=(await caso.service.AnalizarEliminacionArchivadaAsync(caso.id))!;
        Check(await caso.service.EliminarArchivadaAsync(caso.id,"gale","DESARROLLADOR",vista.Token,true),"Confirmación válida elimina la archivada y sus notas");
        using(var db=caso.factory.Create())
        {
            Check(!await db.SesionesPrensas.AnyAsync(s=>s.Id==caso.id) && await db.NotasPrensa.CountAsync()==0 && await db.NotasVersion.CountAsync()==0,"Eliminar sesión, noticia y versiones");
            Check(await db.Sintesis.CountAsync()==0 && await db.SintesisNotas.CountAsync()==0,"Eliminar únicamente su publicación propia y snapshots");
            Check(await db.SesionesPrensas.AnyAsync(s=>s.UsuarioCarga=="No tocar"),"Conservar sesiones ajenas");
            Check(await db.TrazaEventos.CountAsync(t=>t.SesionId==caso.id)==2 && await db.TrazaEventos.AnyAsync(t=>t.Evento=="SESION_ELIMINADA" && t.Usuario=="gale"),"Conservar trazas anteriores y autor de eliminación");
        }
        Check(!await caso.service.EliminarArchivadaAsync(caso.id,"medios","MEDIOS",vista.Token,true),"Doble eliminación no vuelve a ejecutar cambios");
        foreach(var tipo in new[]{"informada","consolidada","filtrada","copia","parent","relacion","mixta","derivada","restaurada"})
        {
            caso=await Crear();
            var antes=(await caso.service.AnalizarEliminacionArchivadaAsync(caso.id))!;
            using(var db=caso.factory.Create())
            {
                if(tipo=="informada") (await db.Sintesis.FindAsync(caso.publicacion))!.Estado="Informada";
                if(tipo is "consolidada" or "filtrada")
                {
                    var externa=new Sintesis{Fecha=new(2026,10,29),Tipo="Especial",Estado="Generada",SintesisOrigenId=tipo=="filtrada"?caso.publicacion:null};
                    db.Sintesis.Add(externa);await db.SaveChangesAsync();
                    db.SintesisNotas.Add(new SintesisNota{SintesisId=externa.Id,NotaPrensaId=caso.nota,NotaVersionId=caso.version});
                }
                if(tipo is "copia" or "parent" or "relacion" or "mixta")
                {
                    var externa=new NotaPrensa{NotaOrigenId=tipo=="copia"?caso.nota:null};
                    db.NotasPrensa.Add(externa);await db.SaveChangesAsync();
                    var v=new NotaVersion{Nota=externa,ParentVersionId=tipo=="parent"?caso.version:null};
                    db.NotasVersion.Add(v);await db.SaveChangesAsync();
                    if(tipo=="relacion") db.NotasRelaciones.Add(new NotaRelacion{NotaId=caso.nota,NotaRelacionadaId=externa.Id});
                    if(tipo=="mixta") db.SintesisNotas.Add(new SintesisNota{SintesisId=caso.publicacion,NotaPrensaId=externa.Id,NotaVersionId=v.Id});
                }
                if(tipo=="derivada") db.Sintesis.Add(new Sintesis{SintesisOrigenId=caso.publicacion});
                if(tipo=="restaurada") (await db.SesionesPrensas.FindAsync(caso.id))!.Estado="Finalizada";
                await db.SaveChangesAsync();
            }
            vista=(await caso.service.AnalizarEliminacionArchivadaAsync(caso.id))!;
            Check(!vista.PuedeEliminar && vista.Bloqueos.Count>0,"Bloquear dependencia externa: "+tipo);
            Check(!await caso.service.EliminarArchivadaAsync(caso.id,"medios","MEDIOS",antes.Token,true),"POST también bloquea: "+tipo);
            using var check=caso.factory.Create();
            Check(await check.NotasPrensa.AnyAsync(n=>n.Id==caso.nota) && await check.NotasVersion.AnyAsync(v=>v.Id==caso.version),"No borra parcialmente ante dependencia: "+tipo);
        }
        return count;
    }
}
