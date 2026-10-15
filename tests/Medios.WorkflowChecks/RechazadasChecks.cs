using Medios.Entities;
using Medios.Services;
using Microsoft.EntityFrameworkCore;

static class RechazadasChecks
{
    public static async Task<int> RunAsync()
    {
        var checks = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            checks++;
        }
        var factory = new MemoriaFactory();
        var service = new NotaService(factory);
        int notaId, destinoId, ajenoId, otraDelegacionId, vinculadaId;
        using (var db = factory.Create())
        {
            var origen = new SesionPrensa { DelegacionId = 1, Estado = "Finalizada" };
            var destino = new SesionPrensa { DelegacionId = 1, Estado = "Borrador", UsuarioCarga = "delegacion" };
            var ajeno = new SesionPrensa { DelegacionId = 1, Estado = "Borrador", UsuarioCarga = "otro" };
            var otra = new SesionPrensa { DelegacionId = 2, Estado = "Borrador", UsuarioCarga = "delegacion" };
            var nota = new NotaPrensa { Sesion = origen };
            nota.VersionActual = new NotaVersion { Nota = nota, Version = 1, EstadoRevision = "Sin Remitir",
                MotivoDescarte = "FALTA INFO", Titulo = "Título", Texto = "Texto", Fuente = "Fuente",
                AmbitoNota = "Nacional", Sintesis = "Resumen", VideoUrl = "https://video.example", OtrosMedios = "Otro medio" };
            var vinculada = new NotaPrensa { Sesion = origen };
            vinculada.VersionActual = new NotaVersion { Nota = vinculada, Version = 1,
                EstadoRevision = "Sin Remitir", MotivoDescarte = "FALTA INFO" };
            var snapshot = new Sintesis { Estado = "Informada" };
            snapshot.NotasIncluidas.Add(new SintesisNota { Nota = vinculada, NotaVersion = vinculada.VersionActual });
            db.SesionesPrensas.AddRange(origen, destino, ajeno, otra);
            db.NotasPrensa.AddRange(nota, vinculada);
            db.Sintesis.Add(snapshot);
            await db.SaveChangesAsync();
            notaId = nota.Id; destinoId = destino.Id; ajenoId = ajeno.Id;
            otraDelegacionId = otra.Id; vinculadaId = vinculada.Id;
        }
        Check((await service.GetRechazadasAsync(1)).Count == 2, "Listar rechazos con delegación heredada de la sesión");
        Check(!(await service.GetRechazadasAsync(2)).Any(), "No listar rechazos de otra delegación");
        Check(!await service.VincularRechazadaAsync(notaId, ajenoId, 1, "delegacion", "Nombre"), "No usar borrador de otro usuario");
        Check(!await service.VincularRechazadaAsync(notaId, otraDelegacionId, 1, "delegacion", "Nombre"), "No usar borrador de otra delegación");
        Check(!await service.VincularRechazadaAsync(vinculadaId, destinoId, 1, "delegacion", "Nombre"), "No mover una nota con snapshot publicado");
        Check(!await service.EliminarRechazadaAsync(vinculadaId, 1), "No eliminar una nota con snapshot publicado");
        Check(!await service.EliminarRechazadaAsync(notaId, 2), "No eliminar una nota de otra delegación");
        Check(await service.VincularRechazadaAsync(notaId, destinoId, 1, "delegacion", "Nombre"), "Vincular rechazo a borrador propio");
        using (var db = factory.Create())
        {
            var nota = await db.NotasPrensa.Include(n => n.VersionActual).Include(n => n.Versiones).SingleAsync(n => n.Id == notaId);
            Check(nota.SesionPrensaId == destinoId && nota.DelegacionId == 1, "Mover la nota original sin duplicarla");
            Check(nota.VersionActual!.EstadoRevision == "Borrador" && nota.VersionActual.MotivoDescarte == null, "Dejar la nota lista para corregir y reenviar");
            Check(nota.Versiones.Count == 2 && nota.Versiones.Single(v => v.Version == 1).MotivoDescarte == "FALTA INFO", "Conservar motivo de rechazo en el historial");
            Check(nota.VersionActual.Sintesis == "Resumen" && nota.VersionActual.VideoUrl == "https://video.example"
                && nota.VersionActual.OtrosMedios == "Otro medio", "Conservar contenido y medios al vincular");
        }
        Check((await service.GetRechazadasAsync(1)).Count == 1, "La nota vinculada sale de Descartadas");
        Check(!await service.VincularRechazadaAsync(notaId, destinoId, 1, "delegacion", "Nombre"), "No recuperar dos veces la misma nota");
        Check(!await service.EliminarRechazadaAsync(notaId, 1), "No eliminar desde Descartadas una nota ya recuperada");
        var sesiones = new SesionService(factory, null!, null!, service);
        int historicoId, notaHistoricaId;
        using (var db = factory.Create())
        {
            var historico = new SesionPrensa { UsuarioCarga = "MEDIOS", DelegacionId = 1, Estado = "Borrador" };
            var nota = new NotaPrensa { Sesion = historico };
            nota.VersionActual = new NotaVersion { Nota = nota, Version = 1, Usuario = "Usuario Medios",
                EstadoRevision = "Borrador", Titulo = "Nota que se conserva", Texto = "Contenido" };
            db.NotasPrensa.Add(nota);
            await db.SaveChangesAsync();
            historicoId = historico.Id; notaHistoricaId = nota.Id;
        }
        Check(await sesiones.EliminarBorradorAsync(historicoId, "medios", false, "Medios Division"), "Eliminar solo el borrador histórico");
        Check((await service.GetNotasLibresAsync("Medios Division")).Any(n => n.Id == notaHistoricaId), "Mostrar la nota conservada en Mis Notas");
        using (var db = factory.Create())
        {
            var nota = await db.NotasPrensa.Include(n => n.Versiones).SingleAsync(n => n.Id == notaHistoricaId);
            Check(nota.SesionPrensaId == null && nota.DelegacionId == 1, "Conservar la delegación sin el borrador");
            Check(nota.Versiones.Count == 2 && nota.Versiones.Single(v => v.Version == 1).Usuario == "Usuario Medios", "Conservar la versión y el autor histórico");
            Check(!await db.SesionesPrensas.AnyAsync(s => s.Id == historicoId), "Eliminar el borrador sin eliminar la nota");
        }
        Check(await sesiones.AgregarNotaExistenteAsync(destinoId, notaHistoricaId, "delegacion"), "La nota conservada se puede vincular a otro borrador de su delegación");
        return checks;
    }
}
