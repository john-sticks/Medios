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
                    NotaId = orden, Titulo = orden == 4 ? "La Plata: titular editable 4" : "Titular editable " + orden,
                    ResumenCambio = "Recuperada de notas rechazadas",
                    Sintesis = string.Join(" ", Enumerable.Repeat("Una noticia ficticia permite verificar que el texto siga siendo editable y conserve los márgenes, el espaciado y las categorías del documento.", orden == 1 ? 30 : 2)),
                    Categoria = new CategoriaNoticia { Nombre = categoria },
                    Partido = orden >= 3 ? new Partido { Nombre = "La Plata" } : null,
                    Localidad = orden >= 3 ? new Localidad { Nombre = "TOLOSA (LA PLATA)" } : null,
                    Fuente = "Diario", OtrosMedios = "Otro medio",
                    Link = orden == 4 ? "https://ejemplo.invalid/noticias/la-plata-un-titulo-extenso-para-comprobar-el-salto-del-hipervinculo-en-varias-lineas-sin-perder-su-destino-original/" : "https://ejemplo.invalid/noticia", VideoUrl = "https://ejemplo.invalid/video"
                };
                sintesis.NotasIncluidas.Add(new SintesisNota { NotaPrensaId = orden, NotaVersion = version, Orden = orden });
            }
            var bytes = SintesisWordDocument.Crear(sintesis, sintesis.NotasIncluidas.ToList(), root);
            using var doc = WordprocessingDocument.Open(new MemoryStream(bytes), false);
            var errors = new OpenXmlValidator().Validate(doc).ToList();
            Check(errors.Count == 0, "Word válido: " + string.Join("; ", errors.Select(e => e.Description + " " + e.Path?.XPath)));
            var body = doc.MainDocumentPart!.Document.Body!;
            var texto = body.InnerText;
            Check(texto.Contains("Titular".ToUpperInvariant()), "El texto del Word es editable");
            Check(texto.Contains("(DIARIO, OTRO MEDIO)"), "Fuente al final del texto");
            Check(texto.Contains("https://ejemplo.invalid/noticia") && !texto.Contains("https://ejemplo.invalid/video"), "Conservar URL de noticia incluso con video");
            Check(doc.MainDocumentPart.HyperlinkRelationships.Any(h => h.Uri.AbsoluteUri == "https://ejemplo.invalid/noticia"), "URL con relación de hipervínculo externa");
            Check(body.Descendants<W.Hyperlink>().All(h => !string.IsNullOrEmpty(h.Id)), "Enlaces de noticias clicables");
            var fechaParrafo = body.Elements<W.Paragraph>().Single(p => p.InnerText.StartsWith("15 de octubre"));
            Check(fechaParrafo.Descendants<W.FontSize>().Single().Val == "40" && fechaParrafo.Descendants<W.Bold>().Any(), "Fecha de portada 20 pt negrita");
            var contenido = body.Elements<W.Paragraph>().Single(p => p.InnerText == "CONTENIDO");
            var espacio = contenido.ParagraphProperties!.GetFirstChild<W.SpacingBetweenLines>()!;
            Check(espacio.Before == "720" && espacio.After == "400", "Contenido: 36 pt antes, 20 después");
            var parrafosCuerpo = body.Elements<W.Paragraph>().Where(p => p.ParagraphProperties?.GetFirstChild<W.Justification>()?.Val?.Value == W.JustificationValues.Both).ToList();
            Check(parrafosCuerpo.All(p => p.ParagraphProperties!.GetFirstChild<W.KeepLines>()?.Val?.Value == false), "Permitir continuación del cuerpo entre páginas");
            Check(parrafosCuerpo.Count > 0 && parrafosCuerpo.All(p => p.ParagraphProperties!.GetFirstChild<W.SpacingBetweenLines>() is { Line.Value: "240", LineRule.Value: var regla } && regla == W.LineSpacingRuleValues.AtLeast), "Cuerpo justificado con interlineado mínimo de 12 pt");
            Check(body.Descendants<W.SimpleField>().Where(f => f.Instruction!.Value!.Contains("PAGEREF")).All(f => f.Descendants<W.FontSize>().Single().Val == "28"), "Números de índice 14 pt");
            Check(texto.Contains("ÁMBITO LA PLATA") && texto.Contains("HECHOS DELICTUALES"), "Word agrupa por categoría y partido");
            Check(texto.IndexOf("ÁMBITO NACIONAL") < texto.IndexOf("ÁMBITO PROVINCIAL"), "Conservar orden del índice");
            Check(!texto.Contains("NOTICIA ORIGINAL") && !texto.Contains("Recuperada de notas rechazadas") && !texto.Contains("ACTUALIZACIÓN"), "Exportar snapshot incluido sin anotaciones del historial");
            Check(texto.Contains("TITULAR EDITABLE 3"), "Conservar la versión capturada en la síntesis");
            var tituloPartido = body.Elements<W.Paragraph>().Single(p => p.InnerText == "LA PLATA: TITULAR EDITABLE 4");
            Check(tituloPartido.Elements<W.Run>().First().InnerText == "LA PLATA"
                && tituloPartido.Elements<W.Run>().First().RunProperties!.GetFirstChild<W.Underline>() != null, "Subrayar solo partido al comienzo del título");
            Check(tituloPartido.PreviousSibling<W.Paragraph>()!.InnerText == "ÁMBITO LA PLATA", "No repetir partido y localidad sobre título con prefijo");
            var pie = doc.MainDocumentPart.FooterParts.Single().Footer!;
            Check(pie.Descendants<W.SimpleField>().All(f => f.Descendants<W.FontSize>().Single().Val == "28"
                && f.Descendants<W.RunFonts>().Single().Ascii == "Bookman Old Style"), "Numeración de pie Bookman 14");
            Check(pie.Elements<W.Paragraph>().Single().ParagraphProperties!.GetFirstChild<W.Justification>()!.Val!.Value == W.JustificationValues.Center, "Pie centrado");
            Check(texto.Contains("información suministrada") == (tipo == "Vespertina"), "Aviso solo en Vespertina");
            if (tipo == "Vespertina")
            {
                var aviso = body.Elements<W.Paragraph>().Single(p => p.InnerText.StartsWith("Nota:"));
                Check(aviso.Descendants<W.FontSize>().Single().Val == "16" && !aviso.Descendants<W.Bold>().Any() && aviso.Descendants<W.Italic>().Any(), "Aviso final reducido a 8 pt cursiva");
                Check(aviso.Descendants<W.RunFonts>().Single().Ascii == "Arial" && aviso.Descendants<W.Color>().Single().Val == "606060", "Tipografía y gris del aviso final");
            }
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
