using Medios.Entities;
using Medios.Infrastructure.Data;
using Medios.Services;
using Microsoft.EntityFrameworkCore;

var comprobaciones = 0;
void Verificar(bool condicion, string caso)
{
    if (!condicion) throw new InvalidOperationException(caso);
    comprobaciones++;
}

var factory = new MemoriaFactory();
var sintesisService = new SintesisService(factory, null!, null!);
var sesionService = new SesionService(factory, null!, sintesisService, null!);
var fecha = new DateOnly(2026, 10, 2);
int sesionId;
using (var db = factory.Create())
{
    var delegacion = new Delegacion { Nombre = "Delegación de prueba" };
    var sesion = new SesionPrensa
    {
        Fecha = fecha, Turno = "Vespertina", UsuarioCarga = "operador",
        Delegacion = delegacion
    };
    db.SesionesPrensas.Add(sesion);
    for (var i = 0; i < 2; i++)
    {
        var nota = new NotaPrensa { Sesion = sesion };
        var version = new NotaVersion { Nota = nota, EsActual = true, EstadoRevision = "Borrador" };
        nota.VersionActual = version;
        db.NotasPrensa.Add(nota);
    }
    // Residuos del flujo anterior: deben conservarse, pero no contarse como remisiones.
    db.SesionesPrensas.AddRange(
        new SesionPrensa { Fecha = fecha, Turno = "Vespertina", Estado = "Remitida", Delegacion = delegacion },
        new SesionPrensa { Fecha = fecha, Turno = "Vespertina", Estado = "Remitida", Delegacion = delegacion });
    await db.SaveChangesAsync();
    sesionId = sesion.Id;
}

for (var ciclo = 0; ciclo < 3; ciclo++)
{
    var sintesisId = await sesionService.ConvertirASintesisAsync(sesionId, "operador");
    Verificar(sintesisId > 0, "Publicar el borrador");
    var bandeja = await sesionService.GetBandejaAnalistaAsync();
    Verificar(bandeja.Count == 1 && bandeja[0].Notas.Count == 2, "Una remisión con dos notas, sin residuos vacíos");
    Verificar((await sesionService.GetBandejaAsync()).Count == 1, "La otra bandeja tampoco cuenta residuos");
    Verificar((await sesionService.GetMetricasAsync()).SesionesRevisadas == 1, "Inicio cuenta la misma remisión con notas que la Bandeja y excluye residuos vacíos");

    int? delegacionId;
    using (var db = factory.Create())
        delegacionId = (await db.Sintesis.FindAsync(sintesisId))!.DelegacionId;
    var devuelta = await sintesisService.ConvertirABorradorAsync(sintesisId, "operador", delegacionId);
    Verificar(devuelta == sesionId, "Modificar conserva la sesión de origen");
    using (var db = factory.Create())
    {
        Verificar(await db.SesionesPrensas.CountAsync() == 3, "No crear sesiones adicionales al modificar varias veces");
        Verificar(!await db.Sintesis.AnyAsync(), "Quitar la síntesis anterior");
        var sesion = (await db.SesionesPrensas.FindAsync(sesionId))!;
        Verificar(sesion.Estado == "Borrador" && sesion.FechaRemision == null, "Restablecer el estado del borrador");
        Verificar(await db.NotasPrensa.CountAsync(n => n.SesionPrensaId == sesionId) == 2, "Conservar ambas notas vinculadas");
    }
}

// Una síntesis que reúne notas de distintas sesiones necesita un borrador nuevo.
int mixtaId;
using (var db = factory.Create())
{
    var origen = (await db.SesionesPrensas.FindAsync(sesionId))!;
    var otra = new SesionPrensa { Fecha = fecha, UsuarioCarga = "otro", DelegacionId = origen.DelegacionId };
    db.SesionesPrensas.Add(otra);
    var notas = await db.NotasPrensa.OrderBy(n => n.Id).ToListAsync();
    notas[1].Sesion = otra;
    var mixta = new Sintesis { Fecha = fecha, Tipo = "Vespertina", DelegacionId = origen.DelegacionId };
    foreach (var nota in notas)
        mixta.NotasIncluidas.Add(new SintesisNota { Nota = nota, NotaVersionId = nota.VersionActualId!.Value });
    db.Sintesis.Add(mixta);
    await db.SaveChangesAsync();
    mixtaId = mixta.Id;
}
int? delegacionMixta;
using (var db = factory.Create()) delegacionMixta = (await db.Sintesis.FindAsync(mixtaId))!.DelegacionId;
var nuevaId = await sintesisService.ConvertirABorradorAsync(mixtaId, "operador", delegacionMixta);
Verificar(nuevaId > 0 && nuevaId != sesionId, "Crear borrador para notas de distintos orígenes");
using (var db = factory.Create())
    Verificar(await db.NotasPrensa.CountAsync(n => n.SesionPrensaId == nuevaId) == 2, "Resolver FK de sesión nueva en un SaveChanges");

// El envío efectivo de una síntesis no puede deshacerse mediante Modificar.
int remitidaId;
using (var db = factory.Create())
{
    var remitida = new Sintesis { Fecha = fecha, Estado = "Remitida", DelegacionId = delegacionMixta };
    foreach (var nota in await db.NotasPrensa.ToListAsync())
        remitida.NotasIncluidas.Add(new SintesisNota { Nota = nota, NotaVersionId = nota.VersionActualId!.Value });
    db.Sintesis.Add(remitida);
    await db.SaveChangesAsync();
    remitidaId = remitida.Id;
}
Verificar(await sintesisService.ConvertirABorradorAsync(remitidaId, "operador", delegacionMixta) == -1,
    "No modificar una síntesis remitida");
using (var db = factory.Create())
    Verificar(await db.Sintesis.AnyAsync(s => s.Id == remitidaId && s.Estado == "Remitida")
        && await db.NotasPrensa.CountAsync(n => n.SesionPrensaId == nuevaId) == 2,
        "Conservar la síntesis remitida y sus notas");

// MEDIOS y DELEGACION guardan notas libres sin crear sesiones automáticamente.
var libresFactory = new MemoriaFactory();
var notasService = new NotaService(libresFactory);
var libresSintesis = new SintesisService(libresFactory, null!, notasService);
var libresSesiones = new SesionService(libresFactory, null!, libresSintesis, notasService);
var notaMedios = await notasService.AgregarLibreAsync(null, null, null, "Título", "Texto", "Fuente", null,
    false, "Nombre Medios", delegacionId: 1, aprobadaDirectamente: true);
var notaDelegacion = await notasService.AgregarLibreAsync(null, null, null, "Título", "Texto", "Fuente", null,
    false, "Nombre Delegación", delegacionId: 2);
using (var db = libresFactory.Create())
{
    Verificar(!await db.SesionesPrensas.AnyAsync() && !await db.Sintesis.AnyAsync(), "Crear notas no crea borradores ni síntesis");
    Verificar(await db.NotasPrensa.CountAsync(n => n.SesionPrensaId == null) == 2, "Ambas notas quedan libres");
    Verificar(await db.NotasVersion.AnyAsync(v => v.NotaId == notaMedios && v.EstadoRevision == "Aprobada"), "MEDIOS conserva aprobación directa");
    Verificar(await db.NotasVersion.AnyAsync(v => v.NotaId == notaDelegacion && v.EstadoRevision == "Sin Remitir"), "DELEGACION conserva su flujo");
}
Verificar((await notasService.GetNotasLibresAsync("Nombre Medios")).Single().Id == notaMedios, "Listar la nota propia de MEDIOS");
Verificar(await notasService.EditarLibreAsync(notaMedios, "Nombre Medios", null, null, null, "Editada", "Texto", "Fuente", null, false), "Editar nota libre de MEDIOS");
using (var db = libresFactory.Create())
    Verificar(await db.NotasVersion.AnyAsync(v => v.NotaId == notaMedios && v.EsActual && v.EstadoRevision == "Aprobada"), "Editar mantiene aprobación");
var borradorMedios = await libresSesiones.CrearAsync(1, fecha, "Matutina", "medios");
Verificar(!await libresSesiones.AgregarNotaExistenteAsync(borradorMedios, notaDelegacion, "medios"), "No incorporar nota de otra delegación");
Verificar(await libresSesiones.AgregarNotaExistenteAsync(borradorMedios, notaMedios, "medios"), "MEDIOS agrega después su nota al borrador");
Verificar(!(await notasService.GetNotasLibresAsync("Nombre Medios")).Any(), "La nota incorporada sale de Mis Notas libres");

// El cierre requiere una decisión explícita para cada nota.
var cierreFactory = new MemoriaFactory();
var cierreService = new SesionService(cierreFactory, null!, null!, null!);
int cierreId;
using (var db = cierreFactory.Create())
{
    var sesion = new SesionPrensa { Estado = "Remitida", Fecha = fecha };
    var nota = new NotaPrensa { Sesion = sesion };
    var version = new NotaVersion { Nota = nota, EsActual = true, EstadoRevision = "Remitida" };
    nota.VersionActual = version;
    db.NotasPrensa.Add(nota);
    await db.SaveChangesAsync();
    cierreId = sesion.Id;
}
foreach (var estado in new[] { "Remitida", "Pendiente", "Borrador", "Sin Remitir", "Aprobada", "Descartada", "Agregada", "Finalizada" })
{
    using (var db = cierreFactory.Create())
    {
        var version = await db.NotasVersion.SingleAsync();
        version.EstadoRevision = estado;
        await db.SaveChangesAsync();
    }
    var atendida = estado is "Aprobada" or "Descartada" or "Agregada" or "Finalizada";
    Verificar(await cierreService.TodasNotasRevisadasAsync(cierreId) == atendida, "Finalizar depende de atender la nota: " + estado);
    if (!atendida)
        Verificar(!await cierreService.MarcarFinalizadaAsync(cierreId), "El servidor bloquea finalizar con nota " + estado);
}
Verificar(!SintesisWorkflowPolicy.PuedeFinalizar(new SesionPrensa { Estado = "Remitida" }), "Una sesión vacía no puede finalizarse");
var incompleta = new SesionPrensa { Estado = "Remitida" };
incompleta.Notas.Add(new NotaPrensa());
Verificar(!SintesisWorkflowPolicy.PuedeFinalizar(incompleta), "Una nota sin versión no está atendida");
incompleta.Notas.Add(new NotaPrensa { VersionActual = new NotaVersion { EstadoRevision = "Aprobada" } });
Verificar(!SintesisWorkflowPolicy.PuedeFinalizar(incompleta), "Aprobar una nota no habilita finalizar si otra sigue pendiente");

comprobaciones += await MockLoginChecks.RunAsync();
comprobaciones += await SintesisMediosChecks.RunAsync();
comprobaciones += await RechazadasChecks.RunAsync();
comprobaciones += await RecuperacionChecks.RunAsync();
comprobaciones += await BorradorUnicidadChecks.RunAsync();
comprobaciones += await CategoriaChecks.RunAsync();
comprobaciones += await ArchivoPdfChecks.RunAsync();
comprobaciones += await PublicadasChecks.RunAsync();
comprobaciones += await InformarChecks.RunAsync();
comprobaciones += await FiltradasChecks.RunAsync();
comprobaciones += WordChecks.Run(args.Length >= 2 && args[0] == "--word-samples" ? args[1] : null);
if (args.Length >= 2 && args[0] == "--pdf-sample")
    await SintesisMediosChecks.GenerarMuestraPdfAsync(args[1], args.Length > 2 ? args[2] : "Especial");
Console.WriteLine($"OK: {comprobaciones} comprobaciones de login Mock, notas, publicación, reversión, consolidación y modelo PDF.");

sealed class MemoriaFactory : IMediosDbContextFactory
{
    private readonly DbContextOptions<MediosDbContext> _options = new DbContextOptionsBuilder<MediosDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
    public MediosDbContext Create() => new(_options);
}
