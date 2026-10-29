using System.Security.Claims;
using Medios.Controllers;
using Medios.Entities;
using Medios.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;

static class ArchivoSesionesChecks
{
    public static async Task<int> RunAsync()
    {
        var count = 0;
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); count++; }
        var factory = new MemoriaFactory();
        var notas = new NotaService(factory);
        var sintesis = new SintesisService(factory, null!, notas);
        var service = new SesionService(factory, null!, sintesis, notas);
        int id, notaId, versionId, snapshotId, vaciaId;
        using (var db = factory.Create())
        {
            var sesion = new SesionPrensa { Estado = "Finalizada", UsuarioCarga = "delegado", PDFPath = "pdf/conservar.pdf", Fecha = new(2026,10,29) };
            var nota = new NotaPrensa { Sesion = sesion };
            nota.VersionActual = new NotaVersion { Nota = nota, Version = 1, EstadoRevision = "Finalizada", Titulo = "Noticia de prueba", Texto = "Texto conservado" };
            db.SesionesPrensas.Add(sesion);
            db.NotasPrensa.Add(nota);
            await db.SaveChangesAsync();
            var snapshot = new Sintesis { Fecha = sesion.Fecha, Tipo = "Vespertina", Estado = "Remitida", DelegacionId = 1 };
            db.Sintesis.Add(snapshot);
            await db.SaveChangesAsync();
            db.SintesisNotas.Add(new SintesisNota { SintesisId = snapshot.Id, NotaPrensaId = nota.Id, NotaVersionId = nota.VersionActual.Id });
            var vacia = new SesionPrensa { Estado = "Finalizada" };
            db.SesionesPrensas.Add(vacia);
            db.SesionesPrensas.AddRange(new SesionPrensa { Estado = "Borrador" }, new SesionPrensa { Estado = "Remitida" }, new SesionPrensa { Estado = "Consolidada", SintesisConsolidadaId = snapshot.Id });
            await db.SaveChangesAsync();
            id=sesion.Id; notaId=nota.Id; versionId=nota.VersionActual.Id; snapshotId=snapshot.Id; vaciaId=vacia.Id;
        }
        foreach (var rol in new[] { "DELEGACION", "SUPERVISOR", "", "ADMINISTRADOR" })
            Check(!await service.ArchivarAsync(id, "otro", rol, "No debe cambiar"), "Solo Medios y Desarrollador archivan");
        foreach (var motivo in new[] { "", "   ", new string('x',501) })
            Check(!await service.ArchivarAsync(id,"medios","MEDIOS",motivo), "Motivo obligatorio y acotado también en servidor");
        foreach (var estado in new[] { "Borrador", "Remitida", "Consolidada" })
        {
            using var db=factory.Create();
            var otra=await db.SesionesPrensas.FirstAsync(s=>s.Estado==estado);
            Check(!await service.ArchivarAsync(otra.Id,"medios","MEDIOS","No"), "No archivar estados ajenos a Finalizadas");
        }
        var antes=(await service.GetBandejaAnalistaAsync("Finalizada")).Count;
        Check(await service.ArchivarAsync(id,"medios","MEDIOS","  Ya no se consolidará  "), "Archivar finalizada con notas aprobadas");
        Check(!await service.ArchivarAsync(id,"medios","MEDIOS","Segundo click"), "Doble POST no repite archivo ni traza");
        using (var db=factory.Create())
        {
            var s=(await db.SesionesPrensas.FindAsync(id))!;
            Check(s.Estado=="Archivada" && s.UsuarioArchivo=="medios" && s.FechaArchivo!=null && s.MotivoArchivo=="Ya no se consolidará", "Metadatos persistentes");
            Check(s.PDFPath=="pdf/conservar.pdf" && await db.NotasPrensa.CountAsync()==1 && await db.NotasVersion.CountAsync()==1, "Archivo no borra archivos, noticias ni versiones");
            Check((await db.NotasPrensa.FindAsync(notaId))!.VersionActualId==versionId && (await db.NotasVersion.FindAsync(versionId))!.Texto=="Texto conservado", "Identidad y contenido intactos");
            Check(await db.SintesisNotas.AnyAsync(sn=>sn.SintesisId==snapshotId && sn.NotaPrensaId==notaId && sn.NotaVersionId==versionId), "Snapshots previos intactos");
            Check(await db.TrazaEventos.CountAsync(t=>t.SesionId==id && t.Evento=="SESION_ARCHIVADA")==1, "Traza única con el cambio");
            Check(db.Model.FindEntityType(typeof(SesionPrensa))!.FindProperty("Estado")!.IsConcurrencyToken, "Estado protegido de escrituras obsoletas");
        }
        Check((await service.GetBandejaAnalistaAsync("Finalizada")).Count==antes-1, "Home y pendientes dejan de contar la archivada");
        Check((await service.GetBandejaAnalistaAsync()).Any(s=>s.Id==id && s.Estado=="Archivada"), "Disponible en tab Archivadas");
        Check((await sintesis.GetSesionesParaConsolidarByIdsAsync(new(){id})).Count==0, "Archivada no se selecciona para consolidar");
        Check(await sintesis.CrearAsync(new(2026,10,29),"Especial","medios",new(){notaId},consolidada:true,sesionesFuente:new(){id})==-3, "POST obsoleto no crea consolidada de una archivada");
        Check(await service.ArchivarAsync(vaciaId,"gale","DESARROLLADOR","Sin notas aptas"), "Desarrollador puede archivar también sin notas");
        Check(!await service.RestaurarArchivadaAsync(id,"delegado","DELEGACION"), "Restaurar también restringido");
        Check(await service.RestaurarArchivadaAsync(id,"gale","DESARROLLADOR"), "Restaurar a Finalizadas");
        Check(!await service.RestaurarArchivadaAsync(id,"gale","DESARROLLADOR"), "Restauración repetida no duplica evento");
        Check((await sintesis.GetSesionesParaConsolidarByIdsAsync(new(){id})).Count==1, "Restaurada vuelve a estar disponible");
        using (var db=factory.Create())
            Check(await db.TrazaEventos.CountAsync(t=>t.SesionId==id)==2 && (await db.SesionesPrensas.FindAsync(id))!.MotivoArchivo=="Ya no se consolidará", "Conservar motivo y recorrido al restaurar");
        var creado=await sintesis.CrearAsync(new(2026,10,29),"Especial","medios",new(){notaId},consolidada:true,sesionesFuente:new(){id});
        using (var db=factory.Create())
            Check(creado>0 && (await db.SesionesPrensas.FindAsync(id))!.SintesisConsolidadaId==creado && (await db.SesionesPrensas.FindAsync(id))!.Estado=="Consolidada", "Consolidación válida también confirma su estado");
        Check(!await service.ArchivarAsync(id,"medios","MEDIOS","Ya consolidada"), "No archivar una sesión ya absorbida");
        var http=new DefaultHttpContext { User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.Role,"DELEGACION")},"test")) };
        var controller=new SesionController(service,notas,sintesis,null!,null!,null!) { ControllerContext=new ControllerContext{HttpContext=http} };
        Check(await controller.Archivar(id) is ForbidResult && await controller.Archivar(id,"motivo") is ForbidResult && await controller.RestaurarArchivada(id) is ForbidResult, "Acceso directo a las tres acciones denegado a Delegación");
        Check(await controller.EliminarArchivada(id) is ForbidResult, "Acceso directo a Eliminar también restringido");
        return count;
    }
}
