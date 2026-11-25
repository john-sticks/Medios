using System.Reflection;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using Medios.Controllers;
using Medios.Entities;
using Medios.Models;
using Medios.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

static class OperativoSolChecks
{
    public static async Task<int> RunAsync()
    {
        int checks = 0;
        void Check(bool ok, string mensaje) { if (!ok) throw new InvalidOperationException(mensaje); checks++; }
        var factory = new MemoriaFactory();
        var servicio = new OperativoSolService(factory);
        using (var db = factory.Create())
        {
            db.Partidos.AddRange(new Partido { IdPartido = 1, Nombre = "GENERAL PUEYRREDON" },
                new Partido { IdPartido = 2, Nombre = "LA PLATA" });
            await db.SaveChangesAsync();
        }
        var inicio = new DateOnly(2026, 12, 15);
        var fin = new DateOnly(2027, 3, 31);
        var sol = new OperativoSolModel { Descripcion = "OPERATIVO SOL 2026/27", FechaDesde = inicio,
            FechaHasta = fin, PartidoIds = new() { 1, 1 } };
        await servicio.GuardarAsync(sol);
        Check((await servicio.ListarAsync()).Single().PartidoIds.SequenceEqual(new[] { 1 }), "Alta sin partidos duplicados");
        Check((await servicio.PartidosVigentesAsync(inicio)).SetEquals(new[] { 1 }), "Fecha inicial inclusiva");
        Check((await servicio.PartidosVigentesAsync(fin)).SetEquals(new[] { 1 }), "Fecha final inclusiva");
        Check((await servicio.PartidosVigentesAsync(inicio.AddDays(-1))).Count == 0, "No agrupa antes del operativo");
        Check((await servicio.PartidosVigentesAsync(fin.AddDays(1))).Count == 0, "No agrupa después del operativo");
        sol.FechaHasta = null;
        await servicio.GuardarAsync(sol);
        Check((await servicio.PartidosVigentesAsync(fin.AddYears(2))).Contains(1), "Fecha final opcional");
        sol.Activo = false;
        await servicio.GuardarAsync(sol);
        Check((await servicio.PartidosVigentesAsync(inicio)).Count == 0, "Desactivar excluye el operativo");
        sol.Activo = true; sol.PartidoIds = new() { 2 };
        await servicio.GuardarAsync(sol);
        Check((await servicio.PartidosVigentesAsync(inicio)).SetEquals(new[] { 2 }) && (await servicio.ListarAsync()).Count == 1,
            "Editar reemplaza partidos y conserva el operativo");
        async Task Rechaza(OperativoSolModel valor, string caso)
        {
            try { await servicio.GuardarAsync(valor); throw new InvalidOperationException(caso); }
            catch (ArgumentException) { checks++; }
        }
        await Rechaza(new() { Descripcion = "Inválido", FechaDesde = inicio, FechaHasta = inicio.AddDays(-1), PartidoIds = new() { 1 } }, "Rechazar rango invertido");
        await Rechaza(new() { Descripcion = "Inválido", FechaDesde = inicio, PartidoIds = new() { 999 } }, "Rechazar partido inexistente");
        await Rechaza(new() { Descripcion = "Inválido", FechaDesde = inicio }, "Rechazar sin partidos");
        await Rechaza(new() { Descripcion = " ", FechaDesde = inicio, PartidoIds = new() { 1 } }, "Rechazar descripción vacía");
        await Rechaza(new() { Descripcion = "Inválido", PartidoIds = new() { 1 } }, "Rechazar fecha ausente");
        await servicio.GuardarAsync(new() { Descripcion = "Superpuesto", FechaDesde = inicio, PartidoIds = new() { 1, 2 } });
        Check((await servicio.PartidosVigentesAsync(inicio)).SetEquals(new[] { 1, 2 }), "Operativos superpuestos unen partidos sin duplicados");
        SintesisNota Nota(int id, int? partido, string ambito) => new() { Orden = id, NotaVersion = new()
            { Id = id, PartidoId = partido, AmbitoNota = ambito, Titulo = "NOTA " + id, Texto = "Contenido " + id,
                Partido = partido.HasValue ? new Partido { IdPartido = partido.Value, Nombre = partido == 1 ? "GENERAL PUEYRREDON" : "LA PLATA" } : null } };
        var notas = new List<SintesisNota> { Nota(1, 1, "Partido"), Nota(2, 2, "Partido"), Nota(3, null, "Nacional"), Nota(4, 1, "Provincial") };
        var grupos = SintesisPdfModel.AgruparConOperativo(notas, new HashSet<int> { 1 });
        Check(grupos.SelectMany(g => g.Categorias).SelectMany(c => c.Notas).Select(n => n.Id).Order().SequenceEqual(new[] { 1, 2, 3, 4 }), "Ninguna nota se pierde o duplica");
        Check(grupos.Where(g => g.Bloque != null).Select(g => g.Bloque).SequenceEqual(new[] { "OPERATIVO SOL", "SÍNTESIS DE PRENSA GENERAL" }), "Orden y títulos de bloques");
        Check(grupos.TakeWhile(g => !g.SectionId.StartsWith("general-")).SelectMany(g => g.Categorias).SelectMany(c => c.Notas).All(n => n.PartidoId == 1), "SOL contiene únicamente sus partidos");
        Check(grupos.Select(g => g.SectionId).Distinct().Count() == grupos.Count, "Referencias del índice únicas");
        Check(SintesisPdfModel.AgruparConOperativo(notas, new HashSet<int>()).All(g => g.Bloque == null), "Sin coincidencias conserva el documento habitual");
        var word = SintesisWordDocument.Crear(new() { Fecha = inicio }, notas, Path.GetTempPath(), grupos);
        using (var stream = new MemoryStream(word))
        using (var doc = WordprocessingDocument.Open(stream, false))
        {
            var texto = doc.MainDocumentPart!.Document.InnerText;
            Check(texto.Contains("OPERATIVO SOL") && texto.Contains("SÍNTESIS DE PRENSA GENERAL"), "Word incluye los dos bloques");
            Check(!new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(doc).Any(), "Word válido con índice y marcadores de bloques");
        }
        var roles = typeof(OperativosSolController).GetCustomAttribute<AuthorizeAttribute>()?.Roles;
        Check(roles == "DESARROLLADOR,MEDIOS", "Acceso restringido a Desarrollador y Medios");
        Check(typeof(OperativosSolController).GetMethod("Guardar")!.IsDefined(typeof(ValidateAntiForgeryTokenAttribute)), "Altas y cambios protegidos contra CSRF");
        return checks;
    }
}
