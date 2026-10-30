using System.Security.Claims;
using Medios.Controllers;
using Medios.Entities;
using Medios.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;

static class ConsolidacionVaciasChecks
{
    public static async Task<int> RunAsync()
    {
        var count = 0;
        void Check(bool ok, string m) { if (!ok) throw new InvalidOperationException(m); count++; }
        var factory = new MemoriaFactory();
        var notas = new NotaService(factory);
        var sintesis = new SintesisService(factory, null!, notas);
        var sesiones = new SesionService(factory, null!, sintesis, notas);
        int sesionId, notaId, legadoId, legadoSesionId, buenaId, borradorId;
        using (var db = factory.Create())
        {
            var delegacion = new Delegacion { Nombre = "Delegación de prueba" };
            var sesion = new SesionPrensa { Delegacion = delegacion, Estado = "Remitida" };
            var nota = new NotaPrensa { Sesion = sesion };
            nota.VersionActual = new NotaVersion { Nota = nota, Version = 1, EstadoRevision = "Remitida", Titulo = "Rechazo sin motivo" };
            var legadoSesion = new SesionPrensa { Delegacion = delegacion, Estado = "Finalizada" };
            var legado = new NotaPrensa { Sesion = legadoSesion };
            legado.VersionActual = new NotaVersion { Nota = legado, Version = 2, EstadoRevision = "Sin Remitir", TipoEvento = "Descarte", AmbitoNota = "Nacional", Titulo = "Legado sin motivo" };
            var buena = new SesionPrensa { Delegacion = delegacion, Estado = "Finalizada" };
            var aprobada = new NotaPrensa { Sesion = buena };
            aprobada.VersionActual = new NotaVersion { Nota = aprobada, EstadoRevision = "Finalizada", AmbitoNota = "Nacional", Titulo = "Aprobada" };
            var borrador = new SesionPrensa { Delegacion = delegacion, Estado = "Borrador", UsuarioCarga = "delegacion" };
            db.NotasPrensa.AddRange(nota, legado, aprobada);
            db.SesionesPrensas.Add(borrador);
            await db.SaveChangesAsync();
            sesionId = sesion.Id; notaId = nota.Id; legadoId = legado.Id; legadoSesionId = legadoSesion.Id; buenaId = buena.Id; borradorId = borrador.Id;
        }
        Check(await notas.DescartarAsync(notaId, "Medios", null), "Descartar sin explicación");
        Check(await sesiones.MarcarFinalizadaAsync(sesionId), "Finalizar una sesión con todos los rechazos atendidos");
        var cargadas = await sintesis.GetSesionesParaConsolidarByIdsAsync(new() { sesionId, legadoSesionId, buenaId });
        var vacia = cargadas.Single(s => s.Id == sesionId);
        Check(vacia.Notas.Count == 1 && !SintesisWorkflowPolicy.TieneNotasConsolidables(vacia), "Una sesión tiene nota registrada pero ninguna consolidable");
        Check(SintesisWorkflowPolicy.EsDescartada(vacia.Notas.Single().VersionActual), "Conservar identidad del rechazo tras pasar a Sin Remitir");
        Check(vacia.Notas.Single().VersionActual!.MotivoDescarte == "Sin motivo informado.", "No perder el motivo al finalizar");
        Check(SintesisWorkflowPolicy.EsDescartada(cargadas.Single(s => s.Id == legadoSesionId).Notas.Single().VersionActual), "Reconocer descarte legado sin motivo por TipoEvento");
        Check(SintesisWorkflowPolicy.TieneNotasConsolidables(cargadas.Single(s => s.Id == buenaId)), "Conservar sesiones aprobadas como candidatas");
        Check((await notas.GetRechazadasAsync(vacia.DelegacionId!.Value)).Any(n => n.Id == legadoId), "Mostrar rechazo legado en Denegadas");
        Check((await sintesis.GetNotasNacProvParaConsolidarAsync(new() { legadoSesionId })).Count == 0, "No ofrecer rechazo Nacional sin motivo en el paso 2");
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "MEDIOS") }, "test")) };
        var controller = new SintesisController(sintesis, null!, null!, null!, null!, null!, null!, notas, new GruposCorreoService(factory))
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new Provider())
        };
        Check(await controller.Consolidar("2026-10-29", "Especial", $"[{legadoSesionId},{buenaId}]") is RedirectResult,
            "Revalidar en POST las sesiones sin notas aptas aunque se manipulen los checks");
        using (var db = factory.Create())
            Check(await db.Sintesis.CountAsync() == 0 && (await db.SesionesPrensas.FindAsync(buenaId))!.Estado == "Finalizada",
                "No generar consolidada parcial ni consumir sesiones aprobadas ante selección inválida");
        Check(await notas.VincularRechazadaAsync(legadoId, borradorId, vacia.DelegacionId.Value, "delegacion", "Operador"),
            "Permitir recuperación del rechazo legado sin modificar la versión original");
        Check(!(await notas.GetRechazadasAsync(vacia.DelegacionId.Value)).Any(n => n.Id == legadoId),
            "Una corrección recuperada no vuelve a alertar como descarte");
        return count;
    }
    private class Provider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext c) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext c, IDictionary<string, object> v) { }
    }
}
