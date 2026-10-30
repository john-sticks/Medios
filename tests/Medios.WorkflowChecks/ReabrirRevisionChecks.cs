using Medios.Entities;
using Medios.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

static class ReabrirRevisionChecks
{
    public static async Task<int> RunAsync()
    {
        int checks=0;
        void Check(bool ok,string text){if(!ok)throw new InvalidOperationException("Reabrir: "+text);checks++;}
        var factory=new MemoriaFactory();
        var root=Path.Combine(Path.GetTempPath(),"keryx-reabrir-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var env=new Env{WebRootPath=root,ContentRootPath=root};var notas=new NotaService(factory);
        var sintesis=new SintesisService(factory,env,notas);var sesiones=new SesionService(factory,env,sintesis,notas);
        int id,aprobada,agregada,denegada,descartada,vieja;
        try
        {
            using(var db=factory.Create())
            {
                var sesion=new SesionPrensa{Estado="Finalizada",DelegacionId=1,Fecha=new DateOnly(2026,10,29),Turno="Vespertina"};
                db.Delegaciones.Add(new Delegacion{Id=1,Nombre="Delegación QA",Activa=true});
                foreach(var state in new[]{"Finalizada","Agregada","Sin Remitir","Descartada"})
                {
                    var n=new NotaPrensa{Sesion=sesion};n.VersionActual=new NotaVersion{Nota=n,Version=1,EstadoRevision=state,
                        Titulo="ORIGINAL "+state,Texto="Texto",Sintesis="Resumen",AmbitoNota="Nacional",Fuente="Medio",OtrosMedios="Otro",
                        Link="https://example.test/nota",ImagenUrl="/uploads/qa.jpg",Direccion="Dirección",Latitud=-34,Longitud=-58,
                        MotivoDescarte=state is "Sin Remitir" or "Descartada"?"Denegada previamente":null,
                        OperadorRevision="revisor-original",TipoEvento=state is "Sin Remitir" or "Descartada"?"Descarte":"Aprobacion"};
                    sesion.Notas.Add(n);
                }
                db.SesionesPrensas.Add(sesion);await db.SaveChangesAsync();id=sesion.Id;
                var ns=sesion.Notas.ToList();aprobada=ns[0].Id;agregada=ns[1].Id;denegada=ns[2].Id;descartada=ns[3].Id;vieja=ns[0].VersionActualId!.Value;
                var fuente=new Sintesis{Fecha=sesion.Fecha,Tipo=sesion.Turno,DelegacionId=1,Estado="Remitida"};
                foreach(var n in ns.Take(2))fuente.NotasIncluidas.Add(new SintesisNota{Nota=n,NotaVersion=n.VersionActual!});
                db.Sintesis.Add(fuente);await db.SaveChangesAsync();
            }
            Check(!await sesiones.ReabrirRevisionAsync(id,"intruso","DELEGACION"),"delegación no reabre");
            Check(!await sesiones.ReabrirRevisionAsync(id,"","MEDIOS"),"actor obligatorio");
            Check(await sesiones.ReabrirRevisionAsync(id,"operador","MEDIOS"),"finalizada vuelve a revisión");
            Check(!await sesiones.ReabrirRevisionAsync(id,"operador","MEDIOS"),"doble clic no duplica reapertura");
            var sesionReabierta=(await sesiones.GetByIdAsync(id))!;
            Check(sesionReabierta.Estado=="Remitida" && sesionReabierta.Notas.Count==4,"misma sesión y notas");
            Check(!SintesisWorkflowPolicy.PuedeFinalizar(sesionReabierta),"debe aprobar nuevamente");
            foreach(var n in sesionReabierta.Notas.Where(n=>n.Id==aprobada || n.Id==agregada))
            {
                var v=n.VersionActual!;
                Check(v.EstadoRevision=="Pendiente" && v.Version==2 && v.ParentVersionId.HasValue && v.OperadorRevision==null,"versión pendiente enlazada a la anterior");
                Check(v.Link=="https://example.test/nota" && v.ImagenUrl=="/uploads/qa.jpg" && v.Sintesis=="Resumen" && v.Latitud==-34 && v.OtrosMedios=="Otro","conserva contenido, enlaces, medios y geografía");
            }
            Check(sesionReabierta.Notas.Single(n=>n.Id==denegada).VersionActual is {Version:1,EstadoRevision:"Sin Remitir",MotivoDescarte:"Denegada previamente"},"denegada devuelta queda intacta");
            Check(sesionReabierta.Notas.Single(n=>n.Id==descartada).VersionActual is {Version:1,EstadoRevision:"Descartada",MotivoDescarte:"Denegada previamente"},"descartada queda intacta");
            using(var db=factory.Create())
            {
                Check((await db.NotasVersion.FindAsync(vieja))!.EstadoRevision=="Finalizada" && db.SintesisNotas.Any(sn=>sn.NotaVersionId==vieja),"snapshot anterior conserva su aprobación e historial");
                Check(await db.NotasVersion.CountAsync()==6 && await db.TrazaEventos.CountAsync()==3,"solo nuevas versiones aprobadas y trazas");
            }
            Check(!(await sintesis.GetSnapshotVersionesPorNotaAsync(new(){aprobada})).ContainsKey(aprobada),"no reutiliza snapshot anterior a reapertura");
            Check(await notas.ModificarYAprobarAsync(aprobada,"editor",null,null,null,"CORREGIDA","Contenido corregido","Medio","https://example.test/corregida",false,ambitoNota:"Nacional"),"se puede corregir la nota");
            Check((await sesiones.GetByIdAsync(id))!.Notas.Single(n=>n.Id==aprobada).VersionActual!.EstadoRevision=="Pendiente","editar no aprueba automáticamente");
            await notas.AprobarAsync(aprobada,"revisor");
            Check(!await sesiones.MarcarFinalizadaAsync(id),"bloquea finalizar si queda una aprobación pendiente");
            await notas.AprobarAsync(agregada,"revisor");
            Check(await sesiones.MarcarFinalizadaAsync(id),"finaliza cuando se volvieron a aprobar las notas");
            var finalizada=(await sesiones.GetByIdAsync(id))!;
            Check(finalizada.Notas.Single(n=>n.Id==aprobada).VersionActual is {Titulo:"CORREGIDA",EstadoRevision:"Finalizada"},"finaliza la versión corregida y no la histórica");
            Check(SintesisWorkflowPolicy.TieneNotasConsolidables(finalizada),"correcciones aptas para consolidar");
            Check((await sintesis.GetNotasNacProvParaConsolidarAsync(new(){id})).SelectMany(g=>g.Variantes).Any(v=>v.NotaId==aprobada && v.Titulo=="CORREGIDA"),"consolidación ofrece la versión corregida");
            using(var db=factory.Create())
            {
                (await db.SesionesPrensas.FindAsync(id))!.SintesisConsolidadaId=999;await db.SaveChangesAsync();
            }
            Check(!await sesiones.ReabrirRevisionAsync(id,"operador","DESARROLLADOR"),"bloquea sesión con vínculo de consolidación");
            using(var db=factory.Create())
            {
                (await db.SesionesPrensas.FindAsync(id))!.SintesisConsolidadaId=null;
                var n=(await db.NotasPrensa.Include(n=>n.VersionActual).FirstAsync(n=>n.Id==aprobada));
                db.Sintesis.Add(new Sintesis{Estado="Generada",NotasIncluidas=new List<SintesisNota>{new(){Nota=n,NotaVersion=n.VersionActual!}}});
                await db.SaveChangesAsync();
            }
            Check(!await sesiones.ReabrirRevisionAsync(id,"operador","MEDIOS"),"protege nota ya incluida en una consolidada aunque falte vínculo de sesión");
            int legadoId;
            using(var db=factory.Create())
            {
                var legado=new SesionPrensa{Estado="Finalizada"};
                foreach(var estado in new[]{"Sin Remitir","Pendiente"})
                {
                    var n=new NotaPrensa{Sesion=legado};
                    n.VersionActual=new NotaVersion{Nota=n,Version=1,EstadoRevision=estado};
                    legado.Notas.Add(n);
                }
                db.SesionesPrensas.Add(legado);await db.SaveChangesAsync();legadoId=legado.Id;
            }
            Check(await sesiones.ReabrirRevisionAsync(legadoId,"operador","DESARROLLADOR"),"admite finalizadas antiguas con estados inconsistentes");
            Check((await sesiones.GetByIdAsync(legadoId))!.Notas.All(n=>n.VersionActual is {Version:2,EstadoRevision:"Pendiente"}),"las notas antiguas sin rechazo también quedan disponibles para aprobar");
            Check(typeof(Medios.Controllers.SesionController).GetMethod("ReabrirRevision")!.IsDefined(typeof(Microsoft.AspNetCore.Mvc.ValidateAntiForgeryTokenAttribute),true),"reapertura protegida con antifalsificación");
            return checks;
        }
        finally{Directory.Delete(root,true);}
    }
    sealed class Env:IWebHostEnvironment
    {
        public string WebRootPath{get;set;}="";public string ContentRootPath{get;set;}="";
        public string ApplicationName{get;set;}="Medios";public string EnvironmentName{get;set;}="Testing";
        public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();
    }
}
