using Medios.Entities;
using Medios.Services;
using Microsoft.EntityFrameworkCore;

static class BorradorUnicidadChecks
{
    public static async Task<int> RunAsync()
    {
        var checks = 0;
        void Check(bool value, string name)
        {
            if (!value) throw new InvalidOperationException(name);
            checks++;
        }
        var factory = new MemoriaFactory();
        var service = new SesionService(factory, null!, null!, null!);
        var fecha = new DateOnly(2026, 9, 28);
        using (var db = factory.Create())
        {
            db.Sintesis.AddRange(
                new Sintesis { DelegacionId = 1, Fecha = fecha, Tipo = "Vespertina", Estado = "Remitida" },
                new Sintesis { DelegacionId = 1, Fecha = fecha, Tipo = "Matutina", Estado = "Generada" });
            await db.SaveChangesAsync();
        }
        Check(await service.CrearAsync(1, fecha, "Vespertina", "medios") == 0,
            "No crear borrador cuando existe una Vespertina sin sesión de origen");
        Check(await service.CrearAsync(1, fecha, "Matutina", "otro") == 0,
            "La Matutina publicada bloquea también a otros operadores de la delegación");
        using (var db = factory.Create())
            Check(!await db.SesionesPrensas.AnyAsync(), "El rechazo no crea sesiones residuales");
        var ampliacion = await service.CrearAsync(1, fecha, "Ampliacion Vespertina", "medios");
        Check(ampliacion > 0, "Permitir una ampliación del turno publicado");
        Check(await service.CrearAsync(2, fecha, "Vespertina", "delegacion") > 0,
            "Otra delegación puede crear su Vespertina");
        Check(await service.CrearAsync(1, fecha.AddDays(1), "Vespertina", "medios") > 0,
            "Permitir Vespertina para otra fecha");
        Check(await service.CrearAsync(1, fecha.AddDays(1), "Vespertina", "otro") == 0,
            "No duplicar un borrador existente de la misma delegación");
        Check(await service.CrearAsync(null, fecha, "Vespertina", "a") > 0,
            "Sin delegación mantener la unicidad por operador");
        Check(await service.CrearAsync(null, fecha, "Vespertina", "b") > 0,
            "Sin delegación no bloquear otros operadores");
        Check(await service.CrearAsync(null, fecha, "Vespertina", "a") == 0,
            "Sin delegación no duplicar un borrador propio");
        int historicoId, remitidaId;
        using (var db = factory.Create())
        {
            var historico = new SesionPrensa { UsuarioCarga = "MEDIOS", Estado = "Borrador" };
            var remitida = new SesionPrensa { UsuarioCarga = "MEDIOS", Estado = "Remitida" };
            db.SesionesPrensas.AddRange(historico, remitida);
            await db.SaveChangesAsync();
            historicoId = historico.Id; remitidaId = remitida.Id;
        }
        Check(!await service.EliminarBorradorAsync(historicoId, "otro"), "No eliminar borradores de otro usuario");
        Check(!await service.EliminarBorradorAsync(remitidaId, "medios"), "No eliminar una sesión remitida");
        Check(await service.EliminarBorradorAsync(historicoId, "medios"), "Eliminar borrador histórico con distinta capitalización del usuario");
        return checks;
    }
}
