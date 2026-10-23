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

        // Publicar varias ampliaciones de La Plata en la misma fecha no ocupa el turno.
        var fechaAmpliaciones = new DateOnly(2026, 10, 23);
        using (var db = factory.Create())
        {
            db.Delegaciones.Add(new Delegacion { Id = 9, Nombre = "LA PLATA" });
            await db.SaveChangesAsync();
        }
        foreach (var tipo in new[] { "Ampliacion Matutina", "Ampliacion Vespertina" })
        {
            for (var numero = 1; numero <= 3; numero++)
            {
                var id = await service.CrearAsync(9, fechaAmpliaciones, tipo, "la-plata");
                Check(id > 0, $"Crear {tipo} número {numero} para La Plata");
                Check(await service.CrearAsync(9, fechaAmpliaciones, tipo, "otro") == 0,
                    "Otro operador de la misma delegación no duplica la ampliación abierta");
                using (var db = factory.Create())
                {
                    var nota = new NotaPrensa { SesionPrensaId = id };
                    nota.VersionActual = new NotaVersion { Nota = nota, EsActual = true, EstadoRevision = "Borrador" };
                    db.NotasPrensa.Add(nota);
                    await db.SaveChangesAsync();
                }
                Check(await service.ConvertirASintesisAsync(id, "la-plata") > 0,
                    $"Remitir {tipo} número {numero} sin bloquear por publicaciones previas");
                using (var db = factory.Create())
                {
                    var sesion = (await db.SesionesPrensas.FindAsync(id))!;
                    Check(sesion.Estado == "Remitida", "Publicar cierra el borrador de la ampliación");
                    // También se permite ampliar después de la revisión/finalización.
                    if (numero == 2) sesion.Estado = "Revisada";
                    if (numero == 3) sesion.Estado = "Finalizada";
                    await db.SaveChangesAsync();
                }
            }
            Check(await service.CrearAsync(9, fechaAmpliaciones, tipo, "la-plata") > 0,
                "Una ampliación finalizada permite preparar la siguiente");
            using (var db = factory.Create())
                Check(await db.Sintesis.CountAsync(s => s.DelegacionId == 9 && s.Fecha == fechaAmpliaciones && s.Tipo == tipo) == 3,
                    "Las tres ampliaciones remitidas conservan registros separados");
        }
        var sinDelegacion = await service.CrearAsync(null, fechaAmpliaciones, "Ampliacion Vespertina", "a");
        Check(sinDelegacion > 0, "Crear ampliación sin delegación por operador");
        Check(await service.CrearAsync(null, fechaAmpliaciones, "Ampliacion Vespertina", "a") == 0,
            "Sin delegación no duplica la ampliación abierta del operador");
        using (var db = factory.Create())
        {
            (await db.SesionesPrensas.FindAsync(sinDelegacion))!.Estado = "Remitida";
            db.SesionesPrensas.Add(new SesionPrensa { DelegacionId = 9, Fecha = fechaAmpliaciones, Turno = "Matutina", Estado = "Remitida" });
            db.SesionesPrensas.Add(new SesionPrensa { DelegacionId = 9, Fecha = fechaAmpliaciones, Turno = "Vespertina", Estado = "Finalizada" });
            await db.SaveChangesAsync();
        }
        Check(await service.CrearAsync(null, fechaAmpliaciones, "Ampliacion Vespertina", "a") > 0,
            "Sin delegación permite otra ampliación después de remitir");
        foreach (var tipo in new[] { "Matutina", "Vespertina" })
            Check(await service.CrearAsync(9, fechaAmpliaciones, tipo, "la-plata") == 0,
                "El turno principal sigue ocupado aunque su sesión ya no sea borrador");
        return checks;
    }
}
