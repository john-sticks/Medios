using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Medios.Entities;
using Medios.Services;
using W = DocumentFormat.OpenXml.Wordprocessing;

static class WordChecks
{
    public static int Run(string? salida = null)
    {
        var count = 0;
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
            count++;
        }
        var root = Path.GetFullPath("src/Medios/wwwroot");
        foreach (var tipo in new[] { "Matutina", "Vespertina", "Ampliacion Vespertina", "Especial" })
        {
            var sintesis = new Sintesis { Tipo = tipo, Fecha = new DateOnly(2026, 10, 15), Estado = "Informada", PDFPath = "/pdf/existente.pdf" };
            foreach (var (categoria, orden) in new[] { ("Hechos Delictuales", 3), ("Ámbito Nacional", 1), ("Ámbito Provincial", 2), ("Ámbito Partido", 4) })
            {
                var version = new NotaVersion
                {
                    NotaId = orden, Titulo = "Titular editable " + orden,
                    Sintesis = string.Join(" ", Enumerable.Repeat("Una noticia ficticia permite verificar que el texto siga siendo editable y conserve los márgenes, el espaciado y las categorías del documento.", orden == 1 ? 7 : 2)),
                    Categoria = new CategoriaNoticia { Nombre = categoria },
                    Partido = orden >= 3 ? new Partido { Nombre = "La Plata" } : null,
                    Localidad = orden >= 3 ? new Localidad { Nombre = "TOLOSA (LA PLATA)" } : null,
                    Fuente = "Diario", OtrosMedios = "Otro medio",
                    Link = "https://ejemplo.invalid/noticia", VideoUrl = "https://ejemplo.invalid/video"
                };
                sintesis.NotasIncluidas.Add(new SintesisNota { NotaPrensaId = orden, NotaVersion = version, Orden = orden });
            }
            var linea = new NotaLineaTiempo
            {
                Ancla = new NotaVersion { Titulo = "Noticia original", Texto = "Texto original preservado.", Fuente = "Diario" },
                Actualizaciones = new() { new() { Fecha = new DateTime(2026, 10, 15), Turno = tipo, ResumenCambio = "Actualización preservada." } }
            };
            var bytes = SintesisWordDocument.Crear(sintesis, sintesis.NotasIncluidas.ToList(), new Dictionary<int, NotaLineaTiempo> { [3] = linea }, root);
            using var doc = WordprocessingDocument.Open(new MemoryStream(bytes), false);
            var errors = new OpenXmlValidator().Validate(doc).ToList();
            Check(errors.Count == 0, "Word válido: " + string.Join("; ", errors.Select(e => e.Description + " " + e.Path?.XPath)));
            var body = doc.MainDocumentPart!.Document.Body!;
            var texto = body.InnerText;
            Check(texto.Contains("Titular".ToUpperInvariant()), "El texto del Word es editable");
            Check(texto.Contains("(DIARIO, OTRO MEDIO)"), "Fuente al final del texto");
            Check(!texto.Contains("ejemplo.invalid") && !texto.Contains("Síntesis IA"), "No agrega links de video ni etiqueta IA");
            Check(texto.Contains("ÁMBITO LA PLATA") && texto.Contains("HECHOS DELICTUALES"), "Word agrupa por categoría y partido");
            Check(texto.IndexOf("ÁMBITO NACIONAL") < texto.IndexOf("ÁMBITO PROVINCIAL"), "Conservar orden del índice");
            Check(texto.Contains("Noticia original".ToUpperInvariant()) && texto.Contains("Actualización preservada."), "Conservar ancla e historial");
            Check(texto.Contains("información suministrada") == (tipo == "Vespertina"), "Aviso solo en Vespertina");
            Check(doc.MainDocumentPart.ImageParts.Any() == (tipo != "Especial"), "Portada según tipo");
            Check(body.Elements<W.SectionProperties>().Single().GetFirstChild<W.PageSize>()!.Width?.Value == 11906, "Word usa A4 como el PDF");
            Check(sintesis.Estado == "Informada" && sintesis.PDFPath == "/pdf/existente.pdf", "Exportar Word no regenera el PDF ni cambia el estado");
            if (salida != null)
            {
                Directory.CreateDirectory(salida);
                File.WriteAllBytes(Path.Combine(salida, tipo.Replace(' ', '_') + ".docx"), bytes);
            }
        }
        return count;
    }
}
