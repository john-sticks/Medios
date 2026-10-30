using System.Security.Claims;
using Medios.Controllers;
using Medios.Entities;
using Medios.Infrastructure.Middleware;
using Medios.Security;
using Medios.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

static class AccesoPublicadasChecks
{
    public static async Task<int> RunAsync()
    {
        int count=0;
        void Check(bool ok,string text){if(!ok)throw new InvalidOperationException(text);count++;}
        var factory=new MemoriaFactory();
        var notas=new NotaService(factory);
        var root=Path.Combine(Path.GetTempPath(),"keryx-acceso-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root,"pdf"));
        var env=new Env{WebRootPath=root,ContentRootPath=root};
        var menu=new MenuXmlService(env);
        var sintesis=new SintesisService(factory,env,notas);
        var sesiones=new SesionService(factory,env,sintesis,notas);
        var fecha=new DateOnly(2026,10,29);
        var archivo="/pdf/sintesis_subido_20261028081203000_qa.pdf";
        var bytes=System.Text.Encoding.ASCII.GetBytes("%PDF-QA autorizacion, solo comprobacion local");
        await File.WriteAllBytesAsync(Path.Combine(root,archivo.TrimStart('/')),bytes);
        using var services=new ServiceCollection().AddSingleton<IWebHostEnvironment>(env).BuildServiceProvider();
        HttpContext Http(string usuario,string rol,int? delegacion)
        {
            var claims=new List<Claim>{new(ClaimTypes.Name,usuario),new("Usuario",usuario),new(ClaimTypes.Role,rol)};
            if(delegacion.HasValue)claims.Add(new("DelegacionId",delegacion.Value.ToString()));
            return new DefaultHttpContext{User=new ClaimsPrincipal(new ClaimsIdentity(claims,"test")),RequestServices=services};
        }
        SintesisController Controller(string usuario="usuario-a",string rol="DELEGACION",int? delegacion=1)=>
            new(sintesis,sesiones,null!,menu,null!,null!,null!,notas,new GruposCorreoService(factory))
            {ControllerContext=new ControllerContext{HttpContext=Http(usuario,rol,delegacion)},TempData=new TempDataDictionary(new DefaultHttpContext(),new MemoryTempData())};
        int publicaId,privadaId,propiaId,ajenaId,sesionAjenaId,notaAjenaId;
        try
        {
            using(var db=factory.Create())
            {
                db.Delegaciones.AddRange(new Delegacion{Id=1,Nombre="Destino A",Activa=true},new Delegacion{Id=2,Nombre="Destino B",Activa=true},new Delegacion{Id=3,Nombre="Inactiva",Activa=false});
                var publica=new Sintesis{Fecha=fecha,Estado="Informada",PDFPath=archivo,DelegacionesDestino=new List<SintesisDelegacionDestino>{new(){DelegacionId=1}}};
                var privada=new Sintesis{Fecha=fecha,Estado="Generada",PDFPath=archivo,OperadorGenera="usuario-a"};
                var propia=new Sintesis{Fecha=fecha,Estado="Remitida",DelegacionId=1,PDFPath=archivo,OperadorGenera="creador-distinto"};
                var ajena=new Sintesis{Fecha=fecha,Estado="Remitida",DelegacionId=2,PDFPath=archivo,OperadorGenera="usuario-a"};
                var sesionAjena=new SesionPrensa{DelegacionId=2,Estado="Remitida",PDFPath=archivo,UsuarioCarga="usuario-a"};
                db.Sintesis.AddRange(publica,privada,propia,ajena);db.SesionesPrensas.Add(sesionAjena);
                var aprobada=new NotaPrensa{DelegacionId=1};aprobada.VersionActual=new NotaVersion{Nota=aprobada,Version=1,Titulo="APROBADA",EstadoRevision="Finalizada"};
                var rechazada=new NotaPrensa{DelegacionId=1};rechazada.VersionActual=new NotaVersion{Nota=rechazada,Version=1,Titulo="RECHAZADA",EstadoRevision="Sin Remitir",MotivoDescarte="Repetida",TipoEvento="Descarte"};
                var descartada=new NotaPrensa{DelegacionId=1};descartada.VersionActual=new NotaVersion{Nota=descartada,Version=1,Titulo="DESCARTADA",EstadoRevision="Descartada"};
                var legado=new NotaPrensa{DelegacionId=1};legado.VersionActual=new NotaVersion{Nota=legado,Version=1,Titulo="DESCARTE SIN MOTIVO",EstadoRevision="Sin Remitir",TipoEvento="Descarte"};
                var notaAjena=new NotaPrensa{Sesion=sesionAjena};notaAjena.VersionActual=new NotaVersion{Nota=notaAjena,Version=1,EstadoRevision="Finalizada"};
                db.NotasPrensa.AddRange(aprobada,rechazada,descartada,legado,notaAjena);await db.SaveChangesAsync();
                foreach(var n in new[]{aprobada,rechazada,descartada,legado})
                    foreach(var s in new[]{publica,propia})db.SintesisNotas.Add(new SintesisNota{SintesisId=s.Id,NotaPrensaId=n.Id,NotaVersionId=n.VersionActualId!.Value});
                await db.SaveChangesAsync();
                publicaId=publica.Id;privadaId=privada.Id;propiaId=propia.Id;ajenaId=ajena.Id;sesionAjenaId=sesionAjena.Id;notaAjenaId=notaAjena.Id;
            }
            foreach(var usuario in new[]{"usuario-a","usuario-b"})
            {
                var controller=Controller(usuario);
                Check(await controller.DescargarPdf(publicaId) is FileContentResult f && f.FileContents.SequenceEqual(bytes),"Usuarios distintos de la misma delegación descargan el mismo PDF publicado");
                Check(await controller.DescargarPdf(propiaId) is FileContentResult,"PDF propio accesible por delegación y no por creador");
                Check(await controller.Detalle(publicaId) is ForbidResult,"Publicada no expone detalle a Delegación, ni por URL");
                Check(await controller.DescargarWord(publicaId) is ForbidResult,"Delegación nunca obtiene Word por URL");
            }
            Check(await Controller(delegacion:2).DescargarPdf(publicaId) is ForbidResult,"No descargar destino de otra delegación cambiando ID");
            Check(await Controller().DescargarPdf(ajenaId) is ForbidResult,"Nombre de operador coincidente no permite descargar otra delegación");
            Check(await Controller().Detalle(ajenaId) is ForbidResult,"Nombre de operador coincidente tampoco permite detalle ajeno");
            Check(await Controller().DescargarPdf(privadaId) is ForbidResult,"Consolidada no informada no se publica por nombre de operador");
            Check(await Controller().GenerarPdf(privadaId) is ForbidResult,"No regenerar consolidada por coincidencia de operador");
            foreach(var id in new int?[]{null,999,3})
            {
                Check(await Controller(delegacion:id).DescargarPdf(publicaId) is ForbidResult,"Sin delegación válida o activa no descarga");
                var resultado=(ViewResult)await Controller(delegacion:id).Publicadas(desde:fecha,hasta:fecha,buscar:true);
                Check(((List<Sintesis>)resultado.Model!).Count==0,"Sin delegación válida no aparecen publicaciones");
            }
            foreach(var rol in new[]{"MEDIOS","DESARROLLADOR","CONSULTA"})
            {
                var vista=(ViewResult)await Controller(rol:rol).Detalle(publicaId);
                var modelo=(Sintesis)vista.Model!;
                Check(modelo.NotasIncluidas.Count==1 && modelo.NotasIncluidas.Single().NotaVersion.Titulo=="APROBADA","Todos los roles reciben un modelo sin notas rechazadas");
            }
            var vistaPropia=(ViewResult)await Controller().Detalle(propiaId);
            Check(((Sintesis)vistaPropia.Model!).NotasIncluidas.Count==1,"Trabajo propio de delegación conserva su detalle, sin notas rechazadas");
            var resultados=await sintesis.GetPublicadasAsync(fecha,fecha,true,1);
            Check(resultados.Single().NotasIncluidas.Count==1,"Contador de Publicadas también excluye descartes");
            using(var db=factory.Create())Check(await db.NotasPrensa.CountAsync()==5 && await db.SintesisNotas.CountAsync()==8,"Filtrar no borra noticias, versiones ni snapshots");
            var sesionController=new SesionController(sesiones,notas,sintesis,null!,null!,null!){ControllerContext=new ControllerContext{HttpContext=Http("usuario-a","DELEGACION",1)}};
            Check(await sesionController.DescargarPdf(sesionAjenaId) is ForbidResult && await sesionController.Detalle(sesionAjenaId) is ForbidResult,"No eludir autorización usando rutas de sesión con el mismo nombre de creador");
            var buscador=new NotaBuscadorController(notas,sesiones,null!,menu){ControllerContext=new ControllerContext{HttpContext=Http("usuario-a","DELEGACION",1)}};
            Check(await buscador.Detalle(notaAjenaId) is ForbidResult,"No abrir notas ajenas directamente por ID");
            foreach(var ruta in new[]{"/pdf/archivo.pdf","/pdf/respaldos/archivo.pdf","/PDF/archivo.pdf"})
            {
                bool continuo=false;var ctx=new DefaultHttpContext();ctx.Request.Path=ruta;
                await new PdfPrivadosMiddleware(_=>{continuo=true;return Task.CompletedTask;}).InvokeAsync(ctx);
                Check(!continuo && ctx.Response.StatusCode==404,"Ruta física PDF no elude la autorización: "+ruta);
            }
            bool estatico=false;var css=new DefaultHttpContext();css.Request.Path="/css/site.css";
            await new PdfPrivadosMiddleware(_=>{estatico=true;return Task.CompletedTask;}).InvokeAsync(css);
            Check(estatico,"Protección PDF no bloquea otros recursos estáticos");
            // Permisos explícitos generales y específicos: lista y descarga comparten expresión.
            foreach(bool todos in new[]{false,true})foreach(bool todas in new[]{false,true})foreach(int? id in new int?[]{null,1,2})
            {
                var s=new Sintesis{Estado="Informada",CanalTodosRoles=todos,CanalTodasDelegaciones=todas,DelegacionesDestino=new List<SintesisDelegacionDestino>{new(){DelegacionId=1}}};
                Check(SintesisAccesoPolicy.PuedeDescargar(s,id)==(id.HasValue && (todos||todas||id==1)),"Matriz de canales por delegación");
            }
        }
        finally{Directory.Delete(root,true);}
        return count;
    }
    private sealed class MemoryTempData:ITempDataProvider
    {
        public IDictionary<string,object> LoadTempData(HttpContext context)=>new Dictionary<string,object>();
        public void SaveTempData(HttpContext context,IDictionary<string,object> values){}
    }
    private sealed class Env:IWebHostEnvironment
    {
        public string WebRootPath{get;set;}="";public string ContentRootPath{get;set;}="";
        public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();
        public string ApplicationName{get;set;}="Medios.Checks";public string EnvironmentName{get;set;}="Testing";
    }
}
