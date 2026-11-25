using System.Security.Claims;
using Medios.Controllers;
using Medios.Entities;
using Medios.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

static class NotasCompartidasChecks
{
    public static async Task<int> RunAsync()
    {
        var checks = 0;
        void Check(bool ok, string caso) { if (!ok) throw new InvalidOperationException(caso); checks++; }
        var factory = new MemoriaFactory();
        var notas = new NotaService(factory);
        var sintesis = new SintesisService(factory, null!, notas);
        var sesiones = new SesionService(factory, null!, sintesis, notas);
        int divisionId, delegacionId;
        using (var db = factory.Create())
        {
            var division = new Delegacion { Nombre = "División Medios", Activa = true };
            var delegacion = new Delegacion { Nombre = "Delegación de prueba", Activa = true };
            db.Delegaciones.AddRange(division, delegacion);
            await db.SaveChangesAsync();
            divisionId = division.Id; delegacionId = delegacion.Id;
        }
        ClaimsPrincipal Usuario(string nombre, string rol) => new(new ClaimsIdentity(new[]
            { new Claim(ClaimTypes.Name, nombre), new Claim(ClaimTypes.Role, rol), new Claim("Usuario", nombre) }, "test"));
        var desarrollador = Usuario("Desarrollador", "DESARROLLADOR");
        Check((await sesiones.GetDelegacionEfectivaAsync(desarrollador))?.Id == divisionId,
            "Desarrollador sin rol MEDIOS adicional utiliza División Medios");
        var notaDev = await notas.AgregarLibreAsync(null, null, null, "Nota del desarrollador", "Texto", "Fuente", null,
            false, "Desarrollador", delegacionId: divisionId, aprobadaDirectamente: true);
        var notaMedios = await notas.AgregarLibreAsync(null, null, null, "Nota de Medios", "Texto", "Fuente", null,
            false, "Medios", delegacionId: divisionId, aprobadaDirectamente: true);
        var notaDelegacion = await notas.AgregarLibreAsync(null, null, null, "Nota de delegación", "Texto", "Fuente", null,
            false, "Delegación", delegacionId: delegacionId);
        Check((await notas.GetNotasLibresAsync("Medios", divisionId)).Select(n => n.Id).ToHashSet().SetEquals(new[] { notaDev, notaMedios }),
            "Medios ve las notas compartidas de Desarrollador y Medios sin notas privadas de delegación");
        Check((await notas.GetNotasLibresAsync("Desarrollador", divisionId)).Count == 2, "Compartición recíproca");
        Check((await notas.GetNotasLibresAsync("Delegación")).Single().Id == notaDelegacion, "Delegación conserva sus notas propias");
        var controller = new NotaController(notas, sesiones, null!, null!, null!, null!, null!, null!)
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext { User = Usuario("Medios", "MEDIOS") } }
        };
        var listado = (ViewResult)await controller.Listado();
        Check(((List<NotaPrensa>)listado.Model!).Any(n => n.Id == notaDev), "Listado de Medios conecta la selección compartida");
        var fecha = new DateOnly(2026, 11, 25);
        var borrador = await sesiones.CrearAsync(divisionId, fecha, "Especial", "Medios");
        Check(borrador > 0, "Medios crea el borrador");
        Check(!await sesiones.AgregarNotaExistenteAsync(borrador, notaDev, "Otro"), "Nadie modifica el borrador de otro operador");
        Check(!await sesiones.AgregarNotaExistenteAsync(borrador, notaDelegacion, "Medios"), "No incorporar notas de otra delegación");
        Check(await sesiones.AgregarNotaExistenteAsync(borrador, notaDev, "Medios"), "Medios incorpora la nota creada por Desarrollador");
        Check((await notas.GetNotasLibresAsync("Medios", divisionId)).All(n => n.Id != notaDev), "Una nota incorporada deja de estar libre");
        Check(await sesiones.ConvertirASintesisAsync(borrador, "Medios") > 0, "Publicar el borrador con nota compartida");
        var finalizadas = await sintesis.GetSesionesFinalizadasParaConsolidarAsync();
        Check(finalizadas.Any(s => s.Id == borrador && SintesisWorkflowPolicy.TieneNotasConsolidables(s)), "Nota compartida disponible para consolidación y Operativo SOL");
        var borradorDev = await sesiones.CrearAsync(divisionId, fecha, "Ampliacion Vespertina", "Desarrollador");
        Check(borradorDev > 0 && await sesiones.AgregarNotaExistenteAsync(borradorDev, notaMedios, "Desarrollador"), "Desarrollador también crea borrador con notas compartidas");
        return checks;
    }
}
