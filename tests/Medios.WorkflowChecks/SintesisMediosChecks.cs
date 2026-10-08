using Medios.Entities;
using Medios.Infrastructure.Data;
using Medios.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using QuestPDF.Infrastructure;

static class SintesisMediosChecks
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
        var notas = new NotaService(factory);
        var sintesis = new SintesisService(factory, null!, notas);
        var sesiones = new SesionService(factory, null!, sintesis, notas);
        int sesionId;
        using (var db = factory.Create())
        {
            var division = new Delegacion { Nombre = "División Medios" };
            var sesion = new SesionPrensa { Delegacion = division, UsuarioCarga = "medios", Estado = "Borrador", Turno = "Especial", Fecha = new DateOnly(2026, 10, 8) };
            foreach (var estado in new[] { "Borrador", "Aprobada", "Remitida", "Sin Remitir" })
            {
                var nota = new NotaPrensa { Sesion = sesion };
                nota.VersionActual = new NotaVersion { Nota = nota, EsActual = true, EstadoRevision = estado };
                db.NotasPrensa.Add(nota);
            }
            await db.SaveChangesAsync();
            sesionId = sesion.Id;
        }
        Check(await sesiones.ConvertirASintesisAsync(sesionId, "otro") == -1, "No publicar el borrador de otro usuario");
        var publicada = await sesiones.ConvertirASintesisAsync(sesionId, "medios");
        Check(publicada > 0, "Publicar síntesis propia de MEDIOS");
        Check(!(await sesiones.GetMiasAsync("medios")).Any(s => s.Estado == "Borrador"), "La publicación sale de Mis Borradores");
        var finalizadas = await sintesis.GetSesionesFinalizadasParaConsolidarAsync();
        Check(finalizadas.Single().Notas.Count == 4 && finalizadas[0].Notas.All(n => SintesisWorkflowPolicy.PuedeConsolidarNota(finalizadas[0], n)), "Todas las notas propias publicadas se pueden consolidar");
        Check(await sesiones.ConvertirASintesisAsync(sesionId, "medios") == -1, "No volver a publicar el mismo borrador");
        using (var db = factory.Create())
        {
            Check(await db.Sintesis.CountAsync() == 1, "Publicar una vez conserva una sola síntesis");
            (await db.Sintesis.FindAsync(publicada))!.Estado = "Remitida";
            await db.SaveChangesAsync();
        }
        Check(await sesiones.RevertirFinalizadaABorradorAsync(sesionId), "Finalizada propia históricamente Remitida puede volver a borrador");
        using (var db = factory.Create())
        {
            Check(!await db.Sintesis.AnyAsync() && !await db.SintesisNotas.AnyAsync(), "Revertir elimina publicación propia y enlaces");
            Check(await db.NotasPrensa.CountAsync() == 4 && (await db.SesionesPrensas.FindAsync(sesionId))!.Estado == "Borrador", "Revertir conserva notas y vuelve al borrador");
        }
        publicada = await sesiones.ConvertirASintesisAsync(sesionId, "medios");
        using (var db = factory.Create())
        {
            var informada = new Sintesis { Estado = "Informada" };
            foreach (var n in await db.NotasPrensa.Include(n => n.VersionActual).ToListAsync())
                informada.NotasIncluidas.Add(new SintesisNota { Nota = n, NotaVersion = n.VersionActual! });
            db.Sintesis.Add(informada);
            await db.SaveChangesAsync();
        }
        Check(!await sesiones.RevertirFinalizadaABorradorAsync(sesionId), "No deshacer contenido incluido en una publicación informada");
        var legacy = new SesionPrensa { Estado = "Finalizada", Delegacion = new Delegacion { Nombre = "División Medios" } };
        foreach (var state in new[] { "Borrador", "Remitida" })
            Check(SintesisWorkflowPolicy.PuedeConsolidarNota(legacy, new NotaPrensa { VersionActual = new NotaVersion { EstadoRevision = state } }), "Incluir notas históricas propias " + state);
        Check(!SintesisWorkflowPolicy.PuedeConsolidarNota(legacy, new NotaPrensa { VersionActual = new NotaVersion { EstadoRevision = "Descartada" } }), "Excluir descartadas de consolidación");
        Check(!SintesisWorkflowPolicy.PuedeConsolidarNota(new SesionPrensa { Estado = "Borrador" }, new NotaPrensa { VersionActual = new NotaVersion { EstadoRevision = "Aprobada" } }), "No consolidar un borrador pendiente");

        var modelo = DatosModelo();
        var grupos = SintesisPdfModel.Agrupar(modelo);
        Check(grupos.Select(g => g.Titulo).SequenceEqual(new[] { "ÁMBITO NACIONAL", "ÁMBITO PROVINCIAL", "ÁMBITO LA PLATA", "ÁMBITO MAR DEL PLATA" }), "Orden de ámbitos y partido elegido");
        Check(grupos.Take(2).All(g => g.Categorias.Single().Titulo == null), "Nacional y Provincial no muestran Sin categoría");
        Check(grupos[2].Categorias.Select(c => c.Titulo).SequenceEqual(new string?[] { null, "INSTITUCIONALES", "ÁMBITO PENITENCIARIO", "ÁMBITO DEPORTIVO", "REPERCUSIONES PERIODÍSTICAS" }), "Orden del modelo prima sobre orden configurado de categorías");
        Check(!grupos.SelectMany(g => g.Categorias).Any(c => c.Titulo == "DENUNCIAS"), "No agregar categorías vacías");
        var conVideo = modelo.First(sn => sn.NotaVersion.VideoUrl != null).NotaVersion;
        Check(SintesisPdfModel.LinkVisible(conVideo) == null, "Ocultar enlaces cuando existe video");
        Check(SintesisPdfModel.CuerpoConFuente(conVideo) == "Resumen de prueba. (DIARIO, OTRO MEDIO)", "Fuente inmediatamente después del texto, sin salto de línea ni marca IA");
        return checks;
    }

    private static List<SintesisNota> DatosModelo()
    {
        var laPlata = new Partido { IdPartido = 1, Nombre = "La Plata" };
        var marDelPlata = new Partido { IdPartido = 2, Nombre = "Mar del Plata" };
        var especificaciones = new[]
        {
            ("Partido", "Repercusiones Periodísticas", laPlata),
            ("Partido", "Ámbito Deportivo", laPlata),
            ("Provincial", "", laPlata),
            ("Partido", "Ámbito Penitenciario", laPlata),
            ("Partido", "Institucionales", marDelPlata),
            ("Partido", "Ámbito Partido", laPlata),
            ("Nacional", "", laPlata),
            ("Partido", "Institucionales", laPlata)
        };
        return especificaciones.Select((dato, index) => new SintesisNota
        {
            Orden = index,
            NotaVersion = new NotaVersion
            {
                AmbitoNota = dato.Item1, Partido = dato.Item3,
                Categoria = dato.Item2.Length > 0 ? new CategoriaNoticia { Nombre = dato.Item2, Orden = index } : null,
                Titulo = "Nota de prueba " + index,
                Texto = "Texto original de prueba.", Sintesis = "Resumen de prueba.\n",
                Fuente = "Diario", OtrosMedios = "Otro medio",
                Link = "https://ejemplo.invalid/noticia", VideoUrl = "https://ejemplo.invalid/video",
                EstadoRevision = "Finalizada", EsActual = true
            }
        }).ToList();
    }

    public static async Task GenerarMuestraPdfAsync(string directorio)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        Directory.CreateDirectory(directorio);
        var env = new PdfEnvironment { WebRootPath = Path.GetFullPath(directorio) };
        // Registrar fuentes Bookman del NAS cuando se suministran junto al directorio de muestra.
        foreach (var fuente in Directory.GetFiles(directorio, "*.otf"))
        {
            using var stream = File.OpenRead(fuente);
            QuestPDF.Drawing.FontManager.RegisterFont(stream);
        }
        var factory = new MemoriaFactory();
        int id;
        using (var db = factory.Create())
        {
            var sintesis = new Sintesis { Fecha = new DateOnly(2026, 10, 8), Tipo = "Especial", OperadorGenera = "Operador de prueba" };
            foreach (var incluida in DatosModelo())
            {
                var nota = new NotaPrensa();
                incluida.NotaVersion.Nota = nota;
                nota.VersionActual = incluida.NotaVersion;
                incluida.Nota = nota;
                sintesis.NotasIncluidas.Add(incluida);
            }
            db.Sintesis.Add(sintesis);
            await db.SaveChangesAsync();
            id = sintesis.Id;
        }
        var servicio = new SintesisService(factory, env, new NotaService(factory));
        Console.WriteLine("PDF de muestra: " + await servicio.GenerarPdfAsync(id));
    }

    private sealed class PdfEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Medios.PdfChecks";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = "";
        public string EnvironmentName { get; set; } = "Testing";
    }
}
