using Medios.Controllers;
using Medios.Entities;
using Medios.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

static class InformarChecks
{
    public static async Task<int> RunAsync()
    {
        int count = 0;
        void Check(bool ok, string mensaje) { if (!ok) throw new InvalidOperationException(mensaje); count++; }
        var factory = new MemoriaFactory();
        var service = new SintesisService(factory, null!, null!);
        int id;
        using (var db = factory.Create())
        {
            var s = new Sintesis { Fecha = new DateOnly(2026, 10, 22), Estado = "Generada" };
            db.Sintesis.Add(s); await db.SaveChangesAsync(); id = s.Id;
        }
        Check(await service.IniciarInformacionAsync(id), "Primera solicitud reserva síntesis");
        Check(!await service.IniciarInformacionAsync(id), "Segunda solicitud no puede reservar envío en curso");
        Check(!await service.EliminarConsolidadaAsync(id), "No permite revertir mientras se informa");
        await service.CancelarInformacionAsync(id, "Generada");
        Check(await service.IniciarInformacionAsync(id), "Permite nuevo intento tras error de envío conocido");
        Check(await service.InformarConCanalesAsync(id, true, null), "Finaliza la publicación reservada");
        var final = (await service.GetByIdAsync(id))!;
        var fecha = final.FechaInformada;
        Check(final.Estado == "Informada" && final.CanalTodosRoles, "Publicada visible para todos los roles");
        Check(!await service.IniciarInformacionAsync(id), "No reserva una síntesis ya informada");
        Check(!await service.InformarConCanalesAsync(id, true, null), "No repite finalización ni cambia la fecha");
        Check((await service.GetByIdAsync(id))!.FechaInformada == fecha, "Repetición conserva fecha original");
        using (var db = factory.Create())
        {
            var s = new Sintesis { Estado = "Generada" }; db.Sintesis.Add(s); await db.SaveChangesAsync(); id = s.Id;
        }
        using (var uno = factory.Create())
        using (var dos = factory.Create())
        {
            var a = (await uno.Sintesis.FindAsync(id))!;
            var b = (await dos.Sintesis.FindAsync(id))!;
            a.Estado = "Informando"; b.Estado = "Informando";
            await uno.SaveChangesAsync();
            try { await dos.SaveChangesAsync(); throw new InvalidOperationException("No detectó carrera de envío"); }
            catch (DbUpdateConcurrencyException) { Check(true, "Reserva concurrente se verifica por Estado en DB"); }
        }
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "MEDIOS"), new Claim(ClaimTypes.Name, "QA") }, "test"));
        var accessor = new HttpContextAccessor { HttpContext = http };
        var audit = new AuditoriaService(factory, accessor);
        var controller = new SintesisController(service, null!, audit, null!, null!, new TrazaService(factory, accessor), null!, null!, new GruposCorreoService(factory))
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new TempProvider())
        };
        await service.CancelarInformacionAsync(id, "Generada");
        await controller.InformarPost(id, todosRoles: false);
        Check((await service.GetByIdAsync(id))!.CanalTodosRoles, "POST fuerza Todos los roles aun si el cliente manda false");
        await controller.InformarPost(id, todosRoles: false);
        using (var db = factory.Create())
            Check(await db.TrazaEventos.CountAsync(t => t.SintesisId == id) == 1, "Doble POST no repite la traza de información");
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "DELEGACION") }, "test"));
        Check(await controller.DescargarWord(id) is ForbidResult, "Delegación no descarga Word por URL directa");
        return count;
    }
    private sealed class TempProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
