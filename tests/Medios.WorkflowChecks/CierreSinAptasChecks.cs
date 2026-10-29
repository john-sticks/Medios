using Medios.Entities;
using Medios.Services;
using Microsoft.EntityFrameworkCore;

static class CierreSinAptasChecks
{
    public static async Task<int> RunAsync()
    {
        int count=0;
        void Check(bool ok,string text){if(!ok)throw new InvalidOperationException(text);count++;}
        var factory=new MemoriaFactory();
        var notas=new NotaService(factory);
        var sintesis=new SintesisService(factory,null!,notas);
        var sesiones=new SesionService(factory,null!,sintesis,notas);
        int id,notaId,versionId,delegacionId,borradorId;
        using(var db=factory.Create())
        {
            var d=new Delegacion{Nombre="Delegación QA"};
            var s=new SesionPrensa{Delegacion=d,Estado="Finalizada",Fecha=new(2026,10,29)};
            var n=new NotaPrensa{Sesion=s};
            n.VersionActual=new NotaVersion{Nota=n,Version=1,EsActual=true,Titulo="Devuelta a delegación",EstadoRevision="Sin Remitir",MotivoDescarte="Revisar",TipoEvento="Descarte"};
            var borrador=new SesionPrensa{Delegacion=d,Estado="Borrador",UsuarioCarga="delegado"};
            db.NotasPrensa.Add(n);db.SesionesPrensas.Add(borrador);await db.SaveChangesAsync();
            id=s.Id;notaId=n.Id;versionId=n.VersionActual.Id;delegacionId=d.Id;borradorId=borrador.Id;
        }
        Check(!await sesiones.QuitarSinAptasAsync(id,"delegado","DELEGACION"),"Solo Medios y Desarrollador quitan cabeceras sin aprobadas");
        Check(await sesiones.QuitarSinAptasAsync(id,"medios","MEDIOS"),"Quitar síntesis sin aprobadas sin borrar la noticia");
        Check(!await sesiones.QuitarSinAptasAsync(id,"medios","MEDIOS"),"No duplicar cierre");
        Check(!(await sesiones.GetBandejaAnalistaAsync()).Any(s=>s.Id==id),"Cabecera quitada no vuelve a bandejas ni contadores");
        Check((await notas.GetRechazadasAsync(delegacionId)).Any(n=>n.Id==notaId),"Delegación conserva la nota devuelta");
        Check(!await notas.AprobarAsync(notaId,"medios") && !await notas.DescartarAsync(notaId,"medios","Otra vez"),"Pantalla vieja no vuelve a revisar una sesión quitada");
        using(var db=factory.Create())
        {
            Check(await db.NotasVersion.CountAsync()==1 && (await db.NotasPrensa.FindAsync(notaId))!.VersionActualId==versionId,"No se borran ni modifican versiones al quitar cabecera");
            Check((await db.SesionesPrensas.FindAsync(id))!.Estado=="Eliminada" && await db.TrazaEventos.AnyAsync(t=>t.NotaId==notaId && t.SesionId==id),"Guardar referencia mínima y evento por noticia para el recorrido");
        }
        Check(await notas.VincularRechazadaAsync(notaId,borradorId,delegacionId,"delegado","Delegado"),"Corregir recuperando misma noticia en nuevo borrador");
        var historial=await notas.GetHistorialNoticiaAsync(notaId);
        Check(historial.Sesiones.ContainsKey(id) && historial.Versiones.Count==2,"Historial conserva síntesis eliminada y nueva versión tras mover la noticia");
        Check(!(await notas.GetRechazadasAsync(delegacionId)).Any(n=>n.Id==notaId),"Recuperada deja de alertar como rechazada pendiente");
        using(var db=factory.Create())
        {
            var s=(await db.SesionesPrensas.FindAsync(borradorId))!;
            s.Estado="Archivada";await db.SaveChangesAsync();
        }
        Check(!await sesiones.QuitarSinAptasAsync(borradorId,"medios","MEDIOS"),"No quitar por cierre de vacía una síntesis con nota pendiente de revisar");
        var proteccion=(await sesiones.AnalizarEliminacionArchivadaAsync(borradorId))!;
        Check(!proteccion.PuedeEliminar,"No borrar una nota con origen/versiones compartidas");
        return count;
    }
}
